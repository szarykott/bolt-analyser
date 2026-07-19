# Per-Hour Earnings Analysis — Implementation Design

Implementation spec for the per-hour earnings regression described in
`.claude/per-hour-earnings-design.md` (the modeling design: dataset shape,
tier ladder, promotion gates, pitfalls). That document remains the modeling
authority; this spec records the implementation decisions made on top of it
(2026-07-19) and resolves its open items.

## Resolved open items

| Open item | Decision |
|---|---|
| Inter-ride idle gaps | Count as worked time, **no cap** within an hour. A clock hour with no ride starting or in progress still gets no row (dataset definition), so gap minutes falling into a fully idle hour drop out. |
| Worked span per ride | `AcceptedTimestamp → RideEnd` — drive-to-pickup is unpaid work; excluding it would inflate the rate. |
| Daylight flag | **Fixed hours, shift semantics, not sunlight**: `noc` = hour ∈ 18:00–6:00, baseline day = 6:00–18:00. No sunrise/sunset data needed. |
| WLS support | Separate `/regression/wls` endpoint in the Python service. `weights` (column name) is **required** there; missing weights or a missing column is a validation error. The existing `/regression/ols` endpoint and `run_ols` are untouched — no branching. |
| Idle-minute district attribution | Gap minutes → district of the *next* pickup (where the driver positioned). Ride-span minutes → district of that ride's pickup (`Stops[0]`). |
| Teasers for locked rungs | **No computed unlock hours.** Static, plain-language note only (see Display). |

## Components

### 1. `src/Bolt.ETL/Analysis/PerHour.fs` (new)

Sibling of `PerRide2.fs`. Contains dataset construction, the ladder engine,
and section building.

**Dataset construction**

- Input: the same weather-eligible `FinishedRide[]` as PerRide2, plus the
  meteo and district providers.
- Build one worked span per ride (`Accepted → RideEnd`); union overlapping
  spans. Gaps between consecutive spans count as worked, attributed to the
  district of the next pickup.
- Split into clock-hour rows. A row exists only for hours with a ride
  starting or in progress. Per row:
  - `workedMinutes` = span minutes + counted gap minutes in the hour;
    `fill = workedMinutes / 60`
  - earnings = Σ over rides of
    `earned × (ride-span minutes in this hour ÷ total ride-span minutes)`
  - target `rate = earnings ÷ fill` (zł per worked hour); WLS weight = `fill`
  - district shares per named district (17 + `unknown`), summing to 1.0
  - weather from the nearest hourly data point: `deszcz`, `śnieg` bools,
    6-way `TemperatureBucket`
  - `noc` (18–6), `weekend` and `godziny_szczytu` reusing PerRide2's
    `isWeekend` / `isRushHour` evaluated at hour start

