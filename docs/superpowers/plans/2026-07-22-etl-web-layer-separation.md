# ETL/Web Layer Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move all presentation concerns (tables, charts, Polish labels, number formatting, sort order, Plotly figures) out of `Bolt.ETL` and into `Bolt.Web`, so the ETL analysis layer returns only pure statistical data.

**Architecture:** ETL analysis modules expose `analyze` functions returning raw data contracts (`OlsResponse`, a new `PerHourResult`, a new `ClusterData`). `AnalysisPipeline.run` packs them into `AnalysisSection { Id; Data: SectionData option }`. `Bolt.Web` owns view-model types (`Views/ResultTable`, `Views/ResultChart`) and mapper modules (`Mappings/*Mapper.fs`) that turn each `SectionData` case into tables/charts, and owns section titles/descriptions keyed by `Id`. Work is sequenced additive-first (new code alongside old) so each task builds green; a single cutover task flips the seam.

**Tech Stack:** F# (net10.0), Giraffe.ViewEngine 1.4.0, Plotly.NET 5.1.0, xUnit, a Python analytics microservice (HTTP, not modified here).

## Global Constraints

- Target framework `net10.0`; F# `<Compile>` **order in `.fsproj` matters** — a file may only reference types defined in files listed above it.
- Number formatting uses `CultureInfo.GetCultureInfo "pl-PL"` (comma decimals, e.g. `6,50`); the missing-value placeholder is `"–"` (en dash).
- **Coefficient names stay Polish** — they are the analytics wire keys (`pd.get_dummies` builds `"column_value"`); Web consumes `Coefficient.Name` as an opaque display label. The Python service and the `toAnalyticsRows` column names are **not** changed.
- `TemperatureBucket.label` (`Bolt.ETL/Meteo/Model.fs`) stays in ETL — it is used to generate analytics **wire values**, not display strings.
- Giraffe.ViewEngine 1.4.0 HTML-encodes both text and attribute values; **never** manually escape strings before handing them to view nodes (double-encoding bug history).
- Significance rule: a coefficient is significant when `PValue = Some p && p < 0.05`; `const` is always excluded from coefficient tables.

## File Structure

**ETL (data only after this plan):**
- `src/Bolt.ETL/Analysis/Report.fs` — pure-data seam types (rewritten in the cutover task).
- `src/Bolt.ETL/Analysis/PerRide.fs` — keeps `runOls`/`toAnalyticsRows`; `buildSection` deleted in cutover.
- `src/Bolt.ETL/Analysis/PerHour.fs` — keeps the data engine (`Dataset`, `Ladder`); gains `analyze`; loses `Rung0.table`, `Display` presentation, notes, `buildSection` in cutover.
- `src/Bolt.ETL/Analysis/RideClustering.fs` — keeps stats call; gains `analyze`; loses `clusterTable`, chart, `buildSection` in cutover.
- `src/Bolt.ETL/AnalysisPipeline.fs` — assembles `SectionData` (cutover).
- `src/Bolt.ETL/Plotting/ClusterMap.fs` — **moved to Web** in cutover; deleted from ETL.

**Web (presentation):**
- `src/Bolt.Web/Views/ResultTable.fs`, `Views/ResultChart.fs` — existing view models (unchanged).
- `src/Bolt.Web/Mappings/OlsMapper.fs` — existing; wired in + tested.
- `src/Bolt.Web/Mappings/PerHourMapper.fs` — **new**; `PerHourResult` → tables.
- `src/Bolt.Web/Mappings/ClusterMap.fs` — **new** (moved from ETL); Plotly map layers.
- `src/Bolt.Web/Mappings/ClusterMapper.fs` — **new**; `ClusterData` → table + chart.
- `src/Bolt.Web/Views/Html.fs` — dispatch on `SectionData`, own section meta (cutover).

**Tests:**
- `tests/Bolt.ETL.Tests/PerHour.Tests.fs` — keep data/engine tests; drop presentation tests (moved to Web) in cutover.
- `tests/Bolt.ETL.Tests/PerRide2.Tests.fs` — rewrite to data-only (stale: calls removed functions).
- `tests/Bolt.Web.Tests/PerHourMapper.Tests.fs` — **new**.
- `tests/Bolt.Web.Tests/OlsMapper.Tests.fs` — **new**.
- `tests/Bolt.Web.Tests/ClusterMapper.Tests.fs` — **new**.
- `tests/Bolt.Web.Tests/Views.Tests.fs` — rewrite fixtures to the new section shape (cutover).

**Baseline note:** the working tree already contains an in-progress migration (Web-side `ResultTable`/`ResultChart`/`OlsMapper` exist; `PerRide.buildSection` returns empty tables). Start from the current working tree, not a clean checkout.

---

### Task 1: ETL — pure-data types + `PerHour.analyze` (additive)

Add the leaf data types and a data-returning `analyze` to `PerHour`, **keeping** the existing `Display`/`Rung0`/`buildSection` so the project still builds. `AnalysisSection`/`AnalysisReport` keep their old shape for now.

**Files:**
- Modify: `src/Bolt.ETL/Analysis/Report.fs` (append new types; add one `open`)
- Modify: `src/Bolt.ETL/Analysis/PerHour.fs` (add `weightedMeans` + `analyze`)
- Test: `tests/Bolt.ETL.Tests/PerHour.Tests.fs` (add weighted-means data test)

**Interfaces:**
- Produces: `type PerHourWeightedMean = { IsWeekend: bool; IsNight: bool; MeanPerHour: float; Count: int }`; `type PerHourRung = { Level: int; Ols: OlsResponse }`; `type PerHourResult = { WeightedMeans: PerHourWeightedMean array; Rungs: PerHourRung array; FullyUnlocked: bool; HasFoldedDistricts: bool }`; `PerHour.analyze : HourlyDataSource -> Task<PerHourResult>`; `PerHour.weightedMeans : HourRow array -> PerHourWeightedMean array`.
- Consumes: `OlsResponse`, `Coefficient` (`Bolt.ETL.Analytics`); `PerHour.Ladder.unlockedRungs`, `PerHour.Display.toAnalyticsRows`, `AnalyticsClient.wlsRegression` (existing).

- [ ] **Step 1: Add the pure-data types to `Report.fs`**

At the top of `src/Bolt.ETL/Analysis/Report.fs`, after `open System`, add `open Bolt.ETL.Analytics` (the fsproj compiles `Analytics/*` before `Analysis/Report.fs`, so this resolves). Then append these types at the end of the file (leave the existing `ResultTable`/`ResultChart`/`AnalysisSection`/`AnalysisReport` in place for now):

