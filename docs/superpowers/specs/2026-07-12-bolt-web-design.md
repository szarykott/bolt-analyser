# Bolt.Web — Interactive Analysis Web Server

**Date:** 2026-07-12
**Status:** Approved design (rev 3 — WebSocket transport)

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
| Transport | WebSocket (htmx `ws` extension). Job is bound 1:1 to the socket; progress push, magic-link submit, and report delivery all travel over the socket. No polling, no bearer token in any URL |
| Bolt authentication | Stateless magic-link handling: whenever a magic-link message arrives on the socket, the server attempts login with it — no server-side "pending auth" state required |
| Token cache | Tokens are cached to disk **only when the `DEBUG` compile flag is set**. In Release builds tokens live in process memory only (disk caching of tokens is a security risk) |
| Session lifetime | Job lives while its socket lives. On disconnect the pipeline is cancelled and the job evicted. No reconnect/resume token — a refresh mid-run means starting over (freshness cache softens the cost) |
| Storage | Per-email subfolders: `~/.config/.bolt-app/<email>/…` plus `metadata.json` with scrape timestamp. Meteo and districts stay shared (not per-email) |
| Analysis output | Structured results: Plotly charts as figure JSON (rendered client-side) plus HTML tables |
| HTTPS | Requests are redirected to HTTPS (sockets use WSS) unless the `DEBUG` compile flag is set |
| WebSocket security | `Origin` header validated on upgrade (mandatory — WebSocket handshakes bypass same-origin policy). Keepalive pings prevent proxy idle timeouts |
| Python service | Runs separately (started by the user); F# calls its URL from `appsettings.json` |
| Downloaded report | Standalone HTML assembled client-side from the delivered report (plotly.js and htmx from CDN; needs internet to view) |
| Web framework | Plain ASP.NET Core minimal APIs on Kestrel, in F#. No Giraffe or other third-party web framework |
| Pipeline placement | Scraping pipeline lives in Bolt.Scraper; analysis pipeline lives in Bolt.ETL; both are **pure libraries** — their console entry points are removed |

## Architecture

```
Bolt.Web (new, F# ASP.NET Core minimal APIs on Kestrel)
  ├─ GET / page, /ws WebSocket endpoint, /health
  ├─ Views module (HTML fragments pushed over socket)
  ├─ JobRegistry (in-memory, one job per live socket)
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
  builds HTML with interpolated strings. The same functions produce the page,
  socket-pushed fragments, and the report.

## Pipeline & Job State

Each accepted WebSocket connection owns at most one job. The registry maps
live connections to jobs and exists only for bookkeeping (same-email conflict
checks, shutdown cleanup) — clients never hold an identifier.

**Job record:**

```fsharp
type Job = {
    Email: string
    Socket: WebSocket             // the owning connection
    State: JobState               // mutable/observable progress state
    Tokens: TokenStore option     // in-memory only in Release builds
    Cancellation: CancellationTokenSource   // fired on socket close
}
```

**States:**

```
Requested
 → CheckingCache        data exists and scraped < 14 days ago? → skip to RunningAnalysis
 → Authenticating       DEBUG builds: disk token cache tried first; Release: memory only
 → AwaitingMagicLink    client shown paste-URL form; magic-link socket message resumes
 → ScrapingRides        profile, activity hours, order history, per-order details
 → FetchingMeteo        compute ride date range, fill missing coverage
 → RunningAnalysis      PerRide1, PerRide2 (+ remote OLS/mirror-check),
                        RideClustering (+ remote ST-DBSCAN)
 → Done(AnalysisReport) | Failed(step, error)
```

**Socket protocol** (all server→client payloads are HTML fragments consumed by
htmx `ws` swaps; client→server messages are htmx `ws-send` form submissions):

```
client → server:  start-analysis { email }
client → server:  magic-link     { email, url }
server → client:  progress fragment        (state + counters, on every transition)
server → client:  magic-link form fragment (on AwaitingMagicLink)
server → client:  error fragment           (on Failed; includes retry form)
server → client:  report fragment          (on Done; full report, ends the job)
```

- **Stateless magic-link handling:** a `magic-link` message triggers a token
  exchange attempt with the given URL regardless of prior state — no check that
  "an email was sent previously". On success the tokens are stored on the job
  and the pipeline resumes (or starts).
- **Token caching:** `TokenStore` disk persistence (`fromPrevious` / save) is
  compiled in only under `#if DEBUG`. Release builds never write tokens to disk
  and never read a cached token file; tokens exist only inside the `Job` record.
