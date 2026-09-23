module Bolt.Scraper.Tests.ScrapePipelineTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open Bolt.Models.BoltApi
open Bolt.Scraper.BoltApi.ApiModels
open Bolt.Scraper.BoltApi.Tokens
open Bolt.Scraper.ScrapePipeline

type private StubHandler(send: HttpRequestMessage -> CancellationToken -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    override _.SendAsync(request, ct) = send request ct

let private config send : ApiConfig =
    { BaseUrl = Uri "https://bolt.test/"
      HttpClient = new HttpClient(new StubHandler(send))
      Tokens = TokenStore.empty "test@example.com"
      RubbishData =
        { DeviceName = "test"
          DeviceUid = "test"
          DeviceOsVersion = "test"
          DeviceType = "test"
          Version = "test"
          Country = "pl"
          Language = "pl" } }

let private handles count : HistoryOrderHandle[] =
    Array.init count (fun i ->
        { OrderHandle = { OrderId = int64 i; CityId = 1; OrderSystem = "bolt" } })

let private orderId (request: HttpRequestMessage) (ct: CancellationToken) =
    task {
        let! body = request.Content.ReadAsStringAsync ct
        use json = JsonDocument.Parse body
        return json.RootElement.GetProperty("order_handle").GetProperty("order_id").GetInt64()
    }

let private ok id =
    new HttpResponseMessage(
        HttpStatusCode.OK,
        Content = new StringContent($"{{\"code\":0,\"message\":\"ok\",\"data\":{{\"id\":{id}}}}}", Encoding.UTF8, "application/json"))

let private noProgress _ = Task.CompletedTask

[<Fact>]
let ``at most ten orders run together and results retain history order`` () : Task =
    task {
        let sync = obj ()
        let tenStarted = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        let release = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        let mutable active = 0
        let mutable started = 0
        let mutable maxActive = 0
        let progress = ResizeArray<int>()

        let send (request: HttpRequestMessage) ct =
            task {
                let! id = orderId request ct
                if request.RequestUri.AbsolutePath.Contains("getPreviousOrder") then
                    lock sync (fun () ->
                        active <- active + 1
                        started <- started + 1
                        maxActive <- max maxActive active
                        if started = 10 then tenStarted.TrySetResult(()) |> ignore)
                    do! release.Task
                else
                    do! Task.Delay((9 - int (id % 10L)) * 2)
                    lock sync (fun () -> active <- active - 1)
                return ok id
            }

        let cfg = config send
        let report = function
            | ScrapingOrderDetails(current, total) ->
                Assert.Equal(24, total)
                progress.Add current
                Task.CompletedTask
            | _ -> Task.CompletedTask

        let work = fetchOrderDetails cfg (handles 24) report CancellationToken.None
        try
            do! tenStarted.Task.WaitAsync(TimeSpan.FromSeconds 5.0)
        with :? TimeoutException ->
            release.TrySetResult(()) |> ignore
            let! result = work.WaitAsync(TimeSpan.FromSeconds 5.0)
            failwithf "Only %d requests started; result: %A" started result
        Assert.Equal(10, started)
        release.TrySetResult(()) |> ignore

        match! work.WaitAsync(TimeSpan.FromSeconds 5.0) with
        | Error e -> failwithf "Unexpected API error: %A" e
        | Ok(successful, failures) ->
            Assert.Equal(10, maxActive)
            Assert.Equal<int>([| 1 .. 24 |], progress.ToArray())
            Assert.Empty failures
            Assert.Equal<int>([| 0 .. 23 |], successful |> Array.map (fun (i, _, _) -> i))
            Assert.Equal<int64>([| 0L .. 23L |], successful |> Array.map (fun (_, x, _) -> x.GetProperty("id").GetInt64()))
            Assert.Equal<int64>([| 0L .. 23L |], successful |> Array.map (fun (_, _, x) -> x.GetProperty("id").GetInt64()))
    }

[<Fact>]
let ``failed order is retried twice then skipped`` () : Task =
    task {
        let mutable detailRequests = 0
        let mutable attempts = 0
        let failures = ResizeArray<int64>()
        let send (request: HttpRequestMessage) ct =
            task {
                let! id = orderId request ct
                if id = 0L && request.RequestUri.AbsolutePath.Contains("getPreviousOrder") then
                    Interlocked.Increment(&attempts) |> ignore
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests, Content = new StringContent("busy"))
                else
                    if id = 0L then Interlocked.Increment(&detailRequests) |> ignore
                    return ok id
            }

        let cfg = config send
        let report = function
            | ScrapingOrderFailed(_, _, id, _) ->
                failures.Add id
                Task.CompletedTask
            | _ -> Task.CompletedTask
        let! result = fetchOrderDetails cfg (handles 24) report CancellationToken.None
        match result with
        | Error e -> failwithf "Unexpected scrape error: %A" e
        | Ok(successful, skipped) ->
            Assert.Equal(23, successful.Length)
            Assert.Equal(0L, skipped[0].OrderId)
            Assert.Equal("HTTP 429", skipped[0].Reason)
            Assert.Single skipped |> ignore
            Assert.Equal<int>([| 1 .. 23 |], successful |> Array.map (fun (i, _, _) -> i))
        Assert.Equal(3, attempts)
        Assert.Equal(0, detailRequests)
        Assert.Equal<int64>([| 0L |], failures.ToArray())
    }

[<Fact>]
let ``second detail succeeds on the third attempt`` () : Task =
    task {
        let mutable previousAttempts = 0
        let mutable detailAttempts = 0
        let send (request: HttpRequestMessage) ct =
            task {
                let! id = orderId request ct
                if request.RequestUri.AbsolutePath.Contains("getPreviousOrder") then
                    previousAttempts <- previousAttempts + 1
                    return ok id
                else
                    detailAttempts <- detailAttempts + 1
                    if detailAttempts < 3 then
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable, Content = new StringContent("busy"))
                    else
                        return ok id
            }

        let cfg = config send
        match! fetchOrderDetails cfg (handles 1) noProgress CancellationToken.None with
        | Error e -> failwithf "Unexpected scrape error: %A" e
        | Ok(successful, skipped) ->
            Assert.Single successful |> ignore
            Assert.Empty skipped
        Assert.Equal(3, previousAttempts)
        Assert.Equal(3, detailAttempts)
    }

[<Fact>]
let ``cancellation aborts pending detail requests`` () : Task =
    task {
        let started = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
        let send (request: HttpRequestMessage) ct =
            task {
                started.TrySetResult(()) |> ignore
                do! Task.Delay(Timeout.Infinite, ct)
                return ok 0
            }

        let cfg = config send
        use cts = new CancellationTokenSource()
        let work = fetchOrderDetails cfg (handles 24) noProgress cts.Token
        do! started.Task.WaitAsync(TimeSpan.FromSeconds 5.0)
        cts.Cancel()

        let mutable canceled = false
        try
            let! _ = work.WaitAsync(TimeSpan.FromSeconds 5.0)
            ()
        with :? OperationCanceledException -> canceled <- true
        Assert.True(canceled)
    }
