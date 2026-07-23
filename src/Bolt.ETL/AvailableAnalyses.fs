namespace Bolt.ETL

open System.Threading.Tasks
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics
open Bolt.Models

module AvailableAnalyses =
    let perRide (rides: FinishedRide array) : Task<OlsResponse> =
        PerRide.prepareRideAnalysisSource rides |> PerRide.runOls

