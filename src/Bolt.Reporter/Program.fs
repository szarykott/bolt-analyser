open System.IO
open Bolt.Infrastrucutre.ces.OptionBuilder
open Bolt.Infrastrucutre.storage.Constants.Paths
open Bolt.Infrastrucutre.storage.Storage
open Bolt.Models.PreviousOrder
open Bolt.Models.PastOrderDetail
open Bolt.Models.ActivityHours
open Bolt.Reporter
open Bolt.Reporter.RideReportingSource
open Bolt.Reporter.Plotting
open Bolt.Reporter.Calculations

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
            
        return rides, activity
    }

let rides, activity = maybeRides.Value

let days = DayReporting.daysFromRideReportingSources rides activity
let weekDays = DayOfWeekReporting.weekDaysFromDayReportingSources days
let hours = HourByHourReporting.hoursFromRideReportingSources rides

// TODO: WHY NOT USE PLOTLY.NET ??? IT SEEMS GREAT!!! AND IT EVEN HAS GEO CHARTS!!!
let finished = finishedRides rides

let asNormalizedSvg plot = svgString 500 300 plot

let averageHourlyEarnings (rides': FinishedRide seq) =
    let perHour = rides' |> Calculations.averageEarnedByHourOfDay |> Array.ofSeq

    if perHour.Length = 0 then
        0m
    else
        perHour |> Array.averageBy (fun (_, m) -> m.Value)

let aggregatedSection (source: IReportingSource seq) 
    =
    $"""

{source 
|> Seq.map (fun i -> i.Label, (totalEarned i.FinishedRides).Value) 
|> linePlot
|> linePlotStyle "Zarobki całkowite" "Okres" "PLN"
|> asNormalizedSvg}

{source 
|> Seq.map (fun i -> i.Label, (averageEarnedPerRide i.FinishedRides).Value) 
|> linePlot
|> linePlotStyle "Zarobki średnie za przejazd" "Okres" "PLN"
|> asNormalizedSvg}

{source 
|> Seq.map (fun i -> i.Label, averageHourlyEarnings i.FinishedRides) 
|> linePlot
|> linePlotStyle "Zarobki średnie godzinowe" "Okres" "PLN"
|> asNormalizedSvg}

{source 
|> Seq.map (fun i -> i.Label, (averageRideDistance i.FinishedRides).Value) 
|> linePlot
|> linePlotStyle "Średni pokonany w kursie dystans" "Okres" "KM"
|> asNormalizedSvg}

{source 
|> Seq.map (fun i -> i.Label, commissionRate i.FinishedRides) 
|> linePlot
|> linePlotStyle "Prowizja Bolt" "Okres" "%"
|> asNormalizedSvg}

"""

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
 |> averageEarnedByHourOfDay
 |> Seq.map (fun (t, m) -> t.Hour, m.Value)
 |> linePlot
 |> asNormalizedSvg}

Ilość przejazdów dla każdej rozpoczynającej się godziny (czas lokalny):

{finished
 |> ridesByHourOfDay
 |> Seq.map (fun (t, c) -> t.Hour, c)
 |> linePlot
 |> asNormalizedSvg}

## Dzień po dniu

{aggregatedSection (days |> Seq.cast<IReportingSource>)}

## Dni tygodnia

{aggregatedSection (weekDays |> Seq.cast<IReportingSource>)}

## Godziny

{aggregatedSection (hours |> Seq.cast<IReportingSource>)}

"""

File.WriteAllText(getQualifiedStorageLocation "report.md", content)
