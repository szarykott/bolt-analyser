module Bolt.Reporter.AllTimeReporting

open System
open Bolt.Reporter.DayReporting
open Bolt.Reporter.RideReportingSource

type AllTimeReportingSource =
    { ActiveTime: TimeSpan
      FinishedRides: FinishedRide seq
      NotHappenedRides: RideThatDidNotHappen seq  }

let monthsFromDayReportingSources (days: DayReportingSource seq) =
    { ActiveTime = days |> activeTimeInDays
      FinishedRides = days |> Seq.collect _.FinishedRides
      NotHappenedRides = days |> Seq.collect _.NotHappenedRides  }
