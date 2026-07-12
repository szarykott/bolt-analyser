# Python Analytics HTTP Service + F# Client

## Context

`python-analytics/` holds three ad-hoc scripts (OLS regression, multicollinearity "mirror check", ST-DBSCAN space-time clustering) that read CSVs exported by `Bolt.ETL` from `~/.config/.bolt-app/`. Goal: expose all three as a generic FastAPI statistical service; `Bolt.ETL` POSTs JSON data, receives structured JSON results, persists them. Decisions made in interview: all three capabilities; JSON records in POST body; structured-JSON-only responses (no text summaries, no plots — PNG plotting stays a CLI concern); generic endpoints (no ride-specific column names); FastAPI + pydantic; uv + pyproject.toml; shared core package with thin FastAPI and CLI layers; ETL calls service and saves results, existing CSV exports stay; **no docker in this task's scope** — local uvicorn, base URL via env var `BOLT_ANALYTICS_URL` (default `http://localhost:8000`); fail fast on service errors.

## Part 1 — Python restructure (`python-analytics/`)

```
python-analytics/
  pyproject.toml            # uv-managed; deletes ad-hoc .venv workflow
  analytics/
    core/
      clustering.py         # st_distance_matrix, circular_mean_hour moved VERBATIM (parity) + run_st_dbscan() -> labels, per-cluster stats (id, size, centroid, circular-mean hour), n_noise
      regression.py         # run_ols(df, target, drop_columns, categorical_columns, standardize) -> coefficients (coef/std_err/t/p/CI), R², adj R², F, n — from model attrs, not summary() text
      diagnostics.py        # run_mirror_check(df, target_columns, group_by/group_value) -> numeric corr, one-hot corr, VIF, optional group means
      frames.py             # list[dict] rows -> DataFrame; JSON number→numeric, string→categorical, bool→categorical ("True"/"False", matches current CSV path); optional categorical_columns override
    api/
      models.py             # pydantic request/response models; row value union str | bool | float | None (smart mode so false ≠ 0.0)
      main.py               # FastAPI app: 3 POST routes + GET /health; core ValueError -> HTTP 400
    cli/
      st_dbscan.py          # keeps argparse/load/summarize/plot (matplotlib stays CLI-ONLY, never imported by api)
      linear_regression.py
      mirror_check.py
  README.md                 # how to run service: uv sync; uv run uvicorn analytics.api.main:app --port 8000; endpoint list + sample curl; CLI usage; BOLT_ANALYTICS_URL note
  # delete old hyphenated scripts + .venv (uv sync recreates .venv)
```

- pyproject: fastapi, uvicorn[standard], pydantic, pandas, numpy, scikit-learn, statsmodels, matplotlib; `[project.scripts]` entries for the three CLIs; hatchling build. Commit `uv.lock`, gitignore `.venv`.
- **NaN/inf sanitization**: VIF is often `inf`, corr can be `NaN` — JSON can't carry them. Core replaces non-finite floats with `None`.

## Part 2 — API contract

