module Bolt.ETL.Tests.PerRide2Tests

open Xunit
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics

let private cannedOls: OlsResponse = {
    NObservations = 100
    RSquared = Some 0.42
    AdjRSquared = Some 0.40
    FStatistic = Some 12.5
    FPvalue = Some 0.0001
    Coefficients =
        [| { Name = "distance_km"; Coef = Some 3.5; StdErr = Some 0.2; TValue = Some 17.5
             PValue = Some 0.0; CiLow = Some 3.1; CiHigh = Some 3.9 }
           { Name = "rain"; Coef = None; StdErr = None; TValue = None
             PValue = None; CiLow = None; CiHigh = None } |]
}

let private cannedMirror: MirrorCheckResponse = {
    NumericCorrelation = { Columns = [| "a" |]; Matrix = [| [| Some 1.0 |] |] }
    EncodedCorrelation = { Columns = [| "a" |]; Matrix = [| [| Some 1.0 |] |] }
    Vif = [| { Feature = "distance_km"; Vif = Some 1.8 } |]
    GroupMeans = Some [| { Group = "centrum"; Mean = Some 4.2 } |]
}

[<Fact>]
let ``coefficientsTable maps names and formats floats`` () =
    let table = PerRide2.coefficientsTable cannedOls
    Assert.Equal<string list>(
        [ "distance_km"; "3.5000"; "0.2000"; "17.5000"; "0.0000"; "3.1000"; "3.9000" ],
        table.Rows[0])
    Assert.Equal<string list>([ "rain"; "–"; "–"; "–"; "–"; "–"; "–" ], table.Rows[1])

[<Fact>]
let ``modelStatsTable lists observation count and fit stats`` () =
    let table = PerRide2.modelStatsTable cannedOls
    Assert.Contains<string list>([ "observations"; "100" ], table.Rows)
    Assert.Contains<string list>([ "R²"; "0.4200" ], table.Rows)

[<Fact>]
let ``vifTable maps features`` () =
    let table = PerRide2.vifTable cannedMirror
    Assert.Equal<string list>([ "distance_km"; "1.8000" ], table.Rows[0])

[<Fact>]
let ``groupMeansTable is None when service returned none`` () =
    Assert.True((PerRide2.groupMeansTable { cannedMirror with GroupMeans = None }).IsNone)
    Assert.True((PerRide2.groupMeansTable cannedMirror).IsSome)
