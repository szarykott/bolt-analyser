namespace Bolt.ETL

module AnalysisPipeline =
    open System
    open System.Threading.Tasks
    open Bolt.ETL.Analysis
    open Bolt.Infrastructure.Repository

    /// Runs every analysis for the given account.
    let run (email: string) : Task<Result<AnalysisReport, string>> =
        task {
            try
                match PreviousOrderRepository.get email with
                | None -> return Error $"No ride data found for {email}"
                | Some orders when Seq.isEmpty orders -> return Error $"No rides found for {email}"
                | Some orders ->
                    let dates = orders |> Seq.map _.Created |> Array.ofSeq

                    let! perRide1 = PerRide1.buildSection (PerRide1.prepareRideAnalysisSource email)
                    let! perRide2 = PerRide2.buildSection (PerRide2.prepareRideAnalysisSource email)
                    let! clustering = RideClustering.buildSection (RideClustering.prepareRideAnalysisSource email)

                    return
                        Ok { Email = email
                             GeneratedAt = DateTimeOffset.UtcNow
                             RideCount = dates.Length
                             DateRange = (Array.min dates, Array.max dates)
                             Sections = [ perRide1; perRide2; clustering ] }
            with ex ->
                return Error $"Analysis failed: {ex.Message}"
        }
