# Report Presentation Cleanup + Polish UI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Declutter the web report to two sections (price regression with significant-only, sorted, explained coefficients; ride clustering) and translate the whole web UI to Polish.

**Architecture:** Display shaping stays in the F# section builders (`Bolt.ETL/Analysis`) and `Views.fs`. Feature names become Polish at the data source: `PerRide2.toAnalyticsRows` emits Polish column keys, so the python OLS service returns Polish coefficient names — no display-side name mapper. The python service itself is untouched.

**Tech Stack:** F# (.NET), Giraffe.ViewEngine, xUnit, python analytics service (unchanged).

**Spec:** `docs/superpowers/specs/2026-07-18-report-presentation-polish-design.md`

## Global Constraints

- Python analytics service code is untouched; only the request payload column names/values change.
- All user-visible web UI text in Polish; `lang="pl"`.
- Displayed numbers use `pl-PL` culture (decimal comma). Coefficients/CI/R²: 2 decimals (`F2`). p-values: `< 0,001` when below 0.001, else 3 decimals (`F3`). Missing values: `–` (en dash placeholder, as today).
- Significance threshold: p < 0.05; `None` p-value counts as insignificant. Intercept `const` never displayed.
- Code, commits, and code comments stay in English (project convention).
- Test commands run from repo root `/home/radoslaw/coding/bolt-sharp`.
- ICU caveat: if a `pl-PL` formatting assertion fails only on the minus sign character (hyphen-minus vs U+2212), adjust the expected string in the test to what `ToString("F2", pl-PL)` actually produced — do not change the production formatting code.

---

### Task 1: `Notes` on `ResultTable` + legend rendering in Views

**Files:**
- Modify: `src/Bolt.ETL/Analysis/Report.fs`
- Modify: `src/Bolt.ETL/Analysis/PerRide1.fs` (compile fix: `Notes = []`)
- Modify: `src/Bolt.ETL/Analysis/PerRide2.fs` (compile fix: `Notes = []` on all 4 existing tables)
- Modify: `src/Bolt.ETL/Analysis/RideClustering.fs` (compile fix: `Notes = []`)
- Modify: `src/Bolt.Web/Views.fs`
- Test: `tests/Bolt.Web.Tests/Views.Tests.fs`

**Interfaces:**
- Produces: `ResultTable` record gains field `Notes: string list` (legend lines rendered as `<ul class="notes">` under the table; empty list renders nothing). Later tasks fill `Notes` on regression tables.

- [ ] **Step 1: Write the failing test**

In `tests/Bolt.Web.Tests/Views.Tests.fs`, append:

```fsharp
[<Fact>]
let ``table notes render as legend list`` () =
    let report = {
        Email = "a@b.pl"
        GeneratedAt = DateTimeOffset.UtcNow
        RideCount = 1
        DateRange = (DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        Sections =
            [ { Id = "s1"; Title = "t"; Description = ""
                Charts = []
                Tables = [ { Title = "T"; Headers = [ "h" ]; Rows = []
                             Notes = [ "objaśnienie <a>" ] } ] } ]
    }
    let html = Views.reportFragment report
    Assert.Contains("<ul class=\"notes\">", html)
    Assert.Contains("objaśnienie &lt;a&gt;", html)
```

Also update the existing table literal in ``report fragment embeds figure json and tables`` (line ~33) to include the new field:

```fsharp
                Tables = [ { Title = "T"; Headers = [ "h" ]; Rows = [ [ "<cell>" ] ]; Notes = [] } ] } ]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~table notes"`
Expected: build FAILURE — `ResultTable` has no field `Notes` (compile error is the failing state here).

- [ ] **Step 3: Implement**

`src/Bolt.ETL/Analysis/Report.fs` — extend the record:

```fsharp
type ResultTable = {
    Title: string
    Headers: string list
    Rows: string list list
    /// Legend / interpretation lines rendered under the table.
    Notes: string list
}
```

