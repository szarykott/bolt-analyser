module Bolt.Web.WebSockets

open System
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Bolt.Web.Jobs
open Bolt.Web.JobSessions

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

/// WebSocket handshakes bypass the same-origin policy, so the Origin header
/// must be checked explicitly (Cross-Site WebSocket Hijacking).
let originAllowed (ctx: HttpContext) =
    match ctx.Request.Headers.Origin |> Seq.tryHead with
    | None -> false
    | Some origin ->
        match Uri.TryCreate(origin, UriKind.Absolute) with
        | true, uri -> String.Equals(uri.Host, ctx.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
        | _ -> false

let handle (deps: PipelineDeps<'session, 'data>) (registry: JobRegistry) (ctx: HttpContext) : Task =
    task {
        if not (originAllowed ctx) then
            ctx.Response.StatusCode <- StatusCodes.Status403Forbidden
        elif not ctx.WebSockets.IsWebSocketRequest then
            ctx.Response.StatusCode <- StatusCodes.Status400BadRequest
        else
            let sessionId =
                match ctx.Request.Cookies.TryGetValue cookieName with
                | true, value -> Some value
                | _ -> None

            use! socket = ctx.WebSockets.AcceptWebSocketAsync()
            let connectionId = Guid.NewGuid()
            let outbox = sessionId |> Option.bind (fun id -> registry.Attach(id, connectionId, ctx.Abort))

            let send (fragment: string) =
                let bytes = Encoding.UTF8.GetBytes fragment
                socket.SendAsync(ArraySegment bytes, WebSocketMessageType.Text, true, CancellationToken.None)

            let forward (reader: ChannelReader<string>) =
                task {
                    try
                        let mutable reading = true
                        while reading do
                            let! available = reader.WaitToReadAsync()
                            if not available then
                                reading <- false
                            else
                                let mutable fragment = Unchecked.defaultof<string>
                                while reader.TryRead(&fragment) do
                                    if socket.State = WebSocketState.Open then
                                        do! send fragment
                    with
                    | :? WebSocketException
                    | :? IO.IOException
                    | :? OperationCanceledException -> ctx.Abort()
                }

            let outgoing: Task =
                match outbox with
                | Some reader -> forward reader :> Task
                | None -> send (Views.Input.expiredFragment ())

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
                            | Some(StartAnalysis email) ->
                                sessionId |> Option.iter (fun id -> registry.Start(id, connectionId, email, deps))
                            | Some(MagicLink(email, url)) ->
                                sessionId |> Option.iter (fun id -> registry.SubmitMagicLink(id, connectionId, email, url))
                            | None -> ()
                with
                | :? WebSocketException
                | :? IO.IOException
                | :? OperationCanceledException
                | :? ObjectDisposedException -> ()
            finally
                sessionId |> Option.iter (fun id -> registry.Detach(id, connectionId))

            try
                do! outgoing
            with
            | :? WebSocketException
            | :? IO.IOException
            | :? OperationCanceledException -> ()

            if socket.State = WebSocketState.Open || socket.State = WebSocketState.CloseReceived then
                try
                    do! socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None)
                with _ ->
                    ()
    }
