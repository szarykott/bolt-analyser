module Bolt.Web.Tests.WebSocketTests

open System
open System.Net.WebSockets
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.Testing
open Microsoft.Extensions.DependencyInjection
open Xunit
open Bolt.Models.BoltApi
open Bolt.Scraper.ScrapePipeline
open Bolt.Web.Jobs
open Bolt.Web.Tests.ReportFixture

let private fakeData: ScrapedData = {
    Email = "a@b.pl"
    Profile = JsonDocument.Parse("{}").RootElement
    ActivityHours = JsonDocument.Parse("{}").RootElement
    OrderHistory = [||]
    Orders = [||]
    SkippedOrders = [||]
}

// Fake deps: cached data, analysis returns instantly. CreateSession never
// talks to the network because every other function is faked. RideCountOf
// is faked non-zero so the empty Orders array doesn't trip the guard.
let private fakeDeps: PipelineDeps<ScrapeSession, ScrapedData> = {
    LoadCached = fun _ -> Some fakeData
    CreateSession = ScrapeSession.create
    HasTokens = fun _ -> true
    RefreshTokens = fun _ _ -> Task.FromResult(Ok())
    RequestMagicLink = fun _ _ -> Task.FromResult(Ok())
    AuthenticateWithUrl = fun _ _ _ -> Task.FromResult(Ok())
    ScrapeRides = fun _ _ _ -> Task.FromResult(Ok fakeData)
    RunAnalysis = fun _ -> Task.FromResult(Ok report)
    RideCountOf = fun _ -> 3
}

let private makeFactory () =
    (new WebApplicationFactory<Bolt.Web.Program.BoltWebMarker>())
        .WithWebHostBuilder(fun b ->
            b.ConfigureServices(fun services ->
                services.AddSingleton<PipelineDeps<ScrapeSession, ScrapedData>>(fakeDeps) |> ignore)
            |> ignore)

let private makeFactoryWith (deps: PipelineDeps<ScrapeSession, ScrapedData>) =
    (new WebApplicationFactory<Bolt.Web.Program.BoltWebMarker>())
        .WithWebHostBuilder(fun b ->
            b.ConfigureServices(fun services ->
                services.AddSingleton<PipelineDeps<ScrapeSession, ScrapedData>>(deps) |> ignore)
            |> ignore)

let private makeFactoryWithGrace (deps: PipelineDeps<ScrapeSession, ScrapedData>) grace =
    (new WebApplicationFactory<Bolt.Web.Program.BoltWebMarker>())
        .WithWebHostBuilder(fun b ->
            b.ConfigureServices(fun services ->
                services.AddSingleton<PipelineDeps<ScrapeSession, ScrapedData>>(deps) |> ignore
                services.AddSingleton<Bolt.Web.JobSessions.JobRegistry>(fun sp ->
                    Bolt.Web.JobSessions.JobRegistry(
                        grace,
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Bolt.Web.JobSessions.JobRegistry>>()))
                |> ignore)
            |> ignore)

let private sessionCookie (factory: WebApplicationFactory<Bolt.Web.Program.BoltWebMarker>) =
    use http = factory.CreateClient()
    use response = http.GetAsync("/start").GetAwaiter().GetResult()
    response.EnsureSuccessStatusCode() |> ignore
    response.Headers.GetValues("Set-Cookie") |> Seq.head |> fun value -> value.Split(';')[0]

let private connectWithCookie (factory: WebApplicationFactory<Bolt.Web.Program.BoltWebMarker>) (cookie: string) =
    let client = factory.Server.CreateWebSocketClient()
    client.ConfigureRequest <- fun req ->
        req.Headers.Origin <- Microsoft.Extensions.Primitives.StringValues(string factory.Server.BaseAddress)
        req.Headers.Cookie <- Microsoft.Extensions.Primitives.StringValues(cookie)
    client.ConnectAsync(Uri(factory.Server.BaseAddress, "/ws"), CancellationToken.None)
        .GetAwaiter().GetResult()