Add `Notes = []` to every existing `ResultTable` construction:
- `PerRide1.fs` `breakdownTable` (one construction, line ~77)
- `PerRide2.fs`: `coefficientsTable`, `modelStatsTable`, `vifTable`, `groupMeansTable` (rewritten in Task 2 anyway)
- `RideClustering.fs` `clusterTable`

`src/Bolt.Web/Views.fs` — render notes in `tableNodes` and add CSS:

```fsharp
let private tableNodes (t: ResultTable) = [
    h4 [] [ str t.Title ]
    table [] [
        thead [] [ tr [] [ for h in t.Headers -> th [] [ str h ] ] ]
        tbody [] [ for r in t.Rows -> tr [] [ for c in r -> td [] [ str c ] ] ]
    ]
    if not (List.isEmpty t.Notes) then
        ul [ _class "notes" ] [ for n in t.Notes -> li [] [ str n ] ]
]
```

CSS block gains one rule (inside the existing `css` string):

```
.notes { font-size: 0.85rem; color: #555; max-width: 80ch; }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.Web.Tests && dotnet test tests/Bolt.ETL.Tests`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat: ResultTable.Notes legend rendered under tables"
```

---

### Task 2: Regression tables — significant-only, sorted, explained; drop diagnostics

**Files:**
- Modify: `src/Bolt.ETL/Analysis/PerRide2.fs`
- Test: `tests/Bolt.ETL.Tests/PerRide2.Tests.fs` (full rewrite)

**Interfaces:**
- Consumes: `ResultTable.Notes` from Task 1.
- Produces: `PerRide2.coefficientsTable : OlsResponse -> ResultTable` (4 Polish columns, significant rows only, |coef| desc, legend notes). `PerRide2.modelStatsTable : OlsResponse -> ResultTable` (liczba przejazdów / R² / skorygowane R² + notes). `PerRide2.buildSection` returns section titled `Regresja ceny przejazdu` with exactly these two tables. `vifTable`, `groupMeansTable`, and the `mirrorCheck` call are DELETED. (Data keys still English in this task; Task 3 changes them.)

- [ ] **Step 1: Rewrite the test file (failing)**

Replace the entire content of `tests/Bolt.ETL.Tests/PerRide2.Tests.fs` with:

```fsharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Bolt.ETL.Tests --filter "FullyQualifiedName~PerRide2"`
Expected: FAIL — old `coefficientsTable` returns 7 columns/all rows; `vifTable`/`groupMeansTable`/`cannedMirror` references gone so the old tests no longer exist (file replaced), new assertions fail against old implementation.

- [ ] **Step 3: Implement**

In `src/Bolt.ETL/Analysis/PerRide2.fs`, replace everything from `let private fmtOpt` (line ~115) to the end of the file with:

```fsharp
    let private pl = CultureInfo.GetCultureInfo "pl-PL"

    let private fmt2 (v: float) = v.ToString("F2", pl)

    let private fmt2Opt (v: float option) =
        v |> Option.map fmt2 |> Option.defaultValue "–"

    let private fmtPValue (p: float option) =
        match p with
        | Some p when p < 0.001 -> "< 0,001"
        | Some p -> p.ToString("F3", pl)
        | None -> "–"

    let private isSignificant (c: Coefficient) =
        match c.PValue with
        | Some p -> p < 0.05
        | None -> false

    let coefficientsTable (response: OlsResponse) : ResultTable =
        let features =
            response.Coefficients |> Array.filter (fun c -> c.Name <> "const")

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
                "Wszystkie cechy modelu są istotne statystycznie (p < 0,05)."
            else
                let names = insignificant |> Array.map _.Name |> String.concat ", "
                $"Cechy statystycznie nieistotne (p ≥ 0,05), pominięte w tabeli: {names}."

        { Title = "Wpływ cech na cenę przejazdu"
          Headers = [ "cecha"; "współczynnik [zł]"; "przedział ufności 95%"; "istotność (p)" ]
          Rows = rows
          Notes =
            [ "cecha — zmienna wpływająca na cenę; nazwy w formie dzielnica_… lub temperatura_… oznaczają różnicę względem pominiętej kategorii bazowej."
              "współczynnik [zł] — o ile złotych zmienia się cena przejazdu, gdy dana cecha występuje (dla dystansu: przy wzroście o 1 odchylenie standardowe); im większa wartość bezwzględna, tym silniejszy wpływ; wiersze są posortowane od najsilniejszego wpływu."
              "przedział ufności 95% — zakres, w którym z 95-procentową pewnością mieści się prawdziwa wartość współczynnika."
              "istotność (p) — prawdopodobieństwo, że tak silny efekt pojawiłby się przypadkiem; wartości poniżej 0,05 uznaje się za istotne statystycznie."
              insignificantNote ] }

    let modelStatsTable (response: OlsResponse) : ResultTable =
        let fitNote =
            match response.RSquared with
            | Some r2 ->
                let pct = int (Math.Round(r2 * 100.0))
                [ $"Model wyjaśnia {pct}%% zmienności ceny przejazdu." ]
            | None -> []

        { Title = "Dopasowanie modelu"
          Headers = [ "statystyka"; "wartość" ]
          Rows =
            [ [ "liczba przejazdów"; string response.NObservations ]
              [ "R²"; fmt2Opt response.RSquared ]
              [ "skorygowane R²"; fmt2Opt response.AdjRSquared ] ]
          Notes =
            fitNote
            @ [ "R² — jaka część zmienności ceny jest wyjaśniona przez model (od 0 do 1, wyżej = lepiej); skorygowane R² dodatkowo uwzględnia liczbę cech w modelu." ] }

    let buildSection (source: RidesDataSource) : Task<AnalysisSection> =
        task {
            let! ols =
                AnalyticsClient.olsRegression {
                    Rows = toAnalyticsRows source
                    Target = "price_pln"
                    DropColumns = [||]
                    CategoricalColumns = None
                    Standardize = true
                }

            return
                { Id = "price-regression"
                  Title = "Regresja ceny przejazdu"
                  Description =
                    "Regresja liniowa (OLS) ceny przejazdu względem dystansu, pory dnia i pogody. "
                    + "W tabeli pokazane są wyłącznie cechy istotne statystycznie."
                  Charts = []
                  Tables = [ coefficientsTable ols; modelStatsTable ols ] }
        }
