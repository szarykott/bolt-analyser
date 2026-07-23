namespace Bolt.ETL.Analysis

module PerRide =

    open System
    open System.Globalization
    open System.Threading.Tasks
    open Bolt.ETL.Analytics
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
                PickupDistrict = districtName.Value
                Rain = weatherData.Rain <> 0.0<mm>
                Snow = weatherData.Snow <> 0.0<cm>
                Temperature = TemperatureBucket.fromCelcius weatherData.Temperature
            }

    let prepareRideAnalysisSource (rides: FinishedRide[]) : RidesDataSource =
        let meteo = (MeteoRepository.get ()).Value
        let districts = (DistrictsRepository.get ()).Value

        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
        let districtProvider = assignCoordinatesToDistrict districts

        { Rows = rides |> Array.map (RideRow.fromRide weatherProvider districtProvider) }

    // JSON rows for the analytics service. Column names are Polish on purpose:
    // pd.get_dummies builds coefficient names as "column_value", so Polish keys
    // (and Polish temperature values) make the OLS response display-ready with
    // no name mapping on the way back. Units of measure / decimal unwrapped
    // before boxing so values serialize as plain JSON numbers; bools stay bools.
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
                "temperatura", box (TemperatureBucket.label r.Temperature)
            ])

    let runOls(source: RidesDataSource) : Task<OlsResponse> =
        task {
            let! ols =
                AnalyticsClient.olsRegression {
                    Rows = toAnalyticsRows source
                    Target = "cena_pln"
                    DropColumns = [||]
                    CategoricalColumns = None
                    Standardize = true
                }

            return ols
        }

    let buildSection (source: RidesDataSource) : Task<AnalysisSection> =
        task {
            let! ols =
                AnalyticsClient.olsRegression {
                    Rows = toAnalyticsRows source
                    Target = "cena_pln"
                    DropColumns = [||]
                    CategoricalColumns = None
                    Standardize = true
                }

            return
                { Id = "price-regression"
                  Title = "Regresja ceny przejazdu"
                  Description =
                    "Regresja liniowa (OLS) ceny przejazdu względem dystansu, pory dnia i pogody. "
                    + "W tabeli pokazane są wyłącznie cechy istotne statystycznie."
                  Charts = []
                  Tables = [] }
        }
