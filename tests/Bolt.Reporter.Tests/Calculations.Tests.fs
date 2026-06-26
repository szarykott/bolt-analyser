module Bolt.Reporter.Tests.CalculationsTests

open System
open Xunit
open Bolt.Models.Shared
open Bolt.Reporter.RideReportingSource
open Bolt.Reporter.Calculations

// --- Builders ---

let private mkRide
    (earned: (string * decimal) list)
    (distance: decimal)
    (created: DateTimeOffset)
    (paymentType: PaymentType)
    (method: PaymentMethodType)
    : FinishedRide =
    { Payment =
        { PaymentMetadata =
            { PaymentType = paymentType
              PaymentMethodType = method }
          Paid = []
          Earned = earned |> List.map (fun (t, v) -> { Title = t; Value = Money v }) }
      Route = { RideDistance = Distance distance; Stops = [] }
      Times =
        { CreatedTimestamp = UnixTime created
          AcceptedTimestamp = UnixTime(created.AddMinutes 1.0)
          RideStart = UnixTime(created.AddMinutes 5.0)
          RideEnd = UnixTime(created.AddMinutes 20.0) }
      State = OrderState.Finished }

let private mkNotHappened (state: OrderState) : RideThatDidNotHappen =
    { Created = UnixTime DateTimeOffset.UnixEpoch
      Stops = Seq.empty
      State = state }

// commission head (negative), a fare line, a tip
let private standardEarned = [ "Prowizja Bolt", -5m; "Kurs", 25m; "Napiwek", 3m ]

let private utc (y, mo, d, h) =
    DateTimeOffset(y, mo, d, h, 30, 0, TimeSpan.Zero)

// --- Earnings ---

[<Fact>]
let ``totalEarned sums net of every Earned line incl negative commission`` () =
    let rides =
        [ mkRide standardEarned 10m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card
          mkRide standardEarned 10m (utc (2024, 1, 1, 11)) PaymentType.InApp PaymentMethodType.Card ]

    // per ride net = -5 + 25 + 3 = 23
    Assert.Equal(46m, (totalEarned rides).Value)

[<Fact>]
let ``earnedByCategory groups and sums by title`` () =
    let rides =
        [ mkRide standardEarned 10m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card
          mkRide standardEarned 10m (utc (2024, 1, 1, 11)) PaymentType.InApp PaymentMethodType.Card ]

    let byCat = earnedByCategory rides |> Seq.map (fun e -> e.Title, e.Value.Value) |> Map.ofSeq

    Assert.Equal(-10m, byCat.["Prowizja Bolt"])
    Assert.Equal(50m, byCat.["Kurs"])
    Assert.Equal(6m, byCat.["Napiwek"])

[<Fact>]
let ``totalTips sums only Napiwek lines`` () =
    let rides =
        [ mkRide standardEarned 10m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card
          mkRide [ "Prowizja Bolt", -5m; "Kurs", 25m ] 10m (utc (2024, 1, 1, 11)) PaymentType.InApp PaymentMethodType.Card ]

    Assert.Equal(3m, (totalTips rides).Value)

[<Fact>]
let ``totalCommission sums the head element (negative)`` () =
    let rides =
        [ mkRide standardEarned 10m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card
          mkRide standardEarned 10m (utc (2024, 1, 1, 11)) PaymentType.InApp PaymentMethodType.Card ]

    Assert.Equal(-10m, (totalCommission rides).Value)

[<Fact>]
let ``commissionRate is abs(commission) over gross`` () =
    let rides =
        [ mkRide standardEarned 10m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card ]

    // net = 23, commission = -5, gross = 23 - (-5) = 28
    Assert.Equal(5m / 28m, commissionRate rides)

[<Fact>]
let ``averageEarnedPerRide and median on empty -> Zero`` () =
    Assert.Equal(Money.Zero, averageEarnedPerRide [])
    Assert.Equal(Money.Zero, medianEarnedPerRide [])

[<Fact>]
let ``medianEarnedPerRide even count averages two middles`` () =
    let mk net =
        mkRide [ "Kurs", net ] 1m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card

    let rides = [ mk 10m; mk 20m; mk 30m; mk 40m ]
    Assert.Equal(25m, (medianEarnedPerRide rides).Value)

// --- Efficiency ---

[<Fact>]
let ``earnedPerActiveHour divides total earned by active hours`` () =
    let rides =
        [ mkRide [ "Kurs", 60m ] 10m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card ]

    Assert.Equal(30m, earnedPerActiveHour rides (TimeSpan.FromHours 2.0))

[<Fact>]
let ``earnedPerActiveHour guards zero active time`` () =
    let rides =
        [ mkRide [ "Kurs", 60m ] 10m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card ]

    Assert.Equal(0m, earnedPerActiveHour rides TimeSpan.Zero)