let private receiveText (socket: WebSocket) =
    use cts = new CancellationTokenSource(TimeSpan.FromSeconds 5.0)
    let buffer = Array.zeroCreate 1_000_000
    let sb = StringBuilder()
    let mutable finished = false
    while not finished do
        let result =
            socket.ReceiveAsync(ArraySegment buffer, cts.Token).GetAwaiter().GetResult()
        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count)) |> ignore
        finished <- result.EndOfMessage
    sb.ToString()

let private receiveUntil (socket: WebSocket) (expected: string) =
    let mutable last = ""
    let mutable rounds = 0
    while not (last.Contains expected) && rounds < 10 do
        last <- receiveText socket
        rounds <- rounds + 1
    Assert.Contains(expected, last)
    last

let private sendText (socket: WebSocket) (text: string) =
    let bytes = Encoding.UTF8.GetBytes text
    socket.SendAsync(ArraySegment bytes, WebSocketMessageType.Text, true, CancellationToken.None)
        .GetAwaiter().GetResult()

[<Fact>]
let ``start page sets a protected session cookie over HTTPS`` () =
    use factory = makeFactory ()
    use http = factory.CreateClient()
    use response = http.GetAsync("https://localhost/start").GetAwaiter().GetResult()
    response.EnsureSuccessStatusCode() |> ignore
    let cookie = response.Headers.GetValues("Set-Cookie") |> Seq.head |> _.ToLowerInvariant()
    Assert.Contains("httponly", cookie)
    Assert.Contains("secure", cookie)
    Assert.Contains("samesite=strict", cookie)

[<Fact>]
let ``ws upgrade without matching origin is rejected`` () =
    use factory = makeFactory ()
    let client = factory.Server.CreateWebSocketClient()
    client.ConfigureRequest <- fun req -> req.Headers.Origin <- "https://evil.example.com"
    let ex =
        Record.Exception(fun () ->
            client.ConnectAsync(Uri(factory.Server.BaseAddress, "/ws"), CancellationToken.None)
                .GetAwaiter().GetResult()
            |> ignore)
    Assert.NotNull ex

[<Fact>]
let ``websocket without a session asks for a fresh start`` () =
    use factory = makeFactory ()
    let client = factory.Server.CreateWebSocketClient()
    client.ConfigureRequest <- fun req -> req.Headers.Origin <- string factory.Server.BaseAddress
    use socket =
        client.ConnectAsync(Uri(factory.Server.BaseAddress, "/ws"), CancellationToken.None)
            .GetAwaiter().GetResult()
    Assert.Contains("Sesja wygasła", receiveText socket)

[<Fact>]
let ``start-analysis on fresh cache streams progress then report`` () =
    use factory = makeFactory ()
    use socket = connectWithCookie factory (sessionCookie factory)

    sendText socket """{"msgType":"start-analysis","email":"a@b.pl"}"""

    let mutable last = ""
    let mutable rounds = 0
    while not (last.Contains "report-content") && rounds < 10 do
        last <- receiveText socket
        rounds <- rounds + 1

    Assert.Contains("report-content", last)
    Assert.Contains("a@b.pl", last)

[<Fact>]
let ``magic link as first message yields an error and starts no job`` () =
    use factory = makeFactory ()
    use socket = connectWithCookie factory (sessionCookie factory)

    sendText socket """{"msgType":"magic-link","email":"a@b.pl","url":"https://link"}"""

    receiveUntil socket "Najpierw rozpocznij analizę, podając adres e-mail." |> ignore

