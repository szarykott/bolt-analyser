module Bolt.Web.WebSockets

open System
open System.Collections.Concurrent
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Bolt.Web.Jobs

// Emails with a running job, across all sockets. Value is unused.
let private activeEmails = ConcurrentDictionary<string, byte>()

let private parseMessage (json: string) : ClientMessage option =
    try
        use doc = JsonDocument.Parse json
        let root = doc.RootElement

        let get (name: string) =
            match root.TryGetProperty name with
            | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
            | _ -> None

        match get "msgType", get "email", get "url" with
        | Some "start-analysis", Some email, _ when email <> "" ->
            Some(StartAnalysis email)
        | Some "magic-link", Some email, Some url when email <> "" && url <> "" ->
            Some(MagicLink(email, url))
        | _ -> None
    with _ ->
        None

let private stateFragment (email: string) (state: JobState) =
    match state with
    | CheckingCache -> Views.progressFragment "Sprawdzanie zapisanych danych…" ""
    | Authenticating -> Views.progressFragment "Logowanie…" ""
    | AwaitingMagicLink error -> Views.magicLinkFragment email error
    | ScrapingRides detail -> Views.progressFragment "Pobieranie przejazdów…" detail
    | FetchingMeteo -> Views.progressFragment "Pobieranie danych pogodowych…" ""
    | RunningAnalysis -> Views.progressFragment "Trwa analiza…" ""
    | Done report -> Views.reportFragment report
    | Failed(step, message) -> Views.errorFragment email step message

/// WebSocket handshakes bypass the same-origin policy, so the Origin header
/// must be checked explicitly (Cross-Site WebSocket Hijacking).
let originAllowed (ctx: HttpContext) =
    match ctx.Request.Headers.Origin |> Seq.tryHead with
    | None -> false
    | Some origin ->
        match Uri.TryCreate(origin, UriKind.Absolute) with
        | true, uri -> String.Equals(uri.Host, ctx.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
        | _ -> false

let handle (deps: PipelineDeps<'session, 'data>) (ctx: HttpContext) : Task =
    task {
        if not (originAllowed ctx) then
            ctx.Response.StatusCode <- StatusCodes.Status403Forbidden
        elif not ctx.WebSockets.IsWebSocketRequest then
            ctx.Response.StatusCode <- StatusCodes.Status400BadRequest
        else
            use! socket = ctx.WebSockets.AcceptWebSocketAsync()
            use sendLock = new SemaphoreSlim(1, 1)

            let send (fragment: string) : Task =
                task {
                    do! sendLock.WaitAsync()
                    try
                        if socket.State = WebSocketState.Open then
                            let bytes = Encoding.UTF8.GetBytes fragment
                            do! socket.SendAsync(ArraySegment bytes, WebSocketMessageType.Text, true, CancellationToken.None)
                    finally
                        sendLock.Release() |> ignore
                }

            // At most one job per socket.
            let mutable jobEmail: string option = None
            let mutable jobCts: CancellationTokenSource option = None
            let mutable magicLinks: Channel<string> option = None

            let releaseJob () =
                jobEmail |> Option.iter (fun e -> activeEmails.TryRemove e |> ignore)
                jobEmail <- None

            let startJob (email: string) =
                task {
                    if not (activeEmails.TryAdd(email, 0uy)) then
                        do! send (Views.errorFragment email "uruchamianie"
                                      "Analiza dla tego adresu e-mail już trwa. Spróbuj ponownie później.")
                    else
                        let cts = new CancellationTokenSource()
                        let channel = Channel.CreateUnbounded<string>()
                        jobEmail <- Some email
                        jobCts <- Some cts
                        magicLinks <- Some channel

                        let notify state =
                            task {
                                do! send (stateFragment email state)

                                match state with
                                | Done _
                                | Failed _ -> releaseJob ()
                                | _ -> ()
                            }
                            :> Task

                        // Fire and forget: the job talks back through `notify`,
                        // the receive loop below keeps handling client messages.
                        JobRunner.run deps email channel.Reader notify cts.Token
                        |> ignore
                }

            let buffer = Array.zeroCreate 65536

            try
                try
                    let mutable closed = false

                    while not closed do
                        let sb = StringBuilder()
                        let mutable finished = false

                        while not finished do
                            let! result = socket.ReceiveAsync(ArraySegment buffer, CancellationToken.None)

                            if result.MessageType = WebSocketMessageType.Close then
                                finished <- true
                                closed <- true
                            else
                                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count)) |> ignore
                                finished <- result.EndOfMessage

                        if not closed then
                            match parseMessage (sb.ToString()) with
                            | Some(StartAnalysis email) when jobEmail.IsNone ->
                                do! startJob email
                            | Some(StartAnalysis _) ->
                                () // this socket already runs a job; ignore
                            | Some(MagicLink(email, url)) ->
                                match magicLinks, jobEmail with
                                | Some channel, Some _ ->
                                    channel.Writer.TryWrite url |> ignore
                                | _ ->
                                    // A job must be started with an e-mail first;
                                    // a magic link can never be the first message.
                                    do! send (Views.errorFragment email "logowanie"
                                                  "Najpierw rozpocznij analizę, podając adres e-mail.")
                            | None -> ()
                with
                | :? WebSocketException -> () // abrupt client disconnect
                | :? OperationCanceledException -> ()
            finally
                jobCts |> Option.iter _.Cancel()
                releaseJob ()

            if socket.State = WebSocketState.Open || socket.State = WebSocketState.CloseReceived then
                try
                    do! socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None)
                with _ ->
                    ()
    }
