module Bolt.Reporter.Calculations

open System
open Bolt.Models.BoltApi
open Bolt.Reporter.RideReportingSource

// --- Private helpers ---

let private rideNetEarned (r: FinishedRide) : Money = r.Payment.Earned |> Seq.sumBy _.Value
let private ridePaid (r: FinishedRide) : Money = r.Payment.Paid |> Seq.sumBy _.Value

let private rideDuration (r: FinishedRide) = r.Times.RideEnd.Value - r.Times.RideStart.Value
let private waitDuration (r: FinishedRide) = r.Times.RideStart.Value - r.Times.AcceptedTimestamp.Value
let private acceptDuration (r: FinishedRide) = r.Times.AcceptedTimestamp.Value - r.Times.CreatedTimestamp.Value

[<Literal>]
let private CommissionTitle = "Prowizja Bolt"

[<Literal>]
let private TipTitle = "Napiwek"

// Warsaw-local clock for temporal buckets
let private localTime (t: DateTimeOffset) =
    let off = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw").GetUtcOffset(t)
    t.ToOffset(off)

let private localHour (t: DateTimeOffset) = (localTime t).Hour
let private localHourKey (t: DateTimeOffset) = TimeOnly((localTime t).Hour, 0, 0)

let private sumDurations (xs: TimeSpan seq) = xs |> Seq.sumBy _.TotalSeconds |> TimeSpan.FromSeconds

let private averageDurations (xs: TimeSpan seq) =
    let arr = xs |> Array.ofSeq

    if arr.Length = 0 then
        TimeSpan.Zero
    else
        arr |> Array.averageBy _.TotalSeconds |> TimeSpan.FromSeconds

// --- 1. Earnings (ride-only) ---

let totalEarned (rides: FinishedRide seq) : Money = rides |> Seq.sumBy rideNetEarned

let totalPaid (rides: FinishedRide seq) : Money = rides |> Seq.sumBy ridePaid

let averageEarnedPerRide (rides: FinishedRide seq) : Money =
    let arr = rides |> Array.ofSeq

    if arr.Length = 0 then
        Money.Zero
    else
        Money((arr |> Array.sumBy (fun r -> (rideNetEarned r).Value)) / decimal arr.Length)

let medianEarnedPerRide (rides: FinishedRide seq) : Money =
    let sorted = rides |> Seq.map (fun r -> (rideNetEarned r).Value) |> Seq.sort |> Array.ofSeq

    match sorted.Length with
    | 0 -> Money.Zero
    | n when n % 2 = 1 -> Money sorted.[n / 2]
    | n -> Money((sorted.[n / 2 - 1] + sorted.[n / 2]) / 2m)

let earnedByCategory (rides: FinishedRide seq) : MoneyElement seq =
    rides
    |> Seq.collect _.Payment.Earned
    |> Seq.groupBy _.Title
    |> Seq.map (fun (title, items) ->
        { Title = title
          Value = items |> Seq.sumBy _.Value })

let totalTips (rides: FinishedRide seq) : Money =
    rides
    |> Seq.collect _.Payment.Earned
    |> Seq.filter (fun e -> e.Title = TipTitle)
    |> Seq.sumBy _.Value

let totalCommission (rides: FinishedRide seq) : Money =
    rides |> Seq.sumBy (fun r -> (Seq.head r.Payment.Earned).Value)

let commissionRate (rides: FinishedRide seq) : decimal =
    let net = (totalEarned rides).Value
    let commission = (totalCommission rides).Value
    let gross = net - commission
    if gross = 0m then 0m else abs commission / gross

// --- 2. Efficiency ---

let totalDistance (rides: FinishedRide seq) : Distance =
    rides |> Seq.sumBy (fun r -> r.Route.RideDistance.Value) |> Distance

let averageRideDistance (rides: FinishedRide seq) : Distance =
    let arr = rides |> Array.ofSeq

    if arr.Length = 0 then
        Distance 0m
    else
        Distance((arr |> Array.sumBy (fun r -> r.Route.RideDistance.Value)) / decimal arr.Length)

let earnedPerKm (rides: FinishedRide seq) : decimal =
    let dist = (totalDistance rides).Value
    if dist = 0m then 0m else (totalEarned rides).Value / dist

let totalRideDuration (rides: FinishedRide seq) : TimeSpan = rides |> Seq.map rideDuration |> sumDurations

let averageRideDuration (rides: FinishedRide seq) : TimeSpan = rides |> Seq.map rideDuration |> averageDurations

let averageWaitTime (rides: FinishedRide seq) : TimeSpan = rides |> Seq.map waitDuration |> averageDurations

let averageAcceptTime (rides: FinishedRide seq) : TimeSpan = rides |> Seq.map acceptDuration |> averageDurations

