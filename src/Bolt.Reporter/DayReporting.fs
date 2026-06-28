module Bolt.Reporter.DayReporting

open System
open Bolt.Reporter.RideReportingSource
open Bolt.Models.ActivityHours
open Bolt.Infrastrucutre.Logging

type DayReportingSource =
    { Day: DateOnly
      ActiveTime: TimeSpan option
      FinishedRides: FinishedRide seq
      NotHappenedRides: RideThatDidNotHappen seq }
    interface IReportingSource with        
        member this.Label = this.Day.ToShortDateString()
        member this.Active = this.ActiveTime
        member this.FinishedRides = this.FinishedRides
        member this.NotHappenedRides = this.NotHappenedRides 


let daysFromRideReportingSources (rides: RideReportingSource seq) (activity: ActivityHours) =
    let rideDate ride =
        match ride.Data with
        | RideType.Finished d -> d.Times.CreatedTimestamp.Value.DateTime |> DateOnly.FromDateTime
        | RideType.DidNotHappen d -> d.Created.Value.Date |> DateOnly.FromDateTime

    let activityTimePerDay =
        activity.Periods
        |> List.collect _.Items
        |> List.map (fun d -> d.Date, int64 d.ActiveSeconds |> TimeSpan.FromSeconds)
        |> Map.ofList
    
    let partition (rides' : RideReportingSource seq) =
        rides' |> Seq.fold (fun (f, nf) r ->
                      match r.Data with
                      | RideType.Finished x -> (x :: f, nf) 
                      | RideType.DidNotHappen x -> (f, x :: nf) )
                      ([], [])
    
    rides
    |> Seq.groupBy rideDate
    |> Seq.rev
    |> Seq.map (fun (date, rides) ->
        let f, nf = partition rides
        in
        { Day = date
          ActiveTime = activityTimePerDay |> Map.tryFind date
          FinishedRides = f |> Seq.sortBy _.Times.CreatedTimestamp
          NotHappenedRides = nf |> Seq.sortBy _.Created })

let activeTimeInDays (days: DayReportingSource seq) : TimeSpan option=
    days 
    |> Seq.map _.ActiveTime
    |> Seq.fold (Option.map2 (+)) (Some TimeSpan.Zero)
