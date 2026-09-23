module Bolt.ETL.Tests.PerRide2Tests

open Xunit
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics
open Bolt.ETL.Meteo.Model
open Bolt.Models

[<Fact>]
let ``toAnalyticsRows retains the regression wire contract`` () =
    let source: PerRide2.RidesDataSource = {
        Rows =
            [| { PricePln = 25.5m
                 Distance = 3.2<km>
                 IsRushHour = true
                 IsWeekend = false
                 PickupDistrict = "centrum"
                 Rain = true
                 Snow = false
                 Temperature = Mild } |]
    }
    let row = (PerRide2.toAnalyticsRows source)[0]
    Assert.Equal<Set<string>>(
        Set [ "cena_pln"; "dystans_km"; "godziny_szczytu"; "weekend"
              "deszcz"; "śnieg"; "dzielnica"; "temperatura" ],
        row |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
    Assert.Equal(box 25.5, row["cena_pln"])
    Assert.Equal(box true, row["deszcz"])
    Assert.Equal(box "umiarkowanie", row["temperatura"])

[<Fact>]
let ``response maps model values without presentation filtering`` () =
    let coefficient name p : Coefficient =
        { Name = name; Coef = Some 2.5; StdErr = None; TValue = None
          PValue = p; CiLow = Some 1.0; CiHigh = Some 4.0 }
    let response: OlsResponse =
        { NObservations = 100; RSquared = Some 0.42; AdjRSquared = Some 0.40
          FStatistic = None; FPvalue = None
          Coefficients = [| coefficient "const" (Some 0.0); coefficient "deszcz" (Some 0.4) |] }
    let result = PerRide2.fromAnalyticsResponse response
    Assert.Equal(100, result.ObservationCount)
    Assert.Equal(Some 0.42, result.RSquared)
    Assert.Single result.Effects |> ignore
    Assert.Equal("deszcz", result.Effects[0].Feature)
    Assert.Equal(Some 0.4, result.Effects[0].PValue)
