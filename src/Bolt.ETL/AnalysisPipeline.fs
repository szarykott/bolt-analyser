namespace Bolt.ETL

module AnalysisPipeline =
    open System
    open Bolt.ETL.Analysis
    open Bolt.Infrastructure.Repository

    /// Runs every analysis for the given account. Synchronous and CPU/IO
    /// bound (analytics service calls are blocking); wrap in Task.Run.
    let run (email: string) : Result<AnalysisReport, string> =
        try
            match PreviousOrderRepository.get email with
            | None -> Error $"No ride data found for {email}"
            | Some orders when Seq.isEmpty orders -> Error $"No rides found for {email}"
            | Some orders ->
                let dates = orders |> Seq.map _.Created |> Array.ofSeq

                let sections =
                    [ PerRide1.buildSection (PerRide1.prepareRideAnalysisSource email)
                      PerRide2.buildSection (PerRide2.prepareRideAnalysisSource email)
                      RideClustering.buildSection (RideClustering.prepareRideAnalysisSource email) ]

                Ok { Email = email
                     GeneratedAt = DateTimeOffset.UtcNow
                     RideCount = dates.Length
                     DateRange = (Array.min dates, Array.max dates)
                     Sections = sections }
        with ex ->
            Error $"Analysis failed: {ex.Message}"
