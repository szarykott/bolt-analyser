module Bolt.ETL.Tests.RideClusteringTests

open Xunit
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics

let private canned: StDbscanResponse = {
    Labels = [| 0; 0; 1; -1 |]
    NPoints = 4
    NClusters = 2
    NNoise = 1
    Clusters =
        [| { Id = 0; Size = 2; CentroidLatitude = 50.06123; CentroidLongitude = 19.92345; MeanHour = 8.5 }
           { Id = 1; Size = 5; CentroidLatitude = 50.07; CentroidLongitude = 19.95; MeanHour = 22.25 } |]
}

[<Fact>]
let ``clusterTable sorts by size descending and formats invariantly`` () =
    let table = RideClustering.clusterTable canned
    Assert.Equal<string list>([ "1"; "5"; "50.07000"; "19.95000"; "22.25" ], table.Rows[0])
    Assert.Equal<string list>([ "0"; "2"; "50.06123"; "19.92345"; "8.50" ], table.Rows[1])
    Assert.Contains("noise: 1", table.Title)
