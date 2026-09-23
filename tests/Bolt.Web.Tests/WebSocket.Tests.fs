module Bolt.Web.Tests.WebSocketTests

open System
open System.Net.WebSockets
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
    PreviousOrders = [||]
    PastOrderDetails = [||]
    SkippedOrders = [||]
}

// Fake deps: cached data, analysis returns instantly. CreateSession never
// talks to the network because every other function is faked. RideCountOf
// is faked non-zero so the empty PreviousOrders array doesn't trip the guard.
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

let private receiveText (socket: WebSocket) =
    let buffer = Array.zeroCreate 1_000_000
    let sb = StringBuilder()
    let mutable finished = false
    while not finished do
        let result =
            socket.ReceiveAsync(ArraySegment buffer, CancellationToken.None).GetAwaiter().GetResult()
        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count)) |> ignore
        finished <- result.EndOfMessage
    sb.ToString()

let private sendText (socket: WebSocket) (text: string) =
    let bytes = Encoding.UTF8.GetBytes text
    socket.SendAsync(ArraySegment bytes, WebSocketMessageType.Text, true, CancellationToken.None)
        .GetAwaiter().GetResult()

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
let ``start-analysis on fresh cache streams progress then report`` () =
    use factory = makeFactory ()
    let client = factory.Server.CreateWebSocketClient()
    client.ConfigureRequest <- fun req -> req.Headers.Origin <- string factory.Server.BaseAddress
    use socket =
        client.ConnectAsync(Uri(factory.Server.BaseAddress, "/ws"), CancellationToken.None)
            .GetAwaiter().GetResult()

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
    let client = factory.Server.CreateWebSocketClient()
    client.ConfigureRequest <- fun req -> req.Headers.Origin <- string factory.Server.BaseAddress
    use socket =
        client.ConnectAsync(Uri(factory.Server.BaseAddress, "/ws"), CancellationToken.None)
            .GetAwaiter().GetResult()

    sendText socket """{"msgType":"magic-link","email":"a@b.pl","url":"https://link"}"""

    let response = receiveText socket
    Assert.Contains("Najpierw rozpocznij analizę, podając adres e-mail.", response)
