module Bolt.Reporter.AllTimeReporting

open Bolt.Reporter.DayReporting
open Bolt.Reporter.RideReportingSource

// cannot have ActiveTime as Bolt only gives it for past 3 months
type AllTimeReportingSource =
    { FinishedRides: FinishedRide seq
      NotHappenedRides: RideThatDidNotHappen seq  }

let monthsFromDayReportingSources (days: DayReportingSource seq) =
    { FinishedRides = days |> Seq.collect _.FinishedRides
      NotHappenedRides = days |> Seq.collect _.NotHappenedRides  }
