module Bolt.Web.JobRunner

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Bolt.Web.Jobs

/// One analysis job, bound to one socket. The first client message must be an
/// e-mail (StartAnalysis); magic-link URLs arrive on `magicLinks` afterwards
/// and each is simply tried. Every state change goes out through `notify`.
/// Cancellation (socket gone) stops the job without a terminal notification.
let run
    (deps: PipelineDeps<'session, 'data>)
    (email: string)
    (magicLinks: ChannelReader<string>)
    (notify: JobState -> Task)
    (ct: CancellationToken)
    : Task =
    task {
        try
            do! notify CheckingCache
            let mutable failure: (string * string) option = None
            let mutable data: 'data option = None

            match deps.LoadCached email with
            | Some cached -> data <- Some cached
            | None ->
                let session = deps.CreateSession email
                do! notify Authenticating

                let mutable authenticated = false

                if deps.HasTokens session then
                    match! deps.RefreshTokens session ct with
                    | Ok() -> authenticated <- true
                    | Error _ -> () // stale cache; fall through to magic link

                if not authenticated then
                    match! deps.RequestMagicLink session ct with
                    | Error e -> failure <- Some("wysyłanie linku do logowania", e)
                    | Ok() ->
                        do! notify (AwaitingMagicLink None)

                        while not authenticated do
                            let! url = magicLinks.ReadAsync ct
                            match! deps.AuthenticateWithUrl session url ct with
                            | Ok() -> authenticated <- true
                            | Error e -> do! notify (AwaitingMagicLink(Some e))

                if failure.IsNone then
                    do! notify (ScrapingRides "")
                    let progress detail = notify (ScrapingRides detail)

                    match! deps.ScrapeRides session progress ct with
                    | Error e -> failure <- Some("pobieranie przejazdów", e)
                    | Ok d -> data <- Some d

            match failure, data with
            | Some(step, e), _ -> do! notify (Failed(step, e))
            | None, Some d when deps.RideCountOf d = 0 ->
                do! notify (Failed("pobieranie przejazdów", "Nie znaleziono przejazdów dla tego konta"))
            | None, Some d ->
                do! notify RunningAnalysis

                match! deps.RunAnalysis d with
                | Ok report -> do! notify (Done report)
                | Error e -> do! notify (Failed("analiza", e))
            | None, None -> () // request-magic-link failed; already reported above
        with
        | :? OperationCanceledException -> ()
        | ex -> do! notify (Failed("błąd wewnętrzny", ex.Message))
    }
