module Bolt.Web.Tests.ReportTests

open System
open System.Text.Json
open Xunit
open Bolt.Models.BoltApi
open Bolt.Web
open Bolt.Tests.BoltApiFixture

[<Fact>]
let ``report rejects history without completed rides`` () =
    let data: ScrapedData =
        { Email = "driver@example.com"
          Profile = JsonDocument.Parse("{}").RootElement
          ActivityHours = JsonDocument.Parse("{}").RootElement
          OrderHistory = [||]
          PreviousOrders = [||]
          PastOrderDetails = [||]
          SkippedOrders = [||] }
    let result = Report.run data |> fun task -> task.GetAwaiter().GetResult()
    Assert.Equal(Error "Nie znaleziono zakończonych przejazdów dla driver@example.com", result)

[<Theory>]
[<InlineData(true, "accepted_timestamp")>]
[<InlineData(true, "ride_start")>]
[<InlineData(true, "ride_end")>]
[<InlineData(true, "ride_distance_str")>]
[<InlineData(false, "payment")>]
[<InlineData(false, "earnings")>]
let ``report turns missing optional data into an uninformative analysis error`` previous path =
    let p, d = previousOrder (), pastOrderDetail ()
    mutate path None (if previous then p else d) |> ignore
    let p, d = deserialize p d
    let data: ScrapedData =
        { Email = "synthetic@example.com"
          Profile = Unchecked.defaultof<JsonElement>
          ActivityHours = Unchecked.defaultof<JsonElement>
          OrderHistory = [||]
          PreviousOrders = [| p |]
          PastOrderDetails = [| d |]
          SkippedOrders = [||] }
    // Conversion fails before any request to the analytics service.
    let result = Report.run data |> fun task -> task.GetAwaiter().GetResult()
    Assert.Equal(Error $"Analiza nie powiodła się: {NullReferenceException().Message}", result)
