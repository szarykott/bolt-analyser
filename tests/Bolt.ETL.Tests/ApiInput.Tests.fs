module Bolt.ETL.Tests.ApiInputTests

open System
open System.Text.Json
open System.Text.Json.Nodes
open Xunit
open Bolt.ETL
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics
open Bolt.Infrastrucutre.Serialization
open Bolt.Models.BoltApi
open Bolt.Tests.BoltApiFixture

// Characterization tests: expected exceptions describe existing defects, not desired policy.
let private rides previous detail =
    let previous, detail = deserialize previous detail
    RideFactory.getFinishedRides [| previous |] [| detail |]

[<Fact>]
let ``synthetic finished response reaches statistics and pickup preparation`` () =
    let result = rides (previousOrder ()) (pastOrderDetail ())
    let statistics = BasicStatistics.generate result
    Assert.Single result |> ignore
    Assert.Equal(20m, statistics.TotalPaid)
    Assert.Equal(16m, statistics.TotalEarnings)
    Assert.Equal(4m, statistics.TotalCommission)
    Assert.Equal(2.5, statistics.TotalDistanceKm)
    Assert.Equal(50.06, (RideClustering.prepareRideAnalysisSource result).Rows[0].Latitude)

[<Fact>]
let ``cancelled response needs neither payment nor finished ride timestamps`` () =
    Assert.Empty(rides (cancelledOrder ()) (cancelledDetail ()))

let optionalFields =
    seq {
        for previous, path in
            [ true, "accepted_timestamp"; true, "ride_start"; true, "ride_end"
              true, "ride_distance_str"; false, "payment"; false, "earnings" ] do
            for missing in [ true; false ] do
                yield [| box previous; box path; box missing |]
    }

[<Theory; MemberData(nameof optionalFields)>]
let ``missing or null optional fields deserialize but crash finished conversion`` previous path missing =
    let p, d = previousOrder (), pastOrderDetail ()
    mutate path (if missing then None else Some "null") (if previous then p else d) |> ignore
    let p, d = deserialize p d // The JSON boundary accepts this input.
    let ex = Assert.Throws<NullReferenceException>(fun () -> RideFactory.getRide p d |> ignore)
    Assert.Contains("Bolt.ETL.RideFactory", ex.StackTrace)

let requiredFields =
    seq {
        for previous, path in
            [ true, "stops"; true, "created"; true, "created_timestamp"; true, "state"
              true, "order_handle"; true, "stops/0/address"; true, "stops/0/lat"
              false, "payment/items"; false, "payment/items/0/trailing_component"
              false, "payment/items/0/trailing_component/text"
              false, "earnings/accordion_items"; false, "earnings/accordion_items/0/items"
              false, "earnings/accordion_items/0/value"
              false, "earnings/accordion_items/0/items/0/value" ] do
            for replacement in [ None; Some "null"; Some "{}"; Some "true" ] do
                yield [| box previous; box path; box replacement |]
    }

[<Theory; MemberData(nameof requiredFields)>]
let ``required fields are rejected before conversion`` previous path replacement =
    let p, d = previousOrder (), pastOrderDetail ()
    mutate path replacement (if previous then p else d) |> ignore
    Assert.Throws<JsonException>(fun () -> deserialize p d |> ignore) |> ignore

[<Theory>]
[<InlineData(true, "stops/0")>]
[<InlineData(false, "payment/items/0")>]
[<InlineData(false, "earnings/accordion_items/0")>]
[<InlineData(false, "earnings/accordion_items/0/items/0")>]
let ``null record elements cannot enter typed collections`` previous path =
    let p, d = previousOrder (), pastOrderDetail ()
    mutate path (Some "null") (if previous then p else d) |> ignore
    Assert.Throws<JsonException>(fun () -> deserialize p d |> ignore) |> ignore

