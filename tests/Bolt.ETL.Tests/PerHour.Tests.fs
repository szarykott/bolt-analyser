module Bolt.ETL.Tests.PerHourTests

open System
open Xunit
open Bolt.ETL.Analysis
open Bolt.Models
open Bolt.Models.Meteo

let private ride (accepted: string) (rideEnd: string) (earned: decimal) (district: string) : FinishedRide =
    { Payment =
        { PaymentMetadata = { PaymentType = "cash"; PaymentMethodType = "cash" }
          Paid = [||]
          Earned = [| { Title = "ride"; Value = earned } |] }
      Route =
        { RideDistance = 1.0<km>
          Stops = [| { Address = district
                       Location = { Latitude = 50.06; Longitude = 19.94 }
                       Timestamp = None } |] }
      Times =
        { CreatedTimestamp = DateTimeOffset.Parse accepted
          AcceptedTimestamp = DateTimeOffset.Parse accepted
          RideStart = DateTimeOffset.Parse accepted
          RideEnd = DateTimeOffset.Parse rideEnd }
      State = "finished" }

let private districtOf (r: FinishedRide) = r.Route.Stops[0].Address

let private clearWeather (_: DateTimeOffset) : WeatherDataPoint =
    { Temperature = 20.0<celcius>; Rain = 0.0<mm>; Snow = 0.0<cm> }

let private buildRows rides = PerHour.Dataset.buildRows clearWeather districtOf rides

[<Fact>]
let ``ride spanning two hours splits earnings and minutes proportionally`` () =
    // 14:30–15:30, 60 zł: 30 min in each hour, 30 zł each, fill 0.5, rate 60
    let rows = buildRows [| ride "2026-07-14T14:30:00+02:00" "2026-07-14T15:30:00+02:00" 60m "A" |]
    Assert.Equal(2, rows.Length)
    Assert.Equal(0.5, rows[0].Fill, 3)
    Assert.Equal(60.0, rows[0].Rate, 3)
    Assert.Equal(0.5, rows[1].Fill, 3)
    Assert.Equal(60.0, rows[1].Rate, 3)

[<Fact>]
let ``gap between rides counts as worked and goes to the next pickup's district`` () =
    // A ends 14:10, B starts 14:40 in district B: hour 14 has 10 + 30 (gap) + 20 min
    let rows =
        buildRows
            [| ride "2026-07-14T13:50:00+02:00" "2026-07-14T14:10:00+02:00" 20m "A"
               ride "2026-07-14T14:40:00+02:00" "2026-07-14T15:00:00+02:00" 20m "B" |]
    let hour14 = rows |> Array.find (fun r -> r.HourStart.Hour = 14)
    Assert.Equal(1.0, hour14.Fill, 3)
    // 10 min of A + (30 gap + 20 ride) of B
    Assert.Equal(10.0 / 60.0, hour14.Shares["A"], 3)
    Assert.Equal(50.0 / 60.0, hour14.Shares["B"], 3)

[<Fact>]
let ``fully idle clock hour gets no row and its gap minutes drop`` () =
    // ride ends 14:50, next starts 16:20 → hour 15 fully idle: no row;
    // hour 14 gets 10 gap min, hour 16 gets 20 gap min
    let rows =
        buildRows
            [| ride "2026-07-14T14:00:00+02:00" "2026-07-14T14:50:00+02:00" 50m "A"
               ride "2026-07-14T16:20:00+02:00" "2026-07-14T16:40:00+02:00" 20m "B" |]
    Assert.Equal<int[]>([| 14; 16 |], rows |> Array.map _.HourStart.Hour)
    Assert.Equal(1.0, (rows |> Array.find (fun r -> r.HourStart.Hour = 14)).Fill, 3)
    // hour 16: gap 16:00–16:20 counts (inside an eligible hour) + 20 ride min
    Assert.Equal(40.0 / 60.0, (rows |> Array.find (fun r -> r.HourStart.Hour = 16)).Fill, 3)

[<Fact>]
let ``overlapping ride spans are unioned not double counted`` () =
    // 14:00–14:40 and 14:20–14:50 → 50 worked minutes, not 70
    let rows =
        buildRows
            [| ride "2026-07-14T14:00:00+02:00" "2026-07-14T14:40:00+02:00" 30m "A"
               ride "2026-07-14T14:20:00+02:00" "2026-07-14T14:50:00+02:00" 30m "A" |]
    Assert.Equal(1, rows.Length)
    Assert.Equal(50.0 / 60.0, rows[0].Fill, 3)
    // earnings still full 60 zł in the hour → rate = 60 / (50/60) = 72
    Assert.Equal(72.0, rows[0].Rate, 3)

[<Fact>]
let ``district shares sum to one`` () =
    let rows =
        buildRows
            [| ride "2026-07-14T14:00:00+02:00" "2026-07-14T14:20:00+02:00" 10m "A"
               ride "2026-07-14T14:30:00+02:00" "2026-07-14T14:50:00+02:00" 10m "B" |]
    for row in rows do
        Assert.Equal(1.0, row.Shares |> Map.toSeq |> Seq.sumBy snd, 6)

[<Fact>]
let ``rate divides earnings by fill`` () =
    // 20 min ride, 30 zł → fill 1/3, rate 90 zł/h
    let rows = buildRows [| ride "2026-07-14T14:10:00+02:00" "2026-07-14T14:30:00+02:00" 30m "A" |]
    Assert.Equal(1.0 / 3.0, rows[0].Fill, 3)
    Assert.Equal(90.0, rows[0].Rate, 3)

[<Fact>]
let ``hour flags come from hour start`` () =
    // Tuesday 23:15 ride → hour 23: night, not weekend, not rush (weekday)
    let rows = buildRows [| ride "2026-07-14T23:15:00+02:00" "2026-07-14T23:45:00+02:00" 20m "A" |]
    Assert.True(rows[0].IsNight)
    Assert.False(rows[0].IsWeekend)
    Assert.False(rows[0].IsRushHour)
    // Tuesday 07:30 → morning rush, day
    let morning = buildRows [| ride "2026-07-14T07:30:00+02:00" "2026-07-14T07:50:00+02:00" 20m "A" |]
    Assert.False(morning[0].IsNight)
    Assert.True(morning[0].IsRushHour)
