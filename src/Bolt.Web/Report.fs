module Bolt.Web.Report

open System
open System.Threading.Tasks
open Bolt.ETL
open Bolt.ETL.Analysis
open Bolt.Models.BoltApi
open Bolt.Models

type AnalysisReport = {
    Email: string
    GeneratedAt: DateTimeOffset
    RideCount: int
    SkippedOrders: SkippedOrder[]
    NotFinishedRideCount: int
    RideErrors: RideError[]
    DateRange: DateTimeOffset * DateTimeOffset
    BasicStatistics: BasicStatistics.BasicStatistics
    PickupClusters: RideClustering.AnalysisResult
}

type AnalysisFailure = {
    Message: string
    RideErrors: RideError[]
}

type PreparedRides = {
    FinishedRides: FinishedRide[]
    NotFinishedRideCount: int
    RideErrors: RideError[]
}

let internal prepare (data: ScrapedData) : PreparedRides =
    let rides = data.Orders |> Array.map RideFactory.parseOrder
    { FinishedRides = rides |> Array.choose (fun ride -> match ride.Data with Finished r -> Some r | _ -> None)
      NotFinishedRideCount = rides |> Array.sumBy (fun ride -> match ride.Data with DidNotHappen _ -> 1 | _ -> 0)
      RideErrors =
        rides
        |> Array.choose (fun ride -> match ride.Data with RideType.Error error -> Some error | _ -> None)
        |> Array.append data.Diagnostics }

let internal runWithClustering cluster (data: ScrapedData) : Task<Result<AnalysisReport, AnalysisFailure>> =
    task {
        let mutable errors = [||]
        try
            let prepared = prepare data
            errors <- prepared.RideErrors
            let allRides = prepared.FinishedRides
            if Array.isEmpty allRides then
                let message =
                    if Array.isEmpty errors then $"Nie znaleziono zakończonych przejazdów dla {data.Email}"
                    else $"Nie udało się przeanalizować żadnego zakończonego kursu. Liczba błędów danych: {errors.Length}."
                return Result.Error { Message = message; RideErrors = errors }
            else
                let dates = allRides |> Array.map _.Times.CreatedTimestamp
                let! pickupClusters =
                    cluster (RideClustering.prepareRideAnalysisSource allRides)

                return
                    Ok { Email = data.Email
                         GeneratedAt = DateTimeOffset.UtcNow
                         RideCount = allRides.Length
                         SkippedOrders =
                            data.SkippedOrders
                            |> Array.filter (fun skipped ->
                                not (data.Diagnostics |> Array.exists (fun error -> error.Stage = "http_response" && error.OrderId = Some skipped.OrderId)))
                         NotFinishedRideCount = prepared.NotFinishedRideCount
                         RideErrors = errors
                         DateRange = Array.min dates, Array.max dates
                         BasicStatistics = BasicStatistics.generate allRides
                         PickupClusters = pickupClusters }
        with ex ->
            return Result.Error { Message = $"Analiza nie powiodła się: {ex.Message}"; RideErrors = errors }
    }

let run data = runWithClustering RideClustering.run data
