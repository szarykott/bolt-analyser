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
            
    let prepareRideAnalysisSource (rides: FinishedRide[]) : RidesDataSource =
        let meteo = (MeteoRepository.get ()).Value
        let districts = (DistrictsRepository.get ()).Value

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
                "temperatura", box (temperatureName r.Temperature)
            ])

    let private pl = CultureInfo.GetCultureInfo "pl-PL"

    let private fmt2 (v: float) = v.ToString("F2", pl)

    let private fmt2Opt (v: float option) =
        v |> Option.map fmt2 |> Option.defaultValue "–"

    let private fmtPValue (p: float option) =
        match p with
        | Some p when p < 0.001 -> "< 0,001"
        | Some p -> p.ToString("F3", pl)
        | None -> "–"

    let private isSignificant (c: Coefficient) =
        match c.PValue with
        | Some p -> p < 0.05
        | None -> false

    let coefficientsTable (response: OlsResponse) : ResultTable =
        let features =
            response.Coefficients |> Array.filter (fun c -> c.Name <> "const")

        let significant, insignificant = features |> Array.partition isSignificant

        let rows =
            significant
            |> Array.sortByDescending (fun c -> c.Coef |> Option.map abs |> Option.defaultValue 0.0)
            |> Array.map (fun c ->
                [ c.Name
                  fmt2Opt c.Coef
                  (match c.CiLow, c.CiHigh with
                   | Some lo, Some hi -> $"od {fmt2 lo} do {fmt2 hi}"
                   | _ -> "–")
                  fmtPValue c.PValue ])
            |> List.ofArray

        let insignificantNote =
            if Array.isEmpty insignificant then
                "Wszystkie cechy modelu są istotne statystycznie (p < 0,05)."
            else
                let names = insignificant |> Array.map _.Name |> String.concat ", "
                $"Cechy statystycznie nieistotne (p ≥ 0,05), pominięte w tabeli: {names}."

        { Title = "Wpływ cech na cenę przejazdu"
          Headers = [ "cecha"; "współczynnik [zł]"; "przedział ufności 95%"; "istotność (p)" ]
          Rows = rows
          Notes =
            [ "cecha — zmienna wpływająca na cenę; nazwy w formie dzielnica_… lub temperatura_… oznaczają różnicę względem pominiętej kategorii bazowej."
              "współczynnik [zł] — o ile złotych zmienia się cena przejazdu, gdy dana cecha występuje (dla dystansu: przy wzroście o 1 odchylenie standardowe); im większa wartość bezwzględna, tym silniejszy wpływ; wiersze są posortowane od najsilniejszego wpływu."
              "przedział ufności 95% — zakres, w którym z 95-procentową pewnością mieści się prawdziwa wartość współczynnika."
              "istotność (p) — prawdopodobieństwo, że tak silny efekt pojawiłby się przypadkiem; wartości poniżej 0,05 uznaje się za istotne statystycznie."
              insignificantNote ] }

    let modelStatsTable (response: OlsResponse) : ResultTable =
        let fitNote =
            match response.RSquared with
            | Some r2 ->
                let pct = int (Math.Round(r2 * 100.0))
                [ $"Model wyjaśnia {pct}%% zmienności ceny przejazdu." ]
            | None -> []

        { Title = "Dopasowanie modelu"
          Headers = [ "statystyka"; "wartość" ]
          Rows =
            [ [ "liczba przejazdów"; string response.NObservations ]
              [ "R²"; fmt2Opt response.RSquared ]
              [ "skorygowane R²"; fmt2Opt response.AdjRSquared ] ]
          Notes =
            fitNote
            @ [ "R² — jaka część zmienności ceny jest wyjaśniona przez model (od 0 do 1, wyżej = lepiej); skorygowane R² dodatkowo uwzględnia liczbę cech w modelu." ] }

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
                  Tables = [ coefficientsTable ols; modelStatsTable ols ] }
        }
