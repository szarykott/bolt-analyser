module Bolt.ETL.Analysis.BasicStatistics

open System
open Bolt.Models

type RideHighlight = {
    Date: DateTimeOffset
    FromAddress: string option
    ToAddress: string option
    DistanceKm: float
    Earnings: decimal
}

type BasicStatistics = {
    TotalPaid: decimal
    TotalTips: decimal
    TotalCommission: decimal
    CommissionRate: decimal
    TotalEarnings: decimal
    PaidCash: decimal
    PaidDigital: decimal
    TotalDistanceKm: float
    LongestRide: RideHighlight
    ShortestRide: RideHighlight
    HighestEarningRide: RideHighlight
    LowestEarningRide: RideHighlight
    HourlyAverages: PerHour.BasicHourlyAverage array
}

let private rideEarnings (ride: FinishedRide) =
    ride.Payment.Earned |> Array.sumBy _.Value

let private rideHighlight (ride: FinishedRide) : RideHighlight =
    { Date = ride.Times.RideStart
      FromAddress = ride.Route.Stops |> Array.tryHead |> Option.map _.Address
      ToAddress = ride.Route.Stops |> Array.tryLast |> Option.map _.Address
      DistanceKm = float ride.Route.RideDistance
      Earnings = rideEarnings ride }

let generate (rides: FinishedRide[]) : BasicStatistics =
    let totalEarnings = rides |> Array.sumBy rideEarnings
    let commission =
        rides
        |> Array.sumBy (fun ride ->
            ride.Payment.Earned
            |> Array.filter (fun item -> item.Title = "Prowizja Bolt")
            |> Array.sumBy _.Value)
        |> abs
    let grossBeforeCommission = totalEarnings + commission
    let paidByType isCash =
        rides
        |> Array.filter (fun ride -> (ride.Payment.PaymentMetadata.PaymentType = "cash") = isCash)
        |> Array.sumBy (fun ride -> ride.Payment.Paid |> Array.sumBy _.Value)
    { TotalPaid = rides |> Array.sumBy (fun ride -> ride.Payment.Paid |> Array.sumBy _.Value)
      TotalTips =
        rides
        |> Array.sumBy (fun ride ->
            ride.Payment.Earned
            |> Array.filter (fun item -> item.Title = "Napiwek")
            |> Array.sumBy _.Value)
      TotalCommission = commission
      CommissionRate = if grossBeforeCommission = 0m then 0m else commission / grossBeforeCommission
      TotalEarnings = totalEarnings
      PaidCash = paidByType true
      PaidDigital = paidByType false
      TotalDistanceKm = rides |> Array.sumBy (fun ride -> float ride.Route.RideDistance)
      LongestRide = rides |> Array.maxBy (fun ride -> ride.Route.RideDistance) |> rideHighlight
      ShortestRide = rides |> Array.minBy (fun ride -> ride.Route.RideDistance) |> rideHighlight
      HighestEarningRide = rides |> Array.maxBy rideEarnings |> rideHighlight
      LowestEarningRide = rides |> Array.minBy rideEarnings |> rideHighlight
      HourlyAverages = PerHour.basicHourlyAverages rides }
