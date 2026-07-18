# FIXME Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove all five FIXMEs: scraped user data lives in memory (disk only in DEBUG), the weather cap threads into analysis, only an e-mail may start a job, and both the scrape progress callback and the analysis pipeline are async with no synchronous blocking.

**Architecture:** A new `ScrapedData` record (in `Bolt.Models`) carries scraped data from `scrapeRides` through `PipelineDeps<'session, 'data>` into `AnalysisPipeline.run`. `ensureMeteoCoverage` takes a date range and returns the weather cap. `AnalyticsClient` and every analysis section become Task-based. Tasks are ordered so `dotnet build Bolt.slnx` stays green after every commit — F# project references force ETL/Scraper signature changes to land together with their `Bolt.Web` call sites.

**Tech Stack:** F# / .NET 10 (`net10.0`), xUnit, ASP.NET Core minimal APIs + WebSockets, System.Text.Json + FSharp.SystemTextJson.

**Spec:** `docs/superpowers/specs/2026-07-13-fixme-cleanup-design.md`

## Global Constraints

- Build with `dotnet build Bolt.slnx`, test with `dotnet test Bolt.slnx` — run both from the repo root. Every task's final commit must leave both green.
- Task 6 additionally requires `dotnet build Bolt.slnx -c Release` to pass (the `#if DEBUG` blocks must compile in both configurations).
- F# compiles files in `<Compile Include=...>` order — every new file must be added to its `.fsproj` at the position stated in its task.
- No new NuGet packages.
- Use the existing custom computation expressions: `taskResult` from `Bolt.Infrastrucutre.ces.TaskResultBuilder`, `maybe` from `Bolt.Infrastrucutre.ces.OptionBuilder` (note: the namespace really is misspelled `Infrastrucutre`).
- The working tree already contains the five FIXME comments in `ScrapePipeline.fs`, `JobRunner.fs`, and `AnalysisPipeline.fs`; they are deleted by the rewrites below. No FIXME text may survive Task 6.
- End every commit message with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

---

### Task 1: Infrastructure — `Json.deserializeElement` and raw repository readers

The scraper gets raw `JsonElement`s from Bolt; today typing happens by writing raw JSON to disk and reading it back through `Json.deserialize`. To keep data in memory we need direct `JsonElement -> 'a` deserialization with the same converter settings, plus raw readers for the three repositories that only ever stored unstructured data (used by the DEBUG cache-load path in Task 5).

**Files:**
- Modify: `src/Bolt.Infrastructure/Serialization.fs`
- Modify: `src/Bolt.Infrastructure/Repository/DriverProfileRepository.fs`
- Modify: `src/Bolt.Infrastructure/Repository/ActivityHoursRepository.fs`
- Modify: `src/Bolt.Infrastructure/Repository/OrderHistoryRepository.fs`
- Create: `tests/Bolt.Infrastructure.Tests/Serialization.Tests.fs`
- Modify: `tests/Bolt.Infrastructure.Tests/Bolt.Infrastructure.Tests.fsproj`

**Interfaces:**
- Consumes: existing `Json.deserialize`, `JsonStorage.readProfile`, `saveUnstructuredDangerous`.
- Produces:
  - `Json.deserializeElement<'a> : JsonElement -> 'a`
  - `DriverProfileRepository.getUnstructured : string -> JsonElement option`
  - `ActivityHoursRepository.getUnstructured : string -> JsonElement option`
  - `OrderHistoryRepository.getUnstructured : string -> JsonNode[] option`

- [ ] **Step 1: Write the failing tests**

Create `tests/Bolt.Infrastructure.Tests/Serialization.Tests.fs`:

```fsharp
module Bolt.Infrastructure.Tests.SerializationTests

open System
open System.IO
open System.Text.Json
open Xunit
open Bolt.Infrastructure.Repository
open Bolt.Infrastrucutre.Serialization

[<Fact>]
let ``deserializeElement uses the shared converter settings`` () =
    // BoltDateTimeOffsetConverter parses "yyyy.MM.dd HH:mm" as UTC — only the
    // shared settings know that format.
    use doc = JsonDocument.Parse """{"name": "x", "created": "2026.07.01 12:30"}"""
    let value = Json.deserializeElement<{| name: string; created: DateTimeOffset |}> doc.RootElement
    Assert.Equal("x", value.name)
    Assert.Equal(DateTimeOffset(2026, 7, 1, 12, 30, 0, TimeSpan.Zero), value.created)

let private withTempRoot (f: unit -> unit) =
    let temp = Path.Combine(Path.GetTempPath(), "bolt-tests-" + Guid.NewGuid().ToString "N")
    Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", temp)
    try f ()
    finally
        Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", null)
        if Directory.Exists temp then Directory.Delete(temp, true)

[<Fact>]
let ``getUnstructured round-trips raw driver profile JSON`` () =
    withTempRoot (fun () ->
        use doc = JsonDocument.Parse """{"driver": {"id": 7}}"""
        DriverProfileRepository.saveUnstructuredDangerous "a@b.pl" doc.RootElement
        let loaded = (DriverProfileRepository.getUnstructured "a@b.pl").Value
        Assert.Equal(doc.RootElement.GetRawText(), loaded.GetRawText()))

[<Fact>]
let ``getUnstructured is None when nothing was saved`` () =
    withTempRoot (fun () ->
        Assert.True((DriverProfileRepository.getUnstructured "a@b.pl").IsNone))
```

Add to `tests/Bolt.Infrastructure.Tests/Bolt.Infrastructure.Tests.fsproj`, **before** `Storage.Tests.fs`:

```xml
<Compile Include="Serialization.Tests.fs" />
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Bolt.slnx --filter "FullyQualifiedName~SerializationTests"`
Expected: build FAILS — `deserializeElement` and `getUnstructured` are not defined.

- [ ] **Step 3: Implement**

In `src/Bolt.Infrastructure/Serialization.fs`, inside `module Json` after `deserializeNode`:

```fsharp
    let deserializeElement<'a> (x: JsonElement) =
        JsonSerializer.Deserialize<'a>(x, serializerSettings)
```

