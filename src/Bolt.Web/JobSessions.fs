module Bolt.Web.JobSessions

open System
open System.Collections.Concurrent
open System.Security.Cryptography
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Bolt.Web.Jobs

let cookieName = "bolt-analysis-session"

type private Attachment = {
    Id: Guid
    Outbox: Channel<string>
    Abort: unit -> unit
}

type private Session = {
    Gate: obj
    mutable Attachment: Attachment option
    mutable LastHtml: string option
    mutable Email: string option
    mutable MagicLinks: Channel<string> option
    mutable Cancellation: CancellationTokenSource option
    mutable Running: bool
    mutable AwaitingLink: bool
    mutable RunId: Guid
    mutable Expiry: CancellationTokenSource option
    mutable Expired: bool
}

let private stateFragment email state =
    match state with
    | CheckingCache -> Views.Processing.progressFragment "Sprawdzanie zapisanych danych…" ""
    | Authenticating -> Views.Processing.progressFragment "Logowanie…" ""
    | AwaitingMagicLink error -> Views.Input.magicLinkFragment email error
    | ScrapingRides detail -> Views.Processing.progressFragment "Pobieranie przejazdów…" detail
    | RunningAnalysis -> Views.Processing.progressFragment "Trwa analiza…" ""
    | Done report -> Views.Report.reportFragment report
    | Failed(step, message) -> Views.Processing.errorFragment email step message

type JobRegistry(gracePeriod: TimeSpan, logger: ILogger<JobRegistry>) =
    let sessions = ConcurrentDictionary<string, Session>()
    let activeEmails = ConcurrentDictionary<string, byte>()

    let publish (session: Session) html =
        session.LastHtml <- Some html
        session.Attachment
        |> Option.iter (fun attachment -> attachment.Outbox.Writer.TryWrite html |> ignore)

    let scheduleExpiry (id: string) (session: Session) =
        let expiry = new CancellationTokenSource()
        session.Expiry <- Some expiry

        task {
            try
                do! Task.Delay(gracePeriod, expiry.Token)

                lock session.Gate (fun () ->
                    if session.Attachment.IsNone && session.Expiry = Some expiry then
                        session.Expiry <- None
                        session.Expired <- true
                        let mutable removed = Unchecked.defaultof<Session>
                        sessions.TryRemove(id, &removed) |> ignore
                        session.Cancellation |> Option.iter _.Cancel()
                        if session.Running then
                            session.Email |> Option.iter (fun email -> activeEmails.TryRemove email |> ignore)
                        session.Running <- false
                        session.AwaitingLink <- false
                        session.MagicLinks <- None
                        logger.LogInformation("Analysis session {SessionId} expired", id[..7]))
            with :? OperationCanceledException ->
                ()
        }
        |> ignore

    member _.GetOrCreate(existing: string option) =
        match existing with
        | Some id when sessions.ContainsKey id -> id, false
        | _ ->
            let rec create () =
                let id = RandomNumberGenerator.GetBytes(32) |> Convert.ToHexString
                let session =
                    { Gate = obj()
                      Attachment = None
                      LastHtml = None
                      Email = None
                      MagicLinks = None
                      Cancellation = None
                      Running = false
                      AwaitingLink = false
                      RunId = Guid.Empty
                      Expiry = None
                      Expired = false }

                if sessions.TryAdd(id, session) then
                    lock session.Gate (fun () -> scheduleExpiry id session)
                    id, true
                else
                    create ()
            create ()

    member _.Attach(id: string, connectionId: Guid, abort: unit -> unit) =
        match sessions.TryGetValue id with
        | false, _ -> None
        | true, session ->
            let outbox =
                Channel.CreateBounded<string>(BoundedChannelOptions(1, FullMode = BoundedChannelFullMode.DropOldest))
            let attached =
                lock session.Gate (fun () ->
                    if session.Expired then
                        None
                    else
                        session.Expiry |> Option.iter _.Cancel()
                        session.Expiry <- None
                        let previous = session.Attachment
                        session.Attachment <- Some { Id = connectionId; Outbox = outbox; Abort = abort }
                        session.LastHtml
                        |> Option.iter (fun html -> outbox.Writer.TryWrite html |> ignore)
                        Some previous)

            match attached with
            | None -> None
            | Some previous ->
                previous |> Option.iter (fun attachment ->
                    attachment.Outbox.Writer.TryComplete() |> ignore
                    attachment.Abort())
                logger.LogInformation("Analysis session {SessionId} connected", id[..7])
                Some outbox.Reader

    member _.Detach(id: string, connectionId: Guid) =
        match sessions.TryGetValue id with
        | false, _ -> ()
        | true, session ->
            lock session.Gate (fun () ->
                match session.Attachment with
                | Some attachment when attachment.Id = connectionId ->
                    attachment.Outbox.Writer.TryComplete() |> ignore
                    session.Attachment <- None
                    scheduleExpiry id session
                    logger.LogInformation("Analysis session {SessionId} disconnected", id[..7])
                | _ -> ())

    member _.Start<'session, 'data>
        (id: string, connectionId: Guid, email: string, deps: PipelineDeps<'session, 'data>) =
        match sessions.TryGetValue id with
        | false, _ -> ()
        | true, session ->
            let start =
                lock session.Gate (fun () ->
                    match session.Attachment with
                    | Some attachment when attachment.Id = connectionId ->
                        if session.Running then
                            None
                        elif not (activeEmails.TryAdd(email, 0uy)) then
                            publish session (Views.Processing.errorFragment email "uruchamianie"
                                "Analiza dla tego adresu e-mail już trwa. Spróbuj ponownie później.")
                            None
                        else
                            let cts = new CancellationTokenSource()
                            let links = Channel.CreateUnbounded<string>()
                            let runId = Guid.NewGuid()
                            session.Email <- Some email
                            session.MagicLinks <- Some links
                            session.Cancellation <- Some cts
                            session.Running <- true
                            session.AwaitingLink <- false
                            session.RunId <- runId
                            Some(links, cts, runId)
                    | _ -> None)

            match start with
            | None -> ()
            | Some(links, cts, runId) ->
                logger.LogInformation("Analysis session {SessionId} started", id[..7])
                let notify state =
                    let html = stateFragment email state
                    lock session.Gate (fun () ->
                        if session.RunId = runId && not session.Expired then
                            publish session html
                            session.AwaitingLink <-
                                match state with
                                | AwaitingMagicLink _ -> true
                                | _ -> false
                            match state with
                            | Done _
                            | Failed _ ->
                                session.Running <- false
                                session.MagicLinks <- None
                                activeEmails.TryRemove email |> ignore
                            | _ -> ())
                    Task.CompletedTask

                JobRunner.run deps email links.Reader notify cts.Token |> ignore

    member _.SubmitMagicLink(id: string, connectionId: Guid, email: string, url: string) =
        match sessions.TryGetValue id with
        | false, _ -> ()
        | true, session ->
            lock session.Gate (fun () ->
                match session.Attachment with
                | Some attachment when attachment.Id = connectionId ->
                    match session.MagicLinks, session.Email with
                    | Some links, Some expected when session.AwaitingLink && expected = email ->
                        links.Writer.TryWrite url |> ignore
                    | Some _, Some expected when session.AwaitingLink ->
                        publish session (Views.Input.magicLinkFragment expected
                            (Some "Adres e-mail nie pasuje do rozpoczętej analizy."))
                    | _, _ when session.Running -> ()
                    | _ ->
                        publish session (Views.Processing.errorFragment email "logowanie"
                            "Najpierw rozpocznij analizę, podając adres e-mail.")
                | _ -> ())
