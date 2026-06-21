module Bolt.Reporter.MonthReporting

open System
open Bolt.Reporter.RideReportingSource
open DayReporting

type MonthReportingSource =
    { Year: int
      Month: int
      ActiveTime: TimeSpan
      FinishedRides: FinishedRide seq
      NotHappenedRides: RideThatDidNotHappen seq  }

let monthsFromDayReportingSources (days: DayReportingSource seq) =
    days
    |> Seq.groupBy (fun d -> (d.Day.Year, d.Day.Month))
    |> Seq.map (fun ((year, month), days) ->
        { Year = year
          Month = month
          ActiveTime = days |> activeTimeInDays
          FinishedRides = days |> Seq.collect _.FinishedRides
          NotHappenedRides = days |> Seq.collect _.NotHappenedRides  })
    |> Seq.sortBy (fun w -> w.Year, w.Month)
