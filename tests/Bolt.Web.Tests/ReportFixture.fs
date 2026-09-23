module Bolt.Web.Tests.ReportFixture

open System
open Bolt.ETL.Analysis
open Bolt.Web.Report

let report: AnalysisReport = {
    Email = "a@b.pl"
    GeneratedAt = DateTimeOffset.Parse "2026-07-12T10:00Z"
    RideCount = 42
    SkippedOrders = [||]
    DateRange = (DateTimeOffset.Parse "2026-01-01Z", DateTimeOffset.Parse "2026-06-30Z")
    PriceRegression =
        Some {
            ObservationCount = 100
            RSquared = Some 0.42
            AdjustedRSquared = Some 0.40
            Effects =
                [| { Feature = "dystans_km"; Estimate = Some 3.5; PValue = Some 0.00001
                     CiLow = Some 3.1; CiHigh = Some 3.9 }
                   { Feature = "deszcz"; Estimate = Some -4.25; PValue = Some 0.012
                     CiLow = Some -7.5; CiHigh = Some -1.0 }
                   { Feature = "śnieg"; Estimate = Some 9.9; PValue = Some 0.4
                     CiLow = Some -2.0; CiHigh = Some 21.8 } |]
        }
    HourlyEarnings =
        Some {
            Averages = [| { IsWeekend = false; IsNight = false; Rate = 80.0; HourCount = 2 } |]
            Models =
                [ { Level = 1
                    ObservationCount = 90
                    RSquared = Some 0.31
                    AdjustedRSquared = Some 0.28
                    FoldedDistricts = [||]
                    Effects =
                        [| { Feature = "weekend"; Estimate = Some 6.5; PValue = Some 0.01
                             CiLow = Some 2.0; CiHigh = Some 11.0 }
                           { Feature = "zła_pogoda"; Estimate = Some 3.0; PValue = Some 0.3
                             CiLow = Some -3.0; CiHigh = Some 9.0 } |] } ]
        }
    PickupClusters =
        { Points = [| { Latitude = 50.06123; Longitude = 19.92345; Hour = 8.5 } |]
          Labels = [| 0 |]
          NoiseCount = 0
          Clusters =
            [| { Id = 0; Size = 1; CentroidLatitude = 50.06123
                 CentroidLongitude = 19.92345; MeanHour = 8.5 } |] }
}