[<Theory; InlineData(0); InlineData(1)>]
let ``short earnings accordion reaches an invalid list access`` count =
    let d = pastOrderDetail ()
    let items = d.["earnings"].["accordion_items"].AsArray()
    while items.Count > count do items.RemoveAt(items.Count - 1)
    Assert.Throws<InvalidOperationException>(fun () -> rides (previousOrder ()) d |> ignore) |> ignore

[<Fact>]
let ``reordered earnings silently lose commission`` () =
    let d = pastOrderDetail ()
    let items = d.["earnings"].["accordion_items"].AsArray()
    let first, second = items.[0].DeepClone(), items.[1].DeepClone()
    items[0] <- second
    items[1] <- first
    let statistics = BasicStatistics.generate (rides (previousOrder ()) d)
    Assert.Equal(20m, statistics.TotalEarnings) // Baseline is 16; commission was discarded.
    Assert.Equal(0m, statistics.TotalCommission)

[<Fact>]
let ``empty payment and earnings items are accepted as incomplete totals`` () =
    let d = pastOrderDetail ()
    mutate "payment/items" (Some "[]") d |> ignore
    mutate "earnings/accordion_items/0/items" (Some "[]") d |> ignore
    let statistics = BasicStatistics.generate (rides (previousOrder ()) d)
    Assert.Equal(0m, statistics.TotalPaid)
    Assert.Equal(-4m, statistics.TotalEarnings)

[<Fact>]
let ``empty stops survive conversion then fail during pickup preparation`` () =
    let p = previousOrder () |> mutate "stops" (Some "[]")
    let result = rides p (pastOrderDetail ())
    Assert.Throws<IndexOutOfRangeException>(fun () -> RideClustering.prepareRideAnalysisSource result |> ignore) |> ignore

[<Fact>]
let ``one malformed ride aborts a mixed history instead of returning valid rides`` () =
    let p, d = deserialize (previousOrder ()) (pastOrderDetail ())
    let _, bad = deserialize (previousOrder ()) (pastOrderDetail () |> mutate "payment" None)
    Assert.Throws<NullReferenceException>(fun () -> RideFactory.getFinishedRides [| p; p |] [| d; bad |] |> ignore) |> ignore

[<Fact>]
let ``order and detail arrays must have equal lengths`` () =
    let p, d = deserialize (previousOrder ()) (pastOrderDetail ())
    for previous, details in [ [| p |], [||]; [||], [| d |] ] do
        Assert.Throws<ArgumentException>(fun () -> RideFactory.getFinishedRides previous details |> ignore) |> ignore

[<Fact>]
let ``unknown order state is silently excluded`` () =
    let p = previousOrder () |> mutate "state" (Some "\"finished_v2\"")
    Assert.Empty(rides p (cancelledDetail ()))

let malformedText =
    seq {
        for field, json, error in
            [ "price_str", "\"\"", typeof<ArgumentOutOfRangeException>
              "tip_str", "\"Napiwek\"", typeof<ArgumentOutOfRangeException>
              "ride_distance_str", "\"1\"", typeof<ArgumentOutOfRangeException>
              "price_str", "\"20.00 zł\"", typeof<FormatException>
              "price_str", "\"x zł\"", typeof<FormatException>
              "tip_str", "\"Napiwek x zł\"", typeof<FormatException>
              "price_str", "\"79228162514264337593543950336 zł\"", typeof<OverflowException>
              "created", "\"2026.02.30 12:00\"", typeof<FormatException>
              "created_timestamp", "253402300800", typeof<ArgumentOutOfRangeException> ] do
            yield [| box field; box json; box error |]
    }

[<Theory; MemberData(nameof malformedText)>]
let ``malformed text fails at the converter with a specific exception`` field json error =
    let p = previousOrder () |> mutate field (Some json)
    Assert.Throws(error, fun () -> Json.deserializeNode<PreviousOrder> p |> ignore) |> ignore

