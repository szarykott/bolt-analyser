module Bolt.Web.ReportViews

open System
open System.Globalization
open Bolt.ETL.Analysis
open Bolt.Web.Report
open Giraffe.ViewEngine
open Plotly.NET

let private pl = CultureInfo.GetCultureInfo "pl-PL"
let private fmt2 (value: float) = value.ToString("F2", pl)
let private fmt2Opt = Option.map fmt2 >> Option.defaultValue "–"

let private fmtPValue = function
    | Some p when p < 0.001 -> "< 0,001"
    | Some p -> p.ToString("F3", pl)
    | None -> "–"

let private tableNodes title headers rows notes = [
    h4 [] [ str title ]
    table [] [
        thead [] [ tr [] [ for h in headers -> th [] [ str h ] ] ]
        tbody [] [ for row in rows -> tr [] [ for cell in row -> td [] [ str cell ] ] ]
    ]
    if not (List.isEmpty notes) then
        ul [ _class "notes" ] [ for note in notes -> li [] [ str note ] ]
]

let private sectionNode id title description children =
    section [ _id id ] ([ h2 [] [ str title ]; p [] [ str description ] ] @ children)

let private coefficientRows (effects: (string * float option * float option * float option * float option) array) =
    let significant, insignificant =
        effects |> Array.partition (fun (_, _, p, _, _) -> p |> Option.exists (fun value -> value < 0.05))
    let rows =
        significant
        |> Array.sortByDescending (fun (_, estimate, _, _, _) -> estimate |> Option.map abs |> Option.defaultValue 0.0)
        |> Array.map (fun (name, estimate, p, low, high) ->
            [ name
              fmt2Opt estimate
              (match low, high with
               | Some lo, Some hi -> $"od {fmt2 lo} do {fmt2 hi}"
               | _ -> "–")
              fmtPValue p ])
        |> List.ofArray
    rows, (insignificant |> Array.map (fun (name, _, _, _, _) -> name))

let private fitTable countLabel interpretation count rSquared adjusted notes =
    let fitNote =
        match rSquared with
        | Some r2 -> [ $"Model wyjaśnia {int (Math.Round(r2 * 100.0))}%% zmienności {interpretation}." ]
        | None -> []
    tableNodes "Dopasowanie modelu" [ "statystyka"; "wartość" ]
        [ [ countLabel; string count ]
          [ "R²"; fmt2Opt rSquared ]
          [ "skorygowane R²"; fmt2Opt adjusted ] ]
        (fitNote @ notes)

let private priceRegressionNode (result: PerRide2.AnalysisResult option) =
    let description = "Regresja liniowa (OLS) ceny przejazdu względem dystansu, pory dnia i pogody."
    let children =
        match result with
        | None -> []
        | Some data ->
            let effects =
                data.Effects
                |> Array.map (fun e -> e.Feature, e.Estimate, e.PValue, e.CiLow, e.CiHigh)
            let rows, insignificant = coefficientRows effects
            let insignificantNote =
                if Array.isEmpty insignificant then
                    "Wszystkie cechy modelu są istotne statystycznie (p < 0,05)."
                else
                    let names = String.concat ", " insignificant
                    $"Cechy statystycznie nieistotne (p ≥ 0,05), pominięte w tabeli: {names}."
            (tableNodes "Wpływ cech na cenę przejazdu"
                [ "cecha"; "współczynnik [zł]"; "przedział ufności 95%"; "istotność (p)" ]
                rows
                [ "cecha — zmienna wpływająca na cenę; nazwy w formie dzielnica_… lub temperatura_… oznaczają różnicę względem pominiętej kategorii bazowej."
                  "współczynnik [zł] — o ile złotych zmienia się cena przejazdu, gdy dana cecha występuje (dla dystansu: przy wzroście o 1 odchylenie standardowe); im większa wartość bezwzględna, tym silniejszy wpływ; wiersze są posortowane od najsilniejszego wpływu."
                  "przedział ufności 95% — zakres, w którym z 95-procentową pewnością mieści się prawdziwa wartość współczynnika."
                  "istotność (p) — prawdopodobieństwo, że tak silny efekt pojawiłby się przypadkiem; wartości poniżej 0,05 uznaje się za istotne statystycznie."
                  insignificantNote ])
            @ fitTable "liczba przejazdów" "ceny przejazdu"
                data.ObservationCount data.RSquared data.AdjustedRSquared
                [ "R² — jaka część zmienności ceny jest wyjaśniona przez model (od 0 do 1, wyżej = lepiej); skorygowane R² dodatkowo uwzględnia liczbę cech w modelu." ]
    let description =
        if result.IsNone then description + " Żaden przejazd nie mieści się w zakresie danych pogodowych."
        else description + " W tabeli pokazane są wyłącznie cechy istotne statystycznie."
    sectionNode "price-regression" "Regresja ceny przejazdu" description children

