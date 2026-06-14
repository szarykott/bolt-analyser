open System
open System.IO
open Bolt.Infrastrucutre.ces.OptionBuilder
open Bolt.Infrastrucutre.storage.Constants.Paths
open Bolt.Infrastrucutre.storage.Storage
open Bolt.Models.PreviousOrder
open Bolt.Models.PastOrderDetail
open Bolt.Reporter.Models
open Bolt.Reporter.Plotting

let getOffset (dt: DateTimeOffset) =
    let timezone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")
    timezone.GetUtcOffset(dt)

let finishedRides (rides: RideReportingSource array) =
    rides |> Array.choose (fun r -> match r.Data with
                                    | RideType.Finished r -> Some r
                                    | RideType.DidNotHappen _ -> None)

let totalFinishedRides= finishedRides >> Seq.length

let totalDistance = finishedRides >> Seq.sumBy _.Route.RideDistance.Value

let earnings = finishedRides >> Seq.collect _.Payment.Earned

let totalEarned  = earnings >> Seq.sumBy _.Value.Value

let averageEarned rides = totalEarned rides / decimal (totalFinishedRides rides) 

let ridesPerHour =
    finishedRides
    >> Seq.groupBy (fun r -> let x = r.Times.CreatedTimestamp.Value in DateTimeOffset(DateOnly(x.Year, x.Month, x.Day), TimeOnly(x.Hour, 0 , 0), getOffset x))
    >> Seq.sortBy (fun r -> let dt, _ = r in dt)

let ridesPerHourOfDay =
    finishedRides
    >> Seq.groupBy (fun r -> let x = r.Times.CreatedTimestamp.Value in TimeOnly(x.Hour + x.Offset.Hours, 0 , 0))
    >> Seq.sortBy (fun r -> let dt, _ = r in dt)

let ridesPerDay =
    finishedRides
    >> Seq.groupBy (fun r -> let x = r.Times.CreatedTimestamp.Value in DateOnly(x.Year, x.Month, x.Day))
    >> Seq.sortBy (fun r -> let dt, _ = r in dt)

let earnedPerHour =
    ridesPerHour
    >> Seq.map (fun r -> let d, rds = r in
                            let hourSum = rds |> Seq.collect _.Payment.Earned |> Seq.sum
                            (d, hourSum.Value))
let averageEarnedPerHour =
    earnedPerHour
    >> Seq.averageBy (fun f -> let _, e = f in e.Value)

let averageEarnedPerHourOfDay =
      earnedPerHour
      >> Seq.groupBy (fun (dt, _) -> TimeOnly(dt.Hour + dt.Offset.Hours, 0, 0))
      >> Seq.sortBy fst
      >> Seq.map (fun (t, slots) -> (t, slots |> Seq.averageBy (fun (_, m) -> m.Value)))

let numRidesPerHourOfDay =
    ridesPerHourOfDay
    >> Seq.map (fun (t, rs) -> (t, Seq.length rs))
    
// App

let maybeRides = maybe {
    let! previousOrders : PreviousOrder seq = JsonStorage.read "previousOrders.json"
    let! pastOrders : PastOrderDetail seq = JsonStorage.read "pastOrderDetails.json"

    return Seq.zip previousOrders pastOrders
                |> Seq.map (fun (a, b) -> buildReportingDataSource a b)
                |> Array.ofSeq
}

let rides = maybeRides.Value

let asNormalizedSideSvg plot = asMarkdownSvg 500 300 plot

let content = $"""
# Raport z twojego Bolta

## Zgrubne dane

Liczba twoich ukończnych przejazdów to %i{totalFinishedRides rides}

Średnio za kurs zarobiłeś %.02f{averageEarned rides} zł

Średnie zarobki godzinowe %.02f{averageEarnedPerHour rides} zł  

Całkowity dystans pokonany podczas kursów z klientami %.02f{totalDistance rides} km

## Wykresy

Uśrednione zarobki dla każdej rozpoczynającej się godziny (czas lokalny):

{plot (rides |> averageEarnedPerHourOfDay |> Array.ofSeq) (fun (t, _) -> t.Hour) (fun (_, v) -> float v) |> asNormalizedSideSvg }

Ilość przejazdów dla każdej rozpoczynającej się godziny (czas lokalny):

{plot (rides |> numRidesPerHourOfDay |> Array.ofSeq) (fun (t, _) -> t.Hour) (fun (_, v) -> float v) |> asNormalizedSideSvg}

"""

File.WriteAllText(getQualifiedStorageLocation "report.md", content)


    
