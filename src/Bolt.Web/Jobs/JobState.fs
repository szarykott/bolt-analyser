module Bolt.Web.Jobs

open System
open System.Threading
open System.Threading.Tasks
open Bolt.ETL.Analysis

type JobState =
    | CheckingCache
    | Authenticating
    | AwaitingMagicLink of error: string option
    | ScrapingRides of detail: string
    | FetchingMeteo
    | RunningAnalysis
    | Done of AnalysisReport
    | Failed of step: string * message: string

type ClientMessage =
    | StartAnalysis of email: string
    | MagicLink of email: string * url: string

/// Everything the job runner needs, injected so the state machine is
/// testable without Bolt, open-meteo or the analytics service. 'data is the
/// scraped payload, opaque to the runner.
type PipelineDeps<'session, 'data> = {
    /// Some only in DEBUG builds when a fresh disk cache exists.
    LoadCached: string -> 'data option
    CreateSession: string -> 'session
    HasTokens: 'session -> bool
    RefreshTokens: 'session -> CancellationToken -> Task<Result<unit, string>>
    RequestMagicLink: 'session -> CancellationToken -> Task<Result<unit, string>>
    AuthenticateWithUrl: 'session -> string -> CancellationToken -> Task<Result<unit, string>>
    ScrapeRides: 'session -> (string -> Task) -> CancellationToken -> Task<Result<'data, string>>
    /// Returns the weather-coverage cap: rides created after it have no weather data.
    EnsureMeteo: 'data -> CancellationToken -> Task<Result<DateTimeOffset, string>>
    RunAnalysis: 'data -> DateTimeOffset -> Task<Result<AnalysisReport, string>>
    RideCountOf: 'data -> int
}
