module Bolt.ETL.RideAnalysis.Model

open System
open Bolt.ETL.Geo.DistrictAssignment
open Bolt.ETL.Meteo.Model
open Bolt.ETL.Shared.Model
open Bolt.Models
open Bolt.Models.Meteo

type RideRow = {
    PricePln: decimal
    PricePerKm: decimal
    Distance: float<km>
    PartOfDay: PartOfDay
    DayOfWeek: DayOfWeek
    PickupDistrict: DistrictName
    Rain: bool
    Snow: bool
    Temperature: TemperatureBucket
    PaymentType: string
}

type RidesDataSource = {
    Rows: RideRow array
}

module RideRow =
    let fromRide weatherProvider districtProvider (ride: FinishedRide) : RideRow =
        let earned = ride.Payment.Earned |> Array.sumBy _.Value
        let distance = ride.Route.RideDistance
        let start = ride.Times.RideStart
        let end' = ride.Times.RideEnd
        let weatherData : WeatherDataPoint = weatherProvider ride.Times.CreatedTimestamp
        let districtName = districtProvider ride.Route.Stops[0].Location |> Option.defaultValue (DistrictName "unknown")
        {
            PricePln = earned
            PricePerKm = earned / decimal distance
            Distance = distance
            PartOfDay = PartOfDay.fromDate start
            DayOfWeek = start.DayOfWeek
            PaymentType = ride.Payment.PaymentMetadata.PaymentType
            PickupDistrict = districtName
            Rain = weatherData.Rain <> 0.0<mm>
            Snow = weatherData.Snow <> 0.0<cm>
            Temperature = TemperatureBucket.fromCelcius weatherData.Temperature
        }