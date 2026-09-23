module Bolt.Web.Jobs

open System
open System.Threading
open System.Threading.Tasks
open Bolt.Web.Report

type JobState =
    | CheckingCache
    | Authenticating
    | AwaitingMagicLink of error: string option
    | ScrapingRides of detail: string
    | RunningAnalysis
    | Done of AnalysisReport
    | Failed of step: string * message: string

type ClientMessage =
    | StartAnalysis of email: string
    | MagicLink of email: string * url: string

/// Everything the job runner needs, injected so the state machine is
/// testable without Bolt or the analytics service. 'data is the
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
    RunAnalysis: 'data -> Task<Result<AnalysisReport, string>>
    RideCountOf: 'data -> int
}
