module Bolt.Web.Jobs

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
/// testable without Bolt, open-meteo or the analytics service.
type PipelineDeps<'session> = {
    IsFresh: string -> bool
    CreateSession: string -> 'session
    HasTokens: 'session -> bool
    RefreshTokens: 'session -> CancellationToken -> Task<Result<unit, string>>
    RequestMagicLink: 'session -> CancellationToken -> Task<Result<unit, string>>
    AuthenticateWithUrl: 'session -> string -> CancellationToken -> Task<Result<unit, string>>
    ScrapeRides: 'session -> (string -> unit) -> CancellationToken -> Task<Result<unit, string>>
    EnsureMeteo: string -> CancellationToken -> Task<Result<unit, string>>
    RunAnalysis: string -> Task<Result<AnalysisReport, string>>
}
