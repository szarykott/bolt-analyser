module Bolt.ETL.Tests.PerHourTests

open System
open Bolt.ETL.Analytics
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

// Ladder tests
open Bolt.ETL.Meteo.Model

/// Hand-built hour row: full fill, Tuesday midday defaults, single district.
let private hourRow (district: string) (temp: TemperatureBucket) (rain: bool) (snow: bool) : PerHour.HourRow =
    { HourStart = DateTimeOffset.Parse "2026-07-14T12:00:00+02:00"
      Fill = 1.0
      Rate = 50.0
      Shares = Map [ district, 1.0 ]
      Rain = rain
      Snow = snow
      Temperature = temp
      IsNight = false
      IsWeekend = false
      IsRushHour = false }

let private centrumRows n = Array.init n (fun _ -> hourRow PerHour.BaselineDistrict Mild false false)

[<Fact>]
let ``exposure is fill weighted column value`` () =
    let rows = [| { hourRow "A" Mild true false with Fill = 0.5 }
                  hourRow "B" Mild false false |]
    let rain : PerHour.ColumnSpec = { Name = "deszcz"
                                      Value = fun r -> if r.Rain then 1.0 else 0.0 }
    Assert.Equal(0.5, PerHour.Ladder.exposure rows rain, 6)

[<Fact>]
let ``rung1 keeps only columns passing the exposure floor`` () =
    // 30 rainy weekend-flagged rows: weekend + zła_pogoda pass (30 ≥ 20),
    // rush hour has zero exposure → omitted
    let rows =
        Array.init 30 (fun _ -> { hourRow "A" Mild true false with IsWeekend = true })
    let rung = PerHour.Ladder.rungColumns rows 1
    Assert.Equal<string list>(
        [ "weekend"; "zła_pogoda" ],
        rung.Columns |> List.map _.Name)

