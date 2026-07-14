module Bolt.Web.Pipeline

open System
open System.Threading.Tasks
open Bolt.ETL
open Bolt.Infrastructure.Repository
open Bolt.Scraper.ScrapePipeline
open Bolt.Web.Jobs

let describeProgress (p: ScrapeProgress) =
    match p with
    | ScrapingProfile -> "driver profile"
    | ScrapingActivityHours -> "activity hours"
    | ScrapingOrderHistory -> "order history"
    | ScrapingOrderDetails(current, total) -> $"order details {current}/{total}"

let freshnessWindow = TimeSpan.FromDays 14.0

let realDeps: PipelineDeps<ScrapeSession> = {
    IsFresh = fun email -> ScrapeMetadataRepository.isFresh DateTimeOffset.UtcNow freshnessWindow email
    CreateSession = ScrapeSession.create
    HasTokens = ScrapeSession.hasTokens
    RefreshTokens = ScrapeSession.refreshTokens
    RequestMagicLink = ScrapeSession.requestMagicLink
    AuthenticateWithUrl = ScrapeSession.authenticateWithUrl
    ScrapeRides = fun session progress ct -> scrapeRides session (describeProgress >> progress) ct
    // Temporary bridge until PipelineDeps carries ScrapedData (next task):
    // reads the repository to derive the ride date range, discards the cap.
    EnsureMeteo = fun email ct ->
        task {
            match PreviousOrderRepository.get email with
            | None -> return Error "No scraped ride data found; cannot determine weather range"
            | Some orders when Seq.isEmpty orders -> return Error "No rides found for this account"
            | Some orders ->
                let dates = orders |> Seq.map _.Created
                let! result = ensureMeteoCoverage (Seq.min dates, Seq.max dates) ct
                return result |> Result.map ignore
        }
    RunAnalysis = AnalysisPipeline.run
}
