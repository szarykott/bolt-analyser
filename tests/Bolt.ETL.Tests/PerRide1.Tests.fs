module Bolt.ETL.Tests.PerRide1Tests

open System
open Xunit
open Bolt.ETL.Analysis
open Bolt.ETL.Geo.DistrictAssignment
open Bolt.ETL.Meteo.Model
open Bolt.ETL.Shared.Model
open Bolt.Models

let private row district price distance : PerRide1.RideRow = {
    PricePln = price
    PricePerKm = price / decimal distance
    Distance = distance * 1.0<km>
    PartOfDay = PartOfDay.fromDate (DateTimeOffset.Parse "2026-01-01T12:00Z")
    DayOfWeek = DayOfWeek.Monday
    PickupDistrict = DistrictName district
    Rain = false
    Snow = false
    Temperature = Mild
    PaymentType = "cash"
}

[<Fact>]
let ``breakdownTable groups, counts and sorts descending`` () =
    let rows = [| row "A" 10m 2.0; row "A" 20m 4.0; row "B" 30m 5.0 |]
    let table = PerRide1.breakdownTable "By district" (fun r -> r.PickupDistrict.Value) rows
    Assert.Equal("By district", table.Title)
    Assert.Equal<string list>([ "group"; "rides"; "avg price [PLN]"; "avg [PLN/km]" ], table.Headers)
    Assert.Equal<string list>([ "A"; "2"; "15.00"; "5.00" ], table.Rows[0])
    Assert.Equal<string list>([ "B"; "1"; "30.00"; "6.00" ], table.Rows[1])

[<Fact>]
let ``buildSection produces four tables and no charts`` () =
    let section = (PerRide1.buildSection { Rows = [| row "A" 10m 2.0 |] }).GetAwaiter().GetResult()
    Assert.Equal("ride-stats", section.Id)
    Assert.Empty section.Charts
    Assert.Equal(4, section.Tables.Length)
