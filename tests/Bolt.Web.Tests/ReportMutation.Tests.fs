module Bolt.Web.Tests.ReportMutationTests

open System
open System.Text.Json
open System.Threading.Tasks
open Xunit
open Xunit.Abstractions
open Bolt.ETL
open Bolt.ETL.Analysis
open Bolt.Models
open Bolt.Models.BoltApi
open Bolt.Tests.BoltApiFixture
open Bolt.Tests.ApiMutations
open Bolt.Web

let private data orders : ScrapedData =
    { Email = "mutations@example.test"
      Profile = Unchecked.defaultof<JsonElement>
      ActivityHours = Unchecked.defaultof<JsonElement>
      OrderHistory = [||]
      Orders = orders
      SkippedOrders = [||]
      Diagnostics = [||] }

// Exercise the JSON write boundary used for the analytics request without a real service.
let private cluster (source: RideClustering.RidesDataSource) : Task<RideClustering.AnalysisResult> =
    JsonSerializer.Serialize(source.Rows) |> ignore
    let points = RideClustering.toPoints source
    // Use a consistent clustering response to isolate input mutations.
    Task.FromResult
        { Points = points
          Labels = Array.zeroCreate source.Rows.Length
          NoiseCount = 0
          Clusters =
            [| { Id = 0; Size = source.Rows.Length
                 CentroidLatitude = points[0].Latitude; CentroidLongitude = points[0].Longitude
                 MeanHour = points[0].Hour } |] }

let private run orders = Report.runWithClustering cluster (data orders) |> fun task -> task.GetAwaiter().GetResult()
let private good () = rawOrder 1L (previousOrder ()) (pastOrderDetail ())

// These tests expose gaps in the existing safety boundary; they do not assert recovery.
[<Theory>]
[<InlineData("coordinate", typeof<ArgumentException>)>]
[<InlineData("timestamp", typeof<ArgumentOutOfRangeException>)>]
[<InlineData("money", typeof<OverflowException>)>]
let ``known gap accepted ride crashes later computation without a diagnostic`` scenario expected =
    let p, d = previousOrder (), pastOrderDetail ()
    match scenario with
    | "coordinate" -> mutate "stops/0/lat" (Some "1e999") p |> ignore
    | "timestamp" -> mutate "accepted_timestamp" (Some "253402300799") p |> ignore
    | _ ->
        let item = d.["payment"].["items"].[0].DeepClone()
        d.["payment"].["items"].AsArray().Add item
        mutate "payment/items/0/trailing_component/text" (Some "\"79228162514264337593543950335 zł\"") d |> ignore
    let raw = rawOrder 2L p d
    let prepared = Report.prepare (data [| good (); raw |])
    Assert.Equal(2, prepared.FinishedRides.Length)
    Assert.Empty prepared.RideErrors
    Assert.Throws(expected, fun () ->
        JsonSerializer.Serialize((RideClustering.prepareRideAnalysisSource prepared.FinishedRides).Rows) |> ignore
        BasicStatistics.generate prepared.FinishedRides |> ignore) |> ignore
    match run [| good (); raw |] with
    | Ok _ -> failwith "Expected the demonstrated whole-report failure"
    | Result.Error failure ->
        Assert.Empty failure.RideErrors
        Assert.StartsWith("Analiza nie powiodła się:", failure.Message)
        Assert.DoesNotContain("kurs 2", failure.Message.ToLowerInvariant())

[<Fact>]
let ``known gap individually valid money values overflow only when histories are combined`` () =
    let d = pastOrderDetail () |> mutate "payment/items/0/trailing_component/text" (Some "\"79228162514264337593543950335 zł\"")
    let large = rawOrder 2L (previousOrder ()) d
    match run [| large |], run [| good () |], run [| good (); large |] with
    | Ok _, Ok _, Result.Error failure -> Assert.Empty failure.RideErrors
    | result -> failwithf "Unexpected combination: %A" result

