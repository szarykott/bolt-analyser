# FIXME Cleanup: In-Memory Scrape Data, Weather Cap, Protocol & Async Fixes

**Date:** 2026-07-13
**Status:** Approved

## Motivation

Four FIXME comments mark known problems:

1. `src/Bolt.Scraper/ScrapePipeline.fs:76` — `scrapeRides` persists user data to disk via repositories; it should keep the data in memory and return it for analysis.
2. `src/Bolt.Scraper/ScrapePipeline.fs:133` — `ensureMeteoCoverage` computes the weather-archive cap (`now − 5 days`) but discards it; analysis cannot exclude rides that have no weather data.
3. `src/Bolt.Web/Jobs/JobRunner.fs:32` — a magic-link URL is accepted as the first websocket message; only an e-mail address should start a job.
4. `src/Bolt.Web/Jobs/JobRunner.fs:59` — the scrape progress callback blocks with `GetAwaiter().GetResult()`.

Product constraint driving item 1: **user data must never be persisted in production**. Scraping anew on every visit is the intended workflow. Disk persistence (and the 14-day freshness cache built on it) is a debugging convenience only, gated behind `#if DEBUG` — the same pattern `TokenStore.fromPrevious` already uses.

## Design

### 1. Data type: `ScrapedData`

New file `src/Bolt.Models/BoltApi/ScrapedData.fs`:

```fsharp
type ScrapedData = {
    Profile: DriverProfile
    ActivityHours: ActivityHours
    OrderHistory: OrderHistory   // the exact type getOrderHistory returns today
    PreviousOrders: PreviousOrder[]
    PastOrderDetails: PastOrderDetail[]
}
```

It lives in **Bolt.Models** because Bolt.ETL does not reference Bolt.Scraper; Models is the shared layer both producer (Scraper) and consumer (ETL) already reference.

### 2. Scrape pipeline (`ScrapePipeline.fs`)

- `scrapeRides` returns `Task<Result<ScrapedData, string>>`. The body collects typed values with `let!` instead of tee-saving through `|>!`.
- After the record is built, an `#if DEBUG` block persists all five repositories plus `ScrapeMetadataRepository.save`. Release builds write no user data to disk.
- The progress callback becomes `report: ScrapeProgress -> Task`; call sites use `do! report (...)` (fixes FIXME 4 at the source).
- `ensureMeteoCoverage` changes signature to:

  ```fsharp
  (rides: PreviousOrder[]) -> CancellationToken -> Task<Result<DateTimeOffset, string>>
  ```

  Rides arrive as an argument (no repository read). On success it returns the capped ride maximum: `min (max rideDates) (now − 5 days)`. The meteo file itself stays on disk — it is shared Kraków weather, not user data. The empty-rides check moves to the job runner (section 4).

### 3. Analysis (`Bolt.ETL`)

- `AnalysisPipeline.run (data: ScrapedData) (weatherCap: DateTimeOffset) : Result<AnalysisReport, string>` — no repository reads for user data.
- Weather-cap filtering: rides with `Created <= weatherCap` feed **PerRide1** and **PerRide2** (the meteo-dependent sections). Their `PastOrderDetail` records are matched by order identity (the same order handle/id used during scraping, where `previous[i]` and `details[i]` come from the same handle). **RideClustering** receives the full ride set (it has no meteo dependency).
- `RideCount` and `DateRange` in the report are computed from the full set, so totals stay honest.
- `prepareRideAnalysisSource` in all three analysis modules takes `(previousRides: PreviousOrder[], pastOrders: PastOrderDetail[])` instead of `email`. Meteo and Districts are still loaded from their repositories inside (shared, non-user data).
- `Bolt.Reporter` is untouched; it keeps reading repositories and therefore only works on DEBUG-produced data.

### 4. Job runner and websocket (`Bolt.Web`)

`PipelineDeps` becomes generic over the data type as well, so the state machine stays testable without scraper types:

```fsharp
type PipelineDeps<'session, 'data> = {
    LoadCached: string -> 'data option          // Some only in DEBUG when cache is fresh; replaces IsFresh
    CreateSession: string -> 'session
    HasTokens: 'session -> bool
    RefreshTokens: 'session -> CancellationToken -> Task<Result<unit, string>>
    RequestMagicLink: 'session -> CancellationToken -> Task<Result<unit, string>>
    AuthenticateWithUrl: 'session -> string -> CancellationToken -> Task<Result<unit, string>>
    ScrapeRides: 'session -> (string -> Task) -> CancellationToken -> Task<Result<'data, string>>
    EnsureMeteo: 'data -> CancellationToken -> Task<Result<DateTimeOffset, string>>
    RunAnalysis: 'data -> DateTimeOffset -> Task<Result<AnalysisReport, string>>
    RideCountOf: 'data -> int                   // empty-rides guard while keeping 'data opaque
}
```

`JobRunner.run` flow:

1. `notify CheckingCache`; `deps.LoadCached email` — `Some data` skips authentication and scraping entirely (DEBUG-only path).
2. Otherwise authenticate: token refresh, then request magic link, then the channel read loop. The `initialMagicLink` parameter and its handling block are **deleted** (FIXME 3).
3. Scrape to get `data`. If `RideCountOf data = 0`, fail as `Failed("scraping rides", "No rides found for this account")` — early and with honest step attribution (today the empty-rides error surfaces from the weather step).
4. `EnsureMeteo data` yields the weather cap; an error becomes `Failed("fetching weather data", …)`.
5. `RunAnalysis data cap` yields `Done report` or `Failed("analysis", …)`.

Progress bridging becomes `let progress detail = notify (ScrapingRides detail)` — no blocking (FIXME 4).

`WebSockets.fs` (FIXME 3): a `MagicLink` message with no running job sends
`Views.errorFragment email "authentication" "Start the analysis with your e-mail address first."` and does **not** start a job. `startJob` loses its `initialUrl` parameter. `ClientMessage` keeps its shape — the e-mail field is still needed for the error fragment.

`Pipeline.realDeps`: `LoadCached` is `#if DEBUG` a freshness check plus a load of all five repositories (if any is missing, return `None`), `#else` `fun _ -> None`. `freshnessWindow` becomes a DEBUG-only detail.

### 5. Error handling

The existing pattern is unchanged: `Result<_, string>` everywhere, the `failure` tuple in the runner, terminal `Failed(step, message)` notification, cancellation as `OperationCanceledException` swallowed silently.

New edge case: if every ride is newer than the weather cap, the weather sections receive zero rides. The prepare/build functions must produce an empty section rather than throw. `MeteoCoverage.missingRanges` keeps its existing inverted-range guard (`from < to'`).

### 6. Testing

- **JobRunner.Tests**: rewrite fake deps to the new record shape. New cases: magic-link-as-first-message flow no longer exists; a `LoadCached` hit skips scraping; zero rides fails at the scraping step; the cap threads from `EnsureMeteo` into `RunAnalysis`.
- **WebSocket.Tests**: magic-link first message yields an error fragment and starts no job.
- **Scraper.Tests**: `ensureMeteoCoverage` takes a rides argument and returns the cap. `MeteoCoverage.missingRanges` tests are untouched.
- **ETL.Tests**: cap filtering — a ride after the cap is excluded from PerRide sections but included in clustering and report totals.
- Existing `ScrapeMetadata` tests stay; the freshness cache is still a real DEBUG feature.

## Out of scope

- Changing `Bolt.Reporter` (DEBUG-data tool).
- Meteo or Districts storage (shared, non-user data — stays on disk).
- Any change to the analysis algorithms themselves.
