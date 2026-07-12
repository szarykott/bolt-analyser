namespace Bolt.ETL.Analysis

module PerRide2 =
    
    open System
    open System.Globalization
    open Bolt.ETL
    open Bolt.ETL.Analytics
    open Bolt.ETL.Geo
    open Bolt.ETL.Geo.DistrictAssignment
    open Bolt.ETL.Meteo.Model
    open Bolt.Infrastructure.Repository
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models
    open Bolt.Models.Meteo
    
    type RideRow = {
        PricePln: decimal
        Distance: float<km>
        IsRushHour: bool
        IsWeekend: bool
        PickupDistrict: string
        Rain: bool
        Snow: bool
        Temperature: TemperatureBucket
    }
    
    type RidesDataSource = {
        Rows: RideRow array
    }
    
    module RideRow =
        let isWeekend (date: DateTimeOffset) =
            match (date.DayOfWeek, date.Hour) with
            | DayOfWeek.Saturday, _ -> true
            | DayOfWeek.Sunday, _ -> true
            | DayOfWeek.Friday, h when h >= 18 -> true
            | DayOfWeek.Monday, h when h < 6 -> true
            | _ -> false
        
        let isRushHour (date: DateTimeOffset) =
            if isWeekend date
            then
                date.Hour >= 22 || date.Hour < 3
            else
                let morningRush = date.Hour >= 6 && date.Hour < 9
                let eveningRush = date.Hour >= 15 && date.Hour < 19
                morningRush || eveningRush
                
        let getGeneralDistrict (district : DistrictName) =
            match district.Value with
            | "Stare Miasto" -> "centrum"
            | "Grzegórzki" -> "centrum"
            | "Prądnik Czerwony" -> "północ"
            | "Prądnik Biały" -> "północ"
            | "Krowodrza" -> "centrum"
            | "Bronowice" -> "północ"
            | "Zwierzyniec" -> "zachód"
            | "Dębniki" -> "południowy-zachód"
            | "Łagiewniki-Borek Fałęcki" -> "południe"
            | "Swoszowice" -> "południe"
            | "Podgórze Duchackie" -> "południe"
            | "Bieżanów-Prokocim" -> "południe"
            | "Podgórze" -> "południe"
            | "Czyżyny" -> "bliski-wschód"
            | "Mistrzejowice" -> "bliski-wschód"
            | "Bieńczyce" -> "bliski-wschód"
            | "Wzgórza Krzesławickie" -> "wschód"
            | "Nowa Huta" -> "wschód"
            | "unknown" -> "poza-krakowem"
            | _ -> failwith "Handle all possible district"
        
        let fromRide weatherProvider districtProvider (ride: FinishedRide) : RideRow =
            let earned = ride.Payment.Earned |> Array.sumBy _.Value
            let distance = ride.Route.RideDistance
            let weatherData : WeatherDataPoint = weatherProvider ride.Times.CreatedTimestamp
            let districtName = districtProvider ride.Route.Stops[0].Location |> Option.defaultValue (DistrictName "unknown")
            {
                PricePln = earned
                Distance = distance
                IsRushHour = isRushHour ride.Times.CreatedTimestamp
                IsWeekend = isWeekend ride.Times.CreatedTimestamp 
                PickupDistrict = getGeneralDistrict districtName
                Rain = weatherData.Rain <> 0.0<mm>
                Snow = weatherData.Snow <> 0.0<cm>
                Temperature = TemperatureBucket.fromCelcius weatherData.Temperature
            }
            
    let prepareRideAnalysisSource (email: string) : RidesDataSource =
        let previousRides = (PreviousOrderRepository.get email).Value |> Array.ofSeq
        let pastOrders = (PastOrderDetailRepository.get email).Value |> Array.ofSeq
        let meteo = (MeteoRepository.get ()).Value
        let districts = (DistrictsRepository.get ()).Value
    
        let weatherProvider = Weather.getDataPoint meteo
        let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts
    
        let finishedRide (ride: Ride) : FinishedRide option =
            match ride.Data with
            | Finished r -> Some r
            | _ -> None
    
        let data =
            Array.zip previousRides pastOrders
            |> Array.map (fun (pr, pod) -> RideFactory.getRide pr pod)
            |> Array.choose finishedRide
            |> Array.map (RideRow.fromRide weatherProvider districtProvider)
    
        { Rows = data }
    
    let saveRidesDataSourceToCsv (data: RidesDataSource) =
        let headers = [|
            "price_pln"
            "distance_km"
            "is_rush_hour"
            "is_weekend"
            "pickup_district"
            "rain"
            "snow"
            "temperature_bucket"
        |]
        
        let data =
            data.Rows
            |> Array.map(fun r -> [|
                r.PricePln.ToString("F2")
                r.Distance.ToString()
                r.IsRushHour.ToString()
                r.IsWeekend.ToString()
                r.PickupDistrict
                r.Rain.ToString()
                r.Snow.ToString()
                r.Temperature.ToString()
            |])
    
        CsvStorage.write "ridesDataSource2.csv" { Headers = headers; Rows = data }

    // JSON rows for the analytics service, keyed by the CSV header names.
    // Units of measure / decimal unwrapped before boxing so values serialize
    // as plain JSON numbers; bools stay bools (service keeps false <> 0.0).
    let toAnalyticsRows (data: RidesDataSource) : Map<string, obj> array =
        data.Rows
        |> Array.map (fun r ->
            Map.ofList [
                "price_pln", box (float r.PricePln)
                "distance_km", box (float r.Distance)
                "is_rush_hour", box r.IsRushHour
                "is_weekend", box r.IsWeekend
                "pickup_district", box r.PickupDistrict
                "rain", box r.Rain
                "snow", box r.Snow
                "temperature_bucket", box (r.Temperature.ToString())
            ])

    let private formatFloat (value: float option) =
        value
        |> Option.map (fun v -> v.ToString(CultureInfo.InvariantCulture))
        |> Option.defaultValue ""

    let runRemoteRegression (data: RidesDataSource) =
        let response =
            AnalyticsClient.olsRegression {
                Rows = toAnalyticsRows data
                Target = "price_pln"
                DropColumns = [||]
                CategoricalColumns = None
                Standardize = true
            }

        JsonStorage.write "perRide2.olsRegression.json" response

        let rows =
            response.Coefficients
            |> Array.map (fun c -> [|
                c.Name
                formatFloat c.Coef
                formatFloat c.StdErr
                formatFloat c.TValue
                formatFloat c.PValue
                formatFloat c.CiLow
                formatFloat c.CiHigh
            |])

        CsvStorage.write "perRide2.olsCoefficients.csv" {
            Headers = [| "name"; "coef"; "std_err"; "t_value"; "p_value"; "ci_low"; "ci_high" |]
            Rows = rows
        }

    let runRemoteMirrorCheck (data: RidesDataSource) =
        let response =
            AnalyticsClient.mirrorCheck {
                Rows = toAnalyticsRows data
                TargetColumns = [| "price_pln" |]
                GroupMeans = Some { By = "pickup_district"; Value = "distance_km" }
            }

        JsonStorage.write "perRide2.mirrorCheck.json" response
