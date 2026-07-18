module Bolt.Web.Pipeline

open System
open Bolt.ETL
open Bolt.Models.BoltApi
open Bolt.Scraper.ScrapePipeline
open Bolt.Web.Jobs
#if DEBUG
open Bolt.Infrastructure.Repository
open Bolt.Infrastrucutre.ces.OptionBuilder
#endif

let describeProgress (p: ScrapeProgress) =
    match p with
    | ScrapingProfile -> "profil kierowcy"
    | ScrapingActivityHours -> "godziny aktywności"
    | ScrapingOrderHistory -> "historia zleceń"
    | ScrapingOrderDetails(current, total) -> $"szczegóły zleceń {current}/{total}"

#if DEBUG
// Reading scraped data back from disk is a debugging convenience: production
// never persists user data, so there is nothing to load.
let freshnessWindow = TimeSpan.FromDays 14.0

let private loadCached (email: string) : ScrapedData option =
    if ScrapeMetadataRepository.isFresh DateTimeOffset.UtcNow freshnessWindow email then
        maybe {
            let! profile = DriverProfileRepository.getUnstructured email
            let! activity = ActivityHoursRepository.getUnstructured email
            let! history = OrderHistoryRepository.getUnstructured email
            let! previous = PreviousOrderRepository.get email
            let! details = PastOrderDetailRepository.get email
            return
                { Email = email
                  Profile = profile
                  ActivityHours = activity
                  OrderHistory = history
                  PreviousOrders = Array.ofSeq previous
                  PastOrderDetails = Array.ofSeq details }
        }
    else
        None
#endif

let realDeps: PipelineDeps<ScrapeSession, ScrapedData> = {
#if DEBUG
    LoadCached = loadCached
#else
    LoadCached = fun _ -> None
#endif
    CreateSession = ScrapeSession.create
    HasTokens = ScrapeSession.hasTokens
    RefreshTokens = ScrapeSession.refreshTokens
    RequestMagicLink = ScrapeSession.requestMagicLink
    AuthenticateWithUrl = ScrapeSession.authenticateWithUrl
    ScrapeRides = fun session progress ct -> scrapeRides session (describeProgress >> progress) ct
    // Adapter keeps ensureMeteoCoverage loosely coupled: it sees only the date
    // range, and the job runner never sees order types.
    EnsureMeteo = fun data ct ->
        let dates = data.PreviousOrders |> Array.map _.Created
        ensureMeteoCoverage (Array.min dates, Array.max dates) ct
    RunAnalysis = AnalysisPipeline.run
    RideCountOf = fun data -> data.PreviousOrders.Length
}
