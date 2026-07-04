# Statistics for `Bolt.Reporter` — driver dashboard calc library

## Context

`Bolt.Reporter/Calculations.fs` is a stub (one unused generic `rides` extractor). Meanwhile `Bolt.Reporter/Program.fs` **already** computes many stats inline (`totalFinishedRides`, `totalDistance`, `totalEarned`, `averageEarned`, `earnedPerHour`, `averageEarnedPerHourOfDay`, `numRidesPerHourOfDay`) directly over `RideReportingSource array`. The reporting sources (`DayReportingSource` / `WeekReportingSource` / `MonthReportingSource`) now expose `FinishedRides`, `NotHappenedRides`, and `ActiveTime`.

Goal: a **driver dashboard** stat library — earnings, efficiency, reliability, payment/temporal patterns — usable over any period. `Calculations.fs` becomes the **single source of truth**; `Program.fs` is rewired to consume it, removing its inline duplicates.

### Decisions settled with user

1. **Refactor + rewire.** `Calculations.fs` is authoritative; `Program.fs` changes to call it. No duplicate logic. (Plan touches 3 areas: `Calculations.fs`, `Program.fs`, new test project.)
2. **Calc is pure on `FinishedRide seq`** (+ `RideThatDidNotHappen seq` for reliability, + `TimeSpan` activeTime for active-time stats). The finished/not-happened partition already exists in `DayReporting.daysFromRideReportingSources`; callers get sequences from `DayReportingSource.FinishedRides` etc. Calc has **no** knowledge of `RideType`.
3. **Typed keys everywhere** (avoid primitive obsession). Temporal → `TimeOnly` / `DayOfWeek`; payment → `PaymentType` / `PaymentMethodType`; state → `OrderState`. `MoneyElement seq` is used **only** by `earnedByCategory` (key is a genuine API string Title).
4. **Money model:** per ride, net earned = `Σ Payment.Earned.Value`. Confirmed against live data (`~/.config/.bolt-app/pastOrderDetails.json`): `Earned` = `[Prowizja Bolt (negative)] ++ gross line-items`; it excludes the "Przychód" total, so the sum is net. `Money` has `+`/`Zero`/`.Value`.
5. **Tips/commission identified from confirmed live titles:**
   - Commission = the prepended first element of `Earned`, `Title = "Prowizja Bolt"`, value **negative**. Use `Seq.head` (structural) — every order has exactly 2 accordion items.
   - Tips = line-item `Title = "Napiwek"` (may be absent → `Money.Zero`).
   - Centralize both string constants in one place.