- **Freshness:** `metadata.json` in the per-email folder records `scrapedAt`.
  A cache hit skips only scraping and meteo; analyses always re-run. This also
  softens the no-reconnect policy: a refresh right after a completed scrape
  restarts the job but short-circuits straight to analysis.
- **Progress detail:** state transitions and counters (e.g., "order details
  120/450") are pushed as fragments the moment they change — no polling delay.
- **Failure:** any step error → `Failed` fragment with step name and message,
  plus a retry form; retry starts a fresh job on the same socket.
- **Disconnect:** socket close or ping timeout → `Cancellation` fired,
  pipeline stops at the next checkpoint, job evicted. No resume.
- **Concurrency:** one job per socket. A `start-analysis` for an email whose
  job is already running on another socket is rejected with an "analysis
  already in progress, try again later" fragment. Different emails run in
  parallel; per-email files are isolated, shared meteo writes are locked.
- **Keepalive:** server pings on an interval (e.g., 30 s); missed pongs count
  as disconnect. Prevents proxy idle timeouts during long scrapes.

## Security Model

- **HTTPS/WSS:** `UseHttpsRedirection` (and HSTS) are enabled unless the
  `DEBUG` compile flag is set; local development runs plain HTTP/WS.
- **No bearer tokens:** the job is identified by its connection. No job
  identifier ever appears in a URL, log, or browser history. Hijacking a job
  requires hijacking the TLS connection itself.
- **Cross-Site WebSocket Hijacking:** the `/ws` upgrade handler validates the
  `Origin` header against the server's own host and rejects mismatches.
  Mandatory, because WebSocket handshakes are not subject to the same-origin
  policy.
- **No disk token cache in Release** (see above): a server restart drops all
  tokens and users must re-authenticate via magic link. Intended trade-off.
- **Honest limits:** anyone who can connect and knows a driver's email can
  trigger scraping and view that driver's report — the magic-link requirement
  is the real gate for fresh scrapes, but a fresh-cache email is viewable
  without authentication. Acceptable for local/trusted v1 usage; noted for
  future hardening.

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
- The finished report is pushed as the final socket message; after delivery the
  job ends (socket may stay open for a new `start-analysis`).
- **Download:** a small JS helper assembles a standalone HTML file client-side
  from the already-delivered report (embedded figure JSON + CDN script tags)
  and offers it as a Blob download. No server round-trip, so download works
  regardless of job/socket state.
- CSV and HTML file writing are removed along with the console entry points.
  The web path keeps results in memory only.

## HTTP Routes & Frontend

```
GET  /         full page: email form ("Make an analysis for me"), htmx + ws extension,
               ws-connect to /ws
GET  /ws       WebSocket upgrade (Origin validated), then socket protocol above
GET  /health   ok + python service reachability
```

- HTMX with the `ws` extension; custom JavaScript limited to a plotly-init
  helper and the client-side report download helper.
- All dynamic UI (progress, magic-link form, errors, report) arrives as
  server-pushed fragments swapped by htmx — no client-side state machine.
- Error surfacing: Python service down → analysis step fails with a clear
  message; Bolt API errors during scrape → Failed at ScrapingRides; invalid
  magic link → stays in AwaitingMagicLink with an error note pushed to client.

## Testing

- **Pipeline state machine:** unit tests with a fake BoltClient and fake meteo
  source — states progress in order, stateless magic-link resume works, the
  freshness check short-circuits, cancellation stops at checkpoints.
- **Socket layer:** `WebApplicationFactory` + `WebSocketClient` integration
  tests — protocol messages produce expected fragments per state; Origin
  mismatch rejected on upgrade; disconnect cancels the pipeline; same-email
  conflict rejected.
- **Analysis mapping:** result-type construction tested against canned Python
  service JSON responses (regression, mirror check, clustering).
- **Web layer:** HTTPS redirect active in Release configuration.

## Out of Scope (v1)

- User authentication/sessions beyond socket ownership.
- Reconnect/resume of jobs after socket loss.
- Persisting job state across server restarts (in-memory registry only).
- Managing the Python service lifecycle from F#.
- Fully offline downloadable report (CDN version only).