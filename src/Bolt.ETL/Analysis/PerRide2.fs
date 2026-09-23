namespace Bolt.ETL.Analysis

module PerRide2 =
    
    open System
    open System.Threading.Tasks
    open Bolt.ETL
    open Bolt.ETL.Analytics
    open Bolt.ETL.Shared
    open Bolt.ETL.Geo
    open Bolt.ETL.Geo.DistrictAssignment
    open Bolt.ETL.Meteo.Model
    open Bolt.Models
    open Bolt.Models.Geo
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

    type PriceEffect = {
        Feature: string
        Estimate: float option
        PValue: float option
        CiLow: float option
        CiHigh: float option
    }

    type AnalysisResult = {
        ObservationCount: int
        RSquared: float option
        AdjustedRSquared: float option
        Effects: PriceEffect array
    }
    
    module RideRow =
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
                IsRushHour = RideTime.isRushHour ride.Times.CreatedTimestamp
                IsWeekend = RideTime.isWeekend ride.Times.CreatedTimestamp
                PickupDistrict = getGeneralDistrict districtName
                Rain = weatherData.Rain <> 0.0<mm>
                Snow = weatherData.Snow <> 0.0<cm>
                Temperature = TemperatureBucket.fromCelcius weatherData.Temperature
            }
            
    let prepareRideAnalysisSource (meteo: Weather) (districts: District seq) (rides: FinishedRide[]) : RidesDataSource =
        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
        let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts

        { Rows = rides |> Array.map (RideRow.fromRide weatherProvider districtProvider) }
    
    let private temperatureName (t: TemperatureBucket) =
        match t with
        | Frost -> "mróz"
        | Cold -> "zimno"
        | Cool -> "chłodno"
        | Mild -> "umiarkowanie"
        | Warm -> "ciepło"
        | Hot -> "upał"

    // Keep the existing wire keys: the analytics service uses them to name
    // coefficients. The Web project owns their presentation.
    let toAnalyticsRows (data: RidesDataSource) : Map<string, obj> array =
        data.Rows
        |> Array.map (fun r ->
            Map.ofList [
                "cena_pln", box (float r.PricePln)
                "dystans_km", box (float r.Distance)
                "godziny_szczytu", box r.IsRushHour
                "weekend", box r.IsWeekend
                "dzielnica", box r.PickupDistrict
                "deszcz", box r.Rain
                "śnieg", box r.Snow
                "temperatura", box (temperatureName r.Temperature)
            ])

    let fromAnalyticsResponse (ols: OlsResponse) : AnalysisResult =
        { ObservationCount = ols.NObservations
          RSquared = ols.RSquared
          AdjustedRSquared = ols.AdjRSquared
          Effects =
            ols.Coefficients
            |> Array.filter (fun c -> c.Name <> "const")
            |> Array.map (fun c ->
                { Feature = c.Name
                  Estimate = c.Coef
                  PValue = c.PValue
                  CiLow = c.CiLow
                  CiHigh = c.CiHigh }) }

    let run (source: RidesDataSource) : Task<AnalysisResult> =
        task {
            let! ols =
                AnalyticsClient.olsRegression {
                    Rows = toAnalyticsRows source
                    Target = "cena_pln"
                    DropColumns = [||]
                    CategoricalColumns = None
                    Standardize = true
                }

            return fromAnalyticsResponse ols
        }
