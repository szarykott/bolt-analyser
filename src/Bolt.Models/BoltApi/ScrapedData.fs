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
    OrderId: int64 option
    Stage: string
    ExceptionType: string
    Message: string
    StackTrace: string
    PreviousOrder: JsonElement option
    PastOrderDetail: JsonElement option
    HistoryEntry: JsonElement option
    ResponseBody: string option
    Endpoint: string option
}

module RideError =
    let create orderId stage (ex: exn) : RideError =
        { OrderId = orderId
          Stage = stage
          ExceptionType = ex.GetType().FullName
          Message = ex.Message
          StackTrace = ex.ToString()
          PreviousOrder = None
          PastOrderDetail = None
          HistoryEntry = None
          ResponseBody = None
          Endpoint = None }

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
    Diagnostics: RideError[]
}