[<Theory; InlineData(0); InlineData(2)>]
let ``known gap mismatched cluster labels crash rendering despite captured ride errors`` length =
    let original = Bolt.Web.Tests.ReportFixture.report
    let response =
        { original with
            RideErrors = [| Bolt.Web.Tests.ReportFixture.rideError |]
            PickupClusters = { original.PickupClusters with Labels = Array.zeroCreate length } }
    Assert.Throws<ArgumentException>(fun () -> Views.Report.reportFragment response |> ignore) |> ignore

[<Fact>]
let ``known gap negative cluster identity reaches invalid palette indexing`` () =
    let original = Bolt.Web.Tests.ReportFixture.report
    let response =
        { original with
            PickupClusters =
                { original.PickupClusters with
                    Labels = [| -2 |]
                    Clusters = [| { original.PickupClusters.Clusters[0] with Id = -2 } |] } }
    Assert.Throws<IndexOutOfRangeException>(fun () -> Views.Report.reportFragment response |> ignore) |> ignore

[<Fact>]
let ``valid all noise response renders the report and pickup points without centroids`` () =
    let original = Bolt.Web.Tests.ReportFixture.report
    let response: Bolt.ETL.Analytics.StDbscanResponse =
        { Labels = [| -1 |]; NPoints = 1; NClusters = 0; NNoise = 1; Clusters = [||] }
    let result = RideClustering.fromAnalyticsResponse original.PickupClusters.Points response
    let html = Views.Report.reportFragment { original with PickupClusters = result } |> Net.WebUtility.HtmlDecode
    Assert.Contains("Nie znaleziono skupisk odbiorów", html)
    Assert.Contains("Noise · n=1", html)
    Assert.Contains("id=\"basic-statistics\"", html)
    Assert.Contains("id=\"chart-ride-clusters-0\"", html)

type ReportMutationAudit(output: ITestOutputHelper) =
    [<Fact>]
    member _.``exhaustive full report audit distinguishes recovered mutations from missing diagnostics`` () =
        let failures = ResizeArray<string>()
        let deferred = ResizeArray<string>()
        let mutable recovered = 0
        let mutable accepted = 0
        for description, raw in singleMutations () do
            let parsed = RideFactory.parseOrder raw
            let unsafeWorkload =
                match parsed.Data with
                | Finished r -> r.Times.RideEnd - r.Times.AcceptedTimestamp > TimeSpan.FromDays 2.
                | _ -> false
            if unsafeWorkload then
                // eligibleHours eagerly materializes every hour. Do not run decades of input in CI.
                deferred.Add description
            else
                match run [| good (); raw |] with
                | Result.Error failure ->
                    failures.Add(description + " => " + failure.Message)
                    Assert.Empty failure.RideErrors
                | Ok report ->
                    Views.Report.reportFragment report |> ignore
                    match parsed.Data with
                    | RideType.Error _ ->
                        Assert.Equal(1, report.RideCount)
                        Assert.Single report.RideErrors |> ignore
                        Assert.True(JsonElement.DeepEquals(raw.PreviousOrder, report.RideErrors[0].PreviousOrder.Value))
                        Assert.True(JsonElement.DeepEquals(raw.PastOrderDetail, report.RideErrors[0].PastOrderDetail.Value))
                        RideDiagnostics.zip None report.RideErrors |> ignore
                        recovered <- recovered + 1
                    | _ -> accepted <- accepted + 1
        output.WriteLine(sprintf "Recovered=%d Accepted=%d Whole-report failures=%d Unsafe durations=%d" recovered accepted failures.Count deferred.Count)
        for failure in failures do output.WriteLine failure
        for duration in deferred do output.WriteLine("DEFERRED WORKLOAD: " + duration)
        Assert.True(recovered > 500)
        Assert.True(accepted > 0)
        Assert.True(failures.Count > 0, "Characterization must be revised when whole-report failure gaps are fixed.")
        Assert.True(deferred.Count > 0)