6. **Local Warsaw time** for all hour-of-day / day-of-week bucketing. Centralize one `localHour` / `localTime` helper (reuse Program's Europe/Warsaw offset approach).
7. **Both total and average** hour-of-day earnings exposed (Program's chart uses the average).
8. **Tests:** new `tests/Bolt.Reporter.Tests` XUnit project referencing `Bolt.Reporter.fsproj` (mirrors `Bolt.App.Tests`; no `.sln` to edit — projects reference each other directly).

## Files

- `Bolt.Reporter/Calculations.fs` — all stat functions (rewrite; drop dead `rides` stub).
- `Bolt.Reporter/Program.fs` — delete inline calc duplicates; pipe `finishedRides`/period sources into `Calculations`.
- `tests/Bolt.Reporter.Tests/Bolt.Reporter.Tests.fsproj` + `Calculations.Tests.fs` — new.

Keep `module Bolt.Reporter.Calculations`; `open System`, `open Bolt.Models.Shared`, `open Bolt.Reporter.RideReportingSource`.

## Private helpers (build first)

```fsharp
let private rideNetEarned (r: FinishedRide) : Money = r.Payment.Earned |> Seq.sumBy _.Value
let private ridePaid      (r: FinishedRide) : Money = r.Payment.Paid   |> Seq.sumBy _.Value

let private rideDuration   (r: FinishedRide) = r.Times.RideEnd.Value   - r.Times.RideStart.Value      // RideEnd - RideStart
let private waitDuration   (r: FinishedRide) = r.Times.RideStart.Value  - r.Times.AcceptedTimestamp.Value
let private acceptDuration (r: FinishedRide) = r.Times.AcceptedTimestamp.Value - r.Times.CreatedTimestamp.Value
// .Value on UnixTime is DateTimeOffset; subtraction yields TimeSpan. RideTimes fields are NOT optional.

[<Literal>] let private CommissionTitle = "Prowizja Bolt"
[<Literal>] let private TipTitle        = "Napiwek"

// Warsaw-local clock for temporal buckets
let private localTime (t: DateTimeOffset) =
    let off = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw").GetUtcOffset(t)
    t.ToOffset(off)
let private localHour r = (localTime r).Hour
```

Notes on types found:
- `Distance` has **no** `+`/`Zero` — sum via `.Value` (decimal) and wrap: `Distance(...)`; empty → `Distance 0m`.
- `MoneyElement.(+)` clobbers `Title` to `"Summed"` — do **not** use it for grouping; build elements explicitly with the group key as `Title`.

## Functions

### 1. Earnings (ride-only)
- `totalEarned : FinishedRide seq -> Money`
- `totalPaid : FinishedRide seq -> Money`
- `averageEarnedPerRide : FinishedRide seq -> Money` (empty → `Money.Zero`)
- `medianEarnedPerRide : FinishedRide seq -> Money` (sort net; even count → mean of two middles; empty → `Zero`)
- `earnedByCategory : FinishedRide seq -> MoneyElement seq` — flatten every ride's `Earned`, group+sum by `Title`
- `totalTips : FinishedRide seq -> Money` — sum `Earned` items where `Title = TipTitle`
- `totalCommission : FinishedRide seq -> Money` — sum `Seq.head` of each ride's `Earned` (negative)
- `commissionRate : FinishedRide seq -> decimal` — `abs(commission) / gross`, where `gross = net - commission`; guard /0 → 0

### 2. Efficiency
Ride-only:
- `totalDistance : FinishedRide seq -> Distance`
- `averageRideDistance : FinishedRide seq -> Distance`
- `earnedPerKm : FinishedRide seq -> decimal` (guard zero distance → 0)
- `totalRideDuration : FinishedRide seq -> TimeSpan`
- `averageRideDuration : FinishedRide seq -> TimeSpan`
- `averageWaitTime : FinishedRide seq -> TimeSpan` (pickup leg, `waitDuration`)
- `averageAcceptTime : FinishedRide seq -> TimeSpan` (`acceptDuration`)

Active-time (extra `activeTime: TimeSpan`; guard zero activeTime → 0):
- `earnedPerActiveHour : FinishedRide seq -> TimeSpan -> decimal`
- `ridesPerActiveHour : FinishedRide seq -> TimeSpan -> decimal`
- `utilization : FinishedRide seq -> TimeSpan -> decimal` — `totalRideDuration / activeTime`

### 3. Reliability (`FinishedRide seq` + `RideThatDidNotHappen seq`)
- `finishedCount` / `notHappenedCount : -> int`
- `cancellationRate : FinishedRide seq -> RideThatDidNotHappen seq -> decimal` — notHappened / total (guard /0 → 0)
- `countByState : FinishedRide seq -> RideThatDidNotHappen seq -> (OrderState * int) seq` — counts of `.State` across both sequences

### 4. Mix & patterns (typed keys)
- `earnedByPaymentType : FinishedRide seq -> (PaymentType * Money) seq`
- `earnedByPaymentMethod : FinishedRide seq -> (PaymentMethodType * Money) seq`
- `earnedByHourOfDay : FinishedRide seq -> (TimeOnly * Money) seq` — group by `localHour CreatedTimestamp`, sum
- `averageEarnedByHourOfDay : FinishedRide seq -> (TimeOnly * Money) seq` — group by `(localDate, localHour)` sum, then group by hour and average those daily sums (matches Program's current chart)
- `earnedByDayOfWeek : FinishedRide seq -> (DayOfWeek * Money) seq`
- `countByPaymentType : FinishedRide seq -> (PaymentType * int) seq`
- `ridesByHourOfDay : FinishedRide seq -> (TimeOnly * int) seq`
- `ridesByDayOfWeek : FinishedRide seq -> (DayOfWeek * int) seq`
- `cashShare : FinishedRide seq -> decimal` — earned where `PaymentMetadata.PaymentType = Cash` / total earned (guard /0 → 0)

All temporal seqs `Seq.sortBy fst`.

## Rewire `Program.fs`

- Keep `finishedRides : RideReportingSource array -> FinishedRide seq` (the `RideType.Finished` filter) and `getOffset` if still needed, OR move `localTime` into `Calculations` and reuse.
- Delete inline `totalFinishedRides`, `totalDistance`, `totalEarned`, `earnings`, `averageEarned`, `ridesPerHour`, `ridesPerHourOfDay`, `ridesPerDay`, `earnedPerHour`, `averageEarnedPerHour`, `averageEarnedPerHourOfDay`, `numRidesPerHourOfDay`.
- Replace report-body calls with `Calculations.*`:
  - count → `Calculations.finishedCount`
  - avg/ride → `averageEarnedPerRide`
  - distance → `totalDistance ... .Value`
  - earnings chart → `averageEarnedByHourOfDay |> scatterPlot (fun (t,m) -> t.Hour, float m.Value)`
  - rides chart → `ridesByHourOfDay |> scatterPlot (fun (t,c) -> t.Hour, float c)`
- `averageEarnedPerHour` (per clock-hour mean) has no period in the new API; if the report still needs it, add an active-hour figure via `earnedPerActiveHour` or keep a thin local helper. Confirm during impl which the Polish report text wants.

## Verification

- `dotnet build Bolt.Reporter/` — clean (F# checks all signatures).
- `dotnet test tests/Bolt.Reporter.Tests/` — new XUnit tests on handcrafted `FinishedRide seq` / `RideThatDidNotHappen seq`:
  - `totalEarned` / `earnedByCategory` sum to expected (incl. negative commission).
  - `totalTips` (Title "Napiwek"), `totalCommission` (`Seq.head`, negative), `commissionRate`.
  - `cancellationRate`, `countByState` on a finished + not-happened mix.
  - `utilization` / `earnedPerActiveHour` with a known `TimeSpan`.
  - `earnedByHourOfDay` / `averageEarnedByHourOfDay` bucket by Warsaw-local `CreatedTimestamp` correctly (incl. a cross-offset case).
- End-to-end: run the reporter against `~/.config/.bolt-app/*.json`; eyeball `report.md` totals vs the Bolt app numbers.
