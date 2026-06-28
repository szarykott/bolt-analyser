# Earnings analysis — pipeline plan

Goal: discover **what influences earnings** in the ride data — no prediction,
just feature-importance ranking with direction of effect. Dataset is small
(~368 finished rides), so the approach is deliberately simple and interpretable:
correlation/collinearity screening, then linear regression with confidence
intervals. Self-coded Python (`pandas` + `statsmodels`), no AutoML, no cloud.

Two independent tracks, each a different unit of analysis → two CSV files:

1. **Track 1 — what influences the price of a ride?** (1 row = 1 ride)
2. **Track 2 — what influences per-hour earnings?** (1 row = 1 active clock-hour)

Three steps, run in order: **STEP 1** export (F#) → **STEP 2** prune mirrors
(Python) → **STEP 3** regression (Python).

---

## STEP 1 — Export data (F#)

### Track 1 — `rides.csv` (1 row = 1 finished ride)

| order_id | price_pln | price_per_km | distance_km | duration_min | part_of_day | weekend | pickup_zone | payment_type | brand | num_stops | rain | temp_bucket |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1001 | 12.0 | 6.0 | 2.0 | 8.0 | afternoon | 0 | center | cash | bolt | 1 | 0 | mild |
| 1002 | 45.0 | 4.5 | 10.0 | 35.0 | evening | 1 | airport | inapp | bolt | 2 | 1 | cold |
| 1003 | 20.0 | 5.0 | 4.0 | 15.0 | morning | 0 | suburb | inapp | hopp | 1 | 0 | mild |

Targets: `price_pln`, `price_per_km`. Everything else = feature.

### Track 2 — `hours.csv` (1 row = 1 active clock-hour)

Built from `activityHours.json` + rides grouped to their start hour.

| date | hour | earned_per_online_hour | part_of_day | day_of_week | weekend | zone | rain | temp_bucket | month |
|---|---|---|---|---|---|---|---|---|---|
| 2026-03-01 | 14 | 28.0 | afternoon | sat | 1 | center | 0 | mild | mar |
| 2026-03-01 | 15 | 0.0 | afternoon | sat | 1 | center | 0 | mild | mar |
| 2026-03-01 | 18 | 52.0 | evening | sat | 1 | airport | 1 | cold | mar |

Target: `earned_per_online_hour`. Everything else = feature.

### CSV format rules (both files)

- One row = one unit of analysis.
- Numbers = clean floats. Strip units in F#: `12.0` not `"12 zł"`, `1.1` not
  `"1,1km"`, dot decimal not comma.
- Categoricals = plain string label (`cash`, `airport`). Do **not** pre-encode —
  Python does one-hot.
- Booleans = `0`/`1`.
- Missing value = empty cell (reads as NaN). Never fake with `0`.
- snake_case headers, no units in the name.

### Warnings

- **Leakage, Track 1:** never export commission, tip, earned breakdown, or paid
  total as features — they *are* the price.
- **Leakage, Track 2:** never add `rides_count` or `avg_distance` as features —
  they *are* how the money happens (trivial "more rides = more money"). Features
  must be contextual only: time, place, weather — things true *before* driving.
- **Track 2 must include idle online hours** (online, no ride → target `0`).
  Dropping them biases earnings high. Those rows show the dead hours — the point.
- **Bucket scarce continuous data** on small data: lat/lng → zone (3–6 buckets),
  hour → part_of_day, temp → temp_bucket. Aim for ~20–30 rows per level.
- **Feature budget:** ~368 rides. Keep features under ~10. Each must earn its slot.

### Feature notes / extra data sources

- Pickup zone = k-means or domain districts on `Route.Stops` lat/lng (k = 4ish).
  Start pickup-only; pickup + dropoff doubles the columns.
- GPS track (driver location at assignment) → a **driver-zone** feature; useful
  and actionable. Speed and heading → drop (weak signal, heading is circular).
  Deadhead distance (driver→pickup) only matters for Track 2 (eats unpaid time),
  not price.
- Weather → `rain` (0/1) + `temp_bucket` (cold/mild/hot). Stronger for Track 2
  (demand driver) than Track 1 (price is mostly distance + time + surge).

---

## STEP 2 — Mirror check + prune (Python)

Two features carrying the same info (collinear) break regression — they split
the credit and look like noise. Find and drop before STEP 3. Pearson correlation
alone misses categoricals, so use three checks.

```python
import pandas as pd
from statsmodels.stats.outliers_influence import variance_inflation_factor

df = pd.read_csv("rides.csv")            # or hours.csv
df = df.drop(columns=["order_id"])
targets = ["price_pln", "price_per_km"]
feat = df.drop(columns=targets)

# 2a — numeric mirrors (Pearson)
print(feat.corr(numeric_only=True))

# 2b — categorical mirrors: one-hot, then corr (catches 0/1 vs numeric)
enc = pd.get_dummies(feat, drop_first=True).astype(float)
print(enc.corr())

# 2c — VIF: catch-all, each column vs all others (numeric + dummy)
vif = pd.DataFrame({
    "feature": enc.columns,
    "VIF": [variance_inflation_factor(enc.values, i) for i in range(enc.shape[1])]
}).sort_values("VIF", ascending=False)
print(vif)

# 2d — multi-level category vs numeric (eyeball group means)
print(df.groupby("pickup_zone")["distance_km"].mean())
```

How to read:

- Correlation pair **> 0.9** = mirror.
- VIF **> 5** suspect, **> 10** definite mirror.
- Group means very different across levels → the category carries that numeric.

Action: drop one of each mirror pair, keeping the more actionable one (distance
over duration; pickup_zone over a distance it mirrors).

### Warnings

- VIF needs `.astype(float)` — dummies are bool and will otherwise crash.
- Drop one per pair, not both.
- The dropped baseline when one-hot encoding is arbitrary; it doesn't affect
  mirror detection.

---

## STEP 3 — Linear regression (Python)

Survivors from STEP 2 → fit one formula with all features together. Output is
direction + strength + significance per feature.

```python
import pandas as pd
import statsmodels.api as sm

df = pd.read_csv("rides.csv")
y = df["price_pln"]                                # one target per run
drop = ["order_id", "price_pln", "price_per_km",
        "duration_min"]                            # + mirrors removed in STEP 2
X = df.drop(columns=drop)

X = pd.get_dummies(X, drop_first=True).astype(float)    # categoricals → 0/1
num = ["distance_km", "num_stops"]                       # numeric cols only
X[num] = (X[num] - X[num].mean()) / X[num].std()         # standardize → comparable
X = sm.add_constant(X)                                   # intercept b0

model = sm.OLS(y, X).fit()
print(model.summary())
```

Example output:

```
                     coef    std err   P>|t|    [0.025  0.975]
const               24.60    0.20      0.000    24.2    25.0
distance_km         12.80    1.90      0.001     9.0    16.6
pickup_zone_airport  9.00    2.40      0.002     4.2    13.8
part_of_day_evening  3.10    1.10      0.012     0.9     5.3
payment_type_inapp  -0.30    0.50      0.550    -1.3     0.7
```

How to read:

- **Sign** = direction (`+` raises, `−` lowers).
- **coef size** = strength — comparable because numerics are standardized and
  dummies are 0/1.
- **P>|t| < 0.05** = real; **> 0.05** = noise, ignore (e.g. `payment_type_inapp`).
- **[0.025, 0.975]** = confidence range. Narrow = sure; crossing 0 = unsure.
- A dummy coefficient = difference vs the dropped baseline (airport vs center).

Run it: Track 1 twice (`price_pln`, then `price_per_km`), Track 2 once
(`earned_per_online_hour`).

### Warnings

- One target per run.
- Standardize numeric columns only, not dummies.
- 368 rows → every finding is a **hypothesis**. Trust the confidence interval
  over the point estimate, and cross-check against the existing F# group-by
  aggregations in `Calculations.fs`.
- Correlation is not causation.

---

## Flow recap

`F# export (2 CSVs)` → `Python STEP 2 prune mirrors` → `Python STEP 3 regression`
→ read coef + p-value → answer per track.

Status: design agreed. Next = build the two F# CSV exporters (STEP 1).