[<Fact>]
let ``magic link can be submitted after websocket reconnect`` () =
    let deps =
        { fakeDeps with
            LoadCached = fun _ -> None
            HasTokens = fun _ -> false }
    use factory = makeFactoryWith deps
    let cookie = sessionCookie factory
    use first = connectWithCookie factory cookie
    sendText first """{"msgType":"start-analysis","email":"a@b.pl"}"""
    receiveUntil first "Dokończ logowanie" |> ignore
    first.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving", CancellationToken.None)
        .GetAwaiter().GetResult()

    use http = factory.CreateClient()
    use request = new Net.Http.HttpRequestMessage(Net.Http.HttpMethod.Get, "/start")
    request.Headers.Add("Cookie", cookie)
    use response = http.SendAsync(request).GetAwaiter().GetResult()
    response.EnsureSuccessStatusCode() |> ignore
    Assert.False(response.Headers.Contains "Set-Cookie")

    use resumed = connectWithCookie factory cookie
    Assert.Contains("Dokończ logowanie", receiveText resumed)
    sendText resumed """{"msgType":"magic-link","email":"other@b.pl","url":"https://link"}"""
    receiveUntil resumed "nie pasuje do rozpoczętej analizy" |> ignore
    sendText resumed """{"msgType":"magic-link","email":"a@b.pl","url":"https://link"}"""
    receiveUntil resumed "report-content" |> ignore

[<Fact>]
let ``scraping continues while websocket is disconnected`` () =
    let scraping = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let finish = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let deps =
        { fakeDeps with
            LoadCached = fun _ -> None
            HasTokens = fun _ -> false
            ScrapeRides = fun _ _ _ -> task {
                scraping.SetResult()
                do! finish.Task
                return Ok fakeData
            } }
    use factory = makeFactoryWith deps
    let cookie = sessionCookie factory
    use first = connectWithCookie factory cookie
    sendText first """{"msgType":"start-analysis","email":"a@b.pl"}"""
    receiveUntil first "Dokończ logowanie" |> ignore
    sendText first """{"msgType":"magic-link","email":"a@b.pl","url":"https://link"}"""
    receiveUntil first "Pobieranie przejazd" |> ignore
    scraping.Task.GetAwaiter().GetResult()
    first.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving", CancellationToken.None)
        .GetAwaiter().GetResult()

    use resumed = connectWithCookie factory cookie
    Assert.Contains("Pobieranie przejazd", receiveText resumed)
    finish.SetResult()
    receiveUntil resumed "report-content" |> ignore

[<Fact>]
let ``completed report is replayed after reconnect`` () =
    let scraping = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let finish = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let completed = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let deps =
        { fakeDeps with
            LoadCached = fun _ -> None
            HasTokens = fun _ -> false
            ScrapeRides = fun _ _ _ -> task {
                scraping.SetResult()
                do! finish.Task
                return Ok fakeData
            }
            RunAnalysis = fun _ ->
                completed.SetResult()
                Task.FromResult(Ok report) }
    use factory = makeFactoryWith deps
    let cookie = sessionCookie factory
    use first = connectWithCookie factory cookie
    sendText first """{"msgType":"start-analysis","email":"a@b.pl"}"""
    receiveUntil first "Dokończ logowanie" |> ignore
    sendText first """{"msgType":"magic-link","email":"a@b.pl","url":"https://link"}"""
    Assert.True(scraping.Task.Wait(TimeSpan.FromSeconds 5.0))
    first.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving", CancellationToken.None)
        .GetAwaiter().GetResult()
    finish.SetResult()
    Assert.True(completed.Task.Wait(TimeSpan.FromSeconds 5.0))

    use resumed = connectWithCookie factory cookie
    receiveUntil resumed "report-content" |> ignore

