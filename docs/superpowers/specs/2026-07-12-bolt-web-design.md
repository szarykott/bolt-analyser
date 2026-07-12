# Bolt.Web — Interactive Analysis Web Server

**Date:** 2026-07-12
**Status:** Approved design (rev 2 — security & session model)

## Goal

Turn the F# side of the application into a web server that serves ride-analysis
results as an interactive web page. A user enters their Bolt driver email,
the backend scrapes their ride data (reusing cached data when fresh), fills in
missing meteo data, runs all analyses from `src/Bolt.ETL/Analysis`, and renders
an HTML report. The user can view the report in the browser and download it as
a standalone interactive HTML file.

## Decisions Made

| Topic | Decision |
|---|---|
| Bolt authentication | Stateless magic-link endpoint: whenever it is hit with a magic-link URL, the server attempts login with it — no server-side "pending auth" session required |
| Token cache | Tokens are cached to disk **only when the `DEBUG` compile flag is set**. In Release builds tokens live in process memory only (disk caching of tokens is a security risk) |
| Storage | Per-email subfolders: `~/.config/.bolt-app/<email>/…` plus `metadata.json` with scrape timestamp. Meteo and districts stay shared (not per-email) |
| Analysis output | Structured results: Plotly charts as figure JSON (rendered client-side) plus HTML tables |
| Progress UX | HTMX polling with a server-generated random GUID poll key; poll replies require exact match of poll key + client IP + User-Agent |
| Session lifetime | Job entry evicted 1 minute after analysis finishes OR immediately after the frontend successfully pulls the finished report, whichever comes first |
| HTTPS | Requests are redirected to HTTPS unless the `DEBUG` compile flag is set |
| Python service | Runs separately (started by the user); F# calls its URL from `appsettings.json` |
| Downloaded report | Standalone HTML assembled client-side from the delivered report (plotly.js and htmx from CDN; needs internet to view) |
| Web framework | Plain ASP.NET Core minimal APIs on Kestrel, in F#. No Giraffe or other third-party web framework |
| Pipeline placement | Scraping pipeline lives in Bolt.Scraper; analysis pipeline lives in Bolt.ETL; both are **pure libraries** — their console entry points are removed |

## Architecture

```
Bolt.Web (new, F# ASP.NET Core minimal APIs on Kestrel)
  ├─ HTTP routes + HTMX fragments + report rendering (Views module)
  ├─ JobRegistry (in-memory, keyed by poll-key GUID)
  └─ references ↓
Bolt.Scraper (pure library — Program.fs removed)
  └─ ScrapePipeline module: auth (magic-link exchange), rides, meteo gap-fill
Bolt.ETL (pure library — Program.fs removed)
  └─ AnalysisPipeline module: runs PerRide1, PerRide2, RideClustering → AnalysisReport
Bolt.Infrastructure
  └─ repositories parameterized by email profile (subfolder per email)
python-analytics: unchanged, runs separately, base URL from appsettings
```

- Bolt.Scraper and Bolt.ETL become pure libraries: `OutputType` library, their
  `Program.fs` console entry points are deleted. Bolt.Web is the only
  executable on the F# side.
- **Districts (Kraków geo data):** loaded at Bolt.Web startup — from
  `krakowDistricts.json` if present, otherwise scraped via `getKrakowDistricts`,
  saved to disk, and kept in memory. Shared across users.
- **Meteo:** shared `krakowMeteoData.json` (weather is the same for all users).
  The pipeline computes the ride date range, checks coverage, fetches only the
  missing ranges, merges, and saves. Writes are guarded by a simple lock.
- **HTML generation:** no template engine. A small `Views` module in Bolt.Web
  builds HTML with interpolated strings. The same functions produce HTMX
  fragments and the report.

## Pipeline & Job State

Jobs live in an in-memory registry (`ConcurrentDictionary<PollKey, Job>`).

**Job record:**

```fsharp
type Job = {
    PollKey: Guid              // generated server-side with RandomNumberGenerator
    Email: string
    ClientIp: string           // captured at job creation
    UserAgent: string          // captured at job creation
    State: JobState            // mutable/observable progress state
    Tokens: TokenStore option  // in-memory only in Release builds
    FinishedAt: DateTimeOffset option
}
```

**States:**

```
Requested
 → CheckingCache        data exists and scraped < 14 days ago? → skip to RunningAnalysis
 → Authenticating       DEBUG builds: disk token cache tried first; Release: memory only
 → AwaitingMagicLink    page shows paste-URL form; hitting the magic-link endpoint resumes
 → ScrapingRides        profile, activity hours, order history, per-order details
 → FetchingMeteo        compute ride date range, fill missing coverage
 → RunningAnalysis      PerRide1, PerRide2 (+ remote OLS/mirror-check),
                        RideClustering (+ remote ST-DBSCAN)
 → Done(AnalysisReport) | Failed(step, error)
```

- **Stateless magic-link handling:** the magic-link endpoint does not depend on
  any pending server-side auth session. Whenever it is hit with an email, a
  magic-link URL, and a valid poll key, the server attempts the token exchange
  with that URL and, on success, stores the resulting tokens on the job and
  resumes (or starts) the scrape. There is no check that "an email was sent
  previously".
- **Token caching:** `TokenStore` disk persistence (`fromPrevious` / save) is
  compiled in only under `#if DEBUG`. Release builds never write tokens to disk
  and never read a cached token file; tokens exist only inside the `Job` record.