```

Notes: `vifTable`, `groupMeansTable`, the old `fmtOpt`, and the `AnalyticsClient.mirrorCheck` call are deleted. `Math.Round` needs `open System` — already open in this file. In F# interpolated strings a literal percent sign is written `%%`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.ETL.Tests --filter "FullyQualifiedName~PerRide2"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.ETL/Analysis/PerRide2.fs tests/Bolt.ETL.Tests/PerRide2.Tests.fs
git commit -m "feat: regression tables show only significant coefficients, sorted, with Polish legend"
```

---

### Task 3: Polish column names at the data source

**Files:**
- Modify: `src/Bolt.ETL/Analysis/PerRide2.fs` (`toAnalyticsRows`, `buildSection` target, new `temperatureName`)
- Test: `tests/Bolt.ETL.Tests/PerRide2.Tests.fs` (append)

**Interfaces:**
- Produces: `PerRide2.toAnalyticsRows` emits keys `cena_pln`, `dystans_km`, `godziny_szczytu`, `weekend`, `deszcz`, `śnieg`, `dzielnica`, `temperatura`; temperature values are `mróz`/`zimno`/`umiarkowanie`/`gorąco`. OLS request `Target = "cena_pln"`. The service one-hot encodes with pandas `column_value` naming, so coefficients return as e.g. `dzielnica_centrum`, `temperatura_mróz`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Bolt.ETL.Tests/PerRide2.Tests.fs` (add `open Bolt.Models` and `open Bolt.ETL.Meteo.Model` to the top of the file):

```fsharp
[<Fact>]
let ``toAnalyticsRows emits Polish column names and temperature values`` () =
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
```

(`box 25.5` compares against `box (float r.PricePln)`; both are `float` 25.5.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bolt.ETL.Tests --filter "FullyQualifiedName~toAnalyticsRows"`
Expected: FAIL — keys are still `price_pln` etc.

