module Bolt.Models.Shared

open System
open System.Text.Json.Serialization

type OrderHandle = {
    [<JsonPropertyName("order_id")>]
    OrderId: int64
    [<JsonPropertyName("city_id")>]
    CityId: int32
    [<JsonPropertyName("order_system")>]
    OrderSystem: string
}

type ImageUrl = {
    [<JsonPropertyName("url")>]
    Url: string
}

type ColorType =
    | [<JsonName("functional_color")>] FunctionalColor
    | [<JsonName("hex_color")>] HexColor

/// Tagged object: "functional_color" carries Reference, "hex_color" carries Light/Dark.
type Color = {
    [<JsonPropertyName("type")>]
    Type: ColorType
    [<JsonPropertyName("reference")>]
    Reference: string option
    [<JsonPropertyName("light")>]
    Light: string option
    [<JsonPropertyName("dark")>]
    Dark: string option
}

type ImageType =
    | [<JsonName("normal")>] Normal
    | [<JsonName("tintable")>] Tintable

/// Tagged object: "normal" carries LightUrl/DarkUrl, "tintable" carries TintableUrl/TintableColor.
type Image = {
    [<JsonPropertyName("type")>]
    Type: ImageType
    [<JsonPropertyName("light_url")>]
    LightUrl: ImageUrl option
    [<JsonPropertyName("dark_url")>]
    DarkUrl: ImageUrl option
    [<JsonPropertyName("tintable_url")>]
    TintableUrl: ImageUrl option
    [<JsonPropertyName("tintable_color")>]
    TintableColor: Color option
}

type AnalyticsParams = {
    [<JsonPropertyName("partner_id")>]
    PartnerId: int64
    [<JsonPropertyName("driver_id")>]
    DriverId: int64
    [<JsonPropertyName("tier")>]
    Tier: int option
    [<JsonPropertyName("tier_name")>]
    TierName: string option
    [<JsonPropertyName("tier_progress")>]
    TierProgress: float option
}

type TappedEvent = {
    [<JsonPropertyName("name")>]
    Name: string
    [<JsonPropertyName("params")>]
    Params: AnalyticsParams option
}

type AnalyticsEvent = {
    [<JsonPropertyName("tapped")>]
    Tapped: TappedEvent
}

type TrailingComponentType =
    | [<JsonName("text")>] Text
    | [<JsonName("text_image")>] TextImage

/// Tagged object: "text" carries TextType, "text_image" carries Image. Text present in both.
type TrailingComponent = {
    [<JsonPropertyName("type")>]
    Type: TrailingComponentType
    [<JsonPropertyName("text")>]
    Text: string
    [<JsonPropertyName("text_type")>]
    TextType: string option
    [<JsonPropertyName("image")>]
    Image: Image option
}

type LeadComponent = {
    [<JsonPropertyName("type")>]
    Type: string
    [<JsonPropertyName("image")>]
    Image: Image
}

type TipGratitudeInfo = {
    [<JsonPropertyName("has_driver_thanked")>]
    HasDriverThanked: bool
    [<JsonPropertyName("trip_details_message")>]
    TripDetailsMessage: string option
}

// --- Order enums ---

type OrderState =
    | [<JsonName("client_cancelled")>] ClientCancelled
    | [<JsonName("client_did_not_show")>] ClientDidNotShow
    | [<JsonName("driver_did_not_respond")>] DriverDidNotRespond
    | [<JsonName("driver_rejected")>] DriverRejected
    | [<JsonName("finished")>] Finished

type PaymentType =
    | [<JsonName("cash")>] Cash
    | [<JsonName("inapp")>] InApp

type PaymentMethodType =
    | [<JsonName("adyen_blik")>] AdyenBlik
    | [<JsonName("applepay")>] ApplePay
    | [<JsonName("business")>] Business
    | [<JsonName("card")>] Card
    | [<JsonName("default")>] Default
    | [<JsonName("googlepay")>] GooglePay

type Brand =
    | [<JsonName("bolt")>] Bolt
    | [<JsonName("hopp")>] Hopp
    
/// Tip amount; on the wire "Napiwek N,NN zł" — plain Money with a
/// constant "Napiwek " prefix.
[<Struct>]
type Tip =
    | Tip of decimal

    member this.Value =
        let (Tip v) = this
        v

/// Distance in kilometres; on the wire "1.1km" or "10km" (dot decimal
/// separator, at most one decimal, no space).
[<Struct>]
type Distance =
    | Distance of decimal

    member this.Value =
        let (Distance v) = this
        v

/// PLN amount; on the wire "N,NN zł" with a no-break space (U+00A0)
/// before "zł", e.g. "19,20 zł", "-7,03 zł".
[<Struct>]
type Money =
    | Money of decimal

    member this.Value =
        let (Money v) = this
        v

/// Unix-seconds timestamp on the wire, DateTimeOffset in the model.
/// Distinct type so its converter can't collide with the global
/// DateTimeOffset converter used for "yyyy.MM.dd HH:mm" strings.
[<Struct>]
type UnixTime =
    | UnixTime of DateTimeOffset

    member this.Value =
        let (UnixTime v) = this
        v