**Ladder engine (pure F#)**

Rung definitions are data: each rung is a list of column specs; a spec
computes its column value from a row and its exposure (`Σ fill` for dummies,
`Σ share·fill` for shares). Rungs per the modeling doc:

| Rung | Columns | ~Coefs (incl. intercept) |
|---|---|---|
| 0 | none — weighted grouped-means table, weekend × dzień/noc | — |
| 1 | `weekend`, `godziny_szczytu`, `zła_pogoda` (deszcz ∪ śnieg) | 4 |
| 2 | + `noc`, `≤0°C LUB >30°C`, `poza_centrum` | 7 |
| 3 | + `0–18°C` (baseline shrinks to 18–30°C), `zła_pogoda` → `deszcz`/`śnieg` | 9 |
| 4 | + `0–10°C`, `10–18°C`, `25–30°C`, `≤0°C`, `>30°C` (baseline 18–25°C); named district shares peel out of `inne_dzielnice` per district | 12 + unlocked districts |

Promotion gates (both ex-ante, from this driver's rows):

1. Global budget: `n ≥ 15 × candidateCoefCount`, where the candidate count
   depends on which districts pass the floor.
2. Per-column exposure floor: ≥ 20 effective hours. A failing column stays
   merged in its parent (districts fold into `inne_dzielnice`; never blocks
   the rung).

Every unlocked rung is regressed on every run (one WLS call per rung).
Rung 0 is computed directly in F# (weighted means need no service). Locked
state carries no numbers — display uses static copy only.

**Display / section building**

One `AnalysisSection` (id `per-hour-earnings`, title „Zarobki na godzinę
pracy"), appended after PerRide2's section. Top to bottom:

1. Rung-0 table, always: weighted mean zł/h for weekend × dzień/noc plus row
   count. Stays permanently, complementary to the regressions.
2. One coefficients table per unlocked rung (highest last), same shape as
   `PerRide2.coefficientsTable`: significant coefficients sorted by |coef|
   with CI and p; insignificant ones listed in a note; p ≥ 0.05 display
   filter unchanged. Model-stats table only for the highest unlocked rung.
3. Static locked-features note (no computed hours), e.g. „Niektóre analizy
   (np. wpływ poszczególnych dzielnic albo dokładnych zakresów temperatury)
   nie są jeszcze pokazywane — mamy na razie za mało godzin jazdy, żeby
   policzyć je uczciwie. Odblokują się same, gdy przybędzie danych." When
   districts are folded: „część dzielnic zliczona razem jako «inne
   dzielnice» — za mało godzin w każdej z osobna."
4. Permanent legend note explaining cross-rung shifts (no state tracking):
   „liczby w wyższych tabelach mogą się różnić od niższych — dokładniejszy
   model oddziela efekty, które prostszy liczył razem."

All copy passes the five-year-old test; no statistics vocabulary. Each rung
shows only its own numbers — never historical side-by-side.

### 2. Analytics contract + client

- `AnalyticsContracts.fs`: new `WlsRequest` — same fields as `OlsRequest`
  plus required `[<JsonPropertyName "weights">] Weights: string` (name of
  the weights column inside `rows`). `OlsRequest` unchanged.
- `AnalyticsClient.fs`: `wlsRegression : WlsRequest -> Task<OlsResponse>`
  posting to `/regression/wls`. Response shape identical to OLS.

### 3. Python analytics service

- `analytics/core/regression.py`: new `run_wls(df, target, weights, ...)`.
  `weights` is required; a missing column raises `ValueError`. The weights
  column is removed from the design matrix and passed to
  `sm.WLS(y, X, weights=w)`. Shared design-matrix preparation may be
  extracted into a private helper; `run_ols` behavior stays byte-identical.
- `analytics/api`: new pydantic request model with required `weights: str`;
  new `/regression/wls` route; missing/invalid weights → 422/400 like other
  validation errors.

### 4. Pipeline integration

`AnalysisPipeline.run` builds the per-hour source from the same
`weatherRides`, appends the section after `perRide2`. Empty-data path
mirrors the existing `emptySection` pattern. With tiny n the section still
renders: rung-0 table + locked note is the day-one answer.

**Errors:** analytics-service failures are caught by the pipeline's existing
outer try (whole report → Error). Singular matrices shouldn't occur (exposure
floor); if the service ever returns NaN → None coefficients they render as
„–" as today.

## Testing

F# (existing `tests/Bolt.ETL.Tests` conventions), pure parts only:

- hour splitting: ride spanning 2–3 hours → proportional earnings/minutes;
  gap counting including the fully-idle-hour drop; overlapping span union
- fill/rate/weight arithmetic; district shares sum to 1.0
- ladder gates: budget threshold; exposure-floor folding (centrum-only
  driver → no district columns; an under-floor district merged into
  `inne_dzielnice`)
- rung-0 weighted means
- temperature bucket → rung-column mapping (baseline chain
  `{0–30} → {18–30} → {18–25}`)

Python — tests reflect the endpoint separation:

- `/regression/wls`: weights column missing → error; request without
  `weights` field → validation error; WLS with non-uniform weights differs
  from OLS on the same data; uniform weights ≈ OLS
- existing `/regression/ols` tests unchanged (guards the no-branching claim)