- [ ] **Step 3: Implement**

In `src/Bolt.ETL/Analysis/PerRide2.fs` replace `toAnalyticsRows` (and its comment) with:

```fsharp
    let private temperatureName (t: TemperatureBucket) =
        match t with
        | Frost -> "mróz"
        | Cold -> "zimno"
        | Mild -> "umiarkowanie"
        | Hot -> "gorąco"

    // JSON rows for the analytics service. Column names are Polish on purpose:
    // pd.get_dummies builds coefficient names as "column_value", so Polish keys
    // (and Polish temperature values) make the OLS response display-ready with
    // no name mapping on the way back. Units of measure / decimal unwrapped
    // before boxing so values serialize as plain JSON numbers; bools stay bools.
    let toAnalyticsRows (data: RidesDataSource) : Map<string, obj> array =
        data.Rows
        |> Array.map (fun r ->
            Map.ofList [
                "cena_pln", box (float r.PricePln)
                "dystans_km", box (float r.Distance)
                "godziny_szczytu", box r.IsRushHour
                "weekend", box r.IsWeekend
                "dzielnica", box r.PickupDistrict
                "deszcz", box r.Rain
                "śnieg", box r.Snow
                "temperatura", box (temperatureName r.Temperature)
            ])
```

In `buildSection`, change the OLS request line:

```fsharp
                    Target = "cena_pln"
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.ETL.Tests --filter "FullyQualifiedName~PerRide2"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.ETL/Analysis/PerRide2.fs tests/Bolt.ETL.Tests/PerRide2.Tests.fs
git commit -m "feat: Polish column names in analytics rows so OLS returns Polish coefficient names"
```

---

### Task 4: Clustering section strings in Polish

**Files:**
- Modify: `src/Bolt.ETL/Analysis/RideClustering.fs`
- Test: `tests/Bolt.ETL.Tests/RideClustering.Tests.fs`

**Interfaces:**
- Consumes: `ResultTable.Notes` (empty here). Logic (sorting, invariant coordinate formatting, ST-DBSCAN parameters) is UNCHANGED — strings only.

- [ ] **Step 1: Update the test (failing)**

In `tests/Bolt.ETL.Tests/RideClustering.Tests.fs`, change the title assertion and add a headers assertion:

```fsharp
[<Fact>]
let ``clusterTable sorts by size descending and formats invariantly`` () =
    let table = RideClustering.clusterTable canned
    Assert.Equal<string list>([ "1"; "5"; "50.07000"; "19.95000"; "22.25" ], table.Rows[0])
    Assert.Equal<string list>([ "0"; "2"; "50.06123"; "19.92345"; "8.50" ], table.Rows[1])
    Assert.Contains("poza skupiskami: 1", table.Title)
    Assert.Equal<string list>(
        [ "skupisko"; "liczba przejazdów"; "szer. geogr."; "dł. geogr."; "średnia godzina" ],
        table.Headers)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bolt.ETL.Tests --filter "FullyQualifiedName~clusterTable"`
Expected: FAIL — title still `Clusters (points: …, noise: …)`.

- [ ] **Step 3: Implement**

In `src/Bolt.ETL/Analysis/RideClustering.fs`:

`clusterTable` header block becomes:

```fsharp
        { Title = $"Skupiska (punkty: {response.NPoints}, poza skupiskami: {response.NNoise})"
          Headers = [ "skupisko"; "liczba przejazdów"; "szer. geogr."; "dł. geogr."; "średnia godzina" ]
```

