namespace Bolt.ETL.Analysis

module PerRide1 = 

    open System
    open System.Globalization
    open System.Threading.Tasks
    open Bolt.ETL
    open Bolt.ETL.Geo
    open Bolt.ETL.Geo.DistrictAssignment
    open Bolt.ETL.Meteo.Model
    open Bolt.ETL.Shared.Model
    open Bolt.Infrastructure.Repository
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
            
    let prepareRideAnalysisSource (rides: FinishedRide[]) : RidesDataSource =
        let meteo = (MeteoRepository.get ()).Value
        let districts = (DistrictsRepository.get ()).Value

        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
        let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts

        { Rows = rides |> Array.map (RideRow.fromRide weatherProvider districtProvider) }
    
    let private fmt (v: float) = v.ToString("F2", CultureInfo.InvariantCulture)

    let breakdownTable (title: string) (key: RideRow -> string) (rows: RideRow array) : ResultTable =
        let dataRows =
            rows
            |> Array.groupBy key
            |> Array.sortByDescending (fun (_, rs) -> rs.Length)
            |> Array.map (fun (k, rs) ->
                [ k
                  string rs.Length
                  fmt (rs |> Array.averageBy (fun r -> float r.PricePln))
                  fmt (rs |> Array.averageBy (fun r -> float r.PricePerKm)) ])
            |> List.ofArray

        { Title = title
          Headers = [ "group"; "rides"; "avg price [PLN]"; "avg [PLN/km]" ]
          Rows = dataRows
          Notes = [] }

    let private weatherLabel (r: RideRow) =
        let precipitation = if r.Snow then "snow" elif r.Rain then "rain" else "dry"
        $"{r.Temperature}/{precipitation}"

    let buildSection (source: RidesDataSource) : Task<AnalysisSection> =
        Task.FromResult
            { Id = "ride-stats"
              Title = "Ride statistics"
              Description = "Ride counts and average earnings broken down by pickup district, time and weather."
              Charts = []
              Tables =
                [ breakdownTable "By pickup district" (fun r -> r.PickupDistrict.Value) source.Rows
                  breakdownTable "By part of day" (fun r -> string r.PartOfDay) source.Rows
                  breakdownTable "By day of week" (fun r -> string r.DayOfWeek) source.Rows
                  breakdownTable "By weather" weatherLabel source.Rows ] }
