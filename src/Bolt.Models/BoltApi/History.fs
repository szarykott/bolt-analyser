namespace Bolt.Models.BoltApi

open System
open System.Text.Json.Serialization
open Bolt.Models.BoltApi.Shared

type HistoryOrderHandle = {
    [<JsonPropertyName("order_handle")>]
    OrderHandle: OrderHandle
}

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
    Price: Money option
    [<JsonPropertyName("tip_str")>]
    Tip: Tip option
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