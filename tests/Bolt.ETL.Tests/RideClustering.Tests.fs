module Bolt.ETL.Tests.RideClusteringTests

open System
open Xunit
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics

[<Fact>]
let ``pickup points keep coordinates and hour of day`` () =
    let source: RideClustering.RidesDataSource = {
        Rows = [| { Latitude = 50.06123
                    Longitude = 19.92345
                    Time = DateTimeOffset.Parse "2026-07-12T08:30:00Z" } |]
    }
    let point = RideClustering.toPoints source |> Array.head
    Assert.Equal(50.06123, point.Latitude)
    Assert.Equal(19.92345, point.Longitude)
    Assert.Equal(8.5, point.Hour)

[<Fact>]
let ``cluster response keeps labels noise and centers as data`` () =
    let points: RideClustering.PickupPoint array =
        [| { Latitude = 50.0; Longitude = 19.0; Hour = 8.5 } |]
    let response: StDbscanResponse =
        { Labels = [| 2 |]; NPoints = 1; NClusters = 1; NNoise = 0
          Clusters =
            [| { Id = 2; Size = 1; CentroidLatitude = 50.0
                 CentroidLongitude = 19.0; MeanHour = 8.5 } |] }
    let result = RideClustering.fromAnalyticsResponse points response
    Assert.Equal<int[]>([| 2 |], result.Labels)
    Assert.Equal(0, result.NoiseCount)
    Assert.Equal(2, result.Clusters[0].Id)
    Assert.Equal(8.5, result.Clusters[0].MeanHour)
