module Bolt.Web.Views.Report

open System.Globalization
open Bolt.ETL.Analysis
open Bolt.Web.Report
open Bolt.Web.Views.Shared
open Giraffe.ViewEngine
open Plotly.NET

let private pl = CultureInfo.GetCultureInfo "pl-PL"
let private fmt2 (value: float) = value.ToString("N2", pl)
let private fmtMoney (value: decimal) = value.ToString("N2", pl) + " zł"

let private tableNodes title headers rows = [
    h4 [] [ str title ]
    table [] [
        thead [] [ tr [] [ for h in headers -> th [] [ str h ] ] ]
        tbody [] [ for row in rows -> tr [] [ for cell in row -> td [] [ str cell ] ] ]
    ]
]

let private sectionNode id title description children =
    section [ _id id ] ([ h2 [] [ str title ]; p [] [ str description ] ] @ children)

let private basicStatisticsNode (report: AnalysisReport) =
    let stats = report.BasicStatistics
    let commissionPercent = (stats.CommissionRate * 100m).ToString("N2", pl)
    let moneyRows =
        [ [ "Zapłacone przez pasażerów"; fmtMoney stats.TotalPaid ]
          [ "W tym gotówką"; fmtMoney stats.PaidCash ]
          [ "W tym cyfrowo"; fmtMoney stats.PaidDigital ]
          [ "Napiwki"; fmtMoney stats.TotalTips ]
          [ "Prowizja Bolt";
            fmtMoney stats.TotalCommission + " (" + commissionPercent + "%)" ]
          [ "Zarobek kierowcy"; fmtMoney stats.TotalEarnings ] ]
    let highlightRows =
        [ "Najdłuższy przejazd", stats.LongestRide
          "Najkrótszy przejazd", stats.ShortestRide
          "Największy zarobek na przejeździe", stats.HighestEarningRide
          "Najmniejszy zarobek na przejeździe", stats.LowestEarningRide ]
        |> List.map (fun (label, ride) ->
            let route =
                match ride.FromAddress, ride.ToAddress with
                | Some fromAddress, Some toAddress -> $"{fromAddress} → {toAddress}"
                | _ -> "–"
            [ label; ride.Date.ToString("yyyy-MM-dd HH:mm", pl); route
              fmt2 ride.DistanceKm + " km"; fmtMoney ride.Earnings ])
    let rows =
        [ for isWeekend, dayLabel in [ false, "dzień roboczy"; true, "weekend" ] do
              for isNight, timeLabel in [ false, "dzień"; true, "noc" ] do
                  let average =
                      stats.HourlyAverages
                      |> Array.tryFind (fun a -> a.IsWeekend = isWeekend && a.IsNight = isNight)
                  let hours = average |> Option.map _.WorkedHours |> Option.defaultValue 0.0
                  let rate =
                      average
                      |> Option.map (fun a -> fmt2 (a.Earnings / a.WorkedHours))
                      |> Option.defaultValue "–"
                  [ $"{dayLabel}, {timeLabel}"; rate; fmt2 hours ] ]
    sectionNode "basic-statistics" "Podstawowe statystyki"
        "Podsumowanie zakończonych przejazdów."
        ([ p [] [ strong [] [ str "Liczba przejazdów: " ]; str (string report.RideCount) ]
           p [] [ strong [] [ str "Przejechany dystans: " ]; str (fmt2 stats.TotalDistanceKm + " km") ] ]
         @ tableNodes "Pieniądze"
             [ "pozycja"; "kwota" ] moneyRows
         @ [ p [ _class "notes" ] [
                 str "Procent prowizji liczony od zarobku kierowcy przed potrąceniem prowizji. "
                 str "Gotówka obejmuje kursy z typem płatności cash; pozostałe typy są liczone jako płatności cyfrowe."
             ] ]
         @ tableNodes "Wyróżnione przejazdy"
             [ "rekord"; "data"; "trasa"; "dystans"; "zarobek kierowcy" ] highlightRows
         @ tableNodes "Zarobki na godzinę pracy"
             [ "kiedy"; "zł za godzinę"; "godziny pracy" ] rows
         @ [ p [ _class "notes" ] [
                 str "Czas pracy obejmuje okresy od przyjęcia do końca kursu oraz przerwy między kursami w godzinach z przejazdem. "
                 str "Pełne godziny bez kursu są pomijane. Noc trwa od 18:00 do 6:00, a weekend od piątku 18:00 do poniedziałku 6:00."
             ] ])

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
            [ "skupisko"; "liczba przejazdów"; "szer. geogr."; "dł. geogr."; "średnia godzina" ] rows
    sectionNode "ride-clusters" "Skupiska odbiorów pasażerów"
        "Przestrzenno-czasowe skupiska (ST-DBSCAN) miejsc odbioru pasażerów."
        (chartNodes @ table)

let sections (report: AnalysisReport) = [
    basicStatisticsNode report
    pickupClustersNode report.PickupClusters
]

let reportFragment (report: AnalysisReport) =
    let fromDate, toDate = report.DateRange

    let header = [
        h1 [] [ str $"Analiza przejazdów Bolt — {report.Email}" ]
        p [] [
            str (
                $"""{report.RideCount} przejazdów między {fromDate.ToString "yyyy-MM-dd"} a {toDate.ToString "yyyy-MM-dd"}, """
                + $"""raport wygenerowano {report.GeneratedAt.ToString "yyyy-MM-dd HH:mm"} UTC."""
            )
        ]
        if not (Array.isEmpty report.SkippedOrders) then
            section [] [
                h2 [] [ str $"Pominięte kursy ({report.SkippedOrders.Length})" ]
                p [] [ str "Nie udało się pobrać szczegółów tych kursów po trzech próbach. Pominięto je w analizie." ]
                ul [] [
                    for order in report.SkippedOrders do
                        li [] [ str $"Kurs {order.OrderId}: {order.Reason}" ]
                ]
            ]
    ]

    panel [
        div [ _id "report-content" ] (header @ sections report)
        button [ _onclick "downloadReport()" ] [ str "Pobierz raport" ]
    ]
    |> render
