module Bolt.ETL.RideAnalysis.Model

open System
open Bolt.ETL.Geo.DistrictAssignment
open Bolt.ETL.Meteo.Model
open Bolt.ETL.Shared.Model

type RideRow = {
    PricePln: float
    PricePerKm: float
    DistanceKm: float
    DurationMin: float
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