namespace Bolt.ETL

module AnalysisPipeline =
    open System
    open System.Threading.Tasks
    open Bolt.ETL.Analysis
    open Bolt.Models
    open Bolt.Models.BoltApi

    /// Rides inside the weather-archive coverage (created at or before the cap).
    let weatherEligible (weatherCap: DateTimeOffset) (rides: FinishedRide[]) : FinishedRide[] =
        rides |> Array.filter (fun r -> r.Times.CreatedTimestamp <= weatherCap)

    let private emptySection id title description =
        { Id = id
          Title = title
          Description = description + " No rides fall within the weather data range."
          Charts = []
          Tables = [] }

    /// Runs every analysis over in-memory scraped data. Weather-dependent
    /// sections (PerRide1, PerRide2) see only rides covered by the weather
    /// archive; clustering and the report totals use the full set.
    let run (data: ScrapedData) (weatherCap: DateTimeOffset) : Task<Result<AnalysisReport, string>> =
        task {
            try
                if Array.isEmpty data.PreviousOrders then
                    return Error $"No rides found for {data.Email}"
                else
                    let dates = data.PreviousOrders |> Array.map _.Created
                    let allRides = RideFactory.getFinishedRides data.PreviousOrders data.PastOrderDetails
                    let weatherRides = weatherEligible weatherCap allRides

                    let! perRide1 =
                        if Array.isEmpty weatherRides then
                            Task.FromResult(emptySection "ride-stats" "Ride statistics"
                                "Ride counts and average earnings broken down by pickup district, time and weather.")
                        else
                            PerRide1.buildSection (PerRide1.prepareRideAnalysisSource weatherRides)

                    let! perRide2 =
                        if Array.isEmpty weatherRides then
                            Task.FromResult(emptySection "price-regression" "Price regression"
                                "OLS regression of ride price against distance, time and weather, with multicollinearity diagnostics.")
                        else
                            PerRide2.buildSection (PerRide2.prepareRideAnalysisSource weatherRides)

                    let! clustering =
                        RideClustering.buildSection (RideClustering.prepareRideAnalysisSource allRides)

                    return
                        Ok { Email = data.Email
                             GeneratedAt = DateTimeOffset.UtcNow
                             RideCount = data.PreviousOrders.Length
                             DateRange = (Array.min dates, Array.max dates)
                             Sections = [ perRide1; perRide2; clustering ] }
            with ex ->
                return Error $"Analysis failed: {ex.Message}"
        }
