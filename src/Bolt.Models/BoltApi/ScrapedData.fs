namespace Bolt.Models.BoltApi

open System.Text.Json
open System.Text.Json.Nodes

/// Everything one scrape run produces, kept in memory for the lifetime of the
/// job. Persisted to disk only in DEBUG builds; production never writes user
/// data.
type SkippedOrder = { OrderId: int64; Reason: string }

type RawOrder = {
    OrderId: int64
    PreviousOrder: JsonElement
    PastOrderDetail: JsonElement
}

type RideError = {
    OrderId: int64
    Stage: string
    ExceptionType: string
    Message: string
    StackTrace: string
    PreviousOrder: JsonElement
    PastOrderDetail: JsonElement
}

type ScrapedData = {
    Email: string
    /// Raw getDriverProfile response; nothing reads it typed yet.
    Profile: JsonElement
    /// Raw getActivityHours response; nothing reads it typed yet.
    ActivityHours: JsonElement
    /// Raw order-history entries as returned by getOrderHistory.
    OrderHistory: JsonNode[]
    Orders: RawOrder[]
    SkippedOrders: SkippedOrder[]
}
