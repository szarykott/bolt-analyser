module Bolt.Models.PastOrderDetail

open System.Text.Json.Serialization
open Bolt.Models.Shared

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
    Value: Money
    [<JsonPropertyName("hint_action")>]
    HintAction: HintAction option
}

type AccordionItem = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("value")>]
    Value: Money
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