- `POST /cluster/st-dbscan`: `{points: [{latitude, longitude, hour}], eps_km=0.5, eps_hours=0.5, min_samples=5}` → `{labels (index-aligned, -1 noise), n_points, n_clusters, n_noise, clusters: [{id, size, centroid_latitude, centroid_longitude, mean_hour}]}`. `hour` = fractional hour-of-day (F# sends `Hour + Minute/60.0` — service stays parsing-free).
- `POST /regression/ols`: `{rows, target, drop_columns=[], categorical_columns=null, standardize=true}` → `{n_observations, r_squared, adj_r_squared, f_statistic, f_pvalue, coefficients: [{name, coef, std_err, t_value, p_value, ci_low, ci_high}]}`.
- `POST /diagnostics/mirror-check`: `{rows, target_columns, group_means: {by, value} | null}` → `{numeric_correlation: {columns, matrix}, encoded_correlation: {...}, vif: [{feature, vif}] desc-sorted, group_means: [{group, mean}] | null}`. Matrix cells and vif are `float | null`.
- `GET /health` → `{"status": "ok"}`.

## Part 3 — F# changes

New files in `src/Bolt.ETL/Analytics/` (client stays ETL-local; Infrastructure stays HTTP-free), added to `Bolt.ETL.fsproj` **before** Analysis files:

- **`AnalyticsContracts.fs`** — DTO records with `[<JsonPropertyName>]` for snake_case (FSharp.SystemTextJson honors these; only `[<JsonConverter>]` on fields is ignored per `Serialization.fs:86` comment). Request rows: `Map<string, obj>` — boxed `float`/`bool`/`string` serialize by runtime type, culture-safe. Nullable response floats: `float option` (`deserializeNullAsNone = true` already set).
- **`AnalyticsClient.fs`** — static `HttpClient`; base URL from `BOLT_ANALYTICS_URL` env var, default `http://localhost:8000`; `post<'req,'resp>` using existing `Bolt.Infrastrucutre.Serialization.Json` (note typo'd namespace); `EnsureSuccessStatusCode()` = fail fast. Exposes `stDbscan`, `olsRegression`, `mirrorCheck`.

Hooks (existing CSV exports untouched):

- **`RideClustering.fs`** — `runRemoteClustering`: point = latitude/longitude + `float Time.Hour + float Time.Minute / 60.0`; CLI-default params. Persist via existing storage (`Storage.fs:13,44`): `JsonStorage.write "rideClustering.result.json"`, `CsvStorage.write "rideClusteringDataSource-clustered.csv"` (3 cols + cluster label, zip with labels), `CsvStorage.write "rideClusters.csv"` (cluster;size;centroid_latitude;centroid_longitude;mean_hour, InvariantCulture).
- **`PerRide2.fs`** — `toAnalyticsRows : RidesDataSource -> Map<string, obj> array` keyed by exact CSV header names (`PerRide2.fs:110-119`); unwrap before boxing: `float r.Distance` (strips `float<km>`), `float r.PricePln` (decimal), bools as `bool`, `r.Temperature.ToString()`. Then `runRemoteRegression` (target `price_pln` → `perRide2.olsRegression.json` + `perRide2.olsCoefficients.csv`) and `runRemoteMirrorCheck` (targets `["price_pln"]`, group_means `pickup_district`/`distance_km` → `perRide2.mirrorCheck.json`).
- **`Program.fs`** — bind prepared sources once, reuse for CSV export + remote calls.

## Implementation order

1. pyproject + package skeleton; `uv sync`; move core logic; rewire CLIs; delete old scripts.
2. pydantic models + FastAPI app; uvicorn up; curl all endpoints; write `python-analytics/README.md` (setup via uv, server run command, endpoints + sample curl payloads, CLI entry points, `BOLT_ANALYTICS_URL`).
3. F# contracts + client; fsproj wiring; `dotnet build`.
4. `runRemote*` + Program.fs; end-to-end run.

## Verification

1. **Baseline BEFORE refactor**: run existing `.venv/bin/python` scripts on real CSVs (`~/.config/.bolt-app/ridesDataSource2.csv`, `rideClusteringDataSource.csv`), save console output to scratchpad.
2. After refactor: rerun CLIs via `uv run` — diff coefficients/cluster summaries against baseline; PNG still produced.
3. Service parity: throwaway scratchpad script converts real CSVs to JSON, POSTs; compare OLS coef/std-err/p/R² and cluster centroids/mean hours to baseline (~1e-9).
4. `uv run uvicorn analytics.api.main:app --port 8000` + `dotnet run --project src/Bolt.ETL`: verify new files appear in `~/.config/.bolt-app/` (`rideClustering.result.json`, `rideClusteringDataSource-clustered.csv`, `rideClusters.csv`, `perRide2.olsRegression.json`, `perRide2.olsCoefficients.csv`, `perRide2.mirrorCheck.json`) and all pre-existing CSVs still written.
5. Kill uvicorn, rerun ETL → nonzero exit (fail-fast confirmed).

## Gotchas baked in

- JSON forbids NaN/Infinity → core sanitizes to null → F# `float option`.
- pydantic union must keep `bool` before/smart vs `float` so `false` ≠ `0.0`.
- `Map<string, obj>` JSON path avoids culture bugs (current CSV `Distance.ToString()` is culture-sensitive).
- matplotlib import confined to `analytics/cli/`.
