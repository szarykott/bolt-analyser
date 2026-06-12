module Bolt.App.bolt.BoltModels

open System
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open Bolt.App.Serialization

type OrderHandle = {
    [<JsonPropertyName("order_id")>]
    OrderId: int64
    [<JsonPropertyName("city_id")>]
    CityId: int32
    [<JsonPropertyName("order_system")>]
    OrderSystem: string
}

type HistoryOrderHandle = {
    [<JsonPropertyName("order_handle")>]
    OrderHandle: OrderHandle
}

// --- Shared primitives ---

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

// --- activityHours.json ---

type ActivityDay = {
    [<JsonPropertyName("date")>]
    Date: DateOnly
    [<JsonPropertyName("active_seconds")>]
    ActiveSeconds: int
}

type ActivityPeriod = {
    [<JsonPropertyName("key")>]
    Key: string
    [<JsonPropertyName("start_date")>]
    StartDate: DateOnly
    [<JsonPropertyName("end_date")>]
    EndDate: DateOnly
    [<JsonPropertyName("active_seconds")>]
    ActiveSeconds: int
    [<JsonPropertyName("items")>]
    Items: ActivityDay list
}

type ActivityHours = {
    [<JsonPropertyName("description_text")>]
    DescriptionText: string
    [<JsonPropertyName("number_of_months")>]
    NumberOfMonths: int
    [<JsonPropertyName("periods")>]
    Periods: ActivityPeriod list
}

// --- driverProfile.json ---

type ActionLink = {
    [<JsonPropertyName("text")>]
    Text: string
    [<JsonPropertyName("url")>]
    Url: string
    [<JsonPropertyName("appearance")>]
    Appearance: string
    [<JsonPropertyName("analytics_event")>]
    AnalyticsEvent: AnalyticsEvent
}

type Profile = {
    [<JsonPropertyName("profile_image")>]
    ProfileImage: Image
    [<JsonPropertyName("first_name")>]
    FirstName: string
    [<JsonPropertyName("last_name")>]
    LastName: string
    [<JsonPropertyName("profile_badge")>]
    ProfileBadge: JsonNode option
}

type TileLayout = {
    [<JsonPropertyName("row")>]
    Row: int
    [<JsonPropertyName("column")>]
    Column: int
    [<JsonPropertyName("height")>]
    Height: int
    [<JsonPropertyName("width")>]
    Width: int
}

type TilePayload = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("value_text")>]
    ValueText: string
    [<JsonPropertyName("action_url")>]
    ActionUrl: string option
    [<JsonPropertyName("content")>]
    Content: JsonNode list
    [<JsonPropertyName("analytics_event")>]
    AnalyticsEvent: AnalyticsEvent
}

type ProfileTile = {
    [<JsonPropertyName("type")>]
    Type: string
    [<JsonPropertyName("layout")>]
    Layout: TileLayout
    [<JsonPropertyName("payload")>]
    Payload: TilePayload
}

type Loyalty = {
    [<JsonPropertyName("progress")>]
    Progress: float
    [<JsonPropertyName("tier_icon")>]
    TierIcon: Image
    [<JsonPropertyName("tier_color")>]
    TierColor: Color
    [<JsonPropertyName("tier_text")>]
    TierText: ActionLink
}

type ProfileHeader = {
    [<JsonPropertyName("profile")>]
    Profile: Profile
    [<JsonPropertyName("items")>]
    Items: ProfileTile list
    [<JsonPropertyName("background_color")>]
    BackgroundColor: Color
    [<JsonPropertyName("loyalty")>]
    Loyalty: Loyalty
}

type Compliment = {
    [<JsonPropertyName("id")>]
    Id: int64
    [<JsonPropertyName("text")>]
    Text: string
}

type DriverProfile = {
    [<JsonPropertyName("header")>]
    Header: ProfileHeader
    [<JsonPropertyName("compliments")>]
    Compliments: Compliment list
    [<JsonPropertyName("compliments_preview_count")>]
    ComplimentsPreviewCount: int
    [<JsonPropertyName("link")>]
    Link: ActionLink
    [<JsonPropertyName("see_more_link")>]
    SeeMoreLink: ActionLink
    [<JsonPropertyName("sections")>]
    Sections: JsonNode list
}

// --- orderHistory.json (single array element) ---

type OrderHistory = {
    [<JsonPropertyName("order_handle")>]
    OrderHandle: OrderHandle
    [<JsonPropertyName("address")>]
    Address: string
    [<JsonPropertyName("created")>]
    Created: DateTimeOffset
    [<JsonPropertyName("created_timestamp")>]
    CreatedTimestamp: UnixTime
    [<JsonPropertyName("payment_type")>]
    PaymentType: PaymentType
    [<JsonPropertyName("payment_method_id")>]
    PaymentMethodId: string
    [<JsonPropertyName("payment_method_type")>]
    PaymentMethodType: PaymentMethodType
    [<JsonPropertyName("price_str")>]
    PriceStr: string option
    [<JsonPropertyName("tip_str")>]
    TipStr: string option
    [<JsonPropertyName("price_review_status")>]
    PriceReviewStatus: string
    [<JsonPropertyName("state")>]
    State: OrderState
    [<JsonPropertyName("accepted_timestamp")>]
    AcceptedTimestamp: UnixTime option
    [<JsonPropertyName("status_html")>]
    StatusHtml: string
    [<JsonPropertyName("payment_icon")>]
    PaymentIcon: Image option
}

