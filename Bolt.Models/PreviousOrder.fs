module Bolt.Models.PreviousOrder

open System
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open Bolt.Models.Shared

type PaymentDetails = {
    [<JsonPropertyName("text")>]
    Text: string
    [<JsonPropertyName("icon")>]
    Icon: Image
}

type StopType =
    | [<JsonName("arrival")>] Arrival
    | [<JsonName("departure")>] Departure

type TripStop = {
    [<JsonPropertyName("type")>]
    Type: StopType
    [<JsonPropertyName("lat")>]
    Lat: float
    [<JsonPropertyName("lng")>]
    Lng: float
    [<JsonPropertyName("address")>]
    Address: string
    [<JsonPropertyName("accuracy_m")>]
    AccuracyM: int
    [<JsonPropertyName("timestamp")>]
    Timestamp: UnixTime option
}

type TripAudioRecording = {
    [<JsonPropertyName("succeded")>]
    Succeded: bool
    [<JsonPropertyName("recording_info")>]
    RecordingInfo: JsonNode option
}

type PreviousOrder = {
    [<JsonPropertyName("order_handle")>]
    OrderHandle: OrderHandle
    [<JsonPropertyName("address")>]
    Address: string
    [<JsonPropertyName("brand")>]
    Brand: Brand
    [<JsonPropertyName("created")>]
    Created: DateTimeOffset
    [<JsonPropertyName("created_timestamp")>]
    CreatedTimestamp: UnixTime
    [<JsonPropertyName("accepted_timestamp")>]
    AcceptedTimestamp: UnixTime option
    [<JsonPropertyName("payment_type")>]
    PaymentType: PaymentType
    [<JsonPropertyName("payment_method_id")>]
    PaymentMethodId: string
    [<JsonPropertyName("payment_method_type")>]
    PaymentMethodType: PaymentMethodType
    [<JsonPropertyName("payment_details")>]
    PaymentDetails: PaymentDetails
    [<JsonPropertyName("cash_campaign_str")>]
    CashCampaign: Money option
    [<JsonPropertyName("cash_payment_str")>]
    CashPayment: Money option
    [<JsonPropertyName("contact_option_available")>]
    ContactOptionAvailable: bool
    [<JsonPropertyName("precise_location_privacy_note")>]
    PreciseLocationPrivacyNote: string
    [<JsonPropertyName("price_str")>]
    Price: Money option
    [<JsonPropertyName("tip_str")>]
    Tip: Tip option
    [<JsonPropertyName("tip_gratitude_info")>]
    TipGratitudeInfo: TipGratitudeInfo option
    [<JsonPropertyName("rewards_points_str")>]
    RewardsPointsStr: string option
    [<JsonPropertyName("price_review_status")>]
    PriceReviewStatus: string
    [<JsonPropertyName("ride_distance_str")>]
    RideDistance: Distance option
    [<JsonPropertyName("ride_start")>]
    RideStart: UnixTime option
    [<JsonPropertyName("ride_end")>]
    RideEnd: UnixTime option
    [<JsonPropertyName("state")>]
    State: OrderState
    [<JsonPropertyName("status_html")>]
    StatusHtml: string
    [<JsonPropertyName("stops")>]
    Stops: TripStop list
    [<JsonPropertyName("trip_audio_recording")>]
    TripAudioRecording: TripAudioRecording
}