[<Fact>]
let ``weather split needs both children above floor`` () =
    // 25 rain hours + 5 snow hours: split would leave śnieg at 5 < 20 → merged flag stays
    let rows =
        Array.append
            (Array.init 25 (fun _ -> hourRow "A" Mild true false))
            (Array.init 5 (fun _ -> hourRow "A" Mild false true))
    let rung3 = PerHour.Ladder.rungColumns rows 3
    let names = rung3.Columns |> List.map _.Name
    Assert.Contains("zła_pogoda", names)
    Assert.DoesNotContain("deszcz", names)
    Assert.DoesNotContain("śnieg", names)
    // 25 + 25: both pass → split
    let rows' =
        Array.append
            (Array.init 25 (fun _ -> hourRow "A" Mild true false))
            (Array.init 25 (fun _ -> hourRow "A" Mild false true))
    let names' = (PerHour.Ladder.rungColumns rows' 3).Columns |> List.map _.Name
    Assert.Contains("deszcz", names')
    Assert.Contains("śnieg", names')
    Assert.DoesNotContain("zła_pogoda", names')

[<Fact>]
let ``centrum only driver gets no district columns at rung 4`` () =
    let rung = PerHour.Ladder.rungColumns (centrumRows 100) 4
    let names = rung.Columns |> List.map _.Name
    Assert.DoesNotContain(names, fun n -> n.StartsWith "dzielnica_")
    Assert.DoesNotContain("inne_dzielnice", names)
    Assert.DoesNotContain("poza_centrum", names)

[<Fact>]
let ``under floor district folds into inne_dzielnice`` () =
    // 30 h in Podgórze (own column), 5 h in Bronowice (folds)
    let rows =
        Array.append
            (Array.init 30 (fun _ -> hourRow "Podgórze" Mild false false))
            (Array.init 5 (fun _ -> hourRow "Bronowice" Mild false false))
    let rung = PerHour.Ladder.rungColumns rows 4
    let names = rung.Columns |> List.map _.Name
    Assert.Contains("dzielnica_Podgórze", names)
    Assert.Contains("inne_dzielnice", names)
    Assert.DoesNotContain("dzielnica_Bronowice", names)
    Assert.Equal<string[]>([| "Bronowice" |], rung.FoldedDistricts)

[<Fact>]
let ``budget gate needs 15 rows per coefficient`` () =
    // rung with 2 columns + intercept = 3 coefs → needs 45 rows
    let make n =
        Array.init n (fun i ->
            { hourRow "A" Mild true false with IsWeekend = i % 2 = 0 })
    let rungOf rows = PerHour.Ladder.rungColumns rows 1
    Assert.False(PerHour.Ladder.isUnlocked (make 44) (rungOf (make 44)))
    Assert.True(PerHour.Ladder.isUnlocked (make 45) (rungOf (make 45)))

[<Fact>]
let ``temperature baseline chain across rungs`` () =
    // 30 frost + 30 cool + 40 mild rows
    let rows =
        Array.concat
            [ Array.init 30 (fun _ -> hourRow "A" Frost false false)
              Array.init 30 (fun _ -> hourRow "A" Cool false false)
              Array.init 40 (fun _ -> hourRow "A" Mild false false) ]
    let names level = (PerHour.Ladder.rungColumns rows level).Columns |> List.map _.Name
    // rung 2: merged extremes flag only
    Assert.Contains("≤0°C LUB >30°C", names 2)
    // rung 3: adds 0–18°C
    Assert.Contains("0–18°C", names 3)
    // rung 4: extremes cannot split (no hot hours) → merged flag stays;
    // 0–18 cannot split (no cold hours) → stays; 25–30 absent (no warm hours)
    let n4 = names 4
    Assert.Contains("≤0°C LUB >30°C", n4)
    Assert.Contains("0–18°C", n4)
    Assert.DoesNotContain("25–30°C", n4)
    Assert.DoesNotContain("18–25°C", n4)  // baseline never a column

[<Fact>]
let ``rung 4 splits temperature fully when every bucket has exposure`` () =
    let rows =
        [ Frost; Cold; Cool; Mild; Warm; Hot ]
        |> List.collect (fun t -> List.init 25 (fun _ -> hourRow "A" t false false))
        |> Array.ofList
    let names = (PerHour.Ladder.rungColumns rows 4).Columns |> List.map _.Name
    for expected in [ "≤0°C"; "0–10°C"; "10–18°C"; "25–30°C"; ">30°C" ] do
        Assert.Contains(expected, names)
    Assert.DoesNotContain("≤0°C LUB >30°C", names)
    Assert.DoesNotContain("0–18°C", names)

[<Fact>]
let ``unlockedRungs returns rungs in order`` () =
    // 60 uniform centrum rows, half weekend: rung1 = weekend only (rush/weather
    // zero exposure) → 2 coefs → needs 30 rows → unlocked
    let rows =
        Array.init 60 (fun i -> { hourRow PerHour.BaselineDistrict Mild false false with IsWeekend = i % 2 = 0 })
    let rungs = PerHour.Ladder.unlockedRungs rows
    Assert.NotEmpty rungs
    Assert.Equal<int list>(
        rungs |> List.map _.Level |> List.sort,
        rungs |> List.map _.Level)

[<Fact>]
let ``unlockedRungs excludes rungs with no columns`` () =
    // 20 weekday, no-rain, mild, centrum-only rows: n >= 15 passes the gate
    // at every level, but weekend/rush/badWeather/night/extremeTemp/pozaCentrum
    // all have zero exposure and districts is centrum-only → every rung's
    // Columns is empty. None should surface.
    let rows = centrumRows 20
    Assert.Empty(PerHour.Ladder.unlockedRungs rows)

[<Fact>]
let ``unlockedRungs dedupes identical adjacent column sets keeping the higher level`` () =
    // weekend rows: only source of "weekend" exposure.
    // rain-only rows: badWeather passes (25), but snow stays at 0 so the
    // rain/snow split never both clear the floor → merged "zła_pogoda" at
    // every level that can split it.
    // frost + hot rows: both clear the floor alone (25 each) → extremeTemp
    // merged at rungs 2/3 (which never split by frost/hot), but splits into
    // separate frost/hot columns at rung 4.
    // Net effect: rung1 = [weekend; zła_pogoda] (unique),
    // rung2 = rung3 = [weekend; zła_pogoda; ≤0°C LUB >30°C] (duplicate pair),
    // rung4 = [weekend; zła_pogoda; ≤0°C; >30°C] (unique).
    let rows =
        Array.concat
            [ Array.init 30 (fun _ -> { hourRow PerHour.BaselineDistrict Mild false false with IsWeekend = true })
              Array.init 25 (fun _ -> hourRow PerHour.BaselineDistrict Mild true false)
              Array.init 25 (fun _ -> hourRow PerHour.BaselineDistrict Frost false false)
              Array.init 25 (fun _ -> hourRow PerHour.BaselineDistrict Hot false false) ]
    let rungs = PerHour.Ladder.unlockedRungs rows
    Assert.Equal<int list>([ 1; 3; 4 ], rungs |> List.map _.Level)
    let columnSets = rungs |> List.map (fun r -> r.Columns |> List.map _.Name)
    Assert.Equal<string list list>(columnSets, columnSets |> List.distinct)

[<Fact>]
let ``rung0 groups by weekend and night with fill weighted means`` () =
    // weekday-day: one full hour at 60 zł/h + one half hour at 120 zł/h
    // weighted mean = (60·1 + 120·0.5) / 1.5 = 80
    let rows =
        [| { hourRow "A" Mild false false with Rate = 60.0 }
           { hourRow "A" Mild false false with Rate = 120.0; Fill = 0.5 }
           { hourRow "A" Mild false false with Rate = 40.0; IsWeekend = true; IsNight = true } |]
    let averages = PerHour.hourlyAverages rows
    Assert.Contains(averages, fun avg -> not avg.IsWeekend && not avg.IsNight && avg.Rate = 80.0 && avg.HourCount = 2)
    Assert.Contains(averages, fun avg -> avg.IsWeekend && avg.IsNight && avg.Rate = 40.0 && avg.HourCount = 1)

[<Fact>]
let ``rung0 sorts groups by rate descending`` () =
    let rows =
        [| { hourRow "A" Mild false false with Rate = 30.0 }
           { hourRow "A" Mild false false with Rate = 90.0; IsNight = true } |]
    let averages = PerHour.hourlyAverages rows
    Assert.True averages[0].IsNight
    Assert.False averages[1].IsNight

let private rung1 = PerHour.Ladder.rungColumns (Array.init 30 (fun _ -> { hourRow "A" Mild true false with IsWeekend = true })) 1

[<Fact>]
let ``toAnalyticsRows emits target weight and rung columns`` () =
    let rows =
        [| { hourRow "A" Mild true false with IsWeekend = true; Fill = 0.5; Rate = 80.0 } |]
    let analyticsRow = (PerHour.toAnalyticsRows rung1 rows)[0]
    Assert.Equal<Set<string>>(
        Set [ "stawka_pln_h"; "waga"; "weekend"; "zła_pogoda" ],
        analyticsRow |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
    Assert.Equal(box 80.0, analyticsRow["stawka_pln_h"])
    Assert.Equal(box 0.5, analyticsRow["waga"])
    Assert.Equal(box 1.0, analyticsRow["weekend"])

[<Fact>]
let ``hourly model keeps all effects and fit values`` () =
    let coefficient name p : Coefficient =
        { Name = name; Coef = Some 6.5; StdErr = None; TValue = None
          PValue = p; CiLow = Some 2.0; CiHigh = Some 11.0 }
    let response: OlsResponse =
        { NObservations = 90; RSquared = Some 0.31; AdjRSquared = Some 0.28
          FStatistic = None; FPvalue = None
          Coefficients = [| coefficient "const" (Some 0.0); coefficient "weekend" (Some 0.3) |] }
    let model = PerHour.modelFromResponse rung1 response
    Assert.Equal(1, model.Level)
    Assert.Equal(90, model.ObservationCount)
    Assert.Equal(Some 0.31, model.RSquared)
    Assert.Single model.Effects |> ignore
    Assert.Equal("weekend", model.Effects[0].Feature)
    Assert.Equal(Some 0.3, model.Effects[0].PValue)