[<Theory>]
[<InlineData("", typeof<ArgumentOutOfRangeException>)>]
[<InlineData("20.00 zł", typeof<FormatException>)>]
[<InlineData("79228162514264337593543950336 zł", typeof<OverflowException>)>]
let ``payment display text parses later than money converter fields`` (text: string) error =
    let d = pastOrderDetail () |> mutate "payment/items/0/trailing_component/text" (Some(Json.serialize text))
    let p, d = deserialize (previousOrder ()) d
    Assert.Throws(error, fun () -> RideFactory.getRide p d |> ignore) |> ignore

[<Theory>]
[<InlineData("20,00 zł")>]
[<InlineData("20,00 zł")>]
[<InlineData("20,00xyz")>]
let ``money and payment display text accept any three character suffix`` text =
    let p = previousOrder () |> mutate "price_str" (Some(Json.serialize text))
    let d = pastOrderDetail () |> mutate "payment/items/0/trailing_component/text" (Some(Json.serialize text))
    let p, d = deserialize p d
    Assert.Equal(20m, p.Price.Value.Value)
    let result = RideFactory.getFinishedRides [| p |] [| d |]
    Assert.Equal(20m, (BasicStatistics.generate result).TotalPaid)

[<Fact>]
let ``tip prefix and distance suffix are not validated`` () =
    let p =
        previousOrder ()
        |> mutate "tip_str" (Some "\"XXXXXXXX5,00xyz\"")
        |> mutate "ride_distance_str" (Some "\"2.5mi\"")
        |> Json.deserializeNode<PreviousOrder>
    Assert.Equal(5m, p.Tip.Value.Value)
    Assert.Equal(2.5, p.RideDistance.Value.Value)

[<Fact>]
let ``comma decimal distance is silently interpreted as a thousands separator`` () =
    let p = previousOrder () |> mutate "ride_distance_str" (Some "\"2,5km\"")
    let statistics = BasicStatistics.generate (rides p (pastOrderDetail ()))
    Assert.Equal(25., statistics.TotalDistanceKm)

[<Theory; InlineData("NaNkm"); InlineData("Infinitykm"); InlineData("1e999km")>]
let ``nonfinite distances reach the domain and statistics`` text =
    let p = previousOrder () |> mutate "ride_distance_str" (Some(Json.serialize text))
    let statistics = BasicStatistics.generate (rides p (pastOrderDetail ()))
    Assert.False(Double.IsFinite statistics.TotalDistanceKm)

[<Fact>]
let ``timestamp boundary values deserialize but need not form a sensible ride`` () =
    let p =
        previousOrder ()
        |> mutate "accepted_timestamp" (Some "253402300799")
        |> mutate "ride_end" (Some "-62135596800")
    let result = rides p (pastOrderDetail ())
    Assert.True(result[0].Times.RideEnd < result[0].Times.AcceptedTimestamp)

[<Fact>]
let ``top level null differs from null in a required record field`` () =
    Assert.Throws<NullReferenceException>(fun () -> Json.deserialize<Money> "null" |> ignore) |> ignore
    Assert.Throws<NullReferenceException>(fun () -> Json.deserialize<Tip> "null" |> ignore) |> ignore
    Assert.Throws<NullReferenceException>(fun () -> Json.deserialize<Distance> "null" |> ignore) |> ignore
    Assert.Null(Json.deserialize<PreviousOrder[]> "null")
    Assert.Throws<JsonException>(fun () -> Json.deserialize<PreviousOrder> "null" |> ignore) |> ignore

[<Theory; InlineData("null"); InlineData("{}"); InlineData("{\"clusters\":null}")>]
let ``malformed cluster records are rejected by the record converter`` json =
    Assert.Throws<JsonException>(fun () -> Json.deserialize<StDbscanResponse> json |> ignore) |> ignore

[<Fact>]
let ``empty and inconsistent cluster responses are accepted without alignment checks`` () =
    let response = Json.deserialize<StDbscanResponse> """{"labels":[],"n_points":1,"n_clusters":0,"n_noise":0,"clusters":[]}"""
    let points: RideClustering.PickupPoint[] = [| { Latitude = 50.; Longitude = 19.; Hour = 12. } |]
    let result = RideClustering.fromAnalyticsResponse points response
    Assert.Single result.Points |> ignore
    Assert.Empty result.Labels

