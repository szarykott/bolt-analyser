module Bolt.ETL.Tests.PerRide2Tests

open Xunit
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics

let private coef name c p lo hi : Coefficient =
    { Name = name; Coef = c; StdErr = Some 0.1; TValue = Some 1.0
      PValue = p; CiLow = lo; CiHigh = hi }

// Names are whatever the service returns; Polish here to mirror production
// (Task 3 makes the request columns Polish).
let private cannedOls: OlsResponse = {
    NObservations = 100
    RSquared = Some 0.42
    AdjRSquared = Some 0.40
    FStatistic = Some 12.5
    FPvalue = Some 0.0001
    Coefficients =
        [| coef "const" (Some 12.0) (Some 0.0) (Some 11.0) (Some 13.0)
           coef "dystans_km" (Some 3.5) (Some 0.00001) (Some 3.1) (Some 3.9)
           coef "deszcz" (Some -4.25) (Some 0.012) (Some -7.5) (Some -1.0)
           coef "śnieg" (Some 9.9) (Some 0.4) (Some -2.0) (Some 21.8) |]
}

[<Fact>]
let ``coefficientsTable keeps significant features sorted by absolute coefficient`` () =
    let table = PerRide2.coefficientsTable cannedOls
    // const excluded, śnieg (p=0.4) excluded; |−4.25| > |3.5|
    Assert.Equal(2, table.Rows.Length)
    Assert.Equal<string list>(
        [ "deszcz"; "-4,25"; "od -7,50 do -1,00"; "0,012" ], table.Rows[0])
    Assert.Equal<string list>(
        [ "dystans_km"; "3,50"; "od 3,10 do 3,90"; "< 0,001" ], table.Rows[1])

[<Fact>]
let ``coefficientsTable has Polish headers`` () =
    let table = PerRide2.coefficientsTable cannedOls
    Assert.Equal<string list>(
        [ "cecha"; "współczynnik [zł]"; "przedział ufności 95%"; "istotność (p)" ],
        table.Headers)

[<Fact>]
let ``coefficientsTable names insignificant features in the notes`` () =
    let table = PerRide2.coefficientsTable cannedOls
    let last = List.last table.Notes
    Assert.Contains("nieistotne", last)
    Assert.Contains("śnieg", last)
    Assert.DoesNotContain("const", last)

[<Fact>]
let ``coefficientsTable notes explain every column`` () =
    let table = PerRide2.coefficientsTable cannedOls
    let notes = String.concat " " table.Notes
    Assert.Contains("cecha", notes)
    Assert.Contains("współczynnik", notes)
    Assert.Contains("przedział ufności", notes)
    Assert.Contains("istotność", notes)

[<Fact>]
let ``coefficientsTable reports when everything is significant`` () =
    let allSignificant =
        { cannedOls with
            Coefficients =
                [| coef "const" (Some 12.0) (Some 0.0) (Some 11.0) (Some 13.0)
                   coef "dystans_km" (Some 3.5) (Some 0.001) (Some 3.1) (Some 3.9) |] }
    let table = PerRide2.coefficientsTable allSignificant
    Assert.Contains("Wszystkie cechy", List.last table.Notes)

[<Fact>]
let ``coefficient with missing p-value counts as insignificant`` () =
    let withMissing =
        { cannedOls with
            Coefficients = [| coef "deszcz" None None None None |] }
    let table = PerRide2.coefficientsTable withMissing
    Assert.Empty(table.Rows)
    Assert.Contains("deszcz", List.last table.Notes)

[<Fact>]
let ``modelStatsTable shows fit in Polish with comma decimals`` () =
    let table = PerRide2.modelStatsTable cannedOls
    Assert.Contains<string list>([ "liczba przejazdów"; "100" ], table.Rows)
    Assert.Contains<string list>([ "R²"; "0,42" ], table.Rows)
    Assert.Contains<string list>([ "skorygowane R²"; "0,40" ], table.Rows)
    Assert.Contains("42%", table.Notes.Head)
