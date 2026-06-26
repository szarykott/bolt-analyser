open System.IO
open Bolt.Infrastrucutre.ces.OptionBuilder
open Bolt.Infrastrucutre.storage.Constants.Paths
open Bolt.Infrastrucutre.storage.Storage
open Bolt.Models.ActivityHours
open Bolt.Models.PreviousOrder
open Bolt.Models.PastOrderDetail
open Bolt.Reporter
open Bolt.Reporter.RideReportingSource
open Bolt.Reporter.Plotting

let finishedRides (rides: RideReportingSource array) =
    rides
    |> Array.choose (fun r ->
        match r.Data with
        | RideType.Finished r -> Some r
        | RideType.DidNotHappen _ -> None)

// App

let maybeRides =
    maybe {
        let! previousOrders: PreviousOrder seq = JsonStorage.read "previousOrders.json"
        let! pastOrders: PastOrderDetail seq = JsonStorage.read "pastOrderDetails.json"

        let! activity: ActivityHours = JsonStorage.read "activityHours.json"
        
        let rides =
            Seq.zip previousOrders pastOrders
            |> Seq.map (fun (a, b) -> buildReportingDataSource a b)
            |> Array.ofSeq
            
        return (rides, activity)
    }

let rides, activity = maybeRides.Value

let days = DayReporting.daysFromRideReportingSources rides activity
let weeks = WeekReporting.weeksFromDayReportingSources days
let months = MonthReporting.monthsFromDayReportingSources days
let all = AllTimeReporting.monthsFromDayReportingSources days

// TODO: WHY NOT USE PLOTLY.NET ??? IT SEEMS GREAT!!! AND IT EVEN HAS GEO CHARTS!!!
let finished = finishedRides rides

let asNormalizedSvg plot = asMarkdownSvg 500 300 plot

let averageHourlyEarnings (rides': FinishedRide seq) =
    let perHour = rides' |> Calculations.averageEarnedByHourOfDay |> Array.ofSeq

    if perHour.Length = 0 then
        0m
    else
        perHour |> Array.averageBy (fun (_, m) -> m.Value)

let content =
    $"""
# Raport z twojego Bolta

## Zgrubne dane

Liczba twoich ukończnych przejazdów to %i{Calculations.finishedCount finished}

Średnio za kurs zarobiłeś %.02f{(Calculations.averageEarnedPerRide finished).Value} zł

Średnie zarobki godzinowe %.02f{averageHourlyEarnings finished} zł

Całkowity dystans pokonany podczas kursów z klientami %.02f{(Calculations.totalDistance finished).Value} km

## Wykresy

Uśrednione zarobki dla każdej rozpoczynającej się godziny (czas lokalny):

{finished
 |> Calculations.averageEarnedByHourOfDay
 |> scatterPlot (fun (t, m) -> float t.Hour, float m.Value)
 |> asNormalizedSvg}

Ilość przejazdów dla każdej rozpoczynającej się godziny (czas lokalny):

{finished
 |> Calculations.ridesByHourOfDay
 |> scatterPlot (fun (t, c) -> float t.Hour, float c)
 |> asNormalizedSvg}

"""

File.WriteAllText(getQualifiedStorageLocation "report.md", content)
