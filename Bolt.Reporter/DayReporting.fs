module Bolt.Reporter.DayReporting

open System
open Bolt.Reporter.RideReportingSource
open Bolt.Models.ActivityHours

type DayReportingSource =
    { Day: DateOnly
      ActiveTime: TimeSpan
      FinishedRides: FinishedRide seq
      NotHappenedRides: RideThatDidNotHappen seq }

let daysFromRideReportingSources (rides: RideReportingSource seq) (activity: ActivityHours) =
    let rideDate ride =
        match ride.Data with
        | RideType.Finished d -> d.Times.CreatedTimestamp.Value.DateTime |> DateOnly.FromDateTime
        | RideType.DidNotHappen d -> d.Created.Value.Date |> DateOnly.FromDateTime

    let activityTimePerDay =
        activity.Periods
        |> List.collect _.Items
        |> List.map (fun d -> (d.Date, int64 d.ActiveSeconds |> TimeSpan.FromSeconds))
        |> Map.ofList
    
    let partition (rides' : RideReportingSource seq) =
        rides' |> Seq.fold (fun (f, nf) r ->
                      match r.Data with
                      | RideType.Finished x -> (x :: f, nf) 
                      | RideType.DidNotHappen x -> (f, x :: nf) )
                      ([], [])
    
    rides
    |> Seq.groupBy rideDate
    |> Seq.map (fun (date, rides) ->
        let f, nf = partition rides
        in
        { Day = date
          ActiveTime = activityTimePerDay |> Map.find date
          FinishedRides = f |> Seq.sortBy _.Times.CreatedTimestamp
          NotHappenedRides = nf |> Seq.sortBy _.Created })

let activeTimeInDays days =
    days |> Seq.sumBy _.ActiveTime.TotalSeconds |> TimeSpan.FromSeconds
