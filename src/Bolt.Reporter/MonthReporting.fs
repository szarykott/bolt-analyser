module Bolt.Reporter.MonthReporting

open System
open Bolt.Reporter.RideReportingSource
open DayReporting

type MonthReportingSource =
    { Year: int
      Month: int
      ActiveTime: TimeSpan option
      FinishedRides: FinishedRide seq
      NotHappenedRides: RideThatDidNotHappen seq  }
    interface IReportingSource with        
        member this.Label= $"{this.Year}/{this.Month}"
        member this.Active = this.ActiveTime
        member this.FinishedRides = this.FinishedRides
        member this.NotHappenedRides = this.NotHappenedRides

let monthsFromDayReportingSources (days: DayReportingSource seq) =
    days
    |> Seq.groupBy (fun d -> d.Day.Year, d.Day.Month)
    |> Seq.map (fun ((year, month), days) ->
        { Year = year
          Month = month
          ActiveTime = days |> activeTimeInDays
          FinishedRides = days |> Seq.collect _.FinishedRides
          NotHappenedRides = days |> Seq.collect _.NotHappenedRides  })
    |> Seq.sortBy (fun w -> w.Year, w.Month)
