module Bolt.Web.Report

open System
open System.Threading.Tasks
open Bolt.ETL
open Bolt.ETL.Analysis
open Bolt.Infrastructure.Repository
open Bolt.Models
open Bolt.Models.BoltApi

type AnalysisReport = {
    Email: string
    GeneratedAt: DateTimeOffset
    RideCount: int
    SkippedOrders: SkippedOrder[]
    DateRange: DateTimeOffset * DateTimeOffset
    PriceRegression: PerRide2.AnalysisResult option
    HourlyEarnings: PerHour.AnalysisResult option
    PickupClusters: RideClustering.AnalysisResult
}

let weatherEligible (weatherCap: DateTimeOffset) (rides: FinishedRide[]) : FinishedRide[] =
    rides |> Array.filter (fun r -> r.Times.CreatedTimestamp <= weatherCap)

let run (data: ScrapedData) (weatherCap: DateTimeOffset) : Task<Result<AnalysisReport, string>> =
    task {
        try
            if Array.isEmpty data.PreviousOrders then
                return Error $"Nie znaleziono przejazdów dla {data.Email}"
            else
                let dates = data.PreviousOrders |> Array.map _.Created
                let allRides = RideFactory.getFinishedRides data.PreviousOrders data.PastOrderDetails
                let weatherRides = weatherEligible weatherCap allRides

                let weatherInputs =
                    if Array.isEmpty weatherRides then None
                    else Some((MeteoRepository.get ()).Value, (DistrictsRepository.get ()).Value)

                let! priceRegression =
                    match weatherInputs with
                    | None -> Task.FromResult None
                    | Some(meteo, districts) ->
                        task {
                            let! result = PerRide2.run (PerRide2.prepareRideAnalysisSource meteo districts weatherRides)
                            return Some result
                        }

                let! hourlyEarnings =
                    match weatherInputs with
                    | None -> Task.FromResult None
                    | Some(meteo, districts) ->
                        task {
                            let! result = PerHour.run (PerHour.prepareHourlySource meteo districts weatherRides)
                            return Some result
                        }

                let! pickupClusters =
                    RideClustering.run (RideClustering.prepareRideAnalysisSource allRides)

                return
                    Ok { Email = data.Email
                         GeneratedAt = DateTimeOffset.UtcNow
                         RideCount = data.PreviousOrders.Length
                         SkippedOrders = data.SkippedOrders
                         DateRange = Array.min dates, Array.max dates
                         PriceRegression = priceRegression
                         HourlyEarnings = hourlyEarnings
                         PickupClusters = pickupClusters }
        with ex ->
            return Error $"Analiza nie powiodła się: {ex.Message}"
    }