let private hourlyEarningsNode (result: PerHour.AnalysisResult option) =
    let description =
        match result with
        | None -> "Analiza zarobków na godzinę pracy względem pory, pogody i dzielnicy. Żaden przejazd nie mieści się w zakresie danych pogodowych."
        | Some _ ->
            "Zarobki na godzinę pracy w zależności od warunków: pory, pogody i dzielnicy. "
            + "Prostsze zestawienia pojawiają się od razu, dokładniejsze odblokowują się wraz z liczbą przepracowanych godzin."
    let children =
        match result with
        | None -> []
        | Some data ->
            let highest = data.Models |> List.tryLast
            let fullyUnlocked =
                highest |> Option.exists (fun model -> model.Level = 4 && Array.isEmpty model.FoldedDistricts)
            let trailingNotes =
                [ if not fullyUnlocked then
                    "Niektóre analizy (np. wpływ poszczególnych dzielnic albo dokładnych zakresów temperatury) nie są jeszcze pokazywane — mamy na razie za mało godzin jazdy, żeby policzyć je uczciwie. Odblokują się same, gdy przybędzie danych."
                  match highest with
                  | Some model when model.Level = 4 && not (Array.isEmpty model.FoldedDistricts) ->
                      "Część dzielnic zliczona razem jako «inne dzielnice» — za mało godzin w każdej z osobna."
                  | _ -> ()
                  if List.length data.Models >= 2 then
                    "Liczby w wyższych tabelach mogą się różnić od niższych — dokładniejszy model oddziela efekty, które prostszy liczył razem." ]
            let averageRows =
                data.Averages
                |> Array.map (fun avg ->
                    let day = if avg.IsWeekend then "weekend" else "dzień roboczy"
                    let time = if avg.IsNight then "noc" else "dzień"
                    [ $"{day}, {time}"; fmt2 avg.Rate; string avg.HourCount ])
                |> List.ofArray
            let averageNotes =
                [ "zł za godzinę — średnia ważona czasem pracy: godziny przepracowane w całości liczą się mocniej niż ledwie zaczęte."
                  "liczba godzin — ile godzin zegarowych z jazdą wpadło do danej grupy." ]
                @ (if List.isEmpty data.Models then trailingNotes else [])
            let averages =
                tableNodes "Średnie zarobki na godzinę pracy"
                    [ "kiedy"; "zł za godzinę"; "liczba godzin" ] averageRows averageNotes
            let models =
                data.Models
                |> List.collect (fun model ->
                    let effects =
                        model.Effects
                        |> Array.map (fun e -> e.Feature, e.Estimate, e.PValue, e.CiLow, e.CiHigh)
                    let rows, insignificant = coefficientRows effects
                    let insignificantNote =
                        if Array.isEmpty insignificant then
                            "Wszystkie warunki w tym zestawieniu są istotne statystycznie (p < 0,05)."
                        else
                            let names = String.concat ", " insignificant
                            $"Warunki, których wpływu nie widać wyraźnie w danych (p ≥ 0,05), pominięte w tabeli: {names}."
                    tableNodes $"Wpływ warunków na zarobki na godzinę — poziom {model.Level}"
                        [ "warunek"; "współczynnik [zł/h]"; "przedział ufności 95%"; "istotność (p)" ]
                        rows
                        [ "warunek — okoliczność panująca w danej godzinie; nazwy dzielnica_… oznaczają różnicę względem Starego Miasta (centrum), zakresy temperatur — względem pozostałych temperatur."
                          "współczynnik [zł/h] — o ile złotych na godzinę pracy zmieniają się zarobki, gdy dany warunek występuje; wiersze posortowane od najsilniejszego wpływu."
                          "przedział ufności 95% — zakres, w którym z 95-procentową pewnością mieści się prawdziwa wartość współczynnika."
                          "istotność (p) — prawdopodobieństwo, że tak silny efekt pojawiłby się przypadkiem; wartości poniżej 0,05 uznaje się za istotne statystycznie."
                          insignificantNote ])
            let stats =
                highest
                |> Option.map (fun model ->
                    fitTable "liczba godzin" "zarobków na godzinę"
                        model.ObservationCount model.RSquared model.AdjustedRSquared
                        ([ "R² — jaka część zmienności zarobków jest wyjaśniona przez model (od 0 do 1, wyżej = lepiej); skorygowane R² dodatkowo uwzględnia liczbę cech w modelu." ]
                         @ trailingNotes))
                |> Option.defaultValue []
            averages @ models @ stats
    sectionNode "per-hour-earnings" "Zarobki na godzinę pracy" description children

let private pickupClustersNode (result: RideClustering.AnalysisResult) =
    let chart =
        [ ClusterMap.ridePointsLayer result
          ClusterMap.centroidLayer result ]
        |> Chart.combine
        |> ClusterMap.withMapStyle result.Points
    let chartId = "chart-ride-clusters-0"
    let chartNodes = [
        h4 [] [ str "Mapa skupisk odbiorów" ]
        div [ _id chartId; _style "width:100%;height:800px" ] []
        script [ _type "application/json"; attr "data-plotly-target" chartId ] [
            rawText ((GenericChart.toFigureJson chart).Replace("</", "<\\/"))
        ]
    ]
    let rows =
        result.Clusters
        |> Array.sortByDescending _.Size
        |> Array.map (fun c ->
            [ string c.Id
              string c.Size
              c.CentroidLatitude.ToString("F5", CultureInfo.InvariantCulture)
              c.CentroidLongitude.ToString("F5", CultureInfo.InvariantCulture)
              c.MeanHour.ToString("F2", CultureInfo.InvariantCulture) ])
        |> List.ofArray
    let table =
        tableNodes
            $"Skupiska (punkty: {result.Points.Length}, poza skupiskami: {result.NoiseCount})"
            [ "skupisko"; "liczba przejazdów"; "szer. geogr."; "dł. geogr."; "średnia godzina" ] rows []
    sectionNode "ride-clusters" "Skupiska odbiorów pasażerów"
        "Przestrzenno-czasowe skupiska (ST-DBSCAN) miejsc odbioru pasażerów."
        (chartNodes @ table)

let sections (report: AnalysisReport) = [
    priceRegressionNode report.PriceRegression
    hourlyEarningsNode report.HourlyEarnings
    pickupClustersNode report.PickupClusters
]
