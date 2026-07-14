namespace Bolt.ETL.Analysis

module PerRide2 =
    
    open System
    open System.Globalization
    open System.Threading.Tasks
    open Bolt.ETL
    open Bolt.ETL.Analytics
    open Bolt.ETL.Geo
    open Bolt.ETL.Geo.DistrictAssignment
    open Bolt.ETL.Meteo.Model
    open Bolt.Infrastructure.Repository
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
    
        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
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

    let private fmtOpt (v: float option) =
        v
        |> Option.map (fun x -> x.ToString("F4", CultureInfo.InvariantCulture))
        |> Option.defaultValue "–"

    let coefficientsTable (response: OlsResponse) : ResultTable =
        { Title = "OLS coefficients (target: price_pln)"
          Headers = [ "feature"; "coef"; "std err"; "t"; "p-value"; "ci low"; "ci high" ]
          Rows =
            response.Coefficients
            |> Array.map (fun c ->
                [ c.Name; fmtOpt c.Coef; fmtOpt c.StdErr; fmtOpt c.TValue
                  fmtOpt c.PValue; fmtOpt c.CiLow; fmtOpt c.CiHigh ])
            |> List.ofArray }

    let modelStatsTable (response: OlsResponse) : ResultTable =
        { Title = "Model statistics"
          Headers = [ "statistic"; "value" ]
          Rows =
            [ [ "observations"; string response.NObservations ]
              [ "R²"; fmtOpt response.RSquared ]
              [ "adjusted R²"; fmtOpt response.AdjRSquared ]
              [ "F statistic"; fmtOpt response.FStatistic ]
              [ "F p-value"; fmtOpt response.FPvalue ] ] }

    let vifTable (response: MirrorCheckResponse) : ResultTable =
        { Title = "Variance inflation factors"
          Headers = [ "feature"; "VIF" ]
          Rows =
            response.Vif
            |> Array.map (fun v -> [ v.Feature; fmtOpt v.Vif ])
            |> List.ofArray }

    let groupMeansTable (response: MirrorCheckResponse) : ResultTable option =
        response.GroupMeans
        |> Option.map (fun groupMeans ->
            { Title = "Mean distance by district"
              Headers = [ "district"; "mean distance [km]" ]
              Rows =
                groupMeans
                |> Array.map (fun g -> [ g.Group; fmtOpt g.Mean ])
                |> List.ofArray })

    let buildSection (source: RidesDataSource) : Task<AnalysisSection> =
        task {
            let! ols =
                AnalyticsClient.olsRegression {
                    Rows = toAnalyticsRows source
                    Target = "price_pln"
                    DropColumns = [||]
                    CategoricalColumns = None
                    Standardize = true
                }

            let! mirror =
                AnalyticsClient.mirrorCheck {
                    Rows = toAnalyticsRows source
                    TargetColumns = [| "price_pln" |]
                    GroupMeans = Some { By = "pickup_district"; Value = "distance_km" }
                }

            return
                { Id = "price-regression"
                  Title = "Price regression"
                  Description = "OLS regression of ride price against distance, time and weather, with multicollinearity diagnostics."
                  Charts = []
                  Tables =
                    [ yield coefficientsTable ols
                      yield modelStatsTable ols
                      yield vifTable mirror
                      yield! groupMeansTable mirror |> Option.toList ] }
        }
