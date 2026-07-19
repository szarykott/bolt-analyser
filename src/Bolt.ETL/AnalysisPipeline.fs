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
          Description = description + " Żaden przejazd nie mieści się w zakresie danych pogodowych."
          Charts = []
          Tables = [] }

    /// Runs the analyses over in-memory scraped data. The weather-dependent
    /// regression (PerRide2) sees only rides covered by the weather archive;
    /// clustering and the report totals use the full set.
    let run (data: ScrapedData) (weatherCap: DateTimeOffset) : Task<Result<AnalysisReport, string>> =
        task {
            try
                if Array.isEmpty data.PreviousOrders then
                    return Error $"Nie znaleziono przejazdów dla {data.Email}"
                else
                    let dates = data.PreviousOrders |> Array.map _.Created
                    let allRides = RideFactory.getFinishedRides data.PreviousOrders data.PastOrderDetails
                    let weatherRides = weatherEligible weatherCap allRides

                    let! perRide2 =
                        if Array.isEmpty weatherRides then
                            Task.FromResult(emptySection "price-regression" "Regresja ceny przejazdu"
                                "Regresja liniowa (OLS) ceny przejazdu względem dystansu, pory dnia i pogody.")
                        else
                            PerRide2.buildSection (PerRide2.prepareRideAnalysisSource weatherRides)

                    let! perHour =
                        if Array.isEmpty weatherRides then
                            Task.FromResult(emptySection "per-hour-earnings" "Zarobki na godzinę pracy"
                                "Analiza zarobków na godzinę pracy względem pory, pogody i dzielnicy.")
                        else
                            PerHour.buildSection (PerHour.prepareHourlySource weatherRides)

                    let! clustering =
                        RideClustering.buildSection (RideClustering.prepareRideAnalysisSource allRides)

                    return
                        Ok { Email = data.Email
                             GeneratedAt = DateTimeOffset.UtcNow
                             RideCount = data.PreviousOrders.Length
                             DateRange = (Array.min dates, Array.max dates)
                             Sections = [ perRide2; perHour; clustering ] }
            with ex ->
                return Error $"Analiza nie powiodła się: {ex.Message}"
        }
