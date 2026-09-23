namespace Bolt.ETL.Analysis

module PerRide1 = 

    open System
    open Bolt.ETL
    open Bolt.ETL.Geo
    open Bolt.ETL.Geo.DistrictAssignment
    open Bolt.ETL.Meteo.Model
    open Bolt.ETL.Shared.Model
    open Bolt.Models
    open Bolt.Models.Geo
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

    type Precipitation = Dry | Rain | Snow

    type BreakdownKey =
        | District of DistrictName
        | TimeOfDay of PartOfDay
        | Weekday of DayOfWeek
        | Weather of TemperatureBucket * Precipitation

    type Breakdown = {
        Key: BreakdownKey
        RideCount: int
        AveragePricePln: float
        AveragePricePerKm: float
    }

    type AnalysisResult = {
        ByDistrict: Breakdown list
        ByPartOfDay: Breakdown list
        ByDayOfWeek: Breakdown list
        ByWeather: Breakdown list
    }
    
    module RideRow =
        let fromRide weatherProvider districtProvider (ride: FinishedRide) : RideRow =
            let earned = ride.Payment.Earned |> Array.sumBy _.Value
            let distance = ride.Route.RideDistance
            let start = ride.Times.RideStart
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
            
    let prepareRideAnalysisSource (meteo: Weather) (districts: District seq) (rides: FinishedRide[]) : RidesDataSource =
        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
        let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts

        { Rows = rides |> Array.map (RideRow.fromRide weatherProvider districtProvider) }
    
    let breakdown (key: RideRow -> BreakdownKey) (rows: RideRow array) : Breakdown list =
        rows
        |> Array.groupBy key
        |> Array.sortByDescending (fun (_, rides) -> rides.Length)
        |> Array.map (fun (group, rides) ->
            { Key = group
              RideCount = rides.Length
              AveragePricePln = rides |> Array.averageBy (fun r -> float r.PricePln)
              AveragePricePerKm = rides |> Array.averageBy (fun r -> float r.PricePerKm) })
        |> List.ofArray

    let private weather (r: RideRow) =
        let precipitation = if r.Snow then Snow elif r.Rain then Rain else Dry
        Weather(r.Temperature, precipitation)

    let run (source: RidesDataSource) : AnalysisResult =
        { ByDistrict = breakdown (fun r -> District r.PickupDistrict) source.Rows
          ByPartOfDay = breakdown (fun r -> TimeOfDay r.PartOfDay) source.Rows
          ByDayOfWeek = breakdown (fun r -> Weekday r.DayOfWeek) source.Rows
          ByWeather = breakdown weather source.Rows }
