module Bolt.Reporter.HourByHourReporting

open System
open Bolt.Reporter.RideReportingSource

type HourByHourReportingSource =
    { Hour: int
      FinishedRides: FinishedRide seq
      NotHappenedRides: RideThatDidNotHappen seq }
    interface IReportingSource with        
        member this.Label = this.Hour.ToString()
        member _.Active = None
        member this.FinishedRides = this.FinishedRides
        member this.NotHappenedRides = this.NotHappenedRides 


let private localTime (t: DateTimeOffset) =
    let off = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw").GetUtcOffset(t)
    t.ToOffset(off)

let private localHour (t: DateTimeOffset) = (localTime t).Hour

let hoursFromRideReportingSources (rides: RideReportingSource seq) =
    let rideHour ride =
        match ride.Data with
        | RideType.Finished d -> d.Times.CreatedTimestamp.Value |> localHour
        | RideType.DidNotHappen d -> d.Created.Value |> localHour

    let partition (rides' : RideReportingSource seq) =
        rides' |> Seq.fold (fun (f, nf) r ->
                      match r.Data with
                      | RideType.Finished x -> (x :: f, nf) 
                      | RideType.DidNotHappen x -> (f, x :: nf) )
                      ([], [])
    
    rides
    |> Seq.groupBy rideHour
    |> Seq.rev
    |> Seq.map (fun (date, rides) ->
        let f, nf = partition rides
        in
        { Hour = date
          FinishedRides = f |> Seq.sortBy _.Times.CreatedTimestamp
          NotHappenedRides = nf |> Seq.sortBy _.Created })
    |> Seq.sortBy (fun i -> i.Hour)