```fsharp
/// Rung 0 of the per-hour ladder: a grouped weighted mean, unformatted.
type PerHourWeightedMean = {
    IsWeekend: bool
    IsNight: bool
    MeanPerHour: float
    Count: int
}

/// One unlocked tier of the per-hour WLS ladder.
type PerHourRung = { Level: int; Ols: OlsResponse }

type PerHourResult = {
    WeightedMeans: PerHourWeightedMean array
    Rungs: PerHourRung array
    FullyUnlocked: bool
    HasFoldedDistricts: bool
}

/// Raw clustering output; points travel with the response for the map layer.
type ClusterData = { Points: StPoint array; Response: StDbscanResponse }
```

- [ ] **Step 2: Write the failing test for weighted means**

Add to `tests/Bolt.ETL.Tests/PerHour.Tests.fs` (it already has a `HourRow` fixture helper; reuse or build rows inline). Test that grouping is by `(IsWeekend, IsNight)` and the mean is fill-weighted:

```fsharp
[<Fact>]
let ``weightedMeans groups by weekend and night with fill-weighted mean`` () =
    let row isWeekend isNight fill rate : PerHour.HourRow =
        { HourStart = System.DateTimeOffset.UnixEpoch; Fill = fill; Rate = rate
          Shares = Map.empty; Rain = false; Snow = false
          Temperature = Bolt.ETL.Meteo.Model.Mild
          IsNight = isNight; IsWeekend = isWeekend; IsRushHour = false }
    let rows =
        [| row false false 1.0 10.0
           row false false 0.5 20.0   // same group: mean = (1*10 + 0.5*20)/(1.5) = 13.33…
           row true true 1.0 30.0 |]
    let means = PerHour.weightedMeans rows
    Assert.Equal(2, means.Length)
    let workday = means |> Array.find (fun m -> not m.IsWeekend && not m.IsNight)
    Assert.Equal(2, workday.Count)
    Assert.Equal(20.0 / 1.5, workday.MeanPerHour, 3)
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Bolt.ETL.Tests --filter "FullyQualifiedName~weightedMeans"`
Expected: FAIL — `PerHour.weightedMeans` not defined (compile error).

- [ ] **Step 4: Implement `weightedMeans` and `analyze`**

In `src/Bolt.ETL/Analysis/PerHour.fs`, add these at module level (after the `Ladder` module, alongside — not inside — `Rung0`/`Display`, which stay for now):

```fsharp
let weightedMeans (rows: HourRow array) : PerHourWeightedMean array =
    rows
    |> Array.groupBy (fun r -> r.IsWeekend, r.IsNight)
    |> Array.map (fun ((isWeekend, isNight), group) ->
        let effectiveHours = group |> Array.sumBy _.Fill
        let earnings = group |> Array.sumBy (fun r -> r.Rate * r.Fill)
        { IsWeekend = isWeekend
          IsNight = isNight
          MeanPerHour = earnings / effectiveHours
          Count = group.Length })

let analyze (source: HourlyDataSource) : Task<PerHourResult> =
    task {
        let rows = source.Rows
        let rungs = Ladder.unlockedRungs rows

        let results = ResizeArray<Ladder.Rung * OlsResponse>()
        for rung in rungs do
            let! response =
                AnalyticsClient.wlsRegression {
                    Rows = Display.toAnalyticsRows rung rows
                    Target = "stawka_pln_h"
                    Weights = "waga"
                    DropColumns = [||]
                    CategoricalColumns = None
                    Standardize = false
                }
            results.Add(rung, response)

        let highest = results |> Seq.tryLast |> Option.map fst
        let fullyUnlocked =
            match highest with
            | Some rung -> rung.Level = 4 && Array.isEmpty rung.FoldedDistricts
            | None -> false
        let hasFolded =
            match highest with
            | Some rung -> rung.Level = 4 && not (Array.isEmpty rung.FoldedDistricts)
            | None -> false

        return
            { WeightedMeans = weightedMeans rows
              Rungs = results |> Seq.map (fun (r, resp) -> { Level = r.Level; Ols = resp }) |> Array.ofSeq
              FullyUnlocked = fullyUnlocked
              HasFoldedDistricts = hasFolded }
    }
```

