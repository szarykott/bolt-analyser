module Bolt.Web.Tests.ReportFixture

open System
open Bolt.Web.Report

let private highlight date fromAddress toAddress distance earnings : RideHighlight =
    { Date = DateTimeOffset.Parse date
      FromAddress = Some fromAddress
      ToAddress = Some toAddress
      DistanceKm = distance
      Earnings = earnings }

let report: AnalysisReport = {
    Email = "a@b.pl"
    GeneratedAt = DateTimeOffset.Parse "2026-07-12T10:00Z"
    RideCount = 42
    SkippedOrders = [||]
    DateRange = (DateTimeOffset.Parse "2026-01-01Z", DateTimeOffset.Parse "2026-06-30Z")
    BasicStatistics =
        { TotalPaid = 1434.50m
          TotalTips = 50m
          TotalCommission = 200m
          CommissionRate = 200m / 1434.50m
          TotalEarnings = 1234.50m
          PaidCash = 400m
          PaidDigital = 1034.50m
          TotalDistanceKm = 57.25
          LongestRide = highlight "2026-06-01T10:00:00+02:00" "Aleja Długa" "Rynek Główny" 20.5 80m
          ShortestRide = highlight "2026-06-02T10:00:00+02:00" "Dworzec" "Planty" 1.2 15m
          HighestEarningRide = highlight "2026-06-03T10:00:00+02:00" "Lotnisko" "Centrum" 16.0 100m
          LowestEarningRide = highlight "2026-06-04T10:00:00+02:00" "Park" "Muzeum" 2.0 5m
          HourlyAverages =
              [| { IsWeekend = false; IsNight = false; Earnings = 120.0; WorkedHours = 1.5 }
                 { IsWeekend = true; IsNight = true; Earnings = 30.0; WorkedHours = 0.5 } |] }
    PickupClusters =
        { Points = [| { Latitude = 50.06123; Longitude = 19.92345; Hour = 8.5 } |]
          Labels = [| 0 |]
          NoiseCount = 0
          Clusters =
            [| { Id = 0; Size = 1; CentroidLatitude = 50.06123
                 CentroidLongitude = 19.92345; MeanHour = 8.5 } |] }
}