(rows and `Notes = []` unchanged.)

`buildSection` return becomes:

```fsharp
            return
                { Id = "ride-clusters"
                  Title = "Skupiska odbiorów pasażerów"
                  Description = "Przestrzenno-czasowe skupiska (ST-DBSCAN) miejsc odbioru pasażerów."
                  Charts = [ { Title = "Mapa skupisk odbiorów"; PlotlyFigureJson = GenericChart.toFigureJson chart } ]
                  Tables = [ clusterTable response ] }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.ETL.Tests`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.ETL/Analysis/RideClustering.fs tests/Bolt.ETL.Tests/RideClustering.Tests.fs
git commit -m "feat: clustering section copy in Polish"
```

---

### Task 5: Pipeline — drop PerRide1 section, Polish messages

**Files:**
- Modify: `src/Bolt.ETL/AnalysisPipeline.fs`

**Interfaces:**
- Produces: `AnalysisPipeline.run` returns `Sections = [ perRide2; clustering ]` (two sections). `PerRide1.fs` module and its tests stay in the project, just not called by the pipeline. Error strings returned to the UI are Polish.

- [ ] **Step 1: Implement**

(No unit test covers section composition — `run` needs the live analytics service; `weatherEligible` keeps its existing test. Verification is compile + full suite + the e2e check in Task 7.)

In `src/Bolt.ETL/AnalysisPipeline.fs`:

- `emptySection` suffix becomes Polish:

```fsharp
    let private emptySection id title description =
        { Id = id
          Title = title
          Description = description + " Żaden przejazd nie mieści się w zakresie danych pogodowych."
          Charts = []
          Tables = [] }
```

- Inside `run`: delete the whole `let! perRide1 = …` binding; update the remaining fallback, error strings, and section list:

```fsharp
                if Array.isEmpty data.PreviousOrders then
                    return Error $"Nie znaleziono przejazdów dla {data.Email}"
                else
                    let dates = data.PreviousOrders |> Array.map _.Created
                    let allRides = RideFactory.getFinishedRides data.PreviousOrders data.PastOrderDetails
                    let weatherRides = weatherEligible weatherCap allRides

                    let! perRide2 =
                        if Array.isEmpty weatherRides then
                            Task.FromResult(emptySection "price-regression" "Regresja ceny przejazdu"
                                "Regresja liniowa (OLS) ceny przejazdu względem dystansu, pory dnia i pogody.")
                        else
                            PerRide2.buildSection (PerRide2.prepareRideAnalysisSource weatherRides)

                    let! clustering =
                        RideClustering.buildSection (RideClustering.prepareRideAnalysisSource allRides)

                    return
                        Ok { Email = data.Email
                             GeneratedAt = DateTimeOffset.UtcNow
                             RideCount = data.PreviousOrders.Length
                             DateRange = (Array.min dates, Array.max dates)
                             Sections = [ perRide2; clustering ] }
            with ex ->
                return Error $"Analiza nie powiodła się: {ex.Message}"
```

- Update the doc comment on `run` (it names PerRide1):

```fsharp
    /// Runs the analyses over in-memory scraped data. The weather-dependent
    /// regression (PerRide2) sees only rides covered by the weather archive;
    /// clustering and the report totals use the full set.