// --- previousOrders.json (single array element) ---

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
    CashCampaignStr: string option
    [<JsonPropertyName("cash_payment_str")>]
    CashPaymentStr: string option
    [<JsonPropertyName("contact_option_available")>]
    ContactOptionAvailable: bool
    [<JsonPropertyName("precise_location_privacy_note")>]
    PreciseLocationPrivacyNote: string
    [<JsonPropertyName("price_str")>]
    PriceStr: string option
    [<JsonPropertyName("tip_str")>]
    TipStr: string option
    [<JsonPropertyName("tip_gratitude_info")>]
    TipGratitudeInfo: TipGratitudeInfo option
    [<JsonPropertyName("rewards_points_str")>]
    RewardsPointsStr: string option
    [<JsonPropertyName("price_review_status")>]
    PriceReviewStatus: string
    [<JsonPropertyName("ride_distance_str")>]
    RideDistanceStr: string option
    [<JsonPropertyName("ride_start")>]
    RideStart: int64 option
    [<JsonPropertyName("ride_end")>]
    RideEnd: int64 option
    [<JsonPropertyName("state")>]
    State: OrderState
    [<JsonPropertyName("status_html")>]
    StatusHtml: string
    [<JsonPropertyName("stops")>]
    Stops: TripStop list
    [<JsonPropertyName("trip_audio_recording")>]
    TripAudioRecording: TripAudioRecording
}

// --- pastOrderDetails.json (single array element) ---

type SummaryLabel = {
    [<JsonPropertyName("icon")>]
    Icon: Image
    [<JsonPropertyName("text")>]
    Text: string
}

type StopLocation = {
    [<JsonPropertyName("lat")>]
    Lat: float
    [<JsonPropertyName("lng")>]
    Lng: float
    [<JsonPropertyName("accuracy_in_meters")>]
    AccuracyInMeters: int
}

type SummaryStop = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("value")>]
    Value: string
    [<JsonPropertyName("is_cancelled")>]
    IsCancelled: bool
    [<JsonPropertyName("color")>]
    Color: Color
    [<JsonPropertyName("location")>]
    Location: StopLocation option
}

type SummaryItem = {
    [<JsonPropertyName("lead_component")>]
    LeadComponent: LeadComponent
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("subtitle")>]
    Subtitle: string
    [<JsonPropertyName("trailing_component")>]
    TrailingComponent: TrailingComponent
    [<JsonPropertyName("action_url")>]
    ActionUrl: string
    [<JsonPropertyName("analytics_event")>]
    AnalyticsEvent: AnalyticsEvent
}

type OrderSummary = {
    [<JsonPropertyName("labels")>]
    Labels: SummaryLabel list
    [<JsonPropertyName("stops")>]
    Stops: SummaryStop list
    [<JsonPropertyName("route_info")>]
    RouteInfo: string
    [<JsonPropertyName("items")>]
    Items: SummaryItem list
}

type AudioRecordingData = {
    [<JsonPropertyName("succeded")>]
    Succeded: bool
}

type PaymentItem = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("subtitle")>]
    Subtitle: string
    [<JsonPropertyName("trailing_component")>]
    TrailingComponent: TrailingComponent
}

type OrderPayment = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("items")>]
    Items: PaymentItem list
    [<JsonPropertyName("tip_gratitude_info")>]
    TipGratitudeInfo: TipGratitudeInfo option
}

type DialogButtonAction = {
    [<JsonPropertyName("type")>]
    Type: string
}

type DialogButton = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("appearance")>]
    Appearance: string
    [<JsonPropertyName("size")>]
    Size: string
    [<JsonPropertyName("action")>]
    Action: DialogButtonAction
}

type Dialog = {
    [<JsonPropertyName("type")>]
    Type: string
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("subtitle")>]
    Subtitle: string
    [<JsonPropertyName("message")>]
    Message: string
    [<JsonPropertyName("buttons")>]
    Buttons: DialogButton list
}

type HintAction = {
    [<JsonPropertyName("type")>]
    Type: string
    [<JsonPropertyName("dialog")>]
    Dialog: Dialog
}

type EarningsLineItem = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("value")>]
    Value: string
    [<JsonPropertyName("hint_action")>]
    HintAction: HintAction option
}

type AccordionItem = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("value")>]
    Value: string
    [<JsonPropertyName("hint_action")>]
    HintAction: HintAction option
    [<JsonPropertyName("items")>]
    Items: EarningsLineItem list
    [<JsonPropertyName("indented")>]
    Indented: bool
}

type EarningsListItem = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("trailing_component")>]
    TrailingComponent: TrailingComponent
    [<JsonPropertyName("has_separator")>]
    HasSeparator: bool
}

type OrderEarnings = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("accordion_items")>]
    AccordionItems: AccordionItem list
    [<JsonPropertyName("list_items")>]
    ListItems: EarningsListItem list
}

type PastOrderDetail = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("summary")>]
    Summary: OrderSummary
    [<JsonPropertyName("can_call_client")>]
    CanCallClient: bool
    [<JsonPropertyName("audio_recording_data")>]
    AudioRecordingData: AudioRecordingData
    [<JsonPropertyName("payment")>]
    Payment: OrderPayment option
    [<JsonPropertyName("earnings")>]
    Earnings: OrderEarnings option
}