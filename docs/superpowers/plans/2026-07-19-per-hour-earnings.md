# Per-Hour Earnings Analysis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-hour earnings WLS regression section (tier ladder, rungs 0–4) to the analysis report, backed by a new `/regression/wls` endpoint in the Python analytics service.

**Architecture:** New F# module `PerHour.fs` (sibling of `PerRide2.fs`) builds clock-hour rows from ride time spans, runs a pure-F# ladder engine that decides which model rungs are unlocked, and calls the Python service once per unlocked rung. Python gains a separate `run_wls` + `/regression/wls` endpoint; the existing OLS path is untouched (no branching). Spec: `docs/superpowers/specs/2026-07-19-per-hour-earnings-design.md`; modeling authority: `.claude/per-hour-earnings-design.md`.

**Tech Stack:** F#/.NET 10 (xunit tests), Python 3.12+ (FastAPI, pydantic, pandas, statsmodels; pytest + httpx for tests, managed by `uv`).

## Global Constraints

- Driver-facing copy is Polish, passes the "five-year-old test" — no statistics vocabulary in copy (headers/notes explain terms like PerRide2 does).
- Column names sent to the analytics service are display-ready Polish (PerRide2 pattern) — coefficient names come back ready to render, no mapping.
- Target: `stawka_pln_h` = hour earnings ÷ fill; WLS weight column: `waga` = fill. `Standardize = false` for all WLS calls (columns are 0/1 dummies and [0,1] shares — standardizing would destroy the "zł per full hour" reading).
- Banned features: distance, ride count (outcomes, not conditions).
- Promotion gates: rung unlocked when `n ≥ 15 × (columns + intercept)`; per-column exposure floor ≥ 20 effective hours (`Σ value·fill`). Constants: `RowsPerCoefficient = 15.0`, `ExposureFloorHours = 20.0`.
- Baseline district = `"Stare Miasto"` (the centrum district; modeling doc's `dzielnica_centrum` baseline). The 8-way `getGeneralDistrict` fold from PerRide2 is NOT reused.
- Baseline chain for temperature: `{0–30°C} → {18–30°C} → {18–25°C}`; labels from `TemperatureBucket.label` (`≤0°C`, `0–10°C`, `10–18°C`, `18–25°C`, `25–30°C`, `>30°C`).
- Worked span per ride: `AcceptedTimestamp → RideEnd`. Gaps between spans count as worked, no cap, attributed to the *next* pickup's district. Hours with no ride starting or in progress get no row.
- `noc` = hour ∈ 18:00–6:00; `weekend`/`godziny_szczytu` reuse `PerRide2.RideRow.isWeekend`/`isRushHour` evaluated at hour start.
- Existing `/regression/ols` behavior stays byte-identical (refactoring into shared private helpers is allowed; public behavior unchanged).
- Commit after every task. F# verification: `dotnet build Bolt.slnx` + `dotnet test tests/Bolt.ETL.Tests`. Python verification: `cd python-analytics && uv run pytest`.

---

### Task 1: Python — `run_wls` in core regression

**Files:**
- Modify: `python-analytics/pyproject.toml`
- Modify: `python-analytics/analytics/core/regression.py`
- Test: `python-analytics/tests/test_regression_wls.py` (create; no `__init__.py` needed — pytest discovers rootdir tests without it)

**Interfaces:**
- Consumes: existing `run_ols(df, target, drop_columns, categorical_columns, standardize)` and `finite_or_none`.
- Produces: `run_wls(df: pd.DataFrame, target: str, weights: str, drop_columns: list[str] = (), categorical_columns: list[str] | None = None, standardize: bool = True) -> dict` — same result dict shape as `run_ols`. Raises `ValueError` if `weights` column missing. Task 2 calls this from the API route.

- [ ] **Step 1: Add pytest + httpx dev dependencies**

Append to `python-analytics/pyproject.toml` (httpx is needed by FastAPI's `TestClient` in Task 2; adding both now keeps one dependency change):

```toml
[dependency-groups]
dev = [
    "pytest>=8",
    "httpx>=0.27",
]
```

Run: `cd python-analytics && uv sync`
Expected: resolves and installs pytest + httpx.

- [ ] **Step 2: Write the failing tests**

Create `python-analytics/tests/test_regression_wls.py`:

```python
"""run_wls: separate from run_ols (no branching). Weights are required;
uniform weights reproduce OLS, non-uniform weights change the fit."""

import numpy as np
import pandas as pd
import pytest

from analytics.core.regression import run_ols, run_wls


def _frame() -> pd.DataFrame:
    rng = np.random.default_rng(42)
    n = 80
    x = rng.uniform(0, 1, n)
    flag = rng.integers(0, 2, n).astype(float)
    y = 30.0 + 8.0 * x - 5.0 * flag + rng.normal(0, 2, n)
    w = rng.uniform(0.2, 1.0, n)
    return pd.DataFrame({"y": y, "x": x, "flag": flag, "w": w})


def test_missing_weights_column_raises():
    df = _frame().drop(columns=["w"])
    with pytest.raises(ValueError, match="weights"):
        run_wls(df, target="y", weights="w", standardize=False)


def test_uniform_weights_match_ols():
    df = _frame()
    df["w"] = 1.0
    wls = run_wls(df, target="y", weights="w", standardize=False)
    ols = run_ols(df.drop(columns=["w"]), target="y", standardize=False)
    for c_wls, c_ols in zip(wls["coefficients"], ols["coefficients"]):
        assert c_wls["name"] == c_ols["name"]
        assert c_wls["coef"] == pytest.approx(c_ols["coef"], rel=1e-9)


def test_nonuniform_weights_differ_from_ols():
    df = _frame()
    wls = run_wls(df, target="y", weights="w", standardize=False)
    ols = run_ols(df.drop(columns=["w"]), target="y", standardize=False)
    coefs_wls = {c["name"]: c["coef"] for c in wls["coefficients"]}
    coefs_ols = {c["name"]: c["coef"] for c in ols["coefficients"]}
    assert coefs_wls != coefs_ols


def test_weights_column_stays_out_of_design_matrix():
    df = _frame()
    wls = run_wls(df, target="y", weights="w", standardize=False)
    names = [c["name"] for c in wls["coefficients"]]
    assert "w" not in names
    assert set(names) == {"const", "x", "flag"}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd python-analytics && uv run pytest tests/test_regression_wls.py -v`
Expected: FAIL — `ImportError: cannot import name 'run_wls'`.

- [ ] **Step 4: Implement `run_wls` with shared private helpers**

Replace `python-analytics/analytics/core/regression.py` with:

```python
"""OLS/WLS regression: categoricals one-hot encoded (drop_first), numeric
features optionally standardized, intercept added. Results come from model
attributes, not the summary() text. WLS is a separate entry point — weights
are required there and absent from OLS (no branching between the two)."""

import pandas as pd
import statsmodels.api as sm

from analytics.core.sanitize import finite_or_none


def _design(df: pd.DataFrame, target: str, drop_columns: list[str],
            categorical_columns: list[str] | None,
            standardize: bool) -> tuple[pd.Series, pd.DataFrame]:
    missing = [c for c in [target, *drop_columns] if c not in df.columns]
    if missing:
        raise ValueError(f"columns not in data: {missing}")

    if categorical_columns:
        df = df.copy()
        for column in categorical_columns:
            if column not in df.columns:
                raise ValueError(f"categorical_columns not in data: [{column!r}]")
            df[column] = df[column].astype(str)

    y = pd.to_numeric(df[target], errors="raise")
    X = df.drop(columns=[target, *drop_columns])
    numeric = X.select_dtypes(include="number").columns.tolist()  # bool excluded

    X = pd.get_dummies(X, drop_first=True).astype(float)  # categoricals -> 0/1
    if standardize and numeric:
        X[numeric] = (X[numeric] - X[numeric].mean()) / X[numeric].std()
    X = sm.add_constant(X)
    return y, X


def _fit_result(model) -> dict:
    conf_int = model.conf_int()
    coefficients = [
        {
            "name": name,
            "coef": finite_or_none(model.params[name]),
            "std_err": finite_or_none(model.bse[name]),
            "t_value": finite_or_none(model.tvalues[name]),
            "p_value": finite_or_none(model.pvalues[name]),
            "ci_low": finite_or_none(conf_int.loc[name, 0]),
            "ci_high": finite_or_none(conf_int.loc[name, 1]),
        }
        for name in model.params.index
    ]
    return {
        "n_observations": int(model.nobs),
        "r_squared": finite_or_none(model.rsquared),
        "adj_r_squared": finite_or_none(model.rsquared_adj),
        "f_statistic": finite_or_none(model.fvalue),
        "f_pvalue": finite_or_none(model.f_pvalue),
        "coefficients": coefficients,
    }


def run_ols(df: pd.DataFrame, target: str, drop_columns: list[str] = (),
            categorical_columns: list[str] | None = None,
            standardize: bool = True) -> dict:
    y, X = _design(df, target, drop_columns, categorical_columns, standardize)
    model = sm.OLS(y, X).fit()
    return _fit_result(model)


def run_wls(df: pd.DataFrame, target: str, weights: str,
            drop_columns: list[str] = (),
            categorical_columns: list[str] | None = None,
            standardize: bool = True) -> dict:
    if weights not in df.columns:
        raise ValueError(f"weights column not in data: [{weights!r}]")
    w = pd.to_numeric(df[weights], errors="raise")
    y, X = _design(df.drop(columns=[weights]), target, list(drop_columns),
                   categorical_columns, standardize)
    model = sm.WLS(y, X, weights=w).fit()
    return _fit_result(model)
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd python-analytics && uv run pytest tests/test_regression_wls.py -v`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add python-analytics/pyproject.toml python-analytics/uv.lock python-analytics/analytics/core/regression.py python-analytics/tests/test_regression_wls.py
git commit -m "feat: WLS regression core, separate from OLS"
```

---

### Task 2: Python — `/regression/wls` endpoint

**Files:**
- Modify: `python-analytics/analytics/api/models.py`
- Modify: `python-analytics/analytics/api/main.py`
- Test: `python-analytics/tests/test_api_wls.py` (create)

**Interfaces:**
- Consumes: `run_wls` from Task 1; existing `to_dataframe`, `OlsRequest`, `OlsResponse`, the `ValueError → 400` exception handler.
- Produces: `POST /regression/wls` accepting `WlsRequest` (= `OlsRequest` fields + required `weights: str`), returning `OlsResponse` shape. Missing `weights` field → 422 (pydantic); weights column absent from rows → 400. F# client (Task 3) posts here.

- [ ] **Step 1: Write the failing tests**

Create `python-analytics/tests/test_api_wls.py`:

```python
"""/regression/wls endpoint: weights required (422 without it, 400 when the
column is absent from rows). /regression/ols is untouched — same request
works there without weights (guards the no-branching separation)."""

from fastapi.testclient import TestClient

from analytics.api.main import app

client = TestClient(app)

ROWS = [
    {"y": 30.0 + 8.0 * (i % 10) / 10.0 - 5.0 * (i % 2), "x": (i % 10) / 10.0,
     "flag": bool(i % 2), "w": 0.5 + (i % 5) / 10.0}
    for i in range(60)
]


def test_wls_happy_path():
    response = client.post("/regression/wls", json={
        "rows": ROWS, "target": "y", "weights": "w", "standardize": False})
    assert response.status_code == 200
    body = response.json()
    assert body["n_observations"] == 60
    names = [c["name"] for c in body["coefficients"]]
    assert "const" in names and "x" in names
    assert "w" not in names


def test_wls_without_weights_field_is_422():
    response = client.post("/regression/wls", json={
        "rows": ROWS, "target": "y", "standardize": False})
    assert response.status_code == 422


def test_wls_missing_weights_column_is_400():
    rows = [{k: v for k, v in row.items() if k != "w"} for row in ROWS]
    response = client.post("/regression/wls", json={
        "rows": rows, "target": "y", "weights": "w", "standardize": False})
    assert response.status_code == 400
    assert "weights" in response.json()["detail"]


def test_ols_endpoint_unchanged_no_weights_needed():
    rows = [{k: v for k, v in row.items() if k != "w"} for row in ROWS]
    response = client.post("/regression/ols", json={
        "rows": rows, "target": "y", "standardize": False})
    assert response.status_code == 200
    assert response.json()["n_observations"] == 60
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd python-analytics && uv run pytest tests/test_api_wls.py -v`
Expected: FAIL — `/regression/wls` returns 404 (route missing); the OLS guard test passes.

- [ ] **Step 3: Add `WlsRequest` model and route**

In `python-analytics/analytics/api/models.py`, after `class OlsRequest`:

```python
class WlsRequest(OlsRequest):
    weights: str  # required: name of the weights column inside rows
```

In `python-analytics/analytics/api/main.py`:
- extend the models import with `WlsRequest`;
- extend the regression import: `from analytics.core.regression import run_ols, run_wls`;
- add after the `/regression/ols` route:

```python
@app.post("/regression/wls")
def regression_wls(request: WlsRequest) -> OlsResponse:
    df = to_dataframe(request.rows)
    result = run_wls(df, target=request.target, weights=request.weights,
                     drop_columns=request.drop_columns,
                     categorical_columns=request.categorical_columns,
                     standardize=request.standardize)
    return OlsResponse(**result)
```

- [ ] **Step 4: Run all python tests**

Run: `cd python-analytics && uv run pytest -v`
Expected: all pass (Task 1 + Task 2 tests).

- [ ] **Step 5: Commit**

```bash
git add python-analytics/analytics/api/models.py python-analytics/analytics/api/main.py python-analytics/tests/test_api_wls.py
git commit -m "feat: /regression/wls endpoint with required weights"
```

---

### Task 3: F# — `WlsRequest` contract and client method

**Files:**
- Modify: `src/Bolt.ETL/Analytics/AnalyticsContracts.fs` (after `OlsRequest`, line ~47)
- Modify: `src/Bolt.ETL/Analytics/AnalyticsClient.fs` (after `olsRegression`, line ~43)

**Interfaces:**
- Consumes: existing `post<'req,'resp>`, `OlsResponse`.
- Produces: `WlsRequest` record; `AnalyticsClient.wlsRegression : WlsRequest -> Task<OlsResponse>`. Task 7 calls this.

No unit test — thin DTO + HTTP wiring with no test seam (same as the untested `olsRegression`); compile is the check.

- [ ] **Step 1: Add `WlsRequest` to `AnalyticsContracts.fs`** (directly after the `OlsRequest` type):

```fsharp
/// Same shape as OlsRequest plus the required weights column name; posts to
/// the separate /regression/wls endpoint (weights are never optional there).
type WlsRequest = {
    [<JsonPropertyName "rows">] Rows: Map<string, obj> array
    [<JsonPropertyName "target">] Target: string
    [<JsonPropertyName "weights">] Weights: string
    [<JsonPropertyName "drop_columns">] DropColumns: string array
    [<JsonPropertyName "categorical_columns">] CategoricalColumns: string array option
    [<JsonPropertyName "standardize">] Standardize: bool
}
```

- [ ] **Step 2: Add client method to `AnalyticsClient.fs`** (after `olsRegression`):

```fsharp
    let wlsRegression (request: WlsRequest) : Task<OlsResponse> =
        post "/regression/wls" request
```

- [ ] **Step 3: Build**

Run: `dotnet build Bolt.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Bolt.ETL/Analytics/AnalyticsContracts.fs src/Bolt.ETL/Analytics/AnalyticsClient.fs
git commit -m "feat: WlsRequest contract and wlsRegression client"
```

---

### Task 4: F# — PerHour dataset construction

**Files:**
- Create: `src/Bolt.ETL/Analysis/PerHour.fs`
- Modify: `src/Bolt.ETL/Bolt.ETL.fsproj` (add `<Compile Include="Analysis\PerHour.fs" />` after the `PerRide2.fs` line)
- Modify: `tests/Bolt.ETL.Tests/Bolt.ETL.Tests.fsproj` (add `<Compile Include="PerHour.Tests.fs" />` after the `PerRide2.Tests.fs` line)
- Test: `tests/Bolt.ETL.Tests/PerHour.Tests.fs` (create)

**Interfaces:**
- Consumes: `FinishedRide` (`Times.AcceptedTimestamp`, `Times.RideEnd`, `Payment.Earned`, `Route.Stops[0].Location`), `PerRide2.RideRow.isWeekend`/`isRushHour`, `TemperatureBucket.fromCelcius`, `WeatherDataPoint`, `MeteoRepository`/`DistrictsRepository`, `DistrictAssignment`.
- Produces (used by Tasks 5–8):
  - `PerHour.HourRow` = `{ HourStart: DateTimeOffset; Fill: float; Rate: float; Shares: Map<string, float>; Rain: bool; Snow: bool; Temperature: TemperatureBucket; IsNight: bool; IsWeekend: bool; IsRushHour: bool }`
  - `PerHour.HourlyDataSource` = `{ Rows: HourRow array }`
  - `PerHour.Dataset.workedSegments : (FinishedRide -> string) -> FinishedRide[] -> WorkedSegment list` where `WorkedSegment` = `{ SegStart: DateTimeOffset; SegEnd: DateTimeOffset; District: string }`
  - `PerHour.Dataset.buildRows : (DateTimeOffset -> WeatherDataPoint) -> (FinishedRide -> string) -> FinishedRide[] -> HourRow array`
  - `PerHour.prepareHourlySource : FinishedRide[] -> HourlyDataSource`

- [ ] **Step 1: Write the failing tests**

Create `tests/Bolt.ETL.Tests/PerHour.Tests.fs`. District lookup is injected, so tests key it off the pickup stop's `Address` — no geometry needed:

```fsharp
module Bolt.ETL.Tests.PerHourTests

open System
open Xunit
open Bolt.ETL.Analysis
open Bolt.Models
open Bolt.Models.Meteo

let private ride (accepted: string) (rideEnd: string) (earned: decimal) (district: string) : FinishedRide =
    { Payment =
        { PaymentMetadata = { PaymentType = "cash"; PaymentMethodType = "cash" }
          Paid = [||]
          Earned = [| { Title = "ride"; Value = earned } |] }
      Route =
        { RideDistance = 1.0<km>
          Stops = [| { Address = district
                       Location = { Latitude = 50.06; Longitude = 19.94 }
                       Timestamp = None } |] }
      Times =
        { CreatedTimestamp = DateTimeOffset.Parse accepted
          AcceptedTimestamp = DateTimeOffset.Parse accepted
          RideStart = DateTimeOffset.Parse accepted
          RideEnd = DateTimeOffset.Parse rideEnd }
      State = "finished" }

let private districtOf (r: FinishedRide) = r.Route.Stops[0].Address

let private clearWeather (_: DateTimeOffset) : WeatherDataPoint =
    { Temperature = 20.0<celcius>; Rain = 0.0<mm>; Snow = 0.0<cm> }

let private buildRows rides = PerHour.Dataset.buildRows clearWeather districtOf rides

[<Fact>]
let ``ride spanning two hours splits earnings and minutes proportionally`` () =
    // 14:30–15:30, 60 zł: 30 min in each hour, 30 zł each, fill 0.5, rate 60
    let rows = buildRows [| ride "2026-07-14T14:30:00+02:00" "2026-07-14T15:30:00+02:00" 60m "A" |]
    Assert.Equal(2, rows.Length)
    Assert.Equal(0.5, rows[0].Fill, 3)
    Assert.Equal(60.0, rows[0].Rate, 3)
    Assert.Equal(0.5, rows[1].Fill, 3)
    Assert.Equal(60.0, rows[1].Rate, 3)

[<Fact>]
let ``gap between rides counts as worked and goes to the next pickup's district`` () =
    // A ends 14:10, B starts 14:40 in district B: hour 14 has 10 + 30 (gap) + 20 min
    let rows =
        buildRows
            [| ride "2026-07-14T13:50:00+02:00" "2026-07-14T14:10:00+02:00" 20m "A"
               ride "2026-07-14T14:40:00+02:00" "2026-07-14T15:00:00+02:00" 20m "B" |]
    let hour14 = rows |> Array.find (fun r -> r.HourStart.Hour = 14)
    Assert.Equal(1.0, hour14.Fill, 3)
    // 10 min of A + (30 gap + 20 ride) of B
    Assert.Equal(10.0 / 60.0, hour14.Shares["A"], 3)
    Assert.Equal(50.0 / 60.0, hour14.Shares["B"], 3)

[<Fact>]
let ``fully idle clock hour gets no row and its gap minutes drop`` () =
    // ride ends 14:50, next starts 16:20 → hour 15 fully idle: no row;
    // hour 14 gets 10 gap min, hour 16 gets 20 gap min
    let rows =
        buildRows
            [| ride "2026-07-14T14:00:00+02:00" "2026-07-14T14:50:00+02:00" 50m "A"
               ride "2026-07-14T16:20:00+02:00" "2026-07-14T16:40:00+02:00" 20m "B" |]
    Assert.Equal<int[]>([| 14; 16 |], rows |> Array.map _.HourStart.Hour)
    Assert.Equal(1.0, (rows |> Array.find (fun r -> r.HourStart.Hour = 14)).Fill, 3)
    // hour 16: gap 16:00–16:20 counts (inside an eligible hour) + 20 ride min
    Assert.Equal(40.0 / 60.0, (rows |> Array.find (fun r -> r.HourStart.Hour = 16)).Fill, 3)

[<Fact>]
let ``overlapping ride spans are unioned not double counted`` () =
    // 14:00–14:40 and 14:20–14:50 → 50 worked minutes, not 70
    let rows =
        buildRows
            [| ride "2026-07-14T14:00:00+02:00" "2026-07-14T14:40:00+02:00" 30m "A"
               ride "2026-07-14T14:20:00+02:00" "2026-07-14T14:50:00+02:00" 30m "A" |]
    Assert.Equal(1, rows.Length)
    Assert.Equal(50.0 / 60.0, rows[0].Fill, 3)
    // earnings still full 60 zł in the hour → rate = 60 / (50/60) = 72
    Assert.Equal(72.0, rows[0].Rate, 3)

[<Fact>]
let ``district shares sum to one`` () =
    let rows =
        buildRows
            [| ride "2026-07-14T14:00:00+02:00" "2026-07-14T14:20:00+02:00" 10m "A"
               ride "2026-07-14T14:30:00+02:00" "2026-07-14T14:50:00+02:00" 10m "B" |]
    for row in rows do
        Assert.Equal(1.0, row.Shares |> Map.toSeq |> Seq.sumBy snd, 6)

[<Fact>]
let ``rate divides earnings by fill`` () =
    // 20 min ride, 30 zł → fill 1/3, rate 90 zł/h
    let rows = buildRows [| ride "2026-07-14T14:10:00+02:00" "2026-07-14T14:30:00+02:00" 30m "A" |]
    Assert.Equal(1.0 / 3.0, rows[0].Fill, 3)
    Assert.Equal(90.0, rows[0].Rate, 3)

[<Fact>]
let ``hour flags come from hour start`` () =
    // Tuesday 23:15 ride → hour 23: night, not weekend, not rush (weekday)
    let rows = buildRows [| ride "2026-07-14T23:15:00+02:00" "2026-07-14T23:45:00+02:00" 20m "A" |]
    Assert.True(rows[0].IsNight)
    Assert.False(rows[0].IsWeekend)
    Assert.False(rows[0].IsRushHour)
    // Tuesday 07:30 → morning rush, day
    let morning = buildRows [| ride "2026-07-14T07:30:00+02:00" "2026-07-14T07:50:00+02:00" 20m "A" |]
    Assert.False(morning[0].IsNight)
    Assert.True(morning[0].IsRushHour)
```

- [ ] **Step 2: Add the test file to `Bolt.ETL.Tests.fsproj`, run to verify failure**

Add `<Compile Include="PerHour.Tests.fs" />` after the `PerRide2.Tests.fs` entry.

Run: `dotnet test tests/Bolt.ETL.Tests`
Expected: FAIL to compile — `PerHour` not defined.

- [ ] **Step 3: Create `src/Bolt.ETL/Analysis/PerHour.fs`**

```fsharp
namespace Bolt.ETL.Analysis

/// Per-hour earnings analysis: WLS regression of zł per worked hour on
/// conditions visible before the hour starts (time, weather, district mix),
/// with a tier ladder that unlocks finer models as data accumulates.
/// Modeling authority: .claude/per-hour-earnings-design.md;
/// implementation spec: docs/superpowers/specs/2026-07-19-per-hour-earnings-design.md.
module PerHour =

    open System
    open Bolt.ETL.Geo
    open Bolt.ETL.Geo.DistrictAssignment
    open Bolt.ETL.Meteo.Model
    open Bolt.Infrastructure.Repository
    open Bolt.Models
    open Bolt.Models.Meteo

    /// The centrum district, dropped as the regression baseline.
    [<Literal>]
    let BaselineDistrict = "Stare Miasto"

    type HourRow = {
        HourStart: DateTimeOffset
        /// Worked minutes / 60, in (0, 1]. Doubles as the WLS weight.
        Fill: float
        /// zł per fully worked hour = hour earnings / Fill.
        Rate: float
        /// Named district -> share of the hour's worked minutes; sums to 1.
        Shares: Map<string, float>
        Rain: bool
        Snow: bool
        Temperature: TemperatureBucket
        IsNight: bool
        IsWeekend: bool
        IsRushHour: bool
    }

    type HourlyDataSource = { Rows: HourRow array }

    type WorkedSegment = {
        SegStart: DateTimeOffset
        SegEnd: DateTimeOffset
        District: string
    }

    module Dataset =

        /// Non-overlapping worked segments. Ride spans (Accepted -> RideEnd)
        /// are unioned; the gap before each ride becomes a segment attributed
        /// to that ride's pickup district (where the driver positioned).
        let workedSegments (districtOf: FinishedRide -> string) (rides: FinishedRide[]) : WorkedSegment list =
            let step (segments, cursor) (ride: FinishedRide) =
                let s = ride.Times.AcceptedTimestamp
                let e = ride.Times.RideEnd
                let district = districtOf ride
                let gap =
                    match cursor with
                    | Some c when s > c -> [ { SegStart = c; SegEnd = s; District = district } ]
                    | _ -> []
                let clippedStart =
                    match cursor with
                    | Some c -> max s c
                    | None -> s
                let rideSegment =
                    if e > clippedStart
                    then [ { SegStart = clippedStart; SegEnd = e; District = district } ]
                    else []
                let cursor' =
                    match cursor with
                    | Some c -> Some(max c e)
                    | None -> Some e
                segments @ gap @ rideSegment, cursor'

            rides
            |> Array.sortBy _.Times.AcceptedTimestamp
            |> Array.fold step ([], None)
            |> fst

        let hourFloor (t: DateTimeOffset) =
            DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset)

        let private overlapMinutes (aStart: DateTimeOffset) (aEnd: DateTimeOffset) (bStart: DateTimeOffset) (bEnd: DateTimeOffset) =
            let s = max aStart bStart
            let e = min aEnd bEnd
            if e > s then (e - s).TotalMinutes else 0.0

        /// Clock hours with a ride starting or in progress. Fully idle hours
        /// never get a row, so gap minutes falling into them drop out.
        let eligibleHours (rides: FinishedRide[]) : DateTimeOffset list =
            rides
            |> Seq.collect (fun r ->
                seq {
                    let mutable h = hourFloor r.Times.AcceptedTimestamp
                    yield h
                    h <- h.AddHours 1.0
                    while h < r.Times.RideEnd do
                        yield h
                        h <- h.AddHours 1.0
                })
            |> Seq.distinct
            |> Seq.sort
            |> List.ofSeq

        /// Earnings land in hours proportionally to the ride span's overlap
        /// with the hour (a zero-length span pays out in its starting hour).
        let private earningsInHour (rides: FinishedRide[]) (hour: DateTimeOffset) : float =
            let hourEnd = hour.AddHours 1.0
            rides
            |> Array.sumBy (fun r ->
                let earned = r.Payment.Earned |> Array.sumBy _.Value |> float
                let total = (r.Times.RideEnd - r.Times.AcceptedTimestamp).TotalMinutes
                if total <= 0.0 then
                    if hourFloor r.Times.AcceptedTimestamp = hour then earned else 0.0
                else
                    earned * overlapMinutes r.Times.AcceptedTimestamp r.Times.RideEnd hour hourEnd / total)

        let buildRows (weatherProvider: DateTimeOffset -> WeatherDataPoint)
                      (districtOf: FinishedRide -> string)
                      (rides: FinishedRide[]) : HourRow array =
            let segments = workedSegments districtOf rides
            eligibleHours rides
            |> List.choose (fun hour ->
                let hourEnd = hour.AddHours 1.0
                let minutesByDistrict =
                    segments
                    |> List.map (fun s -> s.District, overlapMinutes s.SegStart s.SegEnd hour hourEnd)
                    |> List.filter (fun (_, m) -> m > 0.0)
                    |> List.groupBy fst
                    |> List.map (fun (d, xs) -> d, xs |> List.sumBy snd)
                let workedMinutes = minutesByDistrict |> List.sumBy snd
                if workedMinutes <= 0.0 then None
                else
                    let fill = workedMinutes / 60.0
                    let weather = weatherProvider hour
                    Some {
                        HourStart = hour
                        Fill = fill
                        Rate = earningsInHour rides hour / fill
                        Shares =
                            minutesByDistrict
                            |> List.map (fun (d, m) -> d, m / workedMinutes)
                            |> Map.ofList
                        Rain = weather.Rain <> 0.0<mm>
                        Snow = weather.Snow <> 0.0<cm>
                        Temperature = TemperatureBucket.fromCelcius weather.Temperature
                        IsNight = hour.Hour >= 18 || hour.Hour < 6
                        IsWeekend = PerRide2.RideRow.isWeekend hour
                        IsRushHour = PerRide2.RideRow.isRushHour hour
                    })
            |> Array.ofList

    let prepareHourlySource (rides: FinishedRide[]) : HourlyDataSource =
        let meteo = (MeteoRepository.get ()).Value
        let districts = (DistrictsRepository.get ()).Value
        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
        let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts
        let districtOf (ride: FinishedRide) =
            districtProvider ride.Route.Stops[0].Location
            |> Option.defaultValue (DistrictName "unknown")
            |> _.Value
        { Rows = Dataset.buildRows weatherProvider districtOf rides }
```

Note: `MeteoRepository`/`DistrictsRepository` open follows `PerRide2.fs:13` (`Bolt.Infrastructure.Repository`) — copy the exact `open` list style from there if names differ.

- [ ] **Step 4: Add compile entry, build, run tests**

Add `<Compile Include="Analysis\PerHour.fs" />` to `src/Bolt.ETL/Bolt.ETL.fsproj` after the `PerRide2.fs` line (it references `PerRide2.RideRow`, so it must come after).

Run: `dotnet test tests/Bolt.ETL.Tests`
Expected: all PerHour tests PASS, existing tests still PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.ETL/Analysis/PerHour.fs src/Bolt.ETL/Bolt.ETL.fsproj tests/Bolt.ETL.Tests/PerHour.Tests.fs tests/Bolt.ETL.Tests/Bolt.ETL.Tests.fsproj
git commit -m "feat: per-hour dataset construction (spans, gaps, shares)"
```

---

### Task 5: F# — ladder engine (columns, floors, gates)

**Files:**
- Modify: `src/Bolt.ETL/Analysis/PerHour.fs` (append `Ladder` module after `prepareHourlySource`)
- Test: `tests/Bolt.ETL.Tests/PerHour.Tests.fs` (append)

**Interfaces:**
- Consumes: `HourRow`, `BaselineDistrict`, `TemperatureBucket.label`.
- Produces (used by Tasks 6–8):
  - `PerHour.ColumnSpec` = `{ Name: string; Value: HourRow -> float }`
  - `PerHour.Ladder.Rung` = `{ Level: int; Columns: ColumnSpec list; FoldedDistricts: string array }`
  - `PerHour.Ladder.exposure : HourRow array -> ColumnSpec -> float`
  - `PerHour.Ladder.rungColumns : HourRow array -> int -> Rung` (levels 1–4)
  - `PerHour.Ladder.isUnlocked : HourRow array -> Rung -> bool`
  - `PerHour.Ladder.unlockedRungs : HourRow array -> Rung list`
- Constants `RowsPerCoefficient = 15.0`, `ExposureFloorHours = 20.0` live at `PerHour` module level.

**Folding rules** (partition-refinement; a failing column stays merged in its parent, never blocks the rung):
- Any dummy column with exposure `< ExposureFloorHours` is omitted (folds into the intercept/baseline).
- A split (`zła_pogoda → deszcz|śnieg`, `≤0°C LUB >30°C → ≤0°C|>30°C`, `0–18°C → 0–10°C|10–18°C`) applies only when **both** children pass the floor; otherwise the merged parent column is kept (subject to its own floor).
- `25–30°C` is an extraction from the baseline: included only if it passes the floor.
- Districts (rung 4): each non-baseline district with `Σ share·fill ≥ floor` gets its own `dzielnica_<name>` column; the rest fold into `inne_dzielnice`. Catch-all columns (`poza_centrum`, `inne_dzielnice`) are included whenever their exposure is `> 0` — no floor, they never block.

- [ ] **Step 1: Write the failing tests** (append to `PerHour.Tests.fs`)

```fsharp
open Bolt.ETL.Meteo.Model

/// Hand-built hour row: full fill, Tuesday midday defaults, single district.
let private hourRow (district: string) (temp: TemperatureBucket) (rain: bool) (snow: bool) : PerHour.HourRow =
    { HourStart = DateTimeOffset.Parse "2026-07-14T12:00:00+02:00"
      Fill = 1.0
      Rate = 50.0
      Shares = Map [ district, 1.0 ]
      Rain = rain
      Snow = snow
      Temperature = temp
      IsNight = false
      IsWeekend = false
      IsRushHour = false }

let private centrumRows n = Array.init n (fun _ -> hourRow PerHour.BaselineDistrict Mild false false)

[<Fact>]
let ``exposure is fill weighted column value`` () =
    let rows = [| { hourRow "A" Mild true false with Fill = 0.5 }
                  hourRow "B" Mild false false |]
    let rain = { PerHour.ColumnSpec.Name = "deszcz"
                 Value = fun r -> if r.Rain then 1.0 else 0.0 }
    Assert.Equal(0.5, PerHour.Ladder.exposure rows rain, 6)

[<Fact>]
let ``rung1 keeps only columns passing the exposure floor`` () =
    // 30 rainy weekend-flagged rows: weekend + zła_pogoda pass (30 ≥ 20),
    // rush hour has zero exposure → omitted
    let rows =
        Array.init 30 (fun _ -> { hourRow "A" Mild true false with IsWeekend = true })
    let rung = PerHour.Ladder.rungColumns rows 1
    Assert.Equal<string list>(
        [ "weekend"; "zła_pogoda" ],
        rung.Columns |> List.map _.Name)

[<Fact>]
let ``weather split needs both children above floor`` () =
    // 25 rain hours + 5 snow hours: split would leave śnieg at 5 < 20 → merged flag stays
    let rows =
        Array.append
            (Array.init 25 (fun _ -> hourRow "A" Mild true false))
            (Array.init 5 (fun _ -> hourRow "A" Mild false true))
    let rung3 = PerHour.Ladder.rungColumns rows 3
    let names = rung3.Columns |> List.map _.Name
    Assert.Contains("zła_pogoda", names)
    Assert.DoesNotContain("deszcz", names)
    Assert.DoesNotContain("śnieg", names)
    // 25 + 25: both pass → split
    let rows' =
        Array.append
            (Array.init 25 (fun _ -> hourRow "A" Mild true false))
            (Array.init 25 (fun _ -> hourRow "A" Mild false true))
    let names' = (PerHour.Ladder.rungColumns rows' 3).Columns |> List.map _.Name
    Assert.Contains("deszcz", names')
    Assert.Contains("śnieg", names')
    Assert.DoesNotContain("zła_pogoda", names')

[<Fact>]
let ``centrum only driver gets no district columns at rung 4`` () =
    let rung = PerHour.Ladder.rungColumns (centrumRows 100) 4
    let names = rung.Columns |> List.map _.Name
    Assert.DoesNotContain(names, fun n -> n.StartsWith "dzielnica_")
    Assert.DoesNotContain("inne_dzielnice", names)
    Assert.DoesNotContain("poza_centrum", names)

[<Fact>]
let ``under floor district folds into inne_dzielnice`` () =
    // 30 h in Podgórze (own column), 5 h in Bronowice (folds)
    let rows =
        Array.append
            (Array.init 30 (fun _ -> hourRow "Podgórze" Mild false false))
            (Array.init 5 (fun _ -> hourRow "Bronowice" Mild false false))
    let rung = PerHour.Ladder.rungColumns rows 4
    let names = rung.Columns |> List.map _.Name
    Assert.Contains("dzielnica_Podgórze", names)
    Assert.Contains("inne_dzielnice", names)
    Assert.DoesNotContain("dzielnica_Bronowice", names)
    Assert.Equal<string[]>([| "Bronowice" |], rung.FoldedDistricts)

[<Fact>]
let ``budget gate needs 15 rows per coefficient`` () =
    // rung with 2 columns + intercept = 3 coefs → needs 45 rows
    let make n =
        Array.init n (fun i ->
            { hourRow "A" Mild true false with IsWeekend = i % 2 = 0 })
    let rungOf rows = PerHour.Ladder.rungColumns rows 1
    Assert.False(PerHour.Ladder.isUnlocked (make 44) (rungOf (make 44)))
    Assert.True(PerHour.Ladder.isUnlocked (make 45) (rungOf (make 45)))

[<Fact>]
let ``temperature baseline chain across rungs`` () =
    // 30 frost + 30 cool + 40 mild rows
    let rows =
        Array.concat
            [ Array.init 30 (fun _ -> hourRow "A" Frost false false)
              Array.init 30 (fun _ -> hourRow "A" Cool false false)
              Array.init 40 (fun _ -> hourRow "A" Mild false false) ]
    let names level = (PerHour.Ladder.rungColumns rows level).Columns |> List.map _.Name
    // rung 2: merged extremes flag only
    Assert.Contains("≤0°C LUB >30°C", names 2)
    // rung 3: adds 0–18°C
    Assert.Contains("0–18°C", names 3)
    // rung 4: extremes cannot split (no hot hours) → merged flag stays;
    // 0–18 cannot split (no cold hours) → stays; 25–30 absent (no warm hours)
    let n4 = names 4
    Assert.Contains("≤0°C LUB >30°C", n4)
    Assert.Contains("0–18°C", n4)
    Assert.DoesNotContain("25–30°C", n4)
    Assert.DoesNotContain("18–25°C", n4)  // baseline never a column

[<Fact>]
let ``rung 4 splits temperature fully when every bucket has exposure`` () =
    let rows =
        [ Frost; Cold; Cool; Mild; Warm; Hot ]
        |> List.collect (fun t -> List.init 25 (fun _ -> hourRow "A" t false false))
        |> Array.ofList
    let names = (PerHour.Ladder.rungColumns rows 4).Columns |> List.map _.Name
    for expected in [ "≤0°C"; "0–10°C"; "10–18°C"; "25–30°C"; ">30°C" ] do
        Assert.Contains(expected, names)
    Assert.DoesNotContain("≤0°C LUB >30°C", names)
    Assert.DoesNotContain("0–18°C", names)

[<Fact>]
let ``unlockedRungs returns rungs in order`` () =
    // 60 uniform centrum rows, half weekend: rung1 = weekend only (rush/weather
    // zero exposure) → 2 coefs → needs 30 rows → unlocked
    let rows =
        Array.init 60 (fun i -> { hourRow PerHour.BaselineDistrict Mild false false with IsWeekend = i % 2 = 0 })
    let rungs = PerHour.Ladder.unlockedRungs rows
    Assert.NotEmpty rungs
    Assert.Equal<int list>(
        rungs |> List.map _.Level |> List.sort,
        rungs |> List.map _.Level)
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Bolt.ETL.Tests`
Expected: FAIL to compile — `ColumnSpec`, `Ladder` not defined.

- [ ] **Step 3: Implement the ladder** (append to `PerHour.fs`, inside `module PerHour`, after `prepareHourlySource`)

```fsharp
    /// A regression column: display-ready Polish name, value in [0,1] per row
    /// (0/1 for dummies, share of the hour for district columns).
    type ColumnSpec = {
        Name: string
        Value: HourRow -> float
    }

    /// Rows-per-coefficient budget for unlocking a rung.
    let RowsPerCoefficient = 15.0

    /// Minimum effective hours (Σ value·fill) for a column to stand alone.
    let ExposureFloorHours = 20.0

    module Ladder =

        type Rung = {
            Level: int
            Columns: ColumnSpec list
            /// Non-baseline districts folded into inne_dzielnice (rung 4 only).
            FoldedDistricts: string array
        }

        let exposure (rows: HourRow array) (spec: ColumnSpec) : float =
            rows |> Array.sumBy (fun r -> r.Fill * spec.Value r)

        let private passesFloor rows spec = exposure rows spec >= ExposureFloorHours

        let private dummy name (pred: HourRow -> bool) =
            { Name = name; Value = fun r -> if pred r then 1.0 else 0.0 }

        let private shareOf district (r: HourRow) =
            r.Shares |> Map.tryFind district |> Option.defaultValue 0.0

        let private weekend = dummy "weekend" _.IsWeekend
        let private rushHour = dummy "godziny_szczytu" _.IsRushHour
        let private night = dummy "noc" _.IsNight
        let private badWeather = dummy "zła_pogoda" (fun r -> r.Rain || r.Snow)
        let private rain = dummy "deszcz" _.Rain
        let private snow = dummy "śnieg" _.Snow

        let private tempIn buckets = fun (r: HourRow) -> List.contains r.Temperature buckets
        let private extremeTemp = dummy "≤0°C LUB >30°C" (tempIn [ Frost; Hot ])
        let private cold0to18 = dummy "0–18°C" (tempIn [ Cold; Cool ])
        let private frost = dummy (TemperatureBucket.label Frost) (tempIn [ Frost ])
        let private hot = dummy (TemperatureBucket.label Hot) (tempIn [ Hot ])
        let private cold = dummy (TemperatureBucket.label Cold) (tempIn [ Cold ])
        let private cool = dummy (TemperatureBucket.label Cool) (tempIn [ Cool ])
        let private warm = dummy (TemperatureBucket.label Warm) (tempIn [ Warm ])

        let private ifFloor rows col = if passesFloor rows col then [ col ] else []

        /// A split replaces its merged parent only when both children stand
        /// on their own; otherwise the parent column stays (floor permitting).
        let private splitOrMerged rows (children: ColumnSpec list) (parent: ColumnSpec) =
            if children |> List.forall (passesFloor rows)
            then children
            else ifFloor rows parent

        let private pozaCentrum =
            { Name = "poza_centrum"; Value = fun r -> 1.0 - shareOf BaselineDistrict r }

        /// Catch-alls never block and take no floor — included whenever inhabited.
        let private ifInhabited rows col = if exposure rows col > 0.0 then [ col ] else []

        let private districtColumns (rows: HourRow array) =
            let named =
                rows
                |> Array.collect (fun r -> r.Shares |> Map.toArray |> Array.map fst)
                |> Array.distinct
                |> Array.filter (fun d -> d <> BaselineDistrict)
                |> Array.sort
            let unlocked, folded =
                named
                |> Array.partition (fun d ->
                    passesFloor rows { Name = d; Value = shareOf d })
            let namedColumns =
                unlocked
                |> Array.map (fun d -> { Name = $"dzielnica_{d}"; Value = shareOf d })
                |> List.ofArray
            let otherColumn =
                { Name = "inne_dzielnice"
                  Value = fun r -> folded |> Array.sumBy (fun d -> shareOf d r) }
            namedColumns @ ifInhabited rows otherColumn, folded

        let rungColumns (rows: HourRow array) (level: int) : Rung =
            let baseColumns = ifFloor rows weekend @ ifFloor rows rushHour
            match level with
            | 1 ->
                { Level = 1
                  Columns = baseColumns @ ifFloor rows badWeather
                  FoldedDistricts = [||] }
            | 2 ->
                { Level = 2
                  Columns =
                    baseColumns @ ifFloor rows badWeather @ ifFloor rows night
                    @ ifFloor rows extremeTemp @ ifInhabited rows pozaCentrum
                  FoldedDistricts = [||] }
            | 3 ->
                { Level = 3
                  Columns =
                    baseColumns @ splitOrMerged rows [ rain; snow ] badWeather
                    @ ifFloor rows night @ ifFloor rows extremeTemp
                    @ ifFloor rows cold0to18 @ ifInhabited rows pozaCentrum
                  FoldedDistricts = [||] }
            | 4 ->
                let districts, folded = districtColumns rows
                { Level = 4
                  Columns =
                    baseColumns @ splitOrMerged rows [ rain; snow ] badWeather
                    @ ifFloor rows night
                    @ splitOrMerged rows [ frost; hot ] extremeTemp
                    @ splitOrMerged rows [ cold; cool ] cold0to18
                    @ ifFloor rows warm @ districts
                  FoldedDistricts = folded }
            | _ -> invalidArg (nameof level) "rung level must be 1..4"

        /// Global budget gate: n ≥ 15 × (columns + intercept).
        let isUnlocked (rows: HourRow array) (rung: Rung) =
            float rows.Length >= RowsPerCoefficient * float (rung.Columns.Length + 1)

        let unlockedRungs (rows: HourRow array) : Rung list =
            [ 1 .. 4 ]
            |> List.map (rungColumns rows)
            |> List.filter (isUnlocked rows)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.ETL.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.ETL/Analysis/PerHour.fs tests/Bolt.ETL.Tests/PerHour.Tests.fs
git commit -m "feat: per-hour tier ladder with exposure floors and budget gate"
```

---

### Task 6: F# — rung 0 weighted-means table

**Files:**
- Modify: `src/Bolt.ETL/Analysis/PerHour.fs` (append `Rung0` module after `Ladder`)
- Test: `tests/Bolt.ETL.Tests/PerHour.Tests.fs` (append)

**Interfaces:**
- Consumes: `HourRow`, `ResultTable`.
- Produces: `PerHour.Rung0.table : HourRow array -> ResultTable` — Task 7 puts it first in the section.

- [ ] **Step 1: Write the failing tests** (append to `PerHour.Tests.fs`)

```fsharp
[<Fact>]
let ``rung0 groups by weekend and night with fill weighted means`` () =
    // weekday-day: one full hour at 60 zł/h + one half hour at 120 zł/h
    // weighted mean = (60·1 + 120·0.5) / 1.5 = 80
    let rows =
        [| { hourRow "A" Mild false false with Rate = 60.0 }
           { hourRow "A" Mild false false with Rate = 120.0; Fill = 0.5 }
           { hourRow "A" Mild false false with Rate = 40.0; IsWeekend = true; IsNight = true } |]
    let table = PerHour.Rung0.table rows
    Assert.Equal<string list>([ "kiedy"; "zł za godzinę"; "liczba godzin" ], table.Headers)
    Assert.Contains<string list>([ "dzień roboczy, dzień"; "80,00"; "2" ], table.Rows)
    Assert.Contains<string list>([ "weekend, noc"; "40,00"; "1" ], table.Rows)

[<Fact>]
let ``rung0 sorts groups by rate descending`` () =
    let rows =
        [| { hourRow "A" Mild false false with Rate = 30.0 }
           { hourRow "A" Mild false false with Rate = 90.0; IsNight = true } |]
    let table = PerHour.Rung0.table rows
    Assert.Equal("dzień roboczy, noc", table.Rows[0][0])
    Assert.Equal("dzień roboczy, dzień", table.Rows[1][0])
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Bolt.ETL.Tests`
Expected: FAIL to compile — `Rung0` not defined.

- [ ] **Step 3: Implement** (append inside `module PerHour`; also add `open System.Globalization` at the module's opens and the shared formatting helpers **before** `Rung0` so Task 7 reuses them):

```fsharp
    let private pl = CultureInfo.GetCultureInfo "pl-PL"
    let private fmt2 (v: float) = v.ToString("F2", pl)
    let private fmt2Opt (v: float option) =
        v |> Option.map fmt2 |> Option.defaultValue "–"

    /// Rung 0: weighted grouped means, always shown — no service call needed.
    module Rung0 =

        let private groupLabel (r: HourRow) =
            let day = if r.IsWeekend then "weekend" else "dzień roboczy"
            let time = if r.IsNight then "noc" else "dzień"
            $"{day}, {time}"

        let table (rows: HourRow array) : ResultTable =
            let groups =
                rows
                |> Array.groupBy groupLabel
                |> Array.map (fun (label, group) ->
                    let effectiveHours = group |> Array.sumBy _.Fill
                    let earnings = group |> Array.sumBy (fun r -> r.Rate * r.Fill)
                    label, earnings / effectiveHours, group.Length)
                |> Array.sortByDescending (fun (_, rate, _) -> rate)
                |> Array.map (fun (label, rate, count) -> [ label; fmt2 rate; string count ])
                |> List.ofArray
            { Title = "Średnie zarobki na godzinę pracy"
              Headers = [ "kiedy"; "zł za godzinę"; "liczba godzin" ]
              Rows = groups
              Notes =
                [ "zł za godzinę — średnia ważona czasem pracy: godziny przepracowane w całości liczą się mocniej niż ledwie zaczęte."
                  "liczba godzin — ile godzin zegarowych z jazdą wpadło do danej grupy." ] }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.ETL.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.ETL/Analysis/PerHour.fs tests/Bolt.ETL.Tests/PerHour.Tests.fs
git commit -m "feat: rung 0 weighted-means table for per-hour earnings"
```

---

### Task 7: F# — display tables and section building

**Files:**
- Modify: `src/Bolt.ETL/Analysis/PerHour.fs` (append `Display` module + `buildSection`)
- Test: `tests/Bolt.ETL.Tests/PerHour.Tests.fs` (append)

**Interfaces:**
- Consumes: `Ladder.Rung`, `Rung0.table`, `AnalyticsClient.wlsRegression`, `WlsRequest`, `OlsResponse`, `Coefficient`, `ResultTable`, `AnalysisSection`, shared `fmt2`/`fmt2Opt`.
- Produces:
  - `PerHour.Display.toAnalyticsRows : Ladder.Rung -> HourRow array -> Map<string, obj> array`
  - `PerHour.Display.coefficientsTable : Ladder.Rung -> OlsResponse -> ResultTable`
  - `PerHour.Display.modelStatsTable : OlsResponse -> ResultTable`
  - `PerHour.buildSection : HourlyDataSource -> Task<AnalysisSection>` (id `per-hour-earnings`, title „Zarobki na godzinę pracy") — Task 8 wires it into the pipeline.

**Copy (verbatim, from the spec):**
- Locked note (shown unless rung 4 is unlocked with zero folded districts): „Niektóre analizy (np. wpływ poszczególnych dzielnic albo dokładnych zakresów temperatury) nie są jeszcze pokazywane — mamy na razie za mało godzin jazdy, żeby policzyć je uczciwie. Odblokują się same, gdy przybędzie danych."
- Folded-districts note (when highest unlocked rung is 4 and `FoldedDistricts` non-empty): „Część dzielnic zliczona razem jako «inne dzielnice» — za mało godzin w każdej z osobna."
- Legend note (when ≥ 2 regression tables): „Liczby w wyższych tabelach mogą się różnić od niższych — dokładniejszy model oddziela efekty, które prostszy liczył razem."
- These notes are appended to the `Notes` of the **last table** in the section (each rung shows only its own numbers — no historical side-by-side, no computed unlock hours).

- [ ] **Step 1: Write the failing tests** (append to `PerHour.Tests.fs`)

```fsharp
open Bolt.ETL.Analytics

let private coef name c p lo hi : Coefficient =
    { Name = name; Coef = c; StdErr = Some 0.1; TValue = Some 1.0
      PValue = p; CiLow = lo; CiHigh = hi }

let private cannedWls: OlsResponse = {
    NObservations = 90
    RSquared = Some 0.31
    AdjRSquared = Some 0.28
    FStatistic = Some 9.0
    FPvalue = Some 0.001
    Coefficients =
        [| coef "const" (Some 40.0) (Some 0.0) (Some 35.0) (Some 45.0)
           coef "weekend" (Some 6.5) (Some 0.01) (Some 2.0) (Some 11.0)
           coef "zła_pogoda" (Some 3.0) (Some 0.3) (Some -3.0) (Some 9.0) |]
}

let private rung1 = PerHour.Ladder.rungColumns (Array.init 30 (fun _ -> { hourRow "A" Mild true false with IsWeekend = true })) 1

[<Fact>]
let ``toAnalyticsRows emits target weight and rung columns`` () =
    let rows =
        [| { hourRow "A" Mild true false with IsWeekend = true; Fill = 0.5; Rate = 80.0 } |]
    let analyticsRow = (PerHour.Display.toAnalyticsRows rung1 rows)[0]
    Assert.Equal<Set<string>>(
        Set [ "stawka_pln_h"; "waga"; "weekend"; "zła_pogoda" ],
        analyticsRow |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
    Assert.Equal(box 80.0, analyticsRow["stawka_pln_h"])
    Assert.Equal(box 0.5, analyticsRow["waga"])
    Assert.Equal(box 1.0, analyticsRow["weekend"])

[<Fact>]
let ``coefficientsTable filters insignificant and titles the rung`` () =
    let table = PerHour.Display.coefficientsTable rung1 cannedWls
    Assert.Contains("poziom 1", table.Title)
    Assert.Equal(1, table.Rows.Length)  // const excluded, zła_pogoda p=0.3 excluded
    Assert.Equal<string list>(
        [ "weekend"; "6,50"; "od 2,00 do 11,00"; "0,010" ], table.Rows[0])
    Assert.Contains("zła_pogoda", List.last table.Notes)

[<Fact>]
let ``modelStatsTable counts hours not rides`` () =
    let table = PerHour.Display.modelStatsTable cannedWls
    Assert.Contains<string list>([ "liczba godzin"; "90" ], table.Rows)
    Assert.Contains<string list>([ "R²"; "0,31" ], table.Rows)
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Bolt.ETL.Tests`
Expected: FAIL to compile — `Display` not defined.

- [ ] **Step 3: Implement** (append inside `module PerHour`; add `open System.Threading.Tasks` and `open Bolt.ETL.Analytics` to the module opens):

```fsharp
    module Display =

        let toAnalyticsRows (rung: Ladder.Rung) (rows: HourRow array) : Map<string, obj> array =
            rows
            |> Array.map (fun r ->
                Map.ofList
                    (("stawka_pln_h", box r.Rate)
                     :: ("waga", box r.Fill)
                     :: (rung.Columns |> List.map (fun c -> c.Name, box (c.Value r)))))

        let private fmtPValue (p: float option) =
            match p with
            | Some p when p < 0.001 -> "< 0,001"
            | Some p -> p.ToString("F3", pl)
            | None -> "–"

        let private isSignificant (c: Coefficient) =
            match c.PValue with
            | Some p -> p < 0.05
            | None -> false

        let coefficientsTable (rung: Ladder.Rung) (response: OlsResponse) : ResultTable =
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
                    "Wszystkie warunki w tym zestawieniu są istotne statystycznie (p < 0,05)."
                else
                    let names = insignificant |> Array.map _.Name |> String.concat ", "
                    $"Warunki, których wpływu nie widać wyraźnie w danych (p ≥ 0,05), pominięte w tabeli: {names}."
            { Title = $"Wpływ warunków na zarobki na godzinę — poziom {rung.Level}"
              Headers = [ "warunek"; "współczynnik [zł/h]"; "przedział ufności 95%"; "istotność (p)" ]
              Rows = rows
              Notes =
                [ "warunek — okoliczność panująca w danej godzinie; nazwy dzielnica_… oznaczają różnicę względem Starego Miasta (centrum), zakresy temperatur — względem pozostałych temperatur."
                  "współczynnik [zł/h] — o ile złotych na godzinę pracy zmieniają się zarobki, gdy dany warunek występuje; wiersze posortowane od najsilniejszego wpływu."
                  "przedział ufności 95% — zakres, w którym z 95-procentową pewnością mieści się prawdziwa wartość współczynnika."
                  "istotność (p) — prawdopodobieństwo, że tak silny efekt pojawiłby się przypadkiem; wartości poniżej 0,05 uznaje się za istotne statystycznie."
                  insignificantNote ] }

        let modelStatsTable (response: OlsResponse) : ResultTable =
            let fitNote =
                match response.RSquared with
                | Some r2 ->
                    let pct = int (Math.Round(r2 * 100.0))
                    [ $"Model wyjaśnia {pct}%% zmienności zarobków na godzinę." ]
                | None -> []
            { Title = "Dopasowanie modelu"
              Headers = [ "statystyka"; "wartość" ]
              Rows =
                [ [ "liczba godzin"; string response.NObservations ]
                  [ "R²"; fmt2Opt response.RSquared ]
                  [ "skorygowane R²"; fmt2Opt response.AdjRSquared ] ]
              Notes =
                fitNote
                @ [ "R² — jaka część zmienności zarobków jest wyjaśniona przez model (od 0 do 1, wyżej = lepiej); skorygowane R² dodatkowo uwzględnia liczbę cech w modelu." ] }

    let private lockedNote =
        "Niektóre analizy (np. wpływ poszczególnych dzielnic albo dokładnych zakresów temperatury) nie są jeszcze pokazywane — mamy na razie za mało godzin jazdy, żeby policzyć je uczciwie. Odblokują się same, gdy przybędzie danych."

    let private foldedNote =
        "Część dzielnic zliczona razem jako «inne dzielnice» — za mało godzin w każdej z osobna."

    let private legendNote =
        "Liczby w wyższych tabelach mogą się różnić od niższych — dokładniejszy model oddziela efekty, które prostszy liczył razem."

    let private appendNotesToLast (notes: string list) (tables: ResultTable list) =
        match List.rev tables with
        | [] -> []
        | last :: rest -> List.rev ({ last with Notes = last.Notes @ notes } :: rest)

    let buildSection (source: HourlyDataSource) : Task<AnalysisSection> =
        task {
            let rows = source.Rows
            let rungs = Ladder.unlockedRungs rows

            let results = ResizeArray<Ladder.Rung * OlsResponse>()
            for rung in rungs do
                let! response =
                    AnalyticsClient.wlsRegression {
                        Rows = Display.toAnalyticsRows rung rows
                        Target = "stawka_pln_h"
                        Weights = "waga"
                        DropColumns = [||]
                        CategoricalColumns = None
                        Standardize = false
                    }
                results.Add(rung, response)

            let rungTables =
                results |> Seq.map (fun (rung, resp) -> Display.coefficientsTable rung resp) |> List.ofSeq
            let statsTable =
                results
                |> Seq.tryLast
                |> Option.map (fun (_, resp) -> [ Display.modelStatsTable resp ])
                |> Option.defaultValue []

            let highest = results |> Seq.tryLast |> Option.map fst
            let fullyUnlocked =
                match highest with
                | Some rung -> rung.Level = 4 && Array.isEmpty rung.FoldedDistricts
                | None -> false
            let trailingNotes =
                [ if not fullyUnlocked then lockedNote
                  match highest with
                  | Some rung when rung.Level = 4 && not (Array.isEmpty rung.FoldedDistricts) -> foldedNote
                  | _ -> ()
                  if List.length rungTables >= 2 then legendNote ]

            return
                { Id = "per-hour-earnings"
                  Title = "Zarobki na godzinę pracy"
                  Description =
                    "Zarobki na godzinę pracy w zależności od warunków: pory, pogody i dzielnicy. "
                    + "Prostsze zestawienia pojawiają się od razu, dokładniejsze odblokowują się wraz z liczbą przepracowanych godzin."
                  Charts = []
                  Tables = appendNotesToLast trailingNotes (Rung0.table rows :: rungTables @ statsTable) }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Bolt.ETL.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.ETL/Analysis/PerHour.fs tests/Bolt.ETL.Tests/PerHour.Tests.fs
git commit -m "feat: per-hour display tables and section building"
```

---

### Task 8: F# — pipeline integration and end-to-end verification

**Files:**
- Modify: `src/Bolt.ETL/AnalysisPipeline.fs:34-49`

**Interfaces:**
- Consumes: `PerHour.prepareHourlySource`, `PerHour.buildSection`, existing `weatherRides`, `emptySection`.
- Produces: report `Sections = [ perRide2; perHour; clustering ]`.

- [ ] **Step 1: Wire the section into `AnalysisPipeline.run`**

After the `perRide2` binding in `src/Bolt.ETL/AnalysisPipeline.fs`, add:

```fsharp
                    let! perHour =
                        if Array.isEmpty weatherRides then
                            Task.FromResult(emptySection "per-hour-earnings" "Zarobki na godzinę pracy"
                                "Analiza zarobków na godzinę pracy względem pory, pogody i dzielnicy.")
                        else
                            PerHour.buildSection (PerHour.prepareHourlySource weatherRides)
```

and change the sections list to:

```fsharp
                             Sections = [ perRide2; perHour; clustering ] }
```

- [ ] **Step 2: Full build and test run**

Run: `dotnet build Bolt.slnx && dotnet test`
Expected: build succeeds, all F# test projects pass.

Run: `cd python-analytics && uv run pytest`
Expected: all python tests pass.

- [ ] **Step 3: Smoke-test the service round trip (manual, optional if service deps unavailable)**

Start the service: `cd python-analytics && uv run uvicorn analytics.api.main:app --port 8000 &`, then:

```bash
curl -s -X POST localhost:8000/regression/wls -H 'Content-Type: application/json' -d '{
  "rows": [{"stawka_pln_h": 50, "waga": 1.0, "weekend": true},
           {"stawka_pln_h": 60, "waga": 0.5, "weekend": false},
           {"stawka_pln_h": 55, "waga": 0.8, "weekend": true},
           {"stawka_pln_h": 45, "waga": 0.9, "weekend": false}],
  "target": "stawka_pln_h", "weights": "waga", "standardize": false}'
```

Expected: 200 with `coefficients` containing `const` and `weekend`. Kill the server afterwards.

- [ ] **Step 4: Commit**

```bash
git add src/Bolt.ETL/AnalysisPipeline.fs
git commit -m "feat: per-hour earnings section in analysis pipeline"
```