```

- [ ] **Step 2: Build and run all ETL tests**

Run: `dotnet build Bolt.slnx && dotnet test tests/Bolt.ETL.Tests`
Expected: build clean, tests PASS (PerRide1 tests still pass — module untouched).

- [ ] **Step 3: Commit**

```bash
git add src/Bolt.ETL/AnalysisPipeline.fs
git commit -m "feat: report has two sections; pipeline messages in Polish"
```

---

### Task 6: Web UI in Polish (Views, WebSockets, JobRunner, progress descriptions)

**Files:**
- Modify: `src/Bolt.Web/Views.fs`
- Modify: `src/Bolt.Web/WebSockets.fs:38-45,90-91,149-150`
- Modify: `src/Bolt.Web/Jobs/JobRunner.fs:41,56,62,67,73,77`
- Modify: `src/Bolt.Web/Pipeline.fs:13-18`
- Test: `tests/Bolt.Web.Tests/Views.Tests.fs`

**Interfaces:**
- Consumes: fragment functions from `Views.fs` (signatures unchanged: `progressFragment`, `magicLinkFragment`, `errorFragment`, `reportFragment`, `indexPage`).
- Produces: every user-visible string Polish; `lang="pl"`. `Failed(step, …)` step names and `describeProgress` outputs are Polish because they are interpolated into visible fragments.

- [ ] **Step 1: Update tests (failing)**

In `tests/Bolt.Web.Tests/Views.Tests.fs`:

Extend ``index page wires htmx websocket and panel`` with:

```fsharp
    Assert.Contains("lang=\"pl\"", html)
    Assert.Contains("Analiza przejazdów Bolt", html)
```

Extend ``magic link fragment carries email and shows error`` with:

```fsharp
    Assert.Contains("Zaloguj się", html)
```

Extend ``report fragment embeds figure json and tables`` with:

```fsharp
    Assert.Contains("Pobierz raport", html)
    Assert.Contains("42 przejazdów", html)
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Bolt.Web.Tests`
Expected: FAIL — 3 tests (English strings still rendered).

- [ ] **Step 3: Implement**

`src/Bolt.Web/Views.fs` string changes (structure, attributes, ids, and the notes rendering from Task 1 unchanged):

```fsharp
let indexPage () =
    html [ _lang "pl" ] [
        head [] [
            meta [ _charset "utf-8" ]
            title [] [ str "Analiza przejazdów Bolt" ]
            meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
            script [ _src "https://unpkg.com/htmx.org@1.9.12" ] []
            script [ _src "https://unpkg.com/htmx.org@1.9.12/dist/ext/ws.js" ] []
            script [ _src "https://cdn.plot.ly/plotly-2.32.0.min.js" ] []
            script [ _src "/app.js" ] []
            style [] [ rawText css ]
        ]
        body [ attr "hx-ext" "ws"; attr "ws-connect" "/ws" ] [
            h1 [] [ str "Analiza przejazdów Bolt" ]
            panel [
                form [ flag "ws-send" ] [
                    input [ _type "hidden"; _name "msgType"; _value "start-analysis" ]
                    label [] [
                        str "Adres e-mail kierowcy Bolt: "
                        input [ _type "email"; _name "email"; _required; _placeholder "kierowca@przyklad.pl" ]
                    ]
                    button [ _type "submit" ] [ str "Przygotuj analizę" ]
                ]
            ]
        ]
    ]
    |> RenderView.AsString.htmlDocument
```

`magicLinkFragment` body text:

```fsharp
        yield p [] [ str "Sprawdź swoją skrzynkę — Bolt wysłał wiadomość z linkiem do logowania. Wklej ten link poniżej." ]
        yield! errorNode
        yield form [ flag "ws-send" ] [
            input [ _type "hidden"; _name "msgType"; _value "magic-link" ]
            input [ _type "hidden"; _name "email"; _value email ]
            label [] [ str "Link z wiadomości: "; input [ _type "text"; _name "url"; _required ] ]
            button [ _type "submit" ] [ str "Zaloguj się" ]
        ]
```

`errorFragment`:

```fsharp
        p [ _class "error" ] [ str $"Analiza nie powiodła się na etapie: {step}. {message}" ]
        …
            button [ _type "submit" ] [ str "Spróbuj ponownie" ]