[<Fact>]
let ``utilization is ride duration over active time`` () =
    // each ride 15 min driving; two rides = 30 min of a 60 min active hour
    let rides =
        [ mkRide [ "Kurs", 1m ] 1m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card
          mkRide [ "Kurs", 1m ] 1m (utc (2024, 1, 1, 11)) PaymentType.InApp PaymentMethodType.Card ]

    Assert.Equal(0.5m, utilization rides (TimeSpan.FromHours 1.0))

[<Fact>]
let ``totalDistance and earnedPerKm`` () =
    let rides =
        [ mkRide [ "Kurs", 20m ] 4m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card
          mkRide [ "Kurs", 20m ] 6m (utc (2024, 1, 1, 11)) PaymentType.InApp PaymentMethodType.Card ]

    Assert.Equal(10m, (totalDistance rides).Value)
    Assert.Equal(4m, earnedPerKm rides) // 40 / 10

// --- Reliability ---

[<Fact>]
let ``cancellationRate is notHappened over total`` () =
    let finished =
        [ mkRide [ "Kurs", 1m ] 1m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card ]

    let notHappened =
        [ mkNotHappened OrderState.ClientCancelled
          mkNotHappened OrderState.DriverRejected
          mkNotHappened OrderState.ClientDidNotShow ]

    Assert.Equal(0.75m, cancellationRate finished notHappened) // 3 / 4

[<Fact>]
let ``cancellationRate guards empty`` () =
    Assert.Equal(0m, cancellationRate [] [])

[<Fact>]
let ``countByState counts states across both sequences`` () =
    let finished =
        [ mkRide [ "Kurs", 1m ] 1m (utc (2024, 1, 1, 10)) PaymentType.InApp PaymentMethodType.Card
          mkRide [ "Kurs", 1m ] 1m (utc (2024, 1, 1, 11)) PaymentType.InApp PaymentMethodType.Card ]

    let notHappened =
        [ mkNotHappened OrderState.ClientCancelled
          mkNotHappened OrderState.ClientCancelled ]

    let counts = countByState finished notHappened |> Map.ofSeq
    Assert.Equal(2, counts.[OrderState.Finished])
    Assert.Equal(2, counts.[OrderState.ClientCancelled])

// --- Mix & patterns ---

[<Fact>]
let ``cashShare is cash earned over total earned`` () =
    let rides =
        [ mkRide [ "Kurs", 30m ] 1m (utc (2024, 1, 1, 10)) PaymentType.Cash PaymentMethodType.Default
          mkRide [ "Kurs", 10m ] 1m (utc (2024, 1, 1, 11)) PaymentType.InApp PaymentMethodType.Card ]

    Assert.Equal(0.75m, cashShare rides) // 30 / 40

[<Fact>]
let ``earnedByHourOfDay buckets by Warsaw-local hour (winter +1, summer +2)`` () =
    let rides =
        [ mkRide [ "Kurs", 10m ] 1m (utc (2024, 1, 15, 10)) PaymentType.InApp PaymentMethodType.Card // 10:30Z -> 11 local
          mkRide [ "Kurs", 20m ] 1m (utc (2024, 7, 15, 10)) PaymentType.InApp PaymentMethodType.Card ] // 10:30Z -> 12 local

    let byHour = earnedByHourOfDay rides |> Seq.map (fun (t, m) -> t.Hour, m.Value) |> Map.ofSeq

    Assert.Equal(10m, byHour.[11])
    Assert.Equal(20m, byHour.[12])

[<Fact>]
let ``averageEarnedByHourOfDay averages daily sums per hour`` () =
    // same Warsaw hour (11), two different days, sums 10 and 30 -> average 20
    let rides =
        [ mkRide [ "Kurs", 4m ] 1m (utc (2024, 1, 15, 10)) PaymentType.InApp PaymentMethodType.Card
          mkRide [ "Kurs", 6m ] 1m (utc (2024, 1, 15, 10)) PaymentType.InApp PaymentMethodType.Card
          mkRide [ "Kurs", 30m ] 1m (utc (2024, 1, 16, 10)) PaymentType.InApp PaymentMethodType.Card ]

    let avg = averageEarnedByHourOfDay rides |> Seq.map (fun (t, m) -> t.Hour, m.Value) |> Map.ofSeq

    Assert.Equal(20m, avg.[11]) // (10 + 30) / 2

[<Fact>]
let ``temporal sequences are sorted by key`` () =
    let rides =
        [ mkRide [ "Kurs", 1m ] 1m (utc (2024, 1, 1, 20)) PaymentType.InApp PaymentMethodType.Card
          mkRide [ "Kurs", 1m ] 1m (utc (2024, 1, 1, 5)) PaymentType.InApp PaymentMethodType.Card ]

    let hours = ridesByHourOfDay rides |> Seq.map (fun (t, _) -> t.Hour) |> List.ofSeq
    Assert.Equal<int list>(List.sort hours, hours)