In `src/Bolt.Infrastructure/Repository/DriverProfileRepository.fs`, add after `saveUnstructuredDangerous` (and note: the existing `save`/`get` pair in this file is mistyped dead code — leave it alone, it is not this task's business):

```fsharp
    let getUnstructured email : System.Text.Json.JsonElement option =
        JsonStorage.readProfile email fileName
```

In `src/Bolt.Infrastructure/Repository/ActivityHoursRepository.fs`, add after `saveUnstructuredDangerous`:

```fsharp
    let getUnstructured email : System.Text.Json.JsonElement option =
        JsonStorage.readProfile email fileName
```

In `src/Bolt.Infrastructure/Repository/OrderHistoryRepository.fs`, add after `saveUnstructuredDangerous`:

```fsharp
    let getUnstructured email : System.Text.Json.Nodes.JsonNode[] option =
        JsonStorage.readProfile email fileName
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Bolt.slnx --filter "FullyQualifiedName~SerializationTests"`
Expected: 3 tests PASS.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test Bolt.slnx`
Expected: all tests PASS.

```bash
git add src/Bolt.Infrastructure tests/Bolt.Infrastructure.Tests
git commit -m "feat: JsonElement deserialization and raw repository readers"
```

---

### Task 2: Async analytics chain (FIXME 5)

Make `AnalyticsClient` Task-based and propagate: `buildSection` ×3, `AnalysisPipeline.run` (still email-keyed in this task — the `ScrapedData` signature lands in Task 5), `Pipeline.realDeps` (drop `Task.Run`), and the `/health` endpoint. Pure refactor of execution semantics; no behavior change, so no new tests — the existing suite plus a clean build is the gate.

**Files:**
- Modify: `src/Bolt.ETL/Analytics/AnalyticsClient.fs`
- Modify: `src/Bolt.ETL/Analysis/PerRide1.fs:97-106` (buildSection)
- Modify: `src/Bolt.ETL/Analysis/PerRide2.fs:170-195` (buildSection)
- Modify: `src/Bolt.ETL/Analysis/RideClustering.fs:72-93` (buildSection)
- Modify: `src/Bolt.ETL/AnalysisPipeline.fs`
- Modify: `src/Bolt.Web/Pipeline.fs:28` (realDeps.RunAnalysis)
- Modify: `src/Bolt.Web/Program.fs:68-73` (/health)
- Modify: `tests/Bolt.ETL.Tests/PerRide1.Tests.fs:34-35`

**Interfaces:**
- Consumes: existing request/response contracts in `AnalyticsContracts.fs` (unchanged).
- Produces:
  - `AnalyticsClient.isHealthy : unit -> Task<bool>`
  - `AnalyticsClient.stDbscan : StDbscanRequest -> Task<StDbscanResponse>` (olsRegression, mirrorCheck analogous)
  - `PerRide1.buildSection / PerRide2.buildSection / RideClustering.buildSection : RidesDataSource -> Task<AnalysisSection>`
  - `AnalysisPipeline.run : string -> Task<Result<AnalysisReport, string>>`

- [ ] **Step 1: Update the existing test to the async signature (it will fail to compile — that is the red step)**

In `tests/Bolt.ETL.Tests/PerRide1.Tests.fs`, change the `buildSection` call:

```fsharp
let ``buildSection produces four tables and no charts`` () =
    let section = (PerRide1.buildSection { Rows = [| row "A" 10m 2.0 |] }).GetAwaiter().GetResult()
```

(keep the rest of the test body unchanged)

Run: `dotnet build Bolt.slnx`
Expected: FAIL — `buildSection` returns `AnalysisSection`, not a task.

- [ ] **Step 2: Make `AnalyticsClient` async**

Replace `isHealthy` and `post` in `src/Bolt.ETL/Analytics/AnalyticsClient.fs` (add `open System.Threading.Tasks`):

```fsharp
    let isHealthy () : Task<bool> =
        task {
            try
                use! response = client.Value.GetAsync "/health"
                return response.IsSuccessStatusCode
            with _ ->
                return false
        }

    let private post<'req, 'resp> (path: string) (request: 'req) : Task<'resp> =
        task {
            use content = new StringContent(Json.serialize request, Encoding.UTF8, "application/json")
            use! response = client.Value.PostAsync(path, content)
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync()
            return Json.deserialize<'resp> body
        }

    let stDbscan (request: StDbscanRequest) : Task<StDbscanResponse> =
        post "/cluster/st-dbscan" request

    let olsRegression (request: OlsRequest) : Task<OlsResponse> =
        post "/regression/ols" request

    let mirrorCheck (request: MirrorCheckRequest) : Task<MirrorCheckResponse> =
        post "/diagnostics/mirror-check" request
```

- [ ] **Step 3: Make the three `buildSection`s async**

`src/Bolt.ETL/Analysis/PerRide1.fs` — pure CPU, lift with `Task.FromResult` (add `open System.Threading.Tasks`):

```fsharp
    let buildSection (source: RidesDataSource) : Task<AnalysisSection> =
        Task.FromResult
            { Id = "ride-stats"
              Title = "Ride statistics"
              Description = "Ride counts and average earnings broken down by pickup district, time and weather."
              Charts = []
              Tables =
                [ breakdownTable "By pickup district" (fun r -> r.PickupDistrict.Value) source.Rows
                  breakdownTable "By part of day" (fun r -> string r.PartOfDay) source.Rows
                  breakdownTable "By day of week" (fun r -> string r.DayOfWeek) source.Rows
                  breakdownTable "By weather" weatherLabel source.Rows ] }
```

`src/Bolt.ETL/Analysis/PerRide2.fs` (add `open System.Threading.Tasks`):

```fsharp
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

            let! mirror =
                AnalyticsClient.mirrorCheck {
                    Rows = toAnalyticsRows source
                    TargetColumns = [| "price_pln" |]
                    GroupMeans = Some { By = "pickup_district"; Value = "distance_km" }
                }

            return
                { Id = "price-regression"
                  Title = "Price regression"
                  Description = "OLS regression of ride price against distance, time and weather, with multicollinearity diagnostics."
                  Charts = []
                  Tables =
                    [ yield coefficientsTable ols
                      yield modelStatsTable ols
                      yield vifTable mirror
                      yield! groupMeansTable mirror |> Option.toList ] }
        }
```

`src/Bolt.ETL/Analysis/RideClustering.fs` (add `open System.Threading.Tasks`):

```fsharp
    let buildSection (source: RidesDataSource) : Task<AnalysisSection> =
        task {
            let points = toStPoints source

            let! response =
                AnalyticsClient.stDbscan {
                    Points = points
                    EpsKm = 0.7
                    EpsHours = 1
                    MinSamples = 5
                }

            let chart =
                [ ClusterMap.ridePointsLayer points response
                  ClusterMap.centroidLayer response ]
                |> Chart.combine
                |> ClusterMap.withMapStyle points

            return
                { Id = "ride-clusters"
                  Title = "Pickup clusters"
                  Description = "Spatio-temporal clusters (ST-DBSCAN) of ride pickup locations."
                  Charts = [ { Title = "Pickup cluster map"; PlotlyFigureJson = GenericChart.toFigureJson chart } ]
                  Tables = [ clusterTable response ] }
        }
```

- [ ] **Step 4: Make `AnalysisPipeline.run` async (still email-keyed)**

Replace the whole body of `src/Bolt.ETL/AnalysisPipeline.fs`:

```fsharp
namespace Bolt.ETL

module AnalysisPipeline =
    open System
    open System.Threading.Tasks
    open Bolt.ETL.Analysis
    open Bolt.Infrastructure.Repository

    /// Runs every analysis for the given account.
    let run (email: string) : Task<Result<AnalysisReport, string>> =
        task {
            try
                match PreviousOrderRepository.get email with
                | None -> return Error $"No ride data found for {email}"
                | Some orders when Seq.isEmpty orders -> return Error $"No rides found for {email}"
                | Some orders ->
                    let dates = orders |> Seq.map _.Created |> Array.ofSeq

                    let! perRide1 = PerRide1.buildSection (PerRide1.prepareRideAnalysisSource email)
                    let! perRide2 = PerRide2.buildSection (PerRide2.prepareRideAnalysisSource email)
                    let! clustering = RideClustering.buildSection (RideClustering.prepareRideAnalysisSource email)

                    return
                        Ok { Email = email
                             GeneratedAt = DateTimeOffset.UtcNow
                             RideCount = dates.Length
                             DateRange = (Array.min dates, Array.max dates)
                             Sections = [ perRide1; perRide2; clustering ] }
            with ex ->
                return Error $"Analysis failed: {ex.Message}"
        }
```

This deletes the "wrap in Task.Run" doc comment and the FIXME 5 line.

- [ ] **Step 5: Update the two Bolt.Web call sites**

`src/Bolt.Web/Pipeline.fs` line 28 — replace:

```fsharp
    RunAnalysis = fun email -> Task.Run(fun () -> AnalysisPipeline.run email)
```

with:

```fsharp
    RunAnalysis = AnalysisPipeline.run
```

`src/Bolt.Web/Program.fs` — replace the `/health` mapping:

```fsharp
    app.MapGet(
        "/health",
        Func<Threading.Tasks.Task<IResult>>(fun () ->
            task {
                let! healthy = AnalyticsClient.isHealthy ()
                return Results.Json {| status = "ok"; analytics = healthy |}
            })
    )
    |> ignore
```

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build Bolt.slnx && dotnet test Bolt.slnx`
Expected: build OK, all tests PASS (including the updated PerRide1 test).

- [ ] **Step 7: Commit**

```bash
git add src/Bolt.ETL src/Bolt.Web tests/Bolt.ETL.Tests
git commit -m "refactor: async analytics client and analysis pipeline (FIXME 5)"
```

---

### Task 3: ETL takes rides as arguments (behavior-preserving refactor)

Extract the three duplicated previous+past → `FinishedRide` conversions into `RideFactory.getFinishedRides`, and change `prepareRideAnalysisSource` to take `FinishedRide[]` instead of `email`. `AnalysisPipeline.run` keeps its email signature but does all repository reads itself and threads rides down. Pure refactor — existing tests gate it.

**Files:**
- Modify: `src/Bolt.ETL/RideFactory.fs`
- Modify: `src/Bolt.ETL/Analysis/PerRide1.fs:53-73`
- Modify: `src/Bolt.ETL/Analysis/PerRide2.fs:88-108`
- Modify: `src/Bolt.ETL/Analysis/RideClustering.fs:33-48`
- Modify: `src/Bolt.ETL/AnalysisPipeline.fs`

**Interfaces:**
- Consumes: `RideFactory.getRide : PreviousOrder -> PastOrderDetail -> Ride` (existing), `buildSection` signatures from Task 2.
- Produces:
  - `RideFactory.getFinishedRides : PreviousOrder[] -> PastOrderDetail[] -> FinishedRide[]`
  - `PerRide1.prepareRideAnalysisSource / PerRide2.prepareRideAnalysisSource / RideClustering.prepareRideAnalysisSource : FinishedRide[] -> RidesDataSource` (each module keeps its own `RidesDataSource` type)

- [ ] **Step 1: Add `getFinishedRides` to `src/Bolt.ETL/RideFactory.fs`** (append at module end)

```fsharp
    /// Pairs previous orders with their details positionally (both are scraped
    /// from the same handle list, in order) and keeps only finished rides.
    let getFinishedRides
        (previousOrders: PreviousOrder[])
        (pastOrderDetails: PastOrderDetail[])
        : FinishedRide[] =
        Array.zip previousOrders pastOrderDetails
        |> Array.map (fun (pr, pod) -> getRide pr pod)
        |> Array.choose (fun ride ->
            match ride.Data with
            | Finished r -> Some r
            | _ -> None)
```

- [ ] **Step 2: Rewrite the three `prepareRideAnalysisSource` functions**

`src/Bolt.ETL/Analysis/PerRide1.fs` (delete the old email-keyed version; `open Bolt.Models` is already present, keep `open Bolt.Infrastructure.Repository` for Meteo/Districts):

```fsharp
    let prepareRideAnalysisSource (rides: FinishedRide[]) : RidesDataSource =
        let meteo = (MeteoRepository.get ()).Value
        let districts = (DistrictsRepository.get ()).Value

        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
        let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts

        { Rows = rides |> Array.map (RideRow.fromRide weatherProvider districtProvider) }
```

`src/Bolt.ETL/Analysis/PerRide2.fs` — identical shape:

```fsharp
    let prepareRideAnalysisSource (rides: FinishedRide[]) : RidesDataSource =
        let meteo = (MeteoRepository.get ()).Value
        let districts = (DistrictsRepository.get ()).Value

        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
        let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts

        { Rows = rides |> Array.map (RideRow.fromRide weatherProvider districtProvider) }
```

`src/Bolt.ETL/Analysis/RideClustering.fs` (this module no longer touches any repository — remove `open Bolt.Infrastructure.Repository`):

```fsharp
    let prepareRideAnalysisSource (rides: FinishedRide[]) : RidesDataSource =
        { Rows = rides |> Array.map RideRow.fromRide }
```

In all three modules the local `finishedRide` helper and the repository reads for previous/past orders are deleted.

- [ ] **Step 3: Thread rides through `AnalysisPipeline.run`**

In `src/Bolt.ETL/AnalysisPipeline.fs`, replace the `Some orders ->` branch:

```fsharp
                | Some orders ->
                    let previous = Array.ofSeq orders
                    let pastOrders = (PastOrderDetailRepository.get email).Value |> Array.ofSeq
                    let dates = previous |> Array.map _.Created
                    let rides = RideFactory.getFinishedRides previous pastOrders

                    let! perRide1 = PerRide1.buildSection (PerRide1.prepareRideAnalysisSource rides)
                    let! perRide2 = PerRide2.buildSection (PerRide2.prepareRideAnalysisSource rides)
                    let! clustering = RideClustering.buildSection (RideClustering.prepareRideAnalysisSource rides)

                    return
                        Ok { Email = email
                             GeneratedAt = DateTimeOffset.UtcNow
                             RideCount = dates.Length
                             DateRange = (Array.min dates, Array.max dates)
                             Sections = [ perRide1; perRide2; clustering ] }
```

- [ ] **Step 4: Build and run all tests**

Run: `dotnet build Bolt.slnx && dotnet test Bolt.slnx`
Expected: build OK, all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.ETL
git commit -m "refactor: analysis sections take FinishedRide arrays, not emails"
```

---

### Task 4: Weather cap from `ensureMeteoCoverage` (FIXME 2, scraper side)

`ensureMeteoCoverage` takes a `(rideMin, rideMax)` date range (loose coupling — no order types) and returns the capped maximum instead of `unit`. `Pipeline.realDeps` gets a temporary bridge that keeps the current `PipelineDeps` shape compiling; Task 5 replaces it.

**Files:**
- Modify: `src/Bolt.Scraper/ScrapePipeline.fs:64-74` (MeteoCoverage) and `:122-155` (ensureMeteoCoverage)
- Modify: `src/Bolt.Web/Pipeline.fs:27` (EnsureMeteo bridge)
- Modify: `tests/Bolt.Scraper.Tests/MeteoCoverage.Tests.fs`

**Interfaces:**
- Consumes: `MeteoCoverage.missingRanges` (unchanged), `MeteoRepository`, `getWeatherData` (unchanged).
- Produces:
  - `MeteoCoverage.archiveLag : TimeSpan` and `MeteoCoverage.cappedMax : DateTimeOffset -> DateTimeOffset -> DateTimeOffset`
  - `ensureMeteoCoverage : (DateTimeOffset * DateTimeOffset) -> CancellationToken -> Task<Result<DateTimeOffset, string>>` — returns the capped `rideMax`; assumes a valid, non-empty range.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Bolt.Scraper.Tests/MeteoCoverage.Tests.fs` (reuse the file's existing `at` date helper):

```fsharp
[<Fact>]
let ``cappedMax returns rideMax when the archive already covers it`` () =
    Assert.Equal(at "2026-07-01Z", MeteoCoverage.cappedMax (at "2026-07-13Z") (at "2026-07-01Z"))

[<Fact>]
let ``cappedMax clamps to the archive lag before now`` () =
    Assert.Equal(at "2026-07-08Z", MeteoCoverage.cappedMax (at "2026-07-13Z") (at "2026-07-12Z"))
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Bolt.slnx --filter "FullyQualifiedName~MeteoCoverageTests"`
Expected: build FAILS — `cappedMax` not defined.

- [ ] **Step 3: Implement `cappedMax` and the new `ensureMeteoCoverage`**

In `src/Bolt.Scraper/ScrapePipeline.fs`, inside `module MeteoCoverage`:

```fsharp
    /// The open-meteo archive lags a few days behind real time.
    let archiveLag = TimeSpan.FromDays 5.0

    /// Latest ride date the weather archive can cover right now.
    let cappedMax (now: DateTimeOffset) (rideMax: DateTimeOffset) : DateTimeOffset =
        min rideMax (now - archiveLag)
```

Replace `ensureMeteoCoverage` (this deletes the FIXME 2 comment; the meteo lock stays):

```fsharp
/// Fetches whatever weather data is missing for [rideMin, rideMax] and returns
/// the effective coverage cap: rides created after it have no weather data and
/// must be excluded from weather-dependent analyses.
let ensureMeteoCoverage
    (rideMin: DateTimeOffset, rideMax: DateTimeOffset)
    (ct: CancellationToken)
    : Task<Result<DateTimeOffset, string>> =
    task {
        let cap = MeteoCoverage.cappedMax DateTimeOffset.UtcNow rideMax
        let existingRange = MeteoRepository.get () |> Option.bind Weather.hourRange
        let ranges = MeteoCoverage.missingRanges (rideMin, cap) existingRange

        let mutable result = Ok cap
        for from, to' in ranges do
            if Result.isOk result && from < to' then
                ct.ThrowIfCancellationRequested()
                match! getWeatherData from to' ScrapeSession.krakowCenter with
                | Error e -> result <- Error $"Weather fetch failed: {e}"
                | Ok fetched ->
                    lock meteoLock (fun () ->
                        let merged =
                            match MeteoRepository.get () with
                            | Some existing -> Weather.merge existing fetched
                            | None -> fetched
                        MeteoRepository.save merged)
        return result
    }
```

- [ ] **Step 4: Bridge `Pipeline.realDeps` (temporary — Task 5 deletes it)**

In `src/Bolt.Web/Pipeline.fs`, replace `EnsureMeteo = ensureMeteoCoverage` with:

```fsharp
    // Temporary bridge until PipelineDeps carries ScrapedData (next task):
    // reads the repository to derive the ride date range, discards the cap.
    EnsureMeteo = fun email ct ->
        task {
            match PreviousOrderRepository.get email with
            | None -> return Error "No scraped ride data found; cannot determine weather range"
            | Some orders when Seq.isEmpty orders -> return Error "No rides found for this account"
            | Some orders ->
                let dates = orders |> Seq.map _.Created
                let! result = ensureMeteoCoverage (Seq.min dates, Seq.max dates) ct
                return result |> Result.map ignore
        }
```

(`open System.Threading.Tasks` is already present in Pipeline.fs.)

- [ ] **Step 5: Run tests and commit**

Run: `dotnet build Bolt.slnx && dotnet test Bolt.slnx`
Expected: build OK, all tests PASS (2 new).

```bash
git add src/Bolt.Scraper src/Bolt.Web tests/Bolt.Scraper.Tests
git commit -m "feat: ensureMeteoCoverage takes a date range and returns the weather cap (FIXME 2)"
```

---

### Task 5: `ScrapedData` end to end — in-memory scrape, cap threading, email-first protocol, async progress (FIXMEs 1, 3, 4)

The core rewrite. It must land as one commit because `Bolt.Web` references every changed signature: `ScrapedData` record, `scrapeRides` returning it with DEBUG-only persistence and a Task progress callback, `AnalysisPipeline.run (data, cap)` with weather-cap filtering, `PipelineDeps<'session, 'data>`, the `JobRunner` rewrite (no `initialMagicLink`), `WebSockets` (magic-link-first → error fragment), `Program`, and both Web test files.

**Files:**
- Create: `src/Bolt.Models/BoltApi/ScrapedData.fs`
- Modify: `src/Bolt.Models/Bolt.Models.fsproj`
- Modify: `src/Bolt.Scraper/ScrapePipeline.fs` (scrapeRides)
- Modify: `src/Bolt.ETL/AnalysisPipeline.fs`
- Modify: `src/Bolt.Web/Jobs/JobState.fs` (PipelineDeps)
- Modify: `src/Bolt.Web/Jobs/JobRunner.fs`
- Modify: `src/Bolt.Web/Pipeline.fs`
- Modify: `src/Bolt.Web/WebSockets.fs`
- Modify: `src/Bolt.Web/Program.fs`
- Modify: `tests/Bolt.Web.Tests/JobRunner.Tests.fs`
- Modify: `tests/Bolt.Web.Tests/WebSocket.Tests.fs`
- Create: `tests/Bolt.ETL.Tests/AnalysisPipeline.Tests.fs`
- Modify: `tests/Bolt.ETL.Tests/Bolt.ETL.Tests.fsproj`

**Interfaces:**
- Consumes: `Json.deserializeElement`, `getUnstructured` ×3 (Task 1), Task-based `buildSection` (Task 2), `prepareRideAnalysisSource (FinishedRide[])` and `getFinishedRides` (Task 3), range-based `ensureMeteoCoverage` (Task 4), `maybe` CE.
- Produces:
  - `ScrapedData` record (below)
  - `scrapeRides : ScrapeSession -> (ScrapeProgress -> Task) -> CancellationToken -> Task<Result<ScrapedData, string>>`
  - `AnalysisPipeline.run : ScrapedData -> DateTimeOffset -> Task<Result<AnalysisReport, string>>`
  - `AnalysisPipeline.weatherEligible : DateTimeOffset -> FinishedRide[] -> FinishedRide[]`
  - `PipelineDeps<'session, 'data>` (below)
  - `JobRunner.run : PipelineDeps<'session, 'data> -> string -> ChannelReader<string> -> (JobState -> Task) -> CancellationToken -> Task`

- [ ] **Step 1: Create `src/Bolt.Models/BoltApi/ScrapedData.fs`**

```fsharp
namespace Bolt.Models.BoltApi

open System.Text.Json
open System.Text.Json.Nodes

/// Everything one scrape run produces, kept in memory for the lifetime of the
/// job. Persisted to disk only in DEBUG builds; production never writes user
/// data.
type ScrapedData = {
    Email: string
    /// Raw getDriverProfile response; nothing reads it typed yet.
    Profile: JsonElement
    /// Raw getActivityHours response; nothing reads it typed yet.
    ActivityHours: JsonElement
    /// Raw order-history entries as returned by getOrderHistory.
    OrderHistory: JsonNode[]
    PreviousOrders: PreviousOrder[]
    PastOrderDetails: PastOrderDetail[]
}
```

In `src/Bolt.Models/Bolt.Models.fsproj`, add after the `PastOrderDetail.fs` line:

```xml
<Compile Include="BoltApi\ScrapedData.fs"/>
```

Note (spec deviation, deliberate): the spec sketched typed `Profile`/`ActivityHours`/`OrderHistory` fields, but `BoltClient` returns raw JSON, no code reads these three typed anywhere (`DriverProfileRepository.get` is even mistyped dead code), and analysis consumes only `PreviousOrders`/`PastOrderDetails`. Raw fields avoid exercising untested deserialization paths. `Email` was added because `AnalysisReport.Email` needs it.

- [ ] **Step 2: Rewrite the JobRunner unit tests (red)**

Replace the whole of `tests/Bolt.Web.Tests/JobRunner.Tests.fs`:

```fsharp
module Bolt.Web.Tests.JobRunnerTests

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Xunit
open Bolt.ETL.Analysis
open Bolt.Web
open Bolt.Web.Jobs

let private report: AnalysisReport = {
    Email = "a@b.pl"
    GeneratedAt = DateTimeOffset.UtcNow
    RideCount = 1
    DateRange = (DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
    Sections = []
}

let private cap = DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
let private ok () : Task<Result<unit, string>> = Task.FromResult(Ok())
let private err e : Task<Result<unit, string>> = Task.FromResult(Error e)

/// Happy-path deps over a unit session and int data (the ride count);
/// individual tests override fields.
let private baseDeps: PipelineDeps<unit, int> = {
    LoadCached = fun _ -> None
    CreateSession = fun _ -> ()
    HasTokens = fun _ -> false
    RefreshTokens = fun _ _ -> ok ()
    RequestMagicLink = fun _ _ -> ok ()
    AuthenticateWithUrl = fun _ _ _ -> ok ()
    ScrapeRides = fun _ _ _ -> Task.FromResult(Ok 3)
    EnsureMeteo = fun _ _ -> Task.FromResult(Ok cap)
    RunAnalysis = fun _ _ -> Task.FromResult(Ok report)
    RideCountOf = id
}

let private runToEnd deps (feed: string list) =
    let states = ConcurrentQueue<JobState>()
    let channel = Channel.CreateUnbounded<string>()
    for url in feed do channel.Writer.TryWrite url |> ignore
    let notify state = states.Enqueue state; Task.CompletedTask
    (JobRunner.run deps "a@b.pl" channel.Reader notify CancellationToken.None)
        .GetAwaiter()
        .GetResult()
    states |> List.ofSeq

[<Fact>]
let ``cached data skips auth and scraping`` () =
    let states = runToEnd { baseDeps with LoadCached = fun _ -> Some 3 } []
    Assert.Equal<JobState list>(
        [ CheckingCache; FetchingMeteo; RunningAnalysis; Done report ], states)

[<Fact>]
let ``no tokens goes through magic link then scrapes`` () =
    let states = runToEnd baseDeps [ "https://link" ]
    Assert.Contains(AwaitingMagicLink None, states)
    Assert.Contains(FetchingMeteo, states)
    Assert.Equal(Done report, List.last states)

[<Fact>]
let ``invalid magic link keeps awaiting with error`` () =
    let attempts = ConcurrentQueue<string>()
    let deps =
        { baseDeps with
            AuthenticateWithUrl = fun _ url _ ->
                attempts.Enqueue url
                if url = "bad" then err "bad token" else ok () }
    let states = runToEnd deps [ "bad"; "good" ]
    Assert.Contains(AwaitingMagicLink(Some "bad token"), states)
    Assert.Equal(Done report, List.last states)
    Assert.Equal(2, attempts.Count)

[<Fact>]
let ``valid saved tokens skip magic link`` () =
    let deps = { baseDeps with HasTokens = fun _ -> true }
    let states = runToEnd deps []
    Assert.DoesNotContain(AwaitingMagicLink None, states)
    Assert.Equal(Done report, List.last states)

[<Fact>]
let ``scrape failure ends in Failed`` () =
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            ScrapeRides = fun _ _ _ -> Task.FromResult(Error "boom") }
    let states = runToEnd deps []
    Assert.Equal(Failed("scraping rides", "boom"), List.last states)

[<Fact>]
let ``zero scraped rides fail at the scraping step`` () =
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            ScrapeRides = fun _ _ _ -> Task.FromResult(Ok 0) }
    let states = runToEnd deps []
    Assert.Equal(
        Failed("scraping rides", "No rides found for this account"), List.last states)

[<Fact>]
let ``weather cap flows from EnsureMeteo into RunAnalysis`` () =
    let received = ConcurrentQueue<DateTimeOffset>()
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            RunAnalysis = fun _ c -> received.Enqueue c; Task.FromResult(Ok report) }
    runToEnd deps [] |> ignore
    Assert.Equal<DateTimeOffset list>([ cap ], List.ofSeq received)

[<Fact>]
let ``meteo failure ends in Failed at the weather step`` () =
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            EnsureMeteo = fun _ _ -> Task.FromResult(Error "boom") }
    let states = runToEnd deps []
    Assert.Equal(Failed("fetching weather data", "boom"), List.last states)

[<Fact>]
let ``cancellation while awaiting magic link produces no terminal state`` () =
    let states = ConcurrentQueue<JobState>()
    let channel = Channel.CreateUnbounded<string>()
    use cts = new CancellationTokenSource()
    let notify state = states.Enqueue state; Task.CompletedTask
    let running = JobRunner.run baseDeps "a@b.pl" channel.Reader notify cts.Token
    // Give the runner a moment to reach the await, then cancel.
    Task.Delay(100).GetAwaiter().GetResult()
    cts.Cancel()
    running.GetAwaiter().GetResult()
    Assert.DoesNotContain(states, fun s -> match s with Done _ | Failed _ -> true | _ -> false)
```

Note what changed versus the old file: `IsFresh` → `LoadCached`, `PipelineDeps<unit>` → `PipelineDeps<unit, int>`, `runToEnd` lost the `initialLink` parameter, the `initial magic link skips waiting (stateless login)` test is **deleted** (the flow no longer exists — FIXME 3), the cached-path expectation now includes `FetchingMeteo`, and three new tests cover zero rides, cap threading, and meteo failure.

Run: `dotnet build Bolt.slnx`
Expected: FAIL — new deps shape doesn't exist yet.

- [ ] **Step 3: Rewrite `PipelineDeps` in `src/Bolt.Web/Jobs/JobState.fs`**

Replace the `PipelineDeps` type (JobState and ClientMessage stay unchanged):

```fsharp
/// Everything the job runner needs, injected so the state machine is
/// testable without Bolt, open-meteo or the analytics service. 'data is the
/// scraped payload, opaque to the runner.
type PipelineDeps<'session, 'data> = {
    /// Some only in DEBUG builds when a fresh disk cache exists.
    LoadCached: string -> 'data option
    CreateSession: string -> 'session
    HasTokens: 'session -> bool
    RefreshTokens: 'session -> CancellationToken -> Task<Result<unit, string>>
    RequestMagicLink: 'session -> CancellationToken -> Task<Result<unit, string>>
    AuthenticateWithUrl: 'session -> string -> CancellationToken -> Task<Result<unit, string>>
    ScrapeRides: 'session -> (string -> Task) -> CancellationToken -> Task<Result<'data, string>>
    /// Returns the weather-coverage cap: rides created after it have no weather data.
    EnsureMeteo: 'data -> CancellationToken -> Task<Result<DateTimeOffset, string>>
    RunAnalysis: 'data -> DateTimeOffset -> Task<Result<AnalysisReport, string>>
    RideCountOf: 'data -> int
}
```

- [ ] **Step 4: Rewrite `src/Bolt.Web/Jobs/JobRunner.fs`**

```fsharp
module Bolt.Web.JobRunner

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Bolt.Web.Jobs

/// One analysis job, bound to one socket. The first client message must be an
/// e-mail (StartAnalysis); magic-link URLs arrive on `magicLinks` afterwards
/// and each is simply tried. Every state change goes out through `notify`.
/// Cancellation (socket gone) stops the job without a terminal notification.
let run
    (deps: PipelineDeps<'session, 'data>)
    (email: string)
    (magicLinks: ChannelReader<string>)
    (notify: JobState -> Task)
    (ct: CancellationToken)
    : Task =
    task {
        try
            do! notify CheckingCache
            let mutable failure: (string * string) option = None
            let mutable data: 'data option = None

            match deps.LoadCached email with
            | Some cached -> data <- Some cached
            | None ->
                let session = deps.CreateSession email
                do! notify Authenticating

                let mutable authenticated = false

                if deps.HasTokens session then
                    match! deps.RefreshTokens session ct with
                    | Ok() -> authenticated <- true
                    | Error _ -> () // stale cache; fall through to magic link

                if not authenticated then
                    match! deps.RequestMagicLink session ct with
                    | Error e -> failure <- Some("requesting magic link", e)
                    | Ok() ->
                        do! notify (AwaitingMagicLink None)

                        while not authenticated do
                            let! url = magicLinks.ReadAsync ct
                            match! deps.AuthenticateWithUrl session url ct with
                            | Ok() -> authenticated <- true
                            | Error e -> do! notify (AwaitingMagicLink(Some e))

                if failure.IsNone then
                    do! notify (ScrapingRides "")
                    let progress detail = notify (ScrapingRides detail)

                    match! deps.ScrapeRides session progress ct with
                    | Error e -> failure <- Some("scraping rides", e)
                    | Ok d -> data <- Some d

            match failure, data with
            | Some(step, e), _ -> do! notify (Failed(step, e))
            | None, Some d when deps.RideCountOf d = 0 ->
                do! notify (Failed("scraping rides", "No rides found for this account"))
            | None, Some d ->
                do! notify FetchingMeteo

                match! deps.EnsureMeteo d ct with
                | Error e -> do! notify (Failed("fetching weather data", e))
                | Ok cap ->
                    do! notify RunningAnalysis

                    match! deps.RunAnalysis d cap with
                    | Ok report -> do! notify (Done report)
                    | Error e -> do! notify (Failed("analysis", e))
            | None, None -> () // request-magic-link failed; already reported above
        with
        | :? OperationCanceledException -> ()
        | ex -> do! notify (Failed("internal", ex.Message))
    }
```

This deletes FIXMEs 3 and 4 in this file: no `initialMagicLink` parameter, and `progress` returns the `notify` task instead of blocking on it.

- [ ] **Step 5: Rewrite `scrapeRides` in `src/Bolt.Scraper/ScrapePipeline.fs`**

Add opens at the top of the file: `open Bolt.Models.BoltApi` and `open Bolt.Infrastrucutre.Serialization`.

Replace `scrapeRides` (deletes FIXME 1 and the old `report: ScrapeProgress -> unit` callback — FIXME 4's root):

```fsharp
/// Scrapes everything for the session's account and returns it in memory.
/// DEBUG builds also persist the raw responses to disk (same files the
/// debugging tools read); production writes nothing.
let scrapeRides
    (session: ScrapeSession)
    (report: ScrapeProgress -> Task)
    (ct: CancellationToken)
    : Task<Result<ScrapedData, string>> =
    let bolt = BoltClient.fromConfig session.Config
    let email = session.Email

    task {
        let! scraped =
            mapApiError (taskResult {
                do! report ScrapingProfile
                let! profile = BoltClient.getDriverProfile bolt

                do! report ScrapingActivityHours
                let! activity = BoltClient.getActivityHours bolt

                do! report ScrapingOrderHistory
                let! handles, history = BoltClient.getOrderHistory bolt

                let handles = handles |> Array.ofSeq
                let total = handles.Length
                let previous = ResizeArray()
                let details = ResizeArray()
                let mutable i = 0

                do! taskResult {
                    while i < total do
                        ct.ThrowIfCancellationRequested()
                        do! report (ScrapingOrderDetails(i + 1, total))
                        let! p = BoltClient.getPreviousOrder bolt handles[i]
                        let! d = BoltClient.getPastOrderDetails bolt handles[i]
                        previous.Add p
                        details.Add d
                        i <- i + 1
                }

                return (profile, activity, Array.ofSeq history, previous.ToArray(), details.ToArray())
            })

        match scraped with
        | Error e -> return Error e
        | Ok(profile, activity, history, previous, details) ->
            try
                let data =
                    { Email = email
                      Profile = profile
                      ActivityHours = activity
                      OrderHistory = history
                      PreviousOrders = previous |> Array.map Json.deserializeElement<PreviousOrder>
                      PastOrderDetails = details |> Array.map Json.deserializeElement<PastOrderDetail> }
#if DEBUG
                DriverProfileRepository.saveUnstructuredDangerous email profile
                ActivityHoursRepository.saveUnstructuredDangerous email activity
                OrderHistoryRepository.saveUnstructuredDangerous email history
                PreviousOrderRepository.saveUnstructuredDangerous email previous
                PastOrderDetailRepository.saveUnstructuredDangerous email details
                ScrapeMetadataRepository.save email { ScrapedAt = DateTimeOffset.UtcNow }
#endif
                return Ok data
            with ex ->
                return Error $"Could not parse scraped data: {ex.Message}"
    }
```

Types along the way: `profile` and `activity` are `JsonElement`, `history` is a seq of `JsonNode` (so `Array.ofSeq history : JsonNode[]`), `previous`/`details` collect `JsonElement`s. The raw arrays are what DEBUG persists — byte-identical to what the old code wrote, so `Bolt.Reporter` and the cache loader keep working.

- [ ] **Step 6: Final `AnalysisPipeline.run` — `ScrapedData` + weather cap**

Replace the whole of `src/Bolt.ETL/AnalysisPipeline.fs`:

```fsharp
namespace Bolt.ETL

module AnalysisPipeline =
    open System
    open System.Threading.Tasks
    open Bolt.ETL.Analysis
    open Bolt.Models
    open Bolt.Models.BoltApi

    /// Rides inside the weather-archive coverage (created at or before the cap).
    let weatherEligible (weatherCap: DateTimeOffset) (rides: FinishedRide[]) : FinishedRide[] =
        rides |> Array.filter (fun r -> r.Times.CreatedTimestamp <= weatherCap)

    let private emptySection id title description =
        { Id = id
          Title = title
          Description = description + " No rides fall within the weather data range."
          Charts = []
          Tables = [] }

    /// Runs every analysis over in-memory scraped data. Weather-dependent
    /// sections (PerRide1, PerRide2) see only rides covered by the weather
    /// archive; clustering and the report totals use the full set.
    let run (data: ScrapedData) (weatherCap: DateTimeOffset) : Task<Result<AnalysisReport, string>> =
        task {
            try
                if Array.isEmpty data.PreviousOrders then
                    return Error $"No rides found for {data.Email}"
                else
                    let dates = data.PreviousOrders |> Array.map _.Created
                    let allRides = RideFactory.getFinishedRides data.PreviousOrders data.PastOrderDetails
                    let weatherRides = weatherEligible weatherCap allRides

                    let! perRide1 =
                        if Array.isEmpty weatherRides then
                            Task.FromResult(emptySection "ride-stats" "Ride statistics"
                                "Ride counts and average earnings broken down by pickup district, time and weather.")
                        else
                            PerRide1.buildSection (PerRide1.prepareRideAnalysisSource weatherRides)

                    let! perRide2 =
                        if Array.isEmpty weatherRides then
                            Task.FromResult(emptySection "price-regression" "Price regression"
                                "OLS regression of ride price against distance, time and weather, with multicollinearity diagnostics.")
                        else
                            PerRide2.buildSection (PerRide2.prepareRideAnalysisSource weatherRides)

                    let! clustering =
                        RideClustering.buildSection (RideClustering.prepareRideAnalysisSource allRides)

                    return
                        Ok { Email = data.Email
                             GeneratedAt = DateTimeOffset.UtcNow
                             RideCount = data.PreviousOrders.Length
                             DateRange = (Array.min dates, Array.max dates)
                             Sections = [ perRide1; perRide2; clustering ] }
            with ex ->
                return Error $"Analysis failed: {ex.Message}"
        }
```

(`open Bolt.Infrastructure.Repository` is gone — this module no longer reads user data from disk.)

- [ ] **Step 7: Final `src/Bolt.Web/Pipeline.fs`**

Replace the whole file:

```fsharp
module Bolt.Web.Pipeline

open System
open Bolt.ETL
open Bolt.Models.BoltApi
open Bolt.Scraper.ScrapePipeline
open Bolt.Web.Jobs
#if DEBUG
open Bolt.Infrastructure.Repository
open Bolt.Infrastrucutre.ces.OptionBuilder
#endif

let describeProgress (p: ScrapeProgress) =
    match p with
    | ScrapingProfile -> "driver profile"
    | ScrapingActivityHours -> "activity hours"
    | ScrapingOrderHistory -> "order history"
    | ScrapingOrderDetails(current, total) -> $"order details {current}/{total}"

#if DEBUG
// Reading scraped data back from disk is a debugging convenience: production
// never persists user data, so there is nothing to load.
let freshnessWindow = TimeSpan.FromDays 14.0

let private loadCached (email: string) : ScrapedData option =
    if ScrapeMetadataRepository.isFresh DateTimeOffset.UtcNow freshnessWindow email then
        maybe {
            let! profile = DriverProfileRepository.getUnstructured email
            let! activity = ActivityHoursRepository.getUnstructured email
            let! history = OrderHistoryRepository.getUnstructured email
            let! previous = PreviousOrderRepository.get email
            let! details = PastOrderDetailRepository.get email
            return
                { Email = email
                  Profile = profile
                  ActivityHours = activity
                  OrderHistory = history
                  PreviousOrders = Array.ofSeq previous
                  PastOrderDetails = Array.ofSeq details }
        }
    else
        None
#endif

let realDeps: PipelineDeps<ScrapeSession, ScrapedData> = {
#if DEBUG
    LoadCached = loadCached
#else
    LoadCached = fun _ -> None
#endif
    CreateSession = ScrapeSession.create
    HasTokens = ScrapeSession.hasTokens
    RefreshTokens = ScrapeSession.refreshTokens
    RequestMagicLink = ScrapeSession.requestMagicLink
    AuthenticateWithUrl = ScrapeSession.authenticateWithUrl
    ScrapeRides = fun session progress ct -> scrapeRides session (describeProgress >> progress) ct
    // Adapter keeps ensureMeteoCoverage loosely coupled: it sees only the date
    // range, and the job runner never sees order types.
    EnsureMeteo = fun data ct ->
        let dates = data.PreviousOrders |> Array.map _.Created
        ensureMeteoCoverage (Array.min dates, Array.max dates) ct
    RunAnalysis = AnalysisPipeline.run
    RideCountOf = fun data -> data.PreviousOrders.Length
}
```

(`EnsureMeteo` is safe against empty arrays: the runner's `RideCountOf` guard fires before it.)

- [ ] **Step 8: `src/Bolt.Web/WebSockets.fs` — email-first protocol**

Three changes:

`startJob` loses its `initialUrl` parameter and the runner call drops it:

```fsharp
            let startJob (email: string) =
                task {
                    if not (activeEmails.TryAdd(email, 0uy)) then
                        do! send (Views.errorFragment email "startup"
                                      "An analysis for this e-mail is already in progress. Try again later.")
                    else
                        let cts = new CancellationTokenSource()
                        let channel = Channel.CreateUnbounded<string>()
                        jobEmail <- Some email
                        jobCts <- Some cts
                        magicLinks <- Some channel

                        let notify state =
                            task {
                                do! send (stateFragment email state)

                                match state with
                                | Done _
                                | Failed _ -> releaseJob ()
                                | _ -> ()
                            }
                            :> Task

                        // Fire and forget: the job talks back through `notify`,
                        // the receive loop below keeps handling client messages.
                        JobRunner.run deps email channel.Reader notify cts.Token
                        |> ignore
                }
```

The message dispatch replaces the stateless-login fallback with an error fragment:

```fsharp
                        if not closed then
                            match parseMessage (sb.ToString()) with
                            | Some(StartAnalysis email) when jobEmail.IsNone ->
                                do! startJob email
                            | Some(StartAnalysis _) ->
                                () // this socket already runs a job; ignore
                            | Some(MagicLink(email, url)) ->
                                match magicLinks, jobEmail with
                                | Some channel, Some _ ->
                                    channel.Writer.TryWrite url |> ignore
                                | _ ->
                                    // A job must be started with an e-mail first;
                                    // a magic link can never be the first message.
                                    do! send (Views.errorFragment email "authentication"
                                                  "Start the analysis with your e-mail address first.")
                            | None -> ()
```

And `handle` becomes generic over both parameters (signature only):

```fsharp
let handle (deps: PipelineDeps<'session, 'data>) (ctx: HttpContext) : Task =
```

- [ ] **Step 9: `src/Bolt.Web/Program.fs` — concrete deps type**

Add `open Bolt.Models.BoltApi` and change both occurrences of `PipelineDeps<ScrapeSession>` to `PipelineDeps<ScrapeSession, ScrapedData>` (the `AddSingleton` at line 32 and the `GetRequiredService` in the `/ws` mapping).

- [ ] **Step 10: Update `tests/Bolt.Web.Tests/WebSocket.Tests.fs`**

Replace the `fakeDeps` block (add `open System.Text.Json` and `open Bolt.Models.BoltApi` to the opens):

```fsharp
let private fakeData: ScrapedData = {
    Email = "a@b.pl"
    Profile = JsonDocument.Parse("{}").RootElement
    ActivityHours = JsonDocument.Parse("{}").RootElement
    OrderHistory = [||]
    PreviousOrders = [||]
    PastOrderDetails = [||]
}

// Fake deps: cached data, analysis returns instantly. CreateSession never
// talks to the network because every other function is faked. RideCountOf
// is faked non-zero so the empty PreviousOrders array doesn't trip the guard.
let private fakeDeps: PipelineDeps<ScrapeSession, ScrapedData> = {
    LoadCached = fun _ -> Some fakeData
    CreateSession = ScrapeSession.create
    HasTokens = fun _ -> true
    RefreshTokens = fun _ _ -> Task.FromResult(Ok())
    RequestMagicLink = fun _ _ -> Task.FromResult(Ok())
    AuthenticateWithUrl = fun _ _ _ -> Task.FromResult(Ok())
    ScrapeRides = fun _ _ _ -> Task.FromResult(Ok fakeData)
    EnsureMeteo = fun _ _ -> Task.FromResult(Ok DateTimeOffset.UtcNow)
    RunAnalysis = fun _ _ -> Task.FromResult(Ok report)
    RideCountOf = fun _ -> 3
}
```

Update the DI registration in `makeFactory`:

```fsharp
                services.AddSingleton<PipelineDeps<ScrapeSession, ScrapedData>>(fakeDeps) |> ignore)
```

Append the protocol test:

```fsharp
[<Fact>]
let ``magic link as first message yields an error and starts no job`` () =
    use factory = makeFactory ()
    let client = factory.Server.CreateWebSocketClient()
    client.ConfigureRequest <- fun req -> req.Headers.Origin <- string factory.Server.BaseAddress
    use socket =
        client.ConnectAsync(Uri(factory.Server.BaseAddress, "/ws"), CancellationToken.None)
            .GetAwaiter().GetResult()

    sendText socket """{"msgType":"magic-link","email":"a@b.pl","url":"https://link"}"""

    let response = receiveText socket
    Assert.Contains("Start the analysis with your e-mail address first.", response)
```

- [ ] **Step 11: Add the `weatherEligible` unit test**

Create `tests/Bolt.ETL.Tests/AnalysisPipeline.Tests.fs`:

```fsharp
module Bolt.ETL.Tests.AnalysisPipelineTests

open System
open Xunit
open Bolt.ETL
open Bolt.Models

let private rideAt (created: DateTimeOffset) : FinishedRide = {
    Payment =
        { PaymentMetadata = { PaymentType = "cash"; PaymentMethodType = "cash" }
          Paid = [||]
          Earned = [||] }
    Route = { RideDistance = 1.0<km>; Stops = [||] }
    Times =
        { CreatedTimestamp = created
          AcceptedTimestamp = created
          RideStart = created
          RideEnd = created }
    State = "finished"
}

[<Fact>]
let ``weatherEligible keeps rides at or before the cap`` () =
    let cap = DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
    let before = rideAt (cap.AddDays -1.0)
    let atCap = rideAt cap
    let after = rideAt (cap.AddDays 1.0)
    Assert.Equal<FinishedRide[]>(
        [| before; atCap |],
        AnalysisPipeline.weatherEligible cap [| before; atCap; after |])
```

Add to `tests/Bolt.ETL.Tests/Bolt.ETL.Tests.fsproj` after the `RideClustering.Tests.fs` line:

```xml
<Compile Include="AnalysisPipeline.Tests.fs" />
```

- [ ] **Step 12: Build and run the full suite**

Run: `dotnet build Bolt.slnx && dotnet test Bolt.slnx`
Expected: build OK; all tests PASS — 9 JobRunner tests, 3 WebSocket tests (1 new), 1 new ETL test.

- [ ] **Step 13: Commit**

```bash
git add src tests
git commit -m "feat: in-memory ScrapedData pipeline, email-first protocol, async progress (FIXMEs 1, 3, 4)"
```

---

### Task 6: Sweep and dual-configuration verification

**Files:**
- Verify only (no planned modifications; fix anything the checks surface).

**Interfaces:** none.

- [ ] **Step 1: Verify no FIXME survives**

Run: `grep -rn "FIXME" src tests --include="*.fs"`
Expected: no output. If any of the five original FIXME comments survived, delete it (its code is already fixed) and re-run.

- [ ] **Step 2: Verify Release build (the `#if DEBUG` gates must compile both ways)**

Run: `dotnet build Bolt.slnx -c Release`
Expected: Build succeeded, 0 errors. (Warnings about unused `freshnessWindow` must not appear — it is inside `#if DEBUG`.)

- [ ] **Step 3: Full Debug build and test run**

Run: `dotnet build Bolt.slnx && dotnet test Bolt.slnx`
Expected: all tests PASS.

- [ ] **Step 4: Commit (only if the sweep changed anything)**

```bash
git add -A
git commit -m "chore: FIXME sweep after pipeline rewrite"
```