[<Theory; InlineData(1); InlineData(2); InlineData(3)>]
let ``weather analyses also dereference None when weather is empty`` analysis =
    let result = rides (previousOrder ()) (pastOrderDetail ())
    let weather = Bolt.Models.Meteo.Weather.create { Latitude = 50.; Longitude = 19. } Seq.empty
    Assert.Throws<NullReferenceException>(fun () ->
        match analysis with
        | 1 -> PerRide1.prepareRideAnalysisSource weather Seq.empty result |> ignore
        | 2 -> PerRide2.prepareRideAnalysisSource weather Seq.empty result |> ignore
        | _ -> PerHour.prepareHourlySource weather Seq.empty result |> ignore) |> ignore

[<Fact>]
let ``zero distance is accepted but per ride price per kilometre divides by zero`` () =
    let p = previousOrder () |> mutate "ride_distance_str" (Some "\"0km\"")
    let ride = (rides p (pastOrderDetail ()))[0]
    let weather: Bolt.Models.Meteo.WeatherDataPoint =
        { Temperature = 15.0<Bolt.Models.Meteo.celcius>
          Rain = 0.0<Bolt.Models.Meteo.mm>
          Snow = 0.0<Bolt.Models.Meteo.cm> }
    Assert.Throws<DivideByZeroException>(fun () ->
        PerRide1.RideRow.fromRide (fun _ -> weather) (fun _ -> None) ride |> ignore) |> ignore

[<Theory; MemberData(nameof optionalFields)>]
let ``safe parser classifies missing data and preserves exact responses`` previous path missing =
    let p, d = previousOrder (), pastOrderDetail ()
    mutate path (if missing then None else Some "null") (if previous then p else d) |> ignore
    let raw = rawOrder 123L p d
    match (RideFactory.parseOrder raw).Data with
    | Bolt.Models.RideType.Error error ->
        Assert.Equal(Some 123L, error.OrderId)
        Assert.Equal("ride", error.Stage)
        Assert.Equal(typeof<NullReferenceException>.FullName, error.ExceptionType)
        Assert.Contains("RideFactory.get", error.StackTrace)
        Assert.Equal(raw.PreviousOrder.GetRawText(), error.PreviousOrder.Value.GetRawText())
        Assert.Equal(raw.PastOrderDetail.GetRawText(), error.PastOrderDetail.Value.GetRawText())
    | _ -> failwith "Expected a failed ride"

[<Theory>]
[<InlineData(true, "ride_distance_str", "\"\"", "previous_order")>]
[<InlineData(false, "earnings/accordion_items/0/value", "\"\"", "past_order_detail")>]
[<InlineData(false, "earnings/accordion_items", "[]", "ride")>]
[<InlineData(true, "stops", "[]", "ride")>]
[<InlineData(true, "ride_distance_str", "\"NaNkm\"", "ride")>]
let ``safe parser captures deserialization conversion and pickup validation failures`` previous path value stage =
    let p, d = previousOrder (), pastOrderDetail ()
    mutate path (Some value) (if previous then p else d) |> ignore
    match (RideFactory.parseOrder (rawOrder 1L p d)).Data with
    | Bolt.Models.RideType.Error error ->
        Assert.Equal(stage, error.Stage)
        Assert.False(String.IsNullOrWhiteSpace error.Message)
        Assert.False(String.IsNullOrWhiteSpace error.StackTrace)
    | _ -> failwith "Expected an error category"

[<Fact>]
let ``safe parser keeps finished and cancelled categories`` () =
    let finished = RideFactory.parseOrder (rawOrder 1L (previousOrder ()) (pastOrderDetail ()))
    let cancelled = RideFactory.parseOrder (rawOrder 2L (cancelledOrder ()) (cancelledDetail ()))
    match finished.Data, cancelled.Data with
    | Bolt.Models.RideType.Finished _, Bolt.Models.RideType.DidNotHappen _ -> ()
    | _ -> failwith "Expected original ride categories"
