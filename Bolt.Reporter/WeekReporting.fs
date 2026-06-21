module Bolt.Reporter.WeekReporting

open System
open System.Globalization
open Bolt.Reporter.RideReportingSource
open DayReporting

type WeekReportingSource =
    { Year: int
      WeekNo: int
      ActiveTime: TimeSpan
      FinishedRides: FinishedRide seq
      NotHappenedRides: RideThatDidNotHappen seq }

let weeksFromDayReportingSources (days: DayReportingSource seq) =
    days
    |> Seq.groupBy (fun d -> ISOWeek.GetYear(d.Day), ISOWeek.GetWeekOfYear(d.Day))
    |> Seq.map (fun ((year, weekNo), days) ->
        { Year = year
          WeekNo = weekNo
          ActiveTime = days |> activeTimeInDays
          FinishedRides = days |> Seq.collect _.FinishedRides
          NotHappenedRides = days |> Seq.collect _.NotHappenedRides })
    |> Seq.sortBy (fun w -> w.Year, w.WeekNo)