Note: `analyze` references `Display.toAnalyticsRows`, which still exists in this task. (The cutover task moves it to module level.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Bolt.ETL.Tests --filter "FullyQualifiedName~weightedMeans"`
Expected: PASS. Then `dotnet build src/Bolt.ETL` — Expected: build succeeds (old `buildSection` still present).

- [ ] **Step 6: Commit**

```bash
git add src/Bolt.ETL/Analysis/Report.fs src/Bolt.ETL/Analysis/PerHour.fs tests/Bolt.ETL.Tests/PerHour.Tests.fs
git commit -m "feat(etl): add pure-data per-hour result types and analyze"
```

---

### Task 2: ETL — `RideClustering.analyze` (additive)

Add a data-returning `analyze` beside the existing `clusterTable`/`buildSection` (which still compile with Plotly).

**Files:**
- Modify: `src/Bolt.ETL/Analysis/RideClustering.fs` (add `analyze`)
- Test: `tests/Bolt.ETL.Tests/RideClustering.Tests.fs` (create if absent, else append)

**Interfaces:**
- Produces: `RideClustering.analyze : RidesDataSource -> Task<ClusterData>` where `ClusterData = { Points: StPoint array; Response: StDbscanResponse }`.
- Consumes: `RideClustering.toStPoints` (existing), `AnalyticsClient.stDbscan` (existing).

- [ ] **Step 1: Write the failing test**

`analyze` makes an HTTP call, so unit-test only the pure `toStPoints` shaping that `analyze` reuses (the data that lands in `ClusterData.Points`). Add to `tests/Bolt.ETL.Tests/RideClustering.Tests.fs`:

```fsharp
module Bolt.ETL.Tests.RideClusteringTests

open System
open Xunit
open Bolt.ETL.Analysis

[<Fact>]
let ``toStPoints maps hour as fractional hours`` () =
    let src: RideClustering.RidesDataSource =
        { Rows = [| { Latitude = 50.0; Longitude = 19.9
                      Time = DateTimeOffset(2026, 1, 1, 6, 30, 0, TimeSpan.Zero) } |] }
    let pts = RideClustering.toStPoints src
    Assert.Equal(1, pts.Length)
    Assert.Equal(6.5, pts[0].Hour, 3)
    Assert.Equal(50.0, pts[0].Latitude, 6)
```

- [ ] **Step 2: Run test to verify it fails/passes-baseline**

Run: `dotnet test tests/Bolt.ETL.Tests --filter "FullyQualifiedName~RideClusteringTests"`
Expected: if `RideClustering.Tests.fs` is newly created, first add it to `tests/Bolt.ETL.Tests/Bolt.ETL.Tests.fsproj` `<Compile>` list (before the test entry points); the test should then PASS (toStPoints already exists). This test guards the shaping `analyze` depends on.

- [ ] **Step 3: Implement `analyze`**

In `src/Bolt.ETL/Analysis/RideClustering.fs`, add after `buildSection` (keep `buildSection` and the Plotly code for now):

```fsharp
let analyze (source: RidesDataSource) : Task<ClusterData> =
    task {
        let points = toStPoints source
        let! response =
            AnalyticsClient.stDbscan {
                Points = points
                EpsKm = 0.7
                EpsHours = 1
                MinSamples = 5
            }
        return { Points = points; Response = response }
    }
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/Bolt.ETL`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.ETL/Analysis/RideClustering.fs tests/Bolt.ETL.Tests/RideClustering.Tests.fs tests/Bolt.ETL.Tests/Bolt.ETL.Tests.fsproj
git commit -m "feat(etl): add ride-clustering analyze returning pure ClusterData"
```

---

### Task 3: Web — `PerHourMapper` (additive)

Create the Web mapper that turns `PerHourResult` into `Views.ResultTable list`, porting the exact wording/formatting deleted-later from `PerHour.Rung0.table` and `PerHour.Display`. Nothing calls it yet.

**Files:**
- Create: `src/Bolt.Web/Mappings/PerHourMapper.fs`
- Modify: `src/Bolt.Web/Bolt.Web.fsproj` (add `<Compile>` before `Views\Html.fs`)
- Test: `tests/Bolt.Web.Tests/PerHourMapper.Tests.fs` (new)

**Interfaces:**
- Produces: `PerHourMapper.tables : PerHourResult -> Bolt.Web.Views.ResultTable list`.
- Consumes: `PerHourResult`, `PerHourRung`, `PerHourWeightedMean` (`Bolt.ETL.Analysis`); `Coefficient`, `OlsResponse` (`Bolt.ETL.Analytics`); `Bolt.Web.Views.ResultTable`.

- [ ] **Step 1: Register the file in the fsproj**

In `src/Bolt.Web/Bolt.Web.fsproj`, add `<Compile Include="Mappings\PerHourMapper.fs" />` immediately after the existing `<Compile Include="Mappings\OlsMapper.fs" />` and before `Views\Html.fs`. (Move `Views\Html.fs` below all `Mappings\*` if it is currently above them; Html will consume the mappers in the cutover task.)

- [ ] **Step 2: Write the failing tests**

Create `tests/Bolt.Web.Tests/PerHourMapper.Tests.fs` (add it to `tests/Bolt.Web.Tests/Bolt.Web.Tests.fsproj` `<Compile>` before the test entry points):

```fsharp
module Bolt.Web.Tests.PerHourMapperTests

open Xunit
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics
open Bolt.Web.Mappings

let private coef name c p lo hi : Coefficient =
    { Name = name; Coef = c; StdErr = Some 0.1; TValue = Some 1.0
      PValue = p; CiLow = lo; CiHigh = hi }

let private ols coefs : OlsResponse =
    { NObservations = 50; RSquared = Some 0.30; AdjRSquared = Some 0.28
      FStatistic = Some 5.0; FPvalue = Some 0.01; Coefficients = coefs }

[<Fact>]
let ``weighted-means table is sorted by rate desc with Polish labels`` () =
    let result: PerHourResult =
        { WeightedMeans =
            [| { IsWeekend = false; IsNight = false; MeanPerHour = 10.0; Count = 3 }
               { IsWeekend = true;  IsNight = true;  MeanPerHour = 25.0; Count = 1 } |]
          Rungs = [||]; FullyUnlocked = false; HasFoldedDistricts = false }
    let table = (PerHourMapper.tables result).Head
    Assert.Equal("Średnie zarobki na godzinę pracy", table.Title)
    Assert.Equal<string list>([ "kiedy"; "zł za godzinę"; "liczba godzin" ], table.Headers)
    Assert.Equal<string list>([ "weekend, noc"; "25,00"; "1" ], table.Rows[0])
    Assert.Equal<string list>([ "dzień roboczy, dzień"; "10,00"; "3" ], table.Rows[1])

[<Fact>]
let ``rung coefficient table titles carry the level and drop insignificant`` () =
    let result: PerHourResult =
        { WeightedMeans = [||]
          Rungs =
            [| { Level = 1
                 Ols = ols [| coef "const" (Some 5.0) (Some 0.0) (Some 4.0) (Some 6.0)
                              coef "weekend" (Some 6.5) (Some 0.001) (Some 4.0) (Some 9.0)
                              coef "noc" (Some 1.0) (Some 0.9) (Some -1.0) (Some 3.0) |] } |]
          FullyUnlocked = true; HasFoldedDistricts = false }
    let tables = PerHourMapper.tables result
    let rung = tables |> List.find (fun t -> t.Title.Contains "poziom 1")
    Assert.Equal<string list>([ "weekend"; "6,50"; "od 4,00 do 9,00"; "< 0,001" ], rung.Rows[0])
    Assert.Contains("noc", List.last rung.Notes)

[<Fact>]
let ``locked note shown when not fully unlocked`` () =
    let result: PerHourResult =
        { WeightedMeans = [||]
          Rungs = [| { Level = 1; Ols = ols [| coef "weekend" (Some 6.5) (Some 0.001) (Some 4.0) (Some 9.0) |] } |]
          FullyUnlocked = false; HasFoldedDistricts = false }
    let allNotes = PerHourMapper.tables result |> List.collect (fun t -> t.Notes) |> String.concat " "
    Assert.Contains("Odblokują się same", allNotes)
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~PerHourMapperTests"`
Expected: FAIL — `PerHourMapper` not defined.

- [ ] **Step 4: Implement `PerHourMapper`**

Create `src/Bolt.Web/Mappings/PerHourMapper.fs` (ports `Rung0.table`, `Display.coefficientsTable`, `Display.modelStatsTable`, and the trailing-note logic from `PerHour.fs`, retargeted to `PerHourResult` and `Bolt.Web.Views.ResultTable`):

```fsharp
namespace Bolt.Web.Mappings

open System
open System.Globalization
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics
open Bolt.Web.Views

module PerHourMapper =
    let private pl = CultureInfo.GetCultureInfo "pl-PL"
    let private fmt2 (v: float) = v.ToString("F2", pl)
    let private fmt2Opt (v: float option) = v |> Option.map fmt2 |> Option.defaultValue "–"

    let private fmtPValue (p: float option) =
        match p with
        | Some p when p < 0.001 -> "< 0,001"
        | Some p -> p.ToString("F3", pl)
        | None -> "–"

    let private isSignificant (c: Coefficient) =
        match c.PValue with
        | Some p -> p < 0.05
        | None -> false

    let private groupLabel isWeekend isNight =
        let day = if isWeekend then "weekend" else "dzień roboczy"
        let time = if isNight then "noc" else "dzień"
        $"{day}, {time}"

    let private weightedMeansTable (means: PerHourWeightedMean array) : ResultTable =
        { Title = "Średnie zarobki na godzinę pracy"
          Headers = [ "kiedy"; "zł za godzinę"; "liczba godzin" ]
          Rows =
            means
            |> Array.sortByDescending _.MeanPerHour
            |> Array.map (fun m -> [ groupLabel m.IsWeekend m.IsNight; fmt2 m.MeanPerHour; string m.Count ])
            |> List.ofArray
          Notes =
            [ "zł za godzinę — średnia ważona czasem pracy: godziny przepracowane w całości liczą się mocniej niż ledwie zaczęte."
              "liczba godzin — ile godzin zegarowych z jazdą wpadło do danej grupy." ] }

    let private coefficientsTable (rung: PerHourRung) : ResultTable =
        let features = rung.Ols.Coefficients |> Array.filter (fun c -> c.Name <> "const")
        let significant, insignificant = features |> Array.partition isSignificant
        let rows =
            significant
            |> Array.sortByDescending (fun c -> c.Coef |> Option.map abs |> Option.defaultValue 0.0)
            |> Array.map (fun c ->
                [ c.Name
                  fmt2Opt c.Coef
                  (match c.CiLow, c.CiHigh with
                   | Some lo, Some hi -> $"od {fmt2 lo} do {fmt2 hi}"
                   | _ -> "–")
                  fmtPValue c.PValue ])
            |> List.ofArray
        let insignificantNote =
            if Array.isEmpty insignificant then
                "Wszystkie warunki w tym zestawieniu są istotne statystycznie (p < 0,05)."
            else
                let names = insignificant |> Array.map _.Name |> String.concat ", "
                $"Warunki, których wpływu nie widać wyraźnie w danych (p ≥ 0,05), pominięte w tabeli: {names}."
        { Title = $"Wpływ warunków na zarobki na godzinę — poziom {rung.Level}"
          Headers = [ "warunek"; "współczynnik [zł/h]"; "przedział ufności 95%"; "istotność (p)" ]
          Rows = rows
          Notes =
            [ "warunek — okoliczność panująca w danej godzinie; nazwy dzielnica_… oznaczają różnicę względem Starego Miasta (centrum), zakresy temperatur — względem pozostałych temperatur."
              "współczynnik [zł/h] — o ile złotych na godzinę pracy zmieniają się zarobki, gdy dany warunek występuje; wiersze posortowane od najsilniejszego wpływu."
              "przedział ufności 95% — zakres, w którym z 95-procentową pewnością mieści się prawdziwa wartość współczynnika."
              "istotność (p) — prawdopodobieństwo, że tak silny efekt pojawiłby się przypadkiem; wartości poniżej 0,05 uznaje się za istotne statystycznie."
              insignificantNote ] }

    let private modelStatsTable (response: OlsResponse) : ResultTable =
        let fitNote =
            match response.RSquared with
            | Some r2 -> [ $"Model wyjaśnia {int (Math.Round(r2 * 100.0))}%% zmienności zarobków na godzinę." ]
            | None -> []
        { Title = "Dopasowanie modelu"
          Headers = [ "statystyka"; "wartość" ]
          Rows =
            [ [ "liczba godzin"; string response.NObservations ]
              [ "R²"; fmt2Opt response.RSquared ]
              [ "skorygowane R²"; fmt2Opt response.AdjRSquared ] ]
          Notes =
            fitNote
            @ [ "R² — jaka część zmienności zarobków jest wyjaśniona przez model (od 0 do 1, wyżej = lepiej); skorygowane R² dodatkowo uwzględnia liczbę cech w modelu." ] }

    let private lockedNote =
        "Niektóre analizy (np. wpływ poszczególnych dzielnic albo dokładnych zakresów temperatury) nie są jeszcze pokazywane — mamy na razie za mało godzin jazdy, żeby policzyć je uczciwie. Odblokują się same, gdy przybędzie danych."
    let private foldedNote =
        "Część dzielnic zliczona razem jako «inne dzielnice» — za mało godzin w każdej z osobna."
    let private legendNote =
        "Liczby w wyższych tabelach mogą się różnić od niższych — dokładniejszy model oddziela efekty, które prostszy liczył razem."

    let private appendNotesToLast (notes: string list) (tables: ResultTable list) =
        match List.rev tables with
        | [] -> []
        | last :: rest -> List.rev ({ last with Notes = last.Notes @ notes } :: rest)

    let tables (result: PerHourResult) : ResultTable list =
        let rungTables = result.Rungs |> Array.map coefficientsTable |> List.ofArray
        let statsTable =
            result.Rungs |> Array.tryLast |> Option.map (fun r -> [ modelStatsTable r.Ols ]) |> Option.defaultValue []
        let trailingNotes =
            [ if not result.FullyUnlocked then lockedNote
              if result.HasFoldedDistricts then foldedNote
              if List.length rungTables >= 2 then legendNote ]
        appendNotesToLast trailingNotes (weightedMeansTable result.WeightedMeans :: rungTables @ statsTable)
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~PerHourMapperTests"`
Expected: PASS. Then `dotnet build src/Bolt.Web` — Expected: succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/Bolt.Web/Mappings/PerHourMapper.fs src/Bolt.Web/Bolt.Web.fsproj tests/Bolt.Web.Tests/PerHourMapper.Tests.fs tests/Bolt.Web.Tests/Bolt.Web.Tests.fsproj
git commit -m "feat(web): add PerHourMapper mapping PerHourResult to tables"
```

---

### Task 4: Web — OlsMapper tests (guard the price-regression mapper)

`OlsMapper` already exists and already carries the price-regression wording. Add the tests migrated from the stale `PerRide2.Tests.fs` so the mapper is verified before it is wired in.

**Files:**
- Test: `tests/Bolt.Web.Tests/OlsMapper.Tests.fs` (new)
- Modify: `tests/Bolt.Web.Tests/Bolt.Web.Tests.fsproj` (register)

**Interfaces:**
- Consumes: `OlsMapper.coefficientsTable : OlsResponse -> ResultTable`, `OlsMapper.modelStatsTable : OlsResponse -> ResultTable` (existing).

- [ ] **Step 1: Write the tests**

Create `tests/Bolt.Web.Tests/OlsMapper.Tests.fs` (register in the fsproj `<Compile>` before the test entry points):

```fsharp
module Bolt.Web.Tests.OlsMapperTests

open Xunit
open Bolt.ETL.Analytics
open Bolt.Web.Mappings

let private coef name c p lo hi : Coefficient =
    { Name = name; Coef = c; StdErr = Some 0.1; TValue = Some 1.0
      PValue = p; CiLow = lo; CiHigh = hi }

let private cannedOls : OlsResponse =
    { NObservations = 100; RSquared = Some 0.42; AdjRSquared = Some 0.40
      FStatistic = Some 12.5; FPvalue = Some 0.0001
      Coefficients =
        [| coef "const" (Some 12.0) (Some 0.0) (Some 11.0) (Some 13.0)
           coef "dystans_km" (Some 3.5) (Some 0.00001) (Some 3.1) (Some 3.9)
           coef "deszcz" (Some -4.25) (Some 0.012) (Some -7.5) (Some -1.0)
           coef "śnieg" (Some 9.9) (Some 0.4) (Some -2.0) (Some 21.8) |] }

[<Fact>]
let ``coefficientsTable keeps significant features sorted by absolute coefficient`` () =
    let table = OlsMapper.coefficientsTable cannedOls
    Assert.Equal(2, table.Rows.Length)
    Assert.Equal<string list>([ "deszcz"; "-4,25"; "od -7,50 do -1,00"; "0,012" ], table.Rows[0])
    Assert.Equal<string list>([ "dystans_km"; "3,50"; "od 3,10 do 3,90"; "< 0,001" ], table.Rows[1])

[<Fact>]
let ``coefficientsTable has Polish headers`` () =
    let table = OlsMapper.coefficientsTable cannedOls
    Assert.Equal<string list>(
        [ "cecha"; "współczynnik [zł]"; "przedział ufności 95%"; "istotność (p)" ], table.Headers)

[<Fact>]
let ``coefficientsTable names insignificant features in the notes`` () =
    let last = List.last (OlsMapper.coefficientsTable cannedOls).Notes
    Assert.Contains("nieistotne", last)
    Assert.Contains("śnieg", last)
    Assert.DoesNotContain("const", last)

[<Fact>]
let ``coefficient with missing p-value counts as insignificant`` () =
    let table = OlsMapper.coefficientsTable { cannedOls with Coefficients = [| coef "deszcz" None None None None |] }
    Assert.Empty(table.Rows)
    Assert.Contains("deszcz", List.last table.Notes)

[<Fact>]
let ``modelStatsTable shows fit in Polish with comma decimals`` () =
    let table = OlsMapper.modelStatsTable cannedOls
    Assert.Contains<string list>([ "liczba przejazdów"; "100" ], table.Rows)
    Assert.Contains<string list>([ "R²"; "0,42" ], table.Rows)
    Assert.Contains("42%", table.Notes.Head)
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~OlsMapperTests"`
Expected: PASS (mapper already implemented). If any assertion fails, fix `OlsMapper` to match the wording, not the test.

- [ ] **Step 3: Commit**

```bash
git add tests/Bolt.Web.Tests/OlsMapper.Tests.fs tests/Bolt.Web.Tests/Bolt.Web.Tests.fsproj
git commit -m "test(web): cover OlsMapper price-regression tables"
```

---

### Task 5: Web — move `ClusterMap` + add `ClusterMapper` (additive)

Relocate the Plotly map layers into Web and add the clustering mapper. Add the `Plotly.NET` package to Web. ETL keeps its copy for now (removed in cutover).

**Files:**
- Create: `src/Bolt.Web/Mappings/ClusterMap.fs` (verbatim move of `src/Bolt.ETL/Plotting/ClusterMap.fs`)
- Create: `src/Bolt.Web/Mappings/ClusterMapper.fs`
- Modify: `src/Bolt.Web/Bolt.Web.fsproj` (add `Plotly.NET` PackageReference; add both `<Compile>` before `Views\Html.fs`)
- Test: `tests/Bolt.Web.Tests/ClusterMapper.Tests.fs` (new)

**Interfaces:**
- Produces: `ClusterMapper.table : ClusterData -> Bolt.Web.Views.ResultTable`; `ClusterMapper.chart : ClusterData -> Bolt.Web.Views.ResultChart`.
- Consumes: `ClusterData`, `StDbscanResponse`, `StPoint`, `ClusterStats` (`Bolt.ETL.Analytics` / `Bolt.ETL.Analysis`); `Plotly.NET`.

- [ ] **Step 1: Move ClusterMap into Web**

Copy `src/Bolt.ETL/Plotting/ClusterMap.fs` to `src/Bolt.Web/Mappings/ClusterMap.fs` **verbatim except the first line**: change `namespace Bolt.ETL.Plotting` to `namespace Bolt.Web.Mappings`. The body already depends only on `Bolt.ETL.Analytics` contracts and `Plotly.NET`, so nothing else changes. Do **not** delete the ETL copy yet.

- [ ] **Step 2: Register Plotly + files in the fsproj**

In `src/Bolt.Web/Bolt.Web.fsproj`: add `<PackageReference Include="Plotly.NET" Version="5.1.0" />` to the package `ItemGroup`; add `<Compile Include="Mappings\ClusterMap.fs" />` then `<Compile Include="Mappings\ClusterMapper.fs" />` after `PerHourMapper.fs` and before `Views\Html.fs`.

- [ ] **Step 3: Write the failing tests**

Create `tests/Bolt.Web.Tests/ClusterMapper.Tests.fs` (register in fsproj before test entry points):

```fsharp
module Bolt.Web.Tests.ClusterMapperTests

open Xunit
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics
open Bolt.Web.Mappings

let private data : ClusterData =
    { Points =
        [| { Latitude = 50.06; Longitude = 19.94; Hour = 8.5 }
           { Latitude = 50.07; Longitude = 19.95; Hour = 9.0 } |]
      Response =
        { Labels = [| 0; 0 |]; NPoints = 2; NClusters = 1; NNoise = 0
          Clusters =
            [| { Id = 0; Size = 2; CentroidLatitude = 50.065
                 CentroidLongitude = 19.945; MeanHour = 8.75 } |] } }

[<Fact>]
let ``table has Polish headers and cluster rows sorted by size`` () =
    let table = ClusterMapper.table data
    Assert.Equal<string list>(
        [ "skupisko"; "liczba przejazdów"; "szer. geogr."; "dł. geogr."; "średnia godzina" ], table.Headers)
    Assert.Equal("0", table.Rows[0].Head)
    Assert.Contains("punkty: 2", table.Title)

[<Fact>]
let ``chart produces non-empty plotly figure json`` () =
    let chart = ClusterMapper.chart data
    Assert.Contains("\"data\"", chart.PlotlyFigureJson)
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~ClusterMapperTests"`
Expected: FAIL — `ClusterMapper` not defined.

- [ ] **Step 5: Implement `ClusterMapper`**

Create `src/Bolt.Web/Mappings/ClusterMapper.fs` (ports `RideClustering.clusterTable` and the chart build from `RideClustering.buildSection`):

```fsharp
namespace Bolt.Web.Mappings

open System.Globalization
open Bolt.ETL.Analysis
open Bolt.ETL.Analytics
open Bolt.Web.Views
open Plotly.NET

module ClusterMapper =
    let table (data: ClusterData) : ResultTable =
        let response = data.Response
        { Title = $"Skupiska (punkty: {response.NPoints}, poza skupiskami: {response.NNoise})"
          Headers = [ "skupisko"; "liczba przejazdów"; "szer. geogr."; "dł. geogr."; "średnia godzina" ]
          Rows =
            response.Clusters
            |> Array.sortByDescending _.Size
            |> Array.map (fun c ->
                [ string c.Id
                  string c.Size
                  c.CentroidLatitude.ToString("F5", CultureInfo.InvariantCulture)
                  c.CentroidLongitude.ToString("F5", CultureInfo.InvariantCulture)
                  c.MeanHour.ToString("F2", CultureInfo.InvariantCulture) ])
            |> List.ofArray
          Notes = [] }

    let chart (data: ClusterData) : ResultChart =
        let figure =
            [ ClusterMap.ridePointsLayer data.Points data.Response
              ClusterMap.centroidLayer data.Response ]
            |> Chart.combine
            |> ClusterMap.withMapStyle data.Points
            |> GenericChart.toFigureJson
        { Title = "Mapa skupisk odbiorów"; PlotlyFigureJson = figure }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~ClusterMapperTests"`
Expected: PASS. Then `dotnet build src/Bolt.Web` — Expected: succeeds (ETL ClusterMap still present too).

- [ ] **Step 7: Commit**

```bash
git add src/Bolt.Web/Mappings/ClusterMap.fs src/Bolt.Web/Mappings/ClusterMapper.fs src/Bolt.Web/Bolt.Web.fsproj tests/Bolt.Web.Tests/ClusterMapper.Tests.fs tests/Bolt.Web.Tests/Bolt.Web.Tests.fsproj
git commit -m "feat(web): move cluster map to web and add ClusterMapper"
```

---

### Task 6: Cutover — flip the seam to pure data

The atomic switch. Replace the ETL section types with the pure-data envelope, make the pipeline and Html use it, delete the old `buildSection`/`Display`/`Rung0.table`/`clusterTable`/ETL `ClusterMap`, and rewrite the affected tests. Everything in one commit because F# will not build a half-flipped seam.

**Files:**
- Modify: `src/Bolt.ETL/Analysis/Report.fs` (replace old presentation types with `SectionData`/`AnalysisSection`/`AnalysisReport`)
- Modify: `src/Bolt.ETL/Analysis/PerHour.fs` (delete `Rung0`, `Display` presentation, notes, `buildSection`; move `toAnalyticsRows` to module level)
- Modify: `src/Bolt.ETL/Analysis/PerRide.fs` (delete `buildSection`, drop `open System.Globalization`)
- Modify: `src/Bolt.ETL/Analysis/RideClustering.fs` (delete `clusterTable`, `buildSection`, `open Plotly.NET`, `open Bolt.ETL.Plotting`)
- Delete: `src/Bolt.ETL/Plotting/ClusterMap.fs`
- Modify: `src/Bolt.ETL/AnalysisPipeline.fs` (assemble `SectionData`)
- Modify: `src/Bolt.ETL/Bolt.ETL.fsproj` (remove `Plotting\ClusterMap.fs` compile + `Plotly.NET` package)
- Modify: `src/Bolt.Web/Views/Html.fs` (dispatch on `SectionData`; own section meta)
- Modify: `tests/Bolt.ETL.Tests/PerHour.Tests.fs`, `tests/Bolt.ETL.Tests/PerRide2.Tests.fs`, `tests/Bolt.Web.Tests/Views.Tests.fs`, both test fsprojs

**Interfaces:**
- Produces: `type SectionData = PriceRegression of OlsResponse | PerHourEarnings of PerHourResult | RideClusters of ClusterData`; `type AnalysisSection = { Id: string; Data: SectionData option }`; `AnalysisReport` with `Sections: AnalysisSection list`.
- Consumes: `PerRide.runOls`, `PerHour.analyze`, `RideClustering.analyze` (Tasks 1-2); `OlsMapper`, `PerHourMapper`, `ClusterMapper` (Tasks 3-5).

- [ ] **Step 1: Rewrite `Report.fs` to the pure-data seam**

Replace the whole body of `src/Bolt.ETL/Analysis/Report.fs` with (drops `ResultTable`/`ResultChart`; keeps the leaf types added in Task 1):

```fsharp
namespace Bolt.ETL.Analysis

open System
open Bolt.ETL.Analytics

type PerHourWeightedMean = { IsWeekend: bool; IsNight: bool; MeanPerHour: float; Count: int }
type PerHourRung = { Level: int; Ols: OlsResponse }
type PerHourResult =
    { WeightedMeans: PerHourWeightedMean array
      Rungs: PerHourRung array
      FullyUnlocked: bool
      HasFoldedDistricts: bool }

type ClusterData = { Points: StPoint array; Response: StDbscanResponse }

type SectionData =
    | PriceRegression of OlsResponse
    | PerHourEarnings of PerHourResult
    | RideClusters of ClusterData

/// `Id` is a stable structural key (html anchor + ordering); the web layer maps
/// it to a title/description. `Data = None` means the analysis produced nothing.
type AnalysisSection = { Id: string; Data: SectionData option }

type AnalysisReport =
    { Email: string
      GeneratedAt: DateTimeOffset
      RideCount: int
      DateRange: DateTimeOffset * DateTimeOffset
      Sections: AnalysisSection list }
```

- [ ] **Step 2: Strip presentation from `PerHour.fs`**

In `src/Bolt.ETL/Analysis/PerHour.fs`: delete the `Rung0` module, the `Display` module's `fmtPValue`/`isSignificant`/`coefficientsTable`/`modelStatsTable`, the `pl`/`fmt2`/`fmt2Opt` private helpers, the `lockedNote`/`foldedNote`/`legendNote` strings, `appendNotesToLast`, and `buildSection`. Keep `Dataset`, `Ladder`, `ColumnSpec`, `weightedMeans`, `analyze`. Move `toAnalyticsRows` out of the now-deleted `Display` module to module level (unchanged body) and update `analyze` to call `toAnalyticsRows` instead of `Display.toAnalyticsRows`. Remove the now-unused `open System.Globalization` if nothing else uses it.

- [ ] **Step 3: Strip `buildSection` from `PerRide.fs`**

In `src/Bolt.ETL/Analysis/PerRide.fs`: delete `buildSection` (the `task { … Id = "price-regression" … }` block) and remove `open System.Globalization`. Keep `runOls` and `toAnalyticsRows`.

- [ ] **Step 4: Strip presentation from `RideClustering.fs`**

In `src/Bolt.ETL/Analysis/RideClustering.fs`: delete `clusterTable` and `buildSection`; remove `open Plotly.NET` and `open Bolt.ETL.Plotting`. Keep `RideRow`, `prepareRideAnalysisSource`, `toStPoints`, `analyze`.

- [ ] **Step 5: Delete ETL ClusterMap + Plotly reference**

Delete `src/Bolt.ETL/Plotting/ClusterMap.fs`. In `src/Bolt.ETL/Bolt.ETL.fsproj` remove `<Compile Include="Plotting\ClusterMap.fs" />` and the `<PackageReference Include="Plotly.NET" ... />` (and its now-empty `ItemGroup` if applicable).

- [ ] **Step 6: Rewrite `AnalysisPipeline.run` to build `SectionData`**

Replace the body of `src/Bolt.ETL/AnalysisPipeline.fs` `run` (and drop `emptySection`'s Polish description) with:

```fsharp
namespace Bolt.ETL

module AnalysisPipeline =
    open System
    open System.Threading.Tasks
    open Bolt.ETL.Analysis
    open Bolt.Models
    open Bolt.Models.BoltApi

    let weatherEligible (weatherCap: DateTimeOffset) (rides: FinishedRide[]) : FinishedRide[] =
        rides |> Array.filter (fun r -> r.Times.CreatedTimestamp <= weatherCap)

    let run (data: ScrapedData) (weatherCap: DateTimeOffset) : Task<Result<AnalysisReport, string>> =
        task {
            try
                if Array.isEmpty data.PreviousOrders then
                    return Error $"Nie znaleziono przejazdów dla {data.Email}"
                else
                    let dates = data.PreviousOrders |> Array.map _.Created
                    let allRides = RideFactory.getFinishedRides data.PreviousOrders data.PastOrderDetails
                    let weatherRides = weatherEligible weatherCap allRides

                    let! priceData =
                        if Array.isEmpty weatherRides then Task.FromResult None
                        else task {
                            let! ols = PerRide.runOls (PerRide.prepareRideAnalysisSource weatherRides)
                            return Some(PriceRegression ols)
                        }
                    let! perHourData =
                        if Array.isEmpty weatherRides then Task.FromResult None
                        else task {
                            let! result = PerHour.analyze (PerHour.prepareHourlySource weatherRides)
                            return Some(PerHourEarnings result)
                        }
                    let! clusterResult = RideClustering.analyze (RideClustering.prepareRideAnalysisSource allRides)

                    return
                        Ok { Email = data.Email
                             GeneratedAt = DateTimeOffset.UtcNow
                             RideCount = data.PreviousOrders.Length
                             DateRange = (Array.min dates, Array.max dates)
                             Sections =
                               [ { Id = "price-regression"; Data = priceData }
                                 { Id = "per-hour-earnings"; Data = perHourData }
                                 { Id = "ride-clusters"; Data = Some(RideClusters clusterResult) } ] }
            with ex ->
                return Error $"Analiza nie powiodła się: {ex.Message}"
        }
```

- [ ] **Step 7: Verify ETL builds**

Run: `dotnet build src/Bolt.ETL`
Expected: succeeds with no `Plotly.NET` reference and no `ResultTable`/`ResultChart` symbols. If `AvailableAnalyses.fs` still references removed members, it should not — it only uses `PerRide.runOls`.

- [ ] **Step 8: Rewrite `Html.fs` to dispatch on `SectionData`**

In `src/Bolt.Web/Views/Html.fs`: keep `open Bolt.ETL.Analysis`; add `open Bolt.Web.Mappings`. Replace `chartNodes`/`sectionNode` so charts come from the Web `ResultChart` (from mappers) and section meta is owned here. Replace the private render helpers block (from `chartNodes` through `sectionNode`) with:

```fsharp
    // (title, description, empty-message) keyed by the ETL structural section id.
    let private sectionMeta =
        function
        | "price-regression" ->
            "Regresja ceny przejazdu",
            "Regresja liniowa (OLS) ceny przejazdu względem dystansu, pory dnia i pogody. W tabeli pokazane są wyłącznie cechy istotne statystycznie.",
            "Żaden przejazd nie mieści się w zakresie danych pogodowych."
        | "per-hour-earnings" ->
            "Zarobki na godzinę pracy",
            "Zarobki na godzinę pracy w zależności od warunków: pory, pogody i dzielnicy. Prostsze zestawienia pojawiają się od razu, dokładniejsze odblokowują się wraz z liczbą przepracowanych godzin.",
            "Żaden przejazd nie mieści się w zakresie danych pogodowych."
        | "ride-clusters" ->
            "Skupiska odbiorów pasażerów",
            "Przestrzenno-czasowe skupiska (ST-DBSCAN) miejsc odbioru pasażerów.",
            "Brak przejazdów do analizy."
        | other -> other, "", "Brak danych."

    let private chartNode (sectionId: string) (index: int) (c: ResultChart) =
        let chartId = $"chart-{sectionId}-{index}"
        [ h4 [] [ str c.Title ]
          div [ _id chartId; _style "width:100%;height:800px" ] []
          script [ _type "application/json"; attr "data-plotly-target" chartId ] [
              rawText (scriptSafeJson c.PlotlyFigureJson) ] ]

    let private sectionBody (s: AnalysisSection) : XmlNode list * string =
        // returns (chart+table nodes, empty-message-if-no-data)
        match s.Data with
        | Some (PriceRegression ols) ->
            [ OlsMapper.coefficientsTable ols; OlsMapper.modelStatsTable ols ]
            |> List.collect ResultTable.asHtml, ""
        | Some (PerHourEarnings result) ->
            PerHourMapper.tables result |> List.collect ResultTable.asHtml, ""
        | Some (RideClusters data) ->
            (chartNode s.Id 0 (ClusterMapper.chart data))
            @ ResultTable.asHtml (ClusterMapper.table data), ""
        | None ->
            let _, _, empty = sectionMeta s.Id
            [], empty

    let private sectionNode (s: AnalysisSection) =
        let title, description, _ = sectionMeta s.Id
        let body, emptyMsg = sectionBody s
        let intro = [ h2 [] [ str title ]; p [] [ str description ] ]
        let tail = if emptyMsg = "" then [] else [ p [] [ str emptyMsg ] ]
        section [ _id s.Id ] (intro @ body @ tail)
```

`reportFragment` is unchanged (it maps `report.Sections` through `sectionNode`). Confirm `open Giraffe.ViewEngine` provides `XmlNode`; if the explicit type annotation is awkward, drop it and let inference bind the list. Do **not** manually escape any strings — the view engine encodes them.

- [ ] **Step 9: Rewrite `Views.Tests.fs` fixtures to the new shape**

In `tests/Bolt.Web.Tests/Views.Tests.fs`, the `report fragment`, `script-bound json`, and `table notes` tests build the old `{ Title; Description; Charts; Tables }` section. Rewrite their fixtures to the new shape using real payloads. Replace the "report fragment embeds figure json and tables" test with:

```fsharp
[<Fact>]
let ``report fragment renders sections from pure data`` () =
    let ols : OlsResponse =
        { NObservations = 42; RSquared = Some 0.4; AdjRSquared = Some 0.38
          FStatistic = Some 5.0; FPvalue = Some 0.01
          Coefficients =
            [| { Name = "dystans_km"; Coef = Some 3.5; StdErr = Some 0.1; TValue = Some 1.0
                 PValue = Some 0.001; CiLow = Some 3.1; CiHigh = Some 3.9 } |] }
    let clusters : ClusterData =
        { Points = [| { Latitude = 50.0; Longitude = 19.9; Hour = 8.0 } |]
          Response =
            { Labels = [| 0 |]; NPoints = 1; NClusters = 1; NNoise = 0
              Clusters = [| { Id = 0; Size = 1; CentroidLatitude = 50.0
                              CentroidLongitude = 19.9; MeanHour = 8.0 } |] } }
    let report =
        { Email = "a@b.pl"
          GeneratedAt = DateTimeOffset.Parse "2026-07-12T10:00Z"
          RideCount = 42
          DateRange = (DateTimeOffset.Parse "2026-01-01Z", DateTimeOffset.Parse "2026-06-30Z")
          Sections =
            [ { Id = "price-regression"; Data = Some(PriceRegression ols) }
              { Id = "ride-clusters"; Data = Some(RideClusters clusters) } ] }
    let html = Html.reportFragment report
    Assert.Contains("report-content", html)
    Assert.Contains("Regresja ceny przejazdu", html)          // section title owned by Web
    Assert.Contains("data-plotly-target=\"chart-ride-clusters-0\"", html)
    Assert.Contains("dystans_km", html)
    Assert.Contains("42 przejazd&#243;w", html)
    Assert.Contains("Pobierz raport", html)
```

Rewrite the `script-bound json escapes closing tags` test to inject the `</script>` via a chart the mapper produces is impractical (the figure is generated); instead keep a focused escaping test at the `chartNode`→render boundary by asserting `scriptSafeJson` behavior, OR retarget it: assert `Html.reportFragment` of a `None`-data section contains its empty message and no script tag. Keep the `index page`, `fragments rooted in panel`, `magic link`, and `email single-encoded` tests unchanged (they don't touch sections). Update the `table notes render as legend list` test to use a `PriceRegression`/`PerHourEarnings` payload whose mapper emits notes (e.g. assert the `<ul class="notes">` appears for a price-regression section).

- [ ] **Step 10: Drop migrated presentation tests from ETL**

In `tests/Bolt.ETL.Tests/PerHour.Tests.fs`, delete the tests asserting `Rung0.table`, `Display.coefficientsTable`, and `Display.modelStatsTable` (now covered by `PerHourMapper.Tests`). Keep every `Dataset`/`Ladder`/`buildRows`/exposure/budget test and the new `weightedMeans` test. Keep the `Display.toAnalyticsRows` column-name test but retarget it to the module-level `PerHour.toAnalyticsRows`.

In `tests/Bolt.ETL.Tests/PerRide2.Tests.fs`, delete every test calling `PerRide.coefficientsTable`/`PerRide.modelStatsTable` (moved to `OlsMapper.Tests`). Keep only the data test, fixed to the current temperature label (`Mild` → `"18–25°C"`, not `"umiarkowanie"`):

```fsharp
[<Fact>]
let ``toAnalyticsRows emits Polish column names and temperature values`` () =
    let source : PerRide.RidesDataSource =
        { Rows =
            [| { PricePln = 25.5m; Distance = 3.2<km>; IsRushHour = true; IsWeekend = false
                 PickupDistrict = "centrum"; Rain = true; Snow = false; Temperature = Mild } |] }
    let row = (PerRide.toAnalyticsRows source)[0]
    Assert.Equal<Set<string>>(
        Set [ "cena_pln"; "dystans_km"; "godziny_szczytu"; "weekend"; "deszcz"; "śnieg"; "dzielnica"; "temperatura" ],
        row |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
    Assert.Equal(box 25.5, row["cena_pln"])
    Assert.Equal(box true, row["deszcz"])
    Assert.Equal(box "18–25°C", row["temperatura"])
```

- [ ] **Step 11: Build and run the full test suite**

Run: `dotnet build` (solution) then `dotnet test`
Expected: build succeeds; all tests green. Fix compile errors surfaced by the seam change (most likely `Html.fs` type annotations or leftover references to deleted members).

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "refactor: move all presentation from ETL to Web; ETL emits pure data"
```

---

### Task 7: Verification — grep, build, run

Confirm the layer boundary holds and the app renders identically.

- [ ] **Step 1: Grep the ETL layer for leaked presentation**

Run: `rg -n "ResultTable|ResultChart|Plotly|PlotlyFigureJson|zł|poziom|CultureInfo" src/Bolt.ETL`
Expected: matches only surviving **data-key** uses — the Polish column names in `PerRide.toAnalyticsRows`/`PerHour.toAnalyticsRows`, the `TemperatureBucket.label` boundary strings in `Meteo/Model.fs`. No table/chart types, no `Plotly`, no `CultureInfo` number formatting, no `poziom`/`zł/h` display strings. If anything else matches, it is a missed migration — move it to a Web mapper.

- [ ] **Step 2: Full build + test**

Run: `dotnet build && dotnet test`
Expected: green.

- [ ] **Step 3: Run the app end-to-end**

Use the `/run` skill (or `dotnet run --project src/Bolt.Web`), drive a full analysis for a known email, and confirm all three sections render:
- **price-regression**: coefficient table now **populated** (previously blank) with Polish headers + model-stats table.
- **per-hour-earnings**: weighted-means table + one coefficient table per unlocked rung + model-stats + the locked/folded/legend notes as applicable.
- **ride-clusters**: Plotly map renders + cluster table.

Visually diff against a pre-migration run (same input) — output should be identical.

- [ ] **Step 4: Commit any fixups**

```bash
git add -A && git commit -m "chore: verification fixups for layer separation"
```

---

## Self-Review

**Spec coverage:** Report.fs pure-data seam (Task 6 §1) ✓; PerRide strip (Task 6 §3) ✓; PerHour data+strip (Tasks 1, 6 §2) ✓; RideClustering data+strip (Tasks 2, 6 §4) ✓; Plotly move to Web (Task 5, 6 §5) ✓; pipeline SectionData (Task 6 §6) ✓; Web mappers OLS/PerHour/Cluster (Tasks 3-5) ✓; Html dispatch + Web-owned meta (Task 6 §8) ✓; fsproj package/compile edits (Tasks 3,5,6) ✓; test migration ETL→Web + stale-test rewrite (Tasks 3-6) ✓; coefficient-names-stay-Polish boundary (Global Constraints) ✓; verification grep + app run (Task 7) ✓.

**Type consistency:** `PerHourResult`/`PerHourRung`/`PerHourWeightedMean`/`ClusterData`/`SectionData`/`AnalysisSection` defined once (Task 1 + Task 6 §1) and consumed with the same field names in `PerHourMapper.tables`, `ClusterMapper.table/chart`, `AnalysisPipeline.run`, and `Html.sectionBody`. `PerHourMapper.tables : PerHourResult -> ResultTable list`, `ClusterMapper.table/chart : ClusterData -> ResultTable/ResultChart`, `OlsMapper.coefficientsTable/modelStatsTable : OlsResponse -> ResultTable` — signatures match every call site.

**Placeholder scan:** no TBD/TODO; every code step shows full code or an exact, bounded edit (verbatim move with a named single-line change; explicit delete lists). Note: Task 6 §9's `</script>`-escaping test is described rather than pinned to a single mapper output because the Plotly figure JSON is generated — the instruction gives two concrete acceptable retargets, not a vague "handle it".
