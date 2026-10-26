module Bolt.Web.Tests.ReportTests

open System
open System.Text.Json
open Xunit
open Bolt.Models.BoltApi
open Bolt.Web
open Bolt.Tests.BoltApiFixture

[<Fact>]
let ``report rejects history without completed rides`` () =
    let data: ScrapedData =
        { Email = "driver@example.com"
          Profile = JsonDocument.Parse("{}").RootElement
          ActivityHours = JsonDocument.Parse("{}").RootElement
          OrderHistory = [||]
          Orders = [||]
          SkippedOrders = [||] }
    let result = Report.run data |> fun task -> task.GetAwaiter().GetResult()
    let expected: Report.AnalysisFailure =
        { Message = "Nie znaleziono zakończonych przejazdów dla driver@example.com"; RideErrors = [||] }
    Assert.Equal(Error expected, result)

[<Theory>]
[<InlineData(true, "accepted_timestamp")>]
[<InlineData(true, "ride_start")>]
[<InlineData(true, "ride_end")>]
[<InlineData(true, "ride_distance_str")>]
[<InlineData(false, "payment")>]
[<InlineData(false, "earnings")>]
let ``report preserves diagnostics when no finished ride can be analysed`` previous path =
    let p, d = previousOrder (), pastOrderDetail ()
    mutate path None (if previous then p else d) |> ignore
    let data: ScrapedData =
        { Email = "synthetic@example.com"
          Profile = Unchecked.defaultof<JsonElement>
          ActivityHours = Unchecked.defaultof<JsonElement>
          OrderHistory = [||]
          Orders = [| rawOrder 1L p d |]
          SkippedOrders = [||] }
    // Conversion fails before any request to the analytics service.
    let result = Report.run data |> fun task -> task.GetAwaiter().GetResult()
    match result with
    | Ok _ -> failwith "Expected no usable rides"
    | Error failure ->
        Assert.Contains("Pominięto 1 kursów", failure.Message)
        Assert.Single failure.RideErrors |> ignore
        Assert.Equal(typeof<NullReferenceException>.FullName, failure.RideErrors[0].ExceptionType)
        Assert.Contains("Bolt.ETL.RideFactory", failure.RideErrors[0].StackTrace)

[<Fact>]
let ``mixed input excludes broken rides and counts three separate categories`` () =
    let bad = pastOrderDetail () |> mutate "payment" None
    let data: ScrapedData =
        { Email = "synthetic@example.com"
          Profile = Unchecked.defaultof<JsonElement>
          ActivityHours = Unchecked.defaultof<JsonElement>
          OrderHistory = [||]
          Orders =
            [| rawOrder 1L (previousOrder ()) (pastOrderDetail ())
               rawOrder 2L (cancelledOrder ()) (cancelledDetail ())
               rawOrder 3L (previousOrder ()) bad
               rawOrder 4L (previousOrder () |> mutate "ride_distance_str" (Some "\"\"")) (pastOrderDetail ()) |]
          SkippedOrders = [||] }
    let prepared = Report.prepare data
    Assert.Single prepared.FinishedRides |> ignore
    Assert.Equal(1, prepared.NotFinishedRideCount)
    Assert.Equal<int64>([| 3L; 4L |], prepared.RideErrors |> Array.map _.OrderId)
    let statistics = Bolt.ETL.Analysis.BasicStatistics.generate prepared.FinishedRides
    Assert.Equal(20m, statistics.TotalPaid)
    Assert.Equal(16m, statistics.TotalEarnings)

[<Fact>]
let ``partial report analyses only valid courses and keeps failed course diagnostics`` () =
    let data: ScrapedData =
        { Email = "synthetic@example.com"
          Profile = Unchecked.defaultof<JsonElement>
          ActivityHours = Unchecked.defaultof<JsonElement>
          OrderHistory = [||]
          Orders =
            [| rawOrder 1L (previousOrder ()) (pastOrderDetail ())
               rawOrder 2L (cancelledOrder ()) (cancelledDetail ())
               Bolt.Web.Tests.ReportFixture.brokenOrder |]
          SkippedOrders = [||] }
    let cluster (source: Bolt.ETL.Analysis.RideClustering.RidesDataSource) =
        Assert.Single source.Rows |> ignore
        System.Threading.Tasks.Task.FromResult(Bolt.Web.Tests.ReportFixture.report.PickupClusters)
    let result = Report.runWithClustering cluster data |> fun task -> task.GetAwaiter().GetResult()
    match result with
    | Error error -> failwithf "Unexpected error: %s" error.Message
    | Ok report ->
        Assert.Equal(1, report.RideCount)
        Assert.Equal(1, report.NotFinishedRideCount)
        Assert.Single report.RideErrors |> ignore
        Assert.Equal(99L, report.RideErrors[0].OrderId)
        Assert.Equal(20m, report.BasicStatistics.TotalPaid)
        Assert.Equal(16m, report.BasicStatistics.TotalEarnings)

[<Fact>]
let ``analytics failure does not discard already captured parse errors`` () =
    let data: ScrapedData =
        { Email = "synthetic@example.com"
          Profile = Unchecked.defaultof<JsonElement>
          ActivityHours = Unchecked.defaultof<JsonElement>
          OrderHistory = [||]
          Orders = [| rawOrder 1L (previousOrder ()) (pastOrderDetail ()); Bolt.Web.Tests.ReportFixture.brokenOrder |]
          SkippedOrders = [||] }
    let cluster _ = failwith "analytics unavailable"
    let result = Report.runWithClustering cluster data |> fun task -> task.GetAwaiter().GetResult()
    match result with
    | Ok _ -> failwith "Expected analytics error"
    | Error failure ->
        Assert.Contains("analytics unavailable", failure.Message)
        Assert.Single failure.RideErrors |> ignore
