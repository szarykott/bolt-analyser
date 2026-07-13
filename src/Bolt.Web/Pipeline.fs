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
    EnsureMeteo = ensureMeteoCoverage
    RunAnalysis = fun email -> Task.Run(fun () -> AnalysisPipeline.run email)
}