let earnedPerActiveHour (rides: FinishedRide seq) (activeTime: TimeSpan) : decimal =
    if activeTime.TotalHours = 0.0 then
        0m
    else
        (totalEarned rides).Value / decimal activeTime.TotalHours

let ridesPerActiveHour (rides: FinishedRide seq) (activeTime: TimeSpan) : decimal =
    if activeTime.TotalHours = 0.0 then
        0m
    else
        decimal (Seq.length rides) / decimal activeTime.TotalHours

let utilization (rides: FinishedRide seq) (activeTime: TimeSpan) : decimal =
    if activeTime.TotalSeconds = 0.0 then
        0m
    else
        decimal (totalRideDuration rides).TotalSeconds / decimal activeTime.TotalSeconds

// --- 3. Reliability ---

let finishedCount (rides: FinishedRide seq) : int = Seq.length rides

let notHappenedCount (rides: RideThatDidNotHappen seq) : int = Seq.length rides

let cancellationRate (finished: FinishedRide seq) (notHappened: RideThatDidNotHappen seq) : decimal =
    let nh = Seq.length notHappened
    let total = Seq.length finished + nh
    if total = 0 then 0m else decimal nh / decimal total

let countByState (finished: FinishedRide seq) (notHappened: RideThatDidNotHappen seq) : (OrderState * int) seq =
    Seq.append (finished |> Seq.map _.State) (notHappened |> Seq.map _.State)
    |> Seq.countBy id

// --- 4. Mix & patterns (typed keys) ---

let earnedByPaymentType (rides: FinishedRide seq) : (PaymentType * Money) seq =
    rides
    |> Seq.groupBy (fun r -> r.Payment.PaymentMetadata.PaymentType)
    |> Seq.map (fun (k, rs) -> (k, rs |> Seq.sumBy rideNetEarned))
    |> Seq.sortBy fst

let earnedByPaymentMethod (rides: FinishedRide seq) : (PaymentMethodType * Money) seq =
    rides
    |> Seq.groupBy (fun r -> r.Payment.PaymentMetadata.PaymentMethodType)
    |> Seq.map (fun (k, rs) -> (k, rs |> Seq.sumBy rideNetEarned))
    |> Seq.sortBy fst

let earnedByHourOfDay (rides: FinishedRide seq) : (TimeOnly * Money) seq =
    rides
    |> Seq.groupBy (fun r -> localHourKey r.Times.CreatedTimestamp.Value)
    |> Seq.map (fun (t, rs) -> (t, rs |> Seq.sumBy rideNetEarned))
    |> Seq.sortBy fst

let averageEarnedByHourOfDay (rides: FinishedRide seq) : (TimeOnly * Money) seq =
    rides
    |> Seq.groupBy (fun r ->
        let lt = localTime r.Times.CreatedTimestamp.Value
        (DateOnly.FromDateTime lt.DateTime, TimeOnly(lt.Hour, 0, 0)))
    |> Seq.map (fun ((_, hour), rs) -> (hour, (rs |> Seq.sumBy rideNetEarned).Value))
    |> Seq.groupBy fst
    |> Seq.map (fun (hour, daySums) -> (hour, Money(daySums |> Seq.averageBy snd)))
    |> Seq.sortBy fst

let earnedByDayOfWeek (rides: FinishedRide seq) : (DayOfWeek * Money) seq =
    rides
    |> Seq.groupBy (fun r -> (localTime r.Times.CreatedTimestamp.Value).DayOfWeek)
    |> Seq.map (fun (d, rs) -> (d, rs |> Seq.sumBy rideNetEarned))
    |> Seq.sortBy fst

let countByPaymentType (rides: FinishedRide seq) : (PaymentType * int) seq =
    rides
    |> Seq.countBy (fun r -> r.Payment.PaymentMetadata.PaymentType)
    |> Seq.sortBy fst

let ridesByHourOfDay (rides: FinishedRide seq) : (TimeOnly * int) seq =
    rides
    |> Seq.countBy (fun r -> localHourKey r.Times.CreatedTimestamp.Value)
    |> Seq.sortBy fst

let ridesByDayOfWeek (rides: FinishedRide seq) : (DayOfWeek * int) seq =
    rides
    |> Seq.countBy (fun r -> (localTime r.Times.CreatedTimestamp.Value).DayOfWeek)
    |> Seq.sortBy fst

let cashShare (rides: FinishedRide seq) : decimal =
    let total = (totalEarned rides).Value

    if total = 0m then
        0m
    else
        let cash =
            rides
            |> Seq.filter (fun r -> r.Payment.PaymentMetadata.PaymentType = PaymentType.Cash)
            |> Seq.sumBy (fun r -> (rideNetEarned r).Value)

        cash / total