- **Freshness:** `metadata.json` in the per-email folder records `scrapedAt`.
  A cache hit skips only scraping and meteo; analyses always re-run (they are
  cheap and this keeps the report path single-track).
- **Progress detail:** state enum plus optional counters
  (e.g., "order details 120/450") surfaced through the status endpoint.
- **Failure:** any step error → `Failed` with step name and message shown on
  the page; retry starts a new job (new poll key).
- **Concurrency:** poll keys are unique per client, and poll replies are bound
  to the originating IP + User-Agent, so a second browser cannot attach to a
  running job. A new request for an email whose job is already running is
  rejected with an "analysis already in progress, try again later" fragment.
- **Eviction:**
  - `Done` jobs: removed 1 minute after `FinishedAt`, or immediately after the
    frontend successfully pulls the finished report — whichever comes first.
  - `AwaitingMagicLink` jobs: removed after a 10-minute timeout.
  - `Failed` jobs: removed 1 minute after failure (enough for the client to
    render the error).
  - A background sweep (e.g., `PeriodicTimer`) enforces time-based eviction.

## Security Model

- **HTTPS:** `UseHttpsRedirection` (and HSTS) are enabled unless the `DEBUG`
  compile flag is set; local development runs plain HTTP.
- **Poll-key binding:** every status/magic-link/report request must present the
  poll key, and the request's IP and User-Agent must exactly match the values
  captured when the job was created. Any mismatch → 404 (indistinguishable
  from an unknown key; no information leak about live jobs).
- **Honest limits:** IP + User-Agent matching is defense-in-depth, not a
  security boundary — clients behind the same NAT share an IP and User-Agent
  strings are trivially copied. The unguessable poll key carries the actual
  protection, so it is generated with a cryptographically secure RNG and never
  logged.
- **No disk token cache in Release** (see above): a server restart drops all
  tokens and users must re-authenticate via magic link. Intended trade-off.

## Analysis Results & Report

Result types live in Bolt.ETL and are shared across the pipeline:

```fsharp
type ResultTable = { Title: string; Headers: string list; Rows: string list list }
type ResultChart = { Title: string; PlotlyFigureJson: string }   // Plotly.NET GenericChart → figure JSON
type AnalysisSection = { Id: string; Title: string; Description: string
                         Charts: ResultChart list; Tables: ResultTable list }
type AnalysisReport = { Email: string; GeneratedAt: DateTimeOffset
                        RideCount: int; DateRange: DateTimeOffset * DateTimeOffset
                        Sections: AnalysisSection list }
```

**Sections produced:**

- **PerRide1** → descriptive statistics tables: ride counts and price/km broken
  down by district, part of day, and weather buckets.
- **PerRide2** → OLS regression table (feature, coefficient, std error,
  p-value), model statistics (R² and friends), mirror-check diagnostics table.
- **RideClustering** → the existing cluster map (`ClusterMap` layers) as a
  chart, plus a cluster summary table (centroids, sizes).

**Rendering:**

- Charts are embedded as Plotly figure JSON in
  `<script type="application/json">` blocks and initialized client-side with
  `Plotly.newPlot`. plotly.js and htmx come from CDN.
- The finished report is delivered **in the final successful poll response**
  (which also evicts the job). The report fragment contains everything needed
  to render and to download.
- **Download:** a small JS helper assembles a standalone HTML file client-side
  from the already-delivered report (embedded figure JSON + CDN script tags)
  and offers it as a Blob download. No server round-trip, so download keeps
  working after the job entry is evicted.
- CSV and HTML file writing are removed along with the console entry points.
  The web path keeps results in memory only.

## HTTP Routes & Frontend

```
GET  /                              full page: email form ("Make an analysis for me")
POST /analyze              {email}  create job, generate poll key, capture IP+UA
                                    → progress-panel fragment carrying the poll key
GET  /jobs/{pollKey}/status         poll key + IP + UA must match, else 404. Fragment by state:
                                      running  → step + counters, keeps polling (every 2 s)
                                      awaiting → magic-link paste form (polling stops)
                                      failed   → error + retry button
                                      done     → full report fragment (job evicted after send)
POST /magic-link  {email, url, pollKey}
                                    stateless: attempt token exchange with url,
                                    on success resume job → progress fragment
GET  /health                        ok + python service reachability
```

- HTMX only; custom JavaScript limited to a plotly-init helper and the
  client-side report download helper.
- Polling uses `hx-trigger="every 2s"`; the server stops polling by returning a
  fragment without the trigger once the state is terminal or awaiting input.
- Error surfacing: Python service down → analysis step fails with a clear
  message; Bolt API errors during scrape → Failed at ScrapingRides; invalid
  magic link → stays in AwaitingMagicLink with an error note.

## Testing

- **Pipeline state machine:** unit tests with a fake BoltClient and fake meteo
  source — states progress in order, stateless magic-link resume works, the
  freshness check short-circuits.
- **Job registry:** poll-key/IP/UA mismatch → 404; eviction after pull; eviction
  after 1-minute timeout; awaiting-magic-link timeout.
- **Analysis mapping:** result-type construction tested against canned Python
  service JSON responses (regression, mirror check, clustering).
- **Web layer:** `WebApplicationFactory` integration tests — each route returns
  the expected fragment for each job state; HTTPS redirect active in Release
  configuration.

## Out of Scope (v1)

- User authentication/sessions on the web page beyond the poll-key binding.
- Persisting job state across server restarts (in-memory registry only).
- Managing the Python service lifecycle from F#.
- Fully offline downloadable report (CDN version only).