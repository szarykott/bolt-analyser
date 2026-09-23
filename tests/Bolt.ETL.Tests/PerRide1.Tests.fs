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
let ``breakdown groups counts averages and sorts descending`` () =
    let rows = [| row "A" 10m 2.0; row "A" 20m 4.0; row "B" 30m 5.0 |]
    let groups = PerRide1.breakdown (fun r -> PerRide1.District r.PickupDistrict) rows
    Assert.Equal(2, groups.Length)
    Assert.Equal(PerRide1.District(DistrictName "A"), groups[0].Key)
    Assert.Equal(2, groups[0].RideCount)
    Assert.Equal(15.0, groups[0].AveragePricePln)
    Assert.Equal(5.0, groups[0].AveragePricePerKm)
    Assert.Equal(PerRide1.District(DistrictName "B"), groups[1].Key)

[<Fact>]
let ``run produces four independent typed breakdowns`` () =
    let result = PerRide1.run { Rows = [| row "A" 10m 2.0 |] }
    Assert.Single result.ByDistrict |> ignore
    Assert.Single result.ByPartOfDay |> ignore
    Assert.Single result.ByDayOfWeek |> ignore
    Assert.Single result.ByWeather |> ignore
    Assert.Equal(PerRide1.Weather(Mild, PerRide1.Dry), result.ByWeather.Head.Key)
