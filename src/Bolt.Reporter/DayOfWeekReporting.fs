module Bolt.Reporter.DayOfWeekReporting

open System
open Bolt.Reporter.RideReportingSource
open Bolt.Reporter.DayReporting

type DayOfWeekReportingSource =
    { DayOfWeek: DayOfWeek
      ActiveTime: TimeSpan option
      FinishedRides: FinishedRide seq
      NotHappenedRides: RideThatDidNotHappen seq }
    interface IReportingSource with        
        member this.Label = $"{this.DayOfWeek}"
        member this.Active = this.ActiveTime
        member this.FinishedRides = this.FinishedRides
        member this.NotHappenedRides = this.NotHappenedRides 

let weekDaysFromDayReportingSources (days: DayReportingSource seq) =
    days
    |> Seq.groupBy (fun d -> d.Day.DayOfWeek)
    |> Seq.map (fun (dayOfWeek, days) ->
        { DayOfWeek = dayOfWeek
          ActiveTime = days |> activeTimeInDays
          FinishedRides = days |> Seq.collect _.FinishedRides
          NotHappenedRides = days |> Seq.collect _.NotHappenedRides })
    |> Seq.sortBy (fun w -> (int w.DayOfWeek + 6) % 7)