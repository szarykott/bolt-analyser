module Bolt.Tests.BoltApiFixture

open System.Text.Json.Nodes
open Bolt.Infrastrucutre.Serialization
open Bolt.Models.BoltApi

// Synthetic API responses. Each caller gets fresh nodes so mutations stay isolated.
let previousOrder () =
    JsonNode.Parse """{
        "order_handle": {"order_id": 1, "city_id": 1, "order_system": "bolt"},
        "address": "Test start", "brand": "bolt",
        "created": "2026.07.01 12:00", "created_timestamp": 1782907200,
        "accepted_timestamp": 1782907260,
        "payment_type": "cash", "payment_method_id": "cash", "payment_method_type": "cash",
        "payment_details": {"text": "Gotówka", "icon": {"type": "normal"}},
        "contact_option_available": false, "precise_location_privacy_note": "",
        "price_review_status": "", "ride_distance_str": "2.5km",
        "ride_start": 1782907320, "ride_end": 1782907920,
        "state": "finished", "status_html": "",
        "stops": [
            {"type": "departure", "lat": 50.06, "lng": 19.94, "address": "Test start", "accuracy_m": 1},
            {"type": "arrival", "lat": 50.07, "lng": 19.95, "address": "Test end", "accuracy_m": 1}
        ],
        "trip_audio_recording": {"succeded": false}
    }"""

let pastOrderDetail () =
    JsonNode.Parse """{
        "title": "Test ride",
        "summary": {"labels": [], "stops": [], "route_info": "", "items": []},
        "can_call_client": false, "audio_recording_data": {"succeded": false},
        "payment": {"title": "Zapłacono", "items": [
            {"title": "Gotówka", "subtitle": "", "trailing_component": {"type": "text", "text": "20,00 zł"}}
        ]},
        "earnings": {"title": "Zarobki", "accordion_items": [
            {"title": "Przychód", "value": "20,00 zł", "indented": false,
             "items": [{"title": "Cena kursu", "value": "20,00 zł"}]},
            {"title": "Prowizja Bolt", "value": "-4,00 zł", "indented": false, "items": []}
        ], "list_items": []}
    }"""

let cancelledOrder () =
    let node = previousOrder ()
    node["state"] <- JsonValue.Create "cancelled"
    for field in [ "accepted_timestamp"; "ride_start"; "ride_end"; "ride_distance_str" ] do
        node.AsObject().Remove field |> ignore
    node

let cancelledDetail () =
    let node = pastOrderDetail ()
    node.AsObject().Remove "payment" |> ignore
    node.AsObject().Remove "earnings" |> ignore
    node

let deserialize (previous: JsonNode) (detail: JsonNode) =
    Json.deserializeNode<PreviousOrder> previous, Json.deserializeNode<PastOrderDetail> detail

// Paths only address the fixed synthetic fixture (no user-supplied JSON pointers).
let mutate (path: string) (replacement: string option) (node: JsonNode) =
    let segments = path.Split('/')
    let parent =
        segments[..segments.Length - 2]
        |> Array.fold (fun (current: JsonNode) key ->
            match current with
            | :? JsonArray -> current[int key]
            | _ -> current[key]) node
    let key = Array.last segments
    match parent, replacement with
    | (:? JsonArray as array), Some json -> array[int key] <- JsonNode.Parse json
    | _, Some json -> parent[key] <- JsonNode.Parse json
    | _, None -> parent.AsObject().Remove key |> ignore
    node
