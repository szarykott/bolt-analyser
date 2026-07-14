module Bolt.Web.JobRunner

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Bolt.Web.Jobs

/// One analysis job, bound to one socket. Magic-link URLs arrive on
/// `magicLinks` whenever the client sends one (stateless: each is simply
/// tried). Every state change goes out through `notify`. Cancellation
/// (socket gone) stops the job without a terminal notification.
let run
    (deps: PipelineDeps<'session>)
    (email: string)
    (initialMagicLink: string option)
    (magicLinks: ChannelReader<string>)
    (notify: JobState -> Task)
    (ct: CancellationToken)
    : Task =
    task {
        try
            do! notify CheckingCache
            let mutable failure: (string * string) option = None

            if not (deps.IsFresh email) then
                let session = deps.CreateSession email
                do! notify Authenticating

                let mutable authenticated = false

                // FIXME: Remove possibility to log in with magic link as first message, only accept email address in first message
                match initialMagicLink with
                | Some url ->
                    match! deps.AuthenticateWithUrl session url ct with
                    | Ok() -> authenticated <- true
                    | Error e -> do! notify (AwaitingMagicLink(Some e))
                | None -> ()

                if not authenticated && deps.HasTokens session then
                    match! deps.RefreshTokens session ct with
                    | Ok() -> authenticated <- true
                    | Error _ -> () // stale cache; fall through to magic link

                if not authenticated then
                    match! deps.RequestMagicLink session ct with
                    | Error e -> failure <- Some("requesting magic link", e)
                    | Ok() ->
                        do! notify (AwaitingMagicLink None)

                        while not authenticated do
                            let! url = magicLinks.ReadAsync ct
                            match! deps.AuthenticateWithUrl session url ct with
                            | Ok() -> authenticated <- true
                            | Error e -> do! notify (AwaitingMagicLink(Some e))

                if failure.IsNone then
                    do! notify (ScrapingRides "")
                    // FIXME: use proper async semantics, no GetAwaiter.Getresult
                    let progress detail = (notify (ScrapingRides detail)).GetAwaiter().GetResult()

                    match! deps.ScrapeRides session progress ct with
                    | Error e -> failure <- Some("scraping rides", e)
                    | Ok() ->
                        do! notify FetchingMeteo

                        match! deps.EnsureMeteo email ct with
                        | Error e -> failure <- Some("fetching weather data", e)
                        | Ok() -> ()

            match failure with
            | Some(step, e) -> do! notify (Failed(step, e))
            | None ->
                do! notify RunningAnalysis

                match! deps.RunAnalysis email with
                | Ok report -> do! notify (Done report)
                | Error e -> do! notify (Failed("analysis", e))
        with
        | :? OperationCanceledException -> ()
        | ex -> do! notify (Failed("internal", ex.Message))
    }
