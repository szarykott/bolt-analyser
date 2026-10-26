module Bolt.Scraper.Tests.ScrapePipelineTests

open System
open System.IO
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
open Bolt.Tests.BoltApiFixture

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

// These tests exercise scrapeRides, including its final typed deserialization.
// The temporary root isolates persistence performed by development builds.
let private scrapeSynthetic scenario =
    task {
        let previous, detail = previousOrder (), pastOrderDetail ()
        match scenario with
        | "missing-payment" -> mutate "payment" None detail |> ignore
        | "bad-payment-text" -> mutate "payment/items/0/trailing_component/text" (Some "\"\"") detail |> ignore
        | "bad-distance" -> mutate "ride_distance_str" (Some "\"\"") previous |> ignore
        | "missing-stops" -> mutate "stops" None previous |> ignore
        | _ -> ()
        let mutable previousRequests = 0
        let mutable detailRequests = 0
        let response data =
            new HttpResponseMessage(HttpStatusCode.OK,
                Content = new StringContent("{\"code\":0,\"message\":\"ok\",\"data\":" + data + "}", Encoding.UTF8, "application/json"))
        let send (request: HttpRequestMessage) _ =
            let data =
                match request.RequestUri.AbsolutePath with
                | "/driver/getDriverProfile"
                | "/orderDriver/getActivityHours" -> "{}"
                | "/orderDriver/v1/getOrderHistoryPaginated" ->
                    match scenario with
                    | "null-history" -> "{\"orders\":null}"
                    | "missing-history" -> "{}"
                    | "empty-history" -> "{\"orders\":[]}"
                    | _ -> """{"orders":[{"order_handle":{"order_id":1,"city_id":1,"order_system":"bolt"}}]}"""
                | "/orderDriver/v1/getPreviousOrder" ->
                    Interlocked.Increment(&previousRequests) |> ignore
                    if scenario = "null-order" then "null" else previous.ToJsonString()
                | "/driver/getPastOrderDetails" ->
                    Interlocked.Increment(&detailRequests) |> ignore
                    if scenario = "null-detail" then "null" else detail.ToJsonString()
                | path -> failwithf "Unexpected synthetic request: %s" path
            Task.FromResult(response data)
        let cfg = config send
        use client = cfg.HttpClient
        let progress = ResizeArray<ScrapeProgress>()
        let report event = progress.Add event; Task.CompletedTask
        let root = Path.Combine(Path.GetTempPath(), "bolt-input-tests-" + Guid.NewGuid().ToString "N")
        let priorRoot = Environment.GetEnvironmentVariable "BOLT_STORAGE_ROOT"
        Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", root)
        try
            let! result = scrapeRides { Config = cfg; Email = "synthetic@example.com" } report CancellationToken.None
            return result, List.ofSeq progress, previousRequests, detailRequests
        finally
            Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", priorRoot)
            if Directory.Exists root then Directory.Delete(root, true)
    }

[<Theory>]
[<InlineData("valid")>]
[<InlineData("missing-payment")>]
[<InlineData("bad-payment-text")>]
let ``scraping preserves raw data for per ride conversion`` scenario : Task =
    task {
        let! result, progress, previousRequests, detailRequests = scrapeSynthetic scenario
        match result with
        | Error error -> failwithf "Unexpected scrape error: %s" error
        | Ok data ->
            Assert.Single data.Orders |> ignore
            Assert.Empty data.SkippedOrders
            if scenario = "missing-payment" then Assert.False(data.Orders[0].PastOrderDetail.TryGetProperty("payment") |> fst)
            if scenario = "bad-payment-text" then
                Assert.Equal("", data.Orders[0].PastOrderDetail.GetProperty("payment").GetProperty("items").[0].GetProperty("trailing_component").GetProperty("text").GetString())
        Assert.Contains(ScrapingOrderDetails(1, 1), progress)
        Assert.Equal(1, previousRequests)
        Assert.Equal(1, detailRequests)
    }

[<Theory>]
[<InlineData("bad-distance")>]
[<InlineData("missing-stops")>]
let ``malformed order is retained for diagnostics and does not fail scraping`` scenario : Task =
    task {
        let! result, progress, previousRequests, detailRequests = scrapeSynthetic scenario
        match result with
        | Error error -> failwithf "Unexpected scrape failure: %s" error
        | Ok data ->
            Assert.Single data.Orders |> ignore
            match (Bolt.ETL.RideFactory.parseOrder data.Orders[0]).Data with
            | Bolt.Models.RideType.Error error ->
                Assert.Equal(1L, error.OrderId)
                Assert.Equal("previous_order", error.Stage)
                Assert.False(String.IsNullOrEmpty error.StackTrace)
            | _ -> failwith "Expected this order to be classified as an error"
        Assert.Contains(ScrapingOrderDetails(1, 1), progress)
        Assert.Equal(1, previousRequests)
        Assert.Equal(1, detailRequests)
    }

[<Theory; InlineData("null-order"); InlineData("null-detail")>]
let ``null order responses become per ride diagnostics instead of losing the payload`` scenario : Task =
    task {
        let! result, progress, previousRequests, detailRequests = scrapeSynthetic scenario
        match result with
        | Error error -> failwithf "Unexpected fetch error: %s" error
        | Ok data ->
            Assert.Empty data.SkippedOrders
            match (Bolt.ETL.RideFactory.parseOrder data.Orders[0]).Data with
            | Bolt.Models.RideType.Error error ->
                let payload = if scenario = "null-order" then error.PreviousOrder else error.PastOrderDetail
                Assert.Equal(JsonValueKind.Null, payload.ValueKind)
                Assert.Contains("JsonException", error.ExceptionType)
            | _ -> failwith "Expected an error category"
        Assert.Contains(ScrapingOrderDetails(1, 1), progress)
        Assert.Equal(1, previousRequests)
        Assert.Equal(1, detailRequests)
    }

[<Theory; InlineData("null-history"); InlineData("missing-history")>]
let ``invalid history fails before fetching details`` scenario : Task =
    task {
        let! result, _, previousRequests, detailRequests = scrapeSynthetic scenario
        match result with
        | Ok _ -> failwith "Expected invalid history to fail"
        | Error error -> Assert.Contains("DeserializationError", error)
        Assert.Equal(0, previousRequests)
        Assert.Equal(0, detailRequests)
    }

[<Fact>]
let ``empty history produces an empty successful scrape`` () : Task =
    task {
        let! result, _, previousRequests, detailRequests = scrapeSynthetic "empty-history"
        match result with
        | Error error -> failwithf "Unexpected scrape error: %s" error
        | Ok data ->
            Assert.Empty data.Orders
        Assert.Equal(0, previousRequests)
        Assert.Equal(0, detailRequests)
    }