[<Fact>]
let ``expired session cancels the job and offers a fresh start`` () =
    let authenticating = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let cancelled = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let deps =
        { fakeDeps with
            LoadCached = fun _ -> None
            HasTokens = fun _ -> false
            AuthenticateWithUrl = fun _ _ ct -> task {
                authenticating.SetResult()
                try
                    do! Task.Delay(Timeout.Infinite, ct)
                with :? OperationCanceledException ->
                    cancelled.TrySetResult() |> ignore
                return Error "cancelled"
            } }
    use factory = makeFactoryWithGrace deps (TimeSpan.FromMilliseconds 100.0)
    let cookie = sessionCookie factory
    use first = connectWithCookie factory cookie
    sendText first """{"msgType":"start-analysis","email":"a@b.pl"}"""
    receiveUntil first "Dokończ logowanie" |> ignore
    sendText first """{"msgType":"magic-link","email":"a@b.pl","url":"https://link"}"""
    Assert.True(authenticating.Task.Wait(TimeSpan.FromSeconds 5.0))
    first.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving", CancellationToken.None)
        .GetAwaiter().GetResult()
    Assert.True(cancelled.Task.Wait(TimeSpan.FromSeconds 5.0))

    use expired = connectWithCookie factory cookie
    Assert.Contains("Sesja wygasła", receiveText expired)

    use http = factory.CreateClient()
    use request = new Net.Http.HttpRequestMessage(Net.Http.HttpMethod.Get, "/start")
    request.Headers.Add("Cookie", cookie)
    use response = http.SendAsync(request).GetAwaiter().GetResult()
    response.EnsureSuccessStatusCode() |> ignore
    let freshCookie = response.Headers.GetValues("Set-Cookie") |> Seq.head |> fun value -> value.Split(';')[0]
    Assert.True(cookie <> freshCookie)

[<Fact>]
let ``another browser cannot resume a waiting job`` () =
    let deps =
        { fakeDeps with
            LoadCached = fun _ -> None
            HasTokens = fun _ -> false }
    use factory = makeFactoryWith deps
    let ownerCookie = sessionCookie factory
    let otherCookie = sessionCookie factory
    use owner = connectWithCookie factory ownerCookie
    sendText owner """{"msgType":"start-analysis","email":"a@b.pl"}"""
    receiveUntil owner "Dokończ logowanie" |> ignore

    use other = connectWithCookie factory otherCookie
    sendText other """{"msgType":"magic-link","email":"a@b.pl","url":"https://link"}"""
    receiveUntil other "Najpierw rozpocznij analizę" |> ignore
    sendText owner """{"msgType":"magic-link","email":"a@b.pl","url":"https://link"}"""
    receiveUntil owner "report-content" |> ignore

let private download factory (cookie: string option) =
    use http = (factory: WebApplicationFactory<Bolt.Web.Program.BoltWebMarker>).CreateClient()
    use request = new Net.Http.HttpRequestMessage(Net.Http.HttpMethod.Get, "/diagnostics.zip")
    cookie |> Option.iter (fun value -> request.Headers.Add("Cookie", value))
    http.SendAsync(request).GetAwaiter().GetResult()

[<Fact>]
let ``all malformed rides still offer a session scoped downloadable zip`` () =
    let data = { fakeData with Orders = [| brokenOrder |] }
    let deps =
        { fakeDeps with
            LoadCached = fun _ -> Some data
            RunAnalysis = Bolt.Web.Report.run }
    use factory = makeFactoryWith deps
    let cookie = sessionCookie factory
    use socket = connectWithCookie factory cookie
    sendText socket """{"msgType":"start-analysis","email":"a@b.pl"}"""
    let html = receiveUntil socket "diagnostics.zip"
    Assert.Contains("Nie udało się przygotować analizy", System.Net.WebUtility.HtmlDecode html)

    use response = download factory (Some cookie)
    Assert.Equal(Net.HttpStatusCode.OK, response.StatusCode)
    Assert.Equal("application/zip", response.Content.Headers.ContentType.MediaType)
    Assert.Contains("no-store", response.Headers.CacheControl.ToString())
    Assert.Contains("bolt-errors.zip", response.Content.Headers.ContentDisposition.ToString())
    use stream = new MemoryStream(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
    use archive = new ZipArchive(stream, ZipArchiveMode.Read)
    use entry = archive.GetEntry("ride-1-99.json").Open()
    use json = JsonDocument.Parse entry
    Assert.Equal(99L, json.RootElement.GetProperty("order_id").GetInt64())
    Assert.Contains("RideFactory", json.RootElement.GetProperty("stack_trace").GetString())

    use anonymous = download factory None
    Assert.Equal(Net.HttpStatusCode.NotFound, anonymous.StatusCode)
    use other = download factory (Some(sessionCookie factory))
    Assert.Equal(Net.HttpStatusCode.NotFound, other.StatusCode)

    // Reconnecting retains both the explanation and the download.
    socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None).GetAwaiter().GetResult()
    use resumed = connectWithCookie factory cookie
    receiveUntil resumed "diagnostics.zip" |> ignore
    use replayed = download factory (Some cookie)
    Assert.Equal(Net.HttpStatusCode.OK, replayed.StatusCode)

