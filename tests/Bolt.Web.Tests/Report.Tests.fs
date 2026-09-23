module Bolt.Web.Tests.ReportTests

open System.Text.Json
open Xunit
open Bolt.Models.BoltApi
open Bolt.Web

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
