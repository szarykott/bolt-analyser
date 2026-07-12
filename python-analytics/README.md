# python-analytics

Generic statistical service + CLIs: OLS regression, multicollinearity "mirror
check", ST-DBSCAN space-time clustering. Shared core (`analytics/core`), thin
FastAPI layer (`analytics/api`), thin CLI layer (`analytics/cli`). Plotting
(matplotlib) is CLI-only — the service returns structured JSON.

## Setup

```sh
uv sync
```

## Run the service

```sh
uv run uvicorn analytics.api.main:app --port 8000
```

`Bolt.ETL` reads the base URL from the `BOLT_ANALYTICS_URL` env var
(default `http://localhost:8000`).

## Endpoints

- `GET /health` → `{"status": "ok"}`
- `POST /cluster/st-dbscan` — points with `latitude`, `longitude`, `hour`
  (fractional hour-of-day); optional `eps_km` (0.5), `eps_hours` (0.5),
  `min_samples` (5). Returns index-aligned `labels` (-1 = noise) and
  per-cluster stats.
- `POST /regression/ols` — `rows` (list of records), `target`; optional
  `drop_columns`, `categorical_columns`, `standardize` (true). String columns
  are one-hot encoded (drop-first), numeric features standardized. Returns
  fit stats + per-coefficient estimates with CIs.
- `POST /diagnostics/mirror-check` — `rows`, `target_columns`; optional
  `group_means: {by, value}`. Returns numeric and one-hot-encoded correlation
  matrices, VIF (descending), optional group means.

Non-finite results (VIF `inf`, correlation `NaN`) come back as `null`.
Row values keep JSON types: numbers → numeric, strings → categorical,
booleans → 0/1 indicator columns.

Sample:

```sh
curl -s http://localhost:8000/regression/ols \
  -H 'Content-Type: application/json' \
  -d '{"rows": [{"y": 1.0, "x": 2.0, "flag": true, "kind": "a"},
                {"y": 2.0, "x": 4.1, "flag": false, "kind": "b"},
                {"y": 3.0, "x": 6.2, "flag": true, "kind": "a"},
                {"y": 4.0, "x": 7.9, "flag": false, "kind": "b"}],
       "target": "y"}'

curl -s http://localhost:8000/cluster/st-dbscan \
  -H 'Content-Type: application/json' \
  -d '{"points": [{"latitude": 50.06, "longitude": 19.94, "hour": 7.5}],
       "min_samples": 1}'

curl -s http://localhost:8000/diagnostics/mirror-check \
  -H 'Content-Type: application/json' \
  -d '{"rows": [{"y": 1.0, "x": 2.0, "d": "a"}, {"y": 2.0, "x": 4.0, "d": "b"}],
       "target_columns": ["y"],
       "group_means": {"by": "d", "value": "x"}}'
```

## CLIs

```sh
uv run st-dbscan ~/.config/.bolt-app/rideClusteringDataSource.csv  # writes -clustered.csv + -clusters.png
uv run linear-regression [csv] [--target price_pln] [--drop col ...] [--no-standardize]
uv run mirror-check [csv] [--targets price_pln] [--group-by pickup_district] [--group-value distance_km]
```

CSV defaults point at `~/.config/.bolt-app/ridesDataSource2.csv`; delimiter is `;`.
