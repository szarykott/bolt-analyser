# Bolt.Web — Interactive Analysis Web Server

**Date:** 2026-07-12
**Status:** Approved design

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
| Bolt authentication | Try saved tokens first; if missing/expired, web round-trip: page shows a "paste the tracking URL from your email" form that resumes the pipeline |
| Storage | Per-email subfolders: `~/.config/.bolt-app/<email>/…` plus `metadata.json` with scrape timestamp. Meteo and districts stay shared (not per-email) |
| Analysis output | Structured results: Plotly charts as figure JSON (rendered client-side) plus HTML tables |
| Progress UX | HTMX polling of a job-status endpoint every ~2 s |
| Python service | Runs separately (started by the user); F# calls its URL from `appsettings.json` |
| Downloaded report | Single HTML file with plotly.js and htmx loaded from CDN (small file, needs internet to view) |
| Web framework | Plain ASP.NET Core minimal APIs on Kestrel, in F#. No Giraffe or other third-party web framework |
| Pipeline placement | Scraping pipeline lives in Bolt.Scraper; analysis pipeline lives in Bolt.ETL; both exposed as library modules with progress state |

## Architecture

```
Bolt.Web (new, F# ASP.NET Core minimal APIs on Kestrel)
  ├─ HTTP routes + HTMX fragments + report rendering (Views module)
  ├─ JobRegistry (in-memory, one job per email)
  └─ references ↓
Bolt.Scraper (exe → library + thin console exe)
  └─ ScrapePipeline module: auth (saved tokens → magic-link await), rides, meteo gap-fill
Bolt.ETL (exe → library + thin console exe)
  └─ AnalysisPipeline module: runs PerRide1, PerRide2, RideClustering → AnalysisReport
Bolt.Infrastructure
  └─ repositories parameterized by email profile (subfolder per email)
python-analytics: unchanged, runs separately, base URL from appsettings
```

- Both Bolt.Scraper and Bolt.ETL keep console entry points; core logic becomes
  library-callable. The exact exe/library split is an implementation-plan detail.
- **Districts (Kraków geo data):** loaded at Bolt.Web startup — from
  `krakowDistricts.json` if present, otherwise scraped via `getKrakowDistricts`,
  saved to disk, and kept in memory. Shared across users.
- **Meteo:** shared `krakowMeteoData.json` (weather is the same for all users).
  The pipeline computes the ride date range, checks coverage, fetches only the
  missing ranges, merges, and saves. Writes are guarded by a simple lock.
- **HTML generation:** no template engine. A small `Views` module in Bolt.Web
  builds HTML with interpolated strings. The same functions produce HTMX
  fragments, the live report page, and the downloadable report.

## Pipeline & Job State

One job per email, held in an in-memory registry
(`ConcurrentDictionary<Email, Job>`). A job is a background `Task` plus
observable progress state.

**States:**

```
Requested
 → CheckingCache        data exists and scraped < 14 days ago? → skip to RunningAnalysis
 → Authenticating       saved tokens tried first
 → AwaitingMagicLink    paused; web shows paste-URL form; user submit resumes
 → ScrapingRides        profile, activity hours, order history, per-order details
 → FetchingMeteo        compute ride date range, fill missing coverage
 → RunningAnalysis      PerRide1, PerRide2 (+ remote OLS/mirror-check),
                        RideClustering (+ remote ST-DBSCAN)
 → Done(AnalysisReport) | Failed(step, error)
```

- **Magic-link pause:** the job holds a `TaskCompletionSource<string>`. The
  scrape pipeline's token callback awaits it; `POST /jobs/{email}/magic-link`
  sets the result and the pipeline continues. Timeout ~10 minutes → Failed.
- **Freshness:** `metadata.json` in the per-email folder records `scrapedAt`.
  A cache hit skips only scraping and meteo; analyses always re-run (they are
  cheap and this keeps the report path single-track).
- **Progress detail:** state enum plus optional counters
  (e.g., "order details 120/450") surfaced through the status endpoint.
- **Failure:** any step error → `Failed` with step name and message shown on
  the page; retry starts a new job replacing the old one.
- **Concurrency:** a request for an email with a running job attaches to that
  job (polls it) instead of starting a duplicate. Different emails may run in
  parallel; per-email files are isolated, shared meteo writes are locked.

## Analysis Results & Report

Result types live in Bolt.ETL and are shared by the console and web paths:

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
- The live page and the downloaded report use the same `Views` functions. The
  download endpoint returns a full standalone page with
  `Content-Disposition: attachment`, CDN scripts, and all data inline.
- CSV and HTML file writing remain only in the console `Program.fs` paths. The
  web path keeps results in memory (cached per email in the JobRegistry until
  re-run).

## HTTP Routes & Frontend

```
GET  /                          full page: email form ("Make an analysis for me")
POST /analyze          {email}  start or attach to job → progress-panel fragment (hx-get poll)
GET  /jobs/{email}/status       fragment by state:
                                  running  → step + counters, keeps polling (every 2 s)
                                  awaiting → magic-link paste form (polling stops)
                                  failed   → error + retry button
                                  done     → loads report sections
POST /jobs/{email}/magic-link   {url} resumes pipeline → progress fragment again
GET  /report/{email}            report sections fragment (or full page on direct visit)
GET  /report/{email}/download   standalone HTML file, attachment
GET  /health                    ok + python service reachability
```

- HTMX only; no custom JavaScript beyond a tiny plotly-init helper.
- Polling uses `hx-trigger="every 2s"`; the server stops polling by returning a
  fragment without the trigger once the state is terminal or awaiting input.
- The email is the job key, submitted as a plain form value. No auth or
  sessions in v1 — anyone who knows an email can view its report
  (local/trusted usage).
- Error surfacing: Python service down → analysis step fails with a clear
  message; Bolt API errors during scrape → Failed at ScrapingRides; invalid
  magic link → stays in AwaitingMagicLink with an error note.

## Testing

- **Pipeline state machine:** unit tests with a fake BoltClient and fake meteo
  source — states progress in order, magic-link pause/resume works, the
  freshness check short-circuits.
- **Analysis mapping:** result-type construction tested against canned Python
  service JSON responses (regression, mirror check, clustering).
- **Web layer:** `WebApplicationFactory` integration tests — each route returns
  the expected fragment for each job state.

## Out of Scope (v1)

- User authentication/sessions on the web page.
- Persisting job state across server restarts (in-memory registry only).
- Managing the Python service lifecycle from F#.
- Fully offline downloadable report (CDN version only).
