module Bolt.ETL.Tests.ApiInputPropertiesTests

open System
open System.Globalization
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Bolt.ETL
open Bolt.ETL.Analysis
open Bolt.Infrastrucutre.Serialization
open Bolt.Models.BoltApi
open Bolt.Tests.BoltApiFixture

let private check seed property =
    let config =
        Config.QuickThrowOnFailure
            .WithMaxTest(1000)
            .WithReplay(Some { Rnd = Rnd(seed); Size = None })
    Check.One(config, property)

let private pl = CultureInfo.GetCultureInfo "pl-PL"
let private moneyText (value: decimal) = value.ToString("0.00", pl) + "\u00a0zł"

[<Theory; InlineData(101UL); InlineData(2027UL); InlineData(104729UL)>]
let ``wire precision money tip and distance round trips preserve values`` seed =
    let values = Gen.zip (Gen.choose(-10000000, 10000000)) (Gen.choose(0, 100000))
    Prop.forAll (Arb.fromGen values) (fun (cents, tenths) ->
        let amount = decimal cents / 100m
        let distance = float tenths / 10.
        Json.deserialize<Money> (Json.serialize (Money amount)) = Money amount
        && Json.deserialize<Tip> (Json.serialize (Tip amount)) = Tip amount
        && Json.deserialize<Distance> (Json.serialize (Distance distance)) = Distance distance)
    |> check seed

[<Theory; InlineData(101UL); InlineData(2027UL); InlineData(104729UL)>]
let ``generated finished responses preserve payment earnings distance and pickup`` seed =
    let values = Gen.zip (Gen.choose(0, 1000000)) (Gen.choose(0, 100000))
    Prop.forAll (Arb.fromGen values) (fun (cents, tenths) ->
        let amount = decimal cents / 100m
        let commission = Decimal.Round(amount / 5m, 2)
        let distance = float tenths / 10.
        let p =
            previousOrder ()
            |> mutate "ride_distance_str" (Some(Json.serialize (distance.ToString("0.0", CultureInfo.InvariantCulture) + "km")))
        let d =
            pastOrderDetail ()
            |> mutate "payment/items/0/trailing_component/text" (Some(Json.serialize (moneyText amount)))
            |> mutate "earnings/accordion_items/0/items/0/value" (Some(Json.serialize (moneyText amount)))
            |> mutate "earnings/accordion_items/1/value" (Some(Json.serialize (moneyText -commission)))
        let p, d = deserialize p d
        let rides = RideFactory.getFinishedRides [| p |] [| d |]
        let statistics = BasicStatistics.generate rides
        statistics.TotalPaid = amount
        && statistics.TotalEarnings = amount - commission
        && statistics.TotalDistanceKm = distance
        && (RideClustering.prepareRideAnalysisSource rides).Rows[0].Latitude = 50.06)
    |> check seed

let private optionalFields =
    [| true, "accepted_timestamp"; true, "ride_start"; true, "ride_end"
       true, "ride_distance_str"; false, "payment"; false, "earnings" |]

[<Property(MaxTest = 1000, Replay = "101,11400714819323198485")>]
let ``null and omitted optional fields produce identical typed responses`` (choices: uint32 list) =
    let missingP, missingD = previousOrder (), pastOrderDetail ()
    let nullP, nullD = previousOrder (), pastOrderDetail ()
    for choice in choices do
        let previous, path = optionalFields[int (choice % uint32 optionalFields.Length)]
        mutate path None (if previous then missingP else missingD) |> ignore
        mutate path (Some "null") (if previous then nullP else nullD) |> ignore
    deserialize missingP missingD = deserialize nullP nullD

[<Theory; InlineData(101UL); InlineData(2027UL); InlineData(104729UL)>]
let ``optional omissions only crash finished rides and shrink to one omitted field`` seed =
    let scenarios =
        gen {
            let! finished = Gen.elements [ true; false ]
            let! missing = Gen.elements [ true; false ]
            let! count = Gen.choose(0, optionalFields.Length)
            let! fields = Gen.listOfLength count (Gen.choose(0, optionalFields.Length - 1))
            return finished, missing, fields
        }
    let shrink (finished, missing, fields) =
        seq {
            if not (List.isEmpty fields) then yield finished, missing, List.tail fields
        }
    Prop.forAll (Arb.fromGenShrink(scenarios, shrink)) (fun (finished, missing, fields) ->
        let p, d = previousOrder (), pastOrderDetail ()
        if not finished then mutate "state" (Some "\"cancelled\"") p |> ignore
        for index in fields do
            let previous, path = optionalFields[index]
            mutate path (if missing then None else Some "null") (if previous then p else d) |> ignore
        let p, d = deserialize p d
        let ex = Record.Exception(fun () -> RideFactory.getRide p d |> ignore)
        let expectedCrash = finished && not (List.isEmpty fields)
        (if expectedCrash then ex :? NullReferenceException else isNull ex)
        |> Prop.collect (finished, missing, List.distinct fields |> List.length))
    |> check seed

[<Theory; InlineData(101UL); InlineData(2027UL); InlineData(104729UL)>]
let ``generated collection shapes distinguish JSON rejection conversion and pickup failures`` seed =
    let scenarios =
        gen {
            let! stops = Gen.choose(0, 4)
            let! accordion = Gen.choose(0, 4)
            let! nullItem = Gen.elements [ true; false ]
            return stops, accordion, nullItem
        }
    Prop.forAll (Arb.fromGen scenarios) (fun (stops, accordion, nullItem) ->
        let p, d = previousOrder (), pastOrderDetail ()
        let stop = p.["stops"].[0].DeepClone()
        let gross = d.["earnings"].["accordion_items"].[0].DeepClone()
        let fee = d.["earnings"].["accordion_items"].[1].DeepClone()
        let stopArray = p.["stops"].AsArray()
        let earningsArray = d.["earnings"].["accordion_items"].AsArray()
        stopArray.Clear()
        earningsArray.Clear()
        for _ in 1 .. stops do stopArray.Add(stop.DeepClone())
        for index in 0 .. accordion - 1 do
            earningsArray.Add((if index = 0 then gross else fee).DeepClone())
        if nullItem then
            // Include a null record even when the generated stop count is zero.
            stopArray.Add(Unchecked.defaultof<System.Text.Json.Nodes.JsonNode>)
        let mutable stage = "deserialize"
        let ex =
            Record.Exception(fun () ->
                let p, d = deserialize p d
                stage <- "convert"
                let rides = RideFactory.getFinishedRides [| p |] [| d |]
                stage <- "pickup"
                RideClustering.prepareRideAnalysisSource rides |> ignore)
        let matches =
            if nullItem then stage = "deserialize" && ex :? System.Text.Json.JsonException
            elif accordion < 2 then stage = "convert" && ex :? InvalidOperationException
            elif stops = 0 then stage = "pickup" && ex :? IndexOutOfRangeException
            else isNull ex
        matches |> Prop.collect (stage, if isNull ex then "accepted" else ex.GetType().Name))
    |> check seed

[<Theory; InlineData(101UL); InlineData(2027UL); InlineData(104729UL)>]
let ``display text fuzzing records accepted values and known parser failure classes`` seed =
    let scenarios =
        gen {
            let! field = Gen.elements [ "price_str"; "tip_str"; "ride_distance_str"; "payment" ]
            let! length = Gen.choose(0, 35)
            let! chars = Gen.arrayOfLength length (Gen.elements ("0123456789-+,. eNaIfityzłkmNapiwek\u00a0".ToCharArray()))
            return field, String chars
        }
    let shrink (field, text: string) =
        seq {
            for length in 0 .. text.Length - 1 do yield field, text.Substring(0, length)
        }
    Prop.forAll (Arb.fromGenShrink(scenarios, shrink)) (fun (field, text) ->
        let mutable stage = "deserialize"
        let mutable outcome = "accepted"
        let ex =
            Record.Exception(fun () ->
                let p, d = previousOrder (), pastOrderDetail ()
                if field = "payment" then
                    mutate "payment/items/0/trailing_component/text" (Some(Json.serialize text)) d |> ignore
                else mutate field (Some(Json.serialize text)) p |> ignore
                let p, d = deserialize p d
                stage <- "convert"
                RideFactory.getRide p d |> ignore)
        if not (isNull ex) then outcome <- ex.GetType().Name
        (isNull ex || ex :? ArgumentOutOfRangeException || ex :? FormatException || ex :? OverflowException)
        |> Prop.collect (field, stage, outcome))
    |> check seed

[<Theory; InlineData(101UL); InlineData(2027UL); InlineData(104729UL)>]
let ``safe parser classifies generated malformed responses without throwing`` seed =
    let scenarios =
        Gen.zip
            (Gen.elements [ true; false ])
            (Gen.listOf (Gen.choose(0, optionalFields.Length - 1)))
    Prop.forAll (Arb.fromGen scenarios) (fun (finished, fields) ->
        let p, d = previousOrder (), pastOrderDetail ()
        if not finished then mutate "state" (Some "\"cancelled\"") p |> ignore
        for index in fields do
            let previous, path = optionalFields[index]
            mutate path None (if previous then p else d) |> ignore
        let raw = rawOrder 123L p d
        let ride = RideFactory.parseOrder raw
        match ride.Data with
        | Bolt.Models.RideType.Error error ->
            finished && not (List.isEmpty fields)
            && error.OrderId = Some raw.OrderId
            && not (String.IsNullOrWhiteSpace error.StackTrace)
            && error.PreviousOrder.Value.GetRawText() = raw.PreviousOrder.GetRawText()
            && error.PastOrderDetail.Value.GetRawText() = raw.PastOrderDetail.GetRawText()
        | Bolt.Models.RideType.Finished _ -> finished && List.isEmpty fields
        | Bolt.Models.RideType.DidNotHappen _ -> not finished)
    |> check seed