[<Fact>]
let ``completed partial report exposes only its failed rides in the archive`` () =
    let deps = { fakeDeps with RunAnalysis = fun _ -> Task.FromResult(Ok { report with RideErrors = [| rideError |] }) }
    use factory = makeFactoryWith deps
    let cookie = sessionCookie factory
    use socket = connectWithCookie factory cookie
    sendText socket """{"msgType":"start-analysis","email":"a@b.pl"}"""
    receiveUntil socket "diagnostics.zip" |> ignore
    use response = download factory (Some cookie)
    use stream = new MemoryStream(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
    use archive = new ZipArchive(stream, ZipArchiveMode.Read)
    Assert.Equal(2, archive.Entries.Count)
    Assert.NotNull(archive.GetEntry("ride-1-99.json"))

[<Fact>]
let ``starting a new analysis clears old diagnostics before new results arrive`` () =
    let secondRun = TaskCompletionSource<Result<Bolt.Web.Report.AnalysisReport, Bolt.Web.Report.AnalysisFailure>>(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable runs = 0
    let deps =
        { fakeDeps with
            RunAnalysis = fun _ ->
                if Interlocked.Increment(&runs) = 1 then Task.FromResult(Ok { report with RideErrors = [| rideError |] })
                else secondRun.Task }
    use factory = makeFactoryWith deps
    let cookie = sessionCookie factory
    use socket = connectWithCookie factory cookie
    sendText socket """{"msgType":"start-analysis","email":"a@b.pl"}"""
    receiveUntil socket "diagnostics.zip" |> ignore
    sendText socket """{"msgType":"start-analysis","email":"a@b.pl"}"""
    receiveUntil socket "Trwa analiza" |> ignore
    try
        use response = download factory (Some cookie)
        Assert.Equal(Net.HttpStatusCode.NotFound, response.StatusCode)
    finally
        secondRun.TrySetResult(Ok report) |> ignore
    receiveUntil socket "report-content" |> ignore

[<Fact>]
let ``diagnostics disappear when the disconnected session expires`` () =
    let deps = { fakeDeps with RunAnalysis = fun _ -> Task.FromResult(Ok { report with RideErrors = [| rideError |] }) }
    use factory = makeFactoryWithGrace deps (TimeSpan.FromMilliseconds 100.)
    let cookie = sessionCookie factory
    use socket = connectWithCookie factory cookie
    sendText socket """{"msgType":"start-analysis","email":"a@b.pl"}"""
    receiveUntil socket "diagnostics.zip" |> ignore
    socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "leaving", CancellationToken.None).GetAwaiter().GetResult()
    use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 5.)
    let mutable expired = false
    while not expired && not timeout.IsCancellationRequested do
        use response = download factory (Some cookie)
        expired <- response.StatusCode = Net.HttpStatusCode.NotFound
        if not expired then Task.Delay(20).GetAwaiter().GetResult()
    Assert.True(expired, "The disconnected session should release its diagnostics")
