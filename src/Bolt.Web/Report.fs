module Bolt.Web.Report

open System
open System.Threading.Tasks
open Bolt.ETL
open Bolt.ETL.Analysis
open Bolt.Models.BoltApi

type AnalysisReport = {
    Email: string
    GeneratedAt: DateTimeOffset
    RideCount: int
    SkippedOrders: SkippedOrder[]
    DateRange: DateTimeOffset * DateTimeOffset
    BasicStatistics: BasicStatistics.BasicStatistics
    PickupClusters: RideClustering.AnalysisResult
}

let run (data: ScrapedData) : Task<Result<AnalysisReport, string>> =
    task {
        try
            let allRides = RideFactory.getFinishedRides data.PreviousOrders data.PastOrderDetails
            if Array.isEmpty allRides then
                return Error $"Nie znaleziono zakończonych przejazdów dla {data.Email}"
            else
                let dates = allRides |> Array.map _.Times.CreatedTimestamp
                let! pickupClusters =
                    RideClustering.run (RideClustering.prepareRideAnalysisSource allRides)

                return
                    Ok { Email = data.Email
                         GeneratedAt = DateTimeOffset.UtcNow
                         RideCount = allRides.Length
                         SkippedOrders = data.SkippedOrders
                         DateRange = Array.min dates, Array.max dates
                         BasicStatistics = BasicStatistics.generate allRides
                         PickupClusters = pickupClusters }
        with ex ->
            return Error $"Analiza nie powiodła się: {ex.Message}"
    }
