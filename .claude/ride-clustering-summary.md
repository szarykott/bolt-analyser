# Ride Clustering — Summary (2026-07-09)

## What was built

Space-time clustering of ride pickups, two parts:

1. **`src/Bolt.ETL/Analysis/RideClustering.fs`** — ETL step exporting `rideClusteringDataSource.csv` (`latitude;longitude;time`, `HH:MM`). Modeled on `PerRide2.fs`: pickup from `Route.Stops[0].Location`, hour from `Times.CreatedTimestamp`. Wired into `Bolt.ETL.fsproj` and `Program.fs`.
2. **`python-analytics/st-dbscan-clustering.py`** — ST-DBSCAN clustering CLI. Reads the CSV, writes `<stem>-clustered.csv` (`cluster` column, −1 = noise), `<stem>-clusters.png` map, console summary with per-cluster centroid + circular-mean hour.

New deps in `python-analytics/.venv`: `scikit-learn`, `matplotlib`.

## Key decisions & why

| Decision | Why |
|---|---|
| ST-DBSCAN (two radii: `--eps-km`, `--eps-hours`) | Space (degrees) and time (hours) have incompatible units; one distance metric mixing them would be arbitrary. Density-based → no cluster count guess, noise handled. |
| Cyclic temporal distance `min(\|Δt\|, 24−\|Δt\|)` | Time is hour-of-day only; 23:50 and 00:10 are 20 min apart, not 23.7 h. Verified: midnight-straddling cluster comes out as one. |
| sklearn `DBSCAN(metric='precomputed')`, not `st_dbscan` package | Package is euclidean-only, can't wrap time or use haversine. Dense O(n²) matrix — fine < ~30k rows. |
| Sentinel `1e12` km instead of `inf` for out-of-time-window pairs | sklearn rejects non-finite matrix entries. |
| `InvariantCulture` formatting in F# | Polish locale emits comma decimals — would break Python parse. |
| Semicolon delimiter, CLI args with defaults (0.5 km / 0.5 h / 5 pts) | Matches existing bolt-app exports; clustering always needs eps sweeps. |

## Verification done

- `dotnet build` clean; full ETL run produced 368-row CSV, correct format.
- Synthetic test (3 planted clusters + noise, one straddling midnight): all recovered exactly, noise flagged, cyclic time proven.
- Edge cases: missing column → clear error exit 1; malformed rows → warned + dropped.
- Bug found & fixed during verify: circular-mean printed `23:60` (minute rounding overflow).

## Open items

- **Defaults too tight for real data**: 0.5 km/0.5 h → 98.6% noise; 1.5/1.5 → one 307-pt blob. Sweet spot needs sweep, try around `--eps-km 0.7 --eps-hours 1.0 --min-samples 4`.
- **Plot shows space only** — time appears in console summary, not PNG. User asked why; adding temporal panel (e.g. 24h polar clock next to map) discussed but not decided.

## Usage

```
python-analytics/.venv/bin/python python-analytics/st-dbscan-clustering.py \
  ~/.config/.bolt-app/rideClusteringDataSource.csv --eps-km 0.7 --eps-hours 1.0
```