```

`reportFragment` header and button:

```fsharp
    let header = [
        h1 [] [ str $"Analiza przejazdów Bolt — {report.Email}" ]
        p [] [
            str (
                $"""{report.RideCount} przejazdów między {fromDate.ToString "yyyy-MM-dd"} a {toDate.ToString "yyyy-MM-dd"}, """
                + $"""raport wygenerowano {report.GeneratedAt.ToString "yyyy-MM-dd HH:mm"} UTC."""
            )
        ]
    ]
    …
        button [ _onclick "downloadReport()" ] [ str "Pobierz raport" ]
```

`src/Bolt.Web/WebSockets.fs` — `stateFragment` and inline errors:

```fsharp
let private stateFragment (email: string) (state: JobState) =
    match state with
    | CheckingCache -> Views.progressFragment "Sprawdzanie zapisanych danych…" ""
    | Authenticating -> Views.progressFragment "Logowanie…" ""
    | AwaitingMagicLink error -> Views.magicLinkFragment email error
    | ScrapingRides detail -> Views.progressFragment "Pobieranie przejazdów…" detail
    | FetchingMeteo -> Views.progressFragment "Pobieranie danych pogodowych…" ""
    | RunningAnalysis -> Views.progressFragment "Trwa analiza…" ""
    | Done report -> Views.reportFragment report
    | Failed(step, message) -> Views.errorFragment email step message
```

Line ~90: `Views.errorFragment email "uruchamianie" "Analiza dla tego adresu e-mail już trwa. Spróbuj ponownie później."`
Line ~149: `Views.errorFragment email "logowanie" "Najpierw rozpocznij analizę, podając adres e-mail."`

`src/Bolt.Web/Jobs/JobRunner.fs` — step names and message (these land in `errorFragment`'s Polish sentence):

- line 41: `failure <- Some("wysyłanie linku do logowania", e)`
- line 56: `failure <- Some("pobieranie przejazdów", e)`
- line 62: `do! notify (Failed("pobieranie przejazdów", "Nie znaleziono przejazdów dla tego konta"))`
- line 67: `do! notify (Failed("pobieranie danych pogodowych", e))`
- line 73: `do! notify (Failed("analiza", e))`
- line 77: `do! notify (Failed("błąd wewnętrzny", ex.Message))`

`src/Bolt.Web/Pipeline.fs` — `describeProgress` (interpolated into "Pobieranie przejazdów…" detail):

```fsharp
let describeProgress (p: ScrapeProgress) =
    match p with
    | ScrapingProfile -> "profil kierowcy"
    | ScrapingActivityHours -> "godziny aktywności"
    | ScrapingOrderHistory -> "historia zleceń"
    | ScrapingOrderDetails(current, total) -> $"szczegóły zleceń {current}/{total}"
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.Web.Tests`
Expected: PASS (all 8).

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.Web tests/Bolt.Web.Tests
git commit -m "feat: web UI in Polish"
```

---

### Task 7: Full verification

**Files:** none (verification only)

- [ ] **Step 1: Full build and test suite**

Run: `dotnet build Bolt.slnx && dotnet test Bolt.slnx`
Expected: build clean, all test projects PASS.

- [ ] **Step 2: End-to-end eyeball**

Requires the python analytics service and a real Bolt account (magic-link login), so this is user-assisted:

1. Start analytics service: `cd python-analytics && uv run uvicorn analytics.api.main:app --port 8000` (check `src/Bolt.Web/appsettings.json` for the expected URL/port before assuming).
2. Start web app: `dotnet run --project src/Bolt.Web`.
3. Open the page, run an analysis (DEBUG builds can reuse cached scrape data within the 14-day freshness window, skipping the magic link).
4. Confirm: whole flow in Polish; report has exactly 2 sections; coefficient table shows only significant features sorted by |coefficient| with the legend and the insignificant-features sentence underneath; coefficient names like `dzielnica_centrum` / `temperatura_mróz`; decimal commas; clustering section unchanged apart from Polish copy.

- [ ] **Step 3: Use superpowers:verification-before-completion before claiming done**
