# Per-Hour Earnings Analysis — Design Notes

Result of a design discussion (2026-07-19) for a per-hour earnings regression,
a sibling of `src/Bolt.ETL/Analysis/PerRide2.fs`. Goal: answer the driver's
question *"what can I do to earn more per hour?"* The driver's levers are:
**when** to work, **where** to position, and accept/decline per offer.
Features must map to those levers.

## Dataset definition

| Element | Decision |
|---|---|
| Row | One clock hour in which the driver worked (had a ride starting or in progress) |
| Target | **Earnings rate**: zł earned in hour ÷ fraction of hour worked (zł per worked hour) |
| Weight | Fill factor = minutes worked in hour / 60, used as WLS weights |
| Ride spanning hours | Earnings and time split proportionally between the hours it touches |

Empirical assumption (verified by the data owner): there are essentially no
fully idle worked hours — every worked hour contains at least one ride. So
"hours with rides" ≈ "hours worked" and no shift/session log is needed.
**If this assumption breaks (e.g. other drivers' data), selection bias
returns — see pitfall 1.**

## Features (tier 2 = full model)

All features are **conditions visible before the hour starts** — never
outcomes the hour produces.

- **District shares** — one numeric column per general district
  (`dzielnica_…`), value = fraction of the hour's worked time spent in that
  district. Columns sum to 1.0 per row. `dzielnica_centrum` dropped as
  baseline (most data, and the natural driver question is "should I leave
  centrum?").
- **Temperature buckets** — dummy columns from the existing
  `TemperatureBucket` (mróz / zimno / umiarkowanie / gorąco), `umiarkowanie`
  dropped as baseline.
- **Booleans (0/1)** — rain, snow, rush hour, weekend, daylight.

**Banned as features:** distance (km) and ride count. They are outcomes of
the hour, not conditions. See pitfall 4.

## Two nested tiers

- **Tier 1**: time + weather features only (~8 coefficients).
- **Tier 2**: tier 1 + district shares (~14 coefficients).

Both regressions run every time. No explicit switch: with little data,
tier-2 district coefficients simply land in the "statistically insignificant"
note (the same p ≥ 0.05 filter `PerRide2.coefficientsTable` already uses).
Statistics itself gates the tiers.

Known accepted limitation: tier-1 coefficients absorb correlated district
effects (a driver who works nights mostly in centrum gets a night coefficient
that smuggles the centrum premium). Accepted consciously — resolves itself
as the driver accumulates tier-2-worthy data.

## Math used

- **OLS (ordinary least squares)**: fits `y = Xβ + ε` minimizing squared
  residuals. Same as PerRide2 (statsmodels in the Python analytics service).
- **WLS (weighted least squares)**: `sm.WLS(y, X, weights=w)` minimizes
  `Σ wᵢ(yᵢ − xᵢβ)²`. Weight = fill factor, so a 10-minute stump row pulls
  the fit 6× weaker than a full hour. Needed because dividing the target by
  fill inflates the variance of short-exposure rows (a rate estimated from
  10 minutes is much noisier than one from 60).
- **Fractional dummy encoding**: standard one-hot generalized to shares in
  [0, 1]. Coefficient of `dzielnica_X` = expected zł/h difference of a *full*
  hour in X versus a full hour in the baseline district.
- **Dummy variable trap**: share columns sum to 1.0, which is perfectly
  collinear with the intercept's constant column → singular design matrix.
  Fix: drop one category per categorical group (it becomes the baseline
  absorbed by the intercept). Same reason `pd.get_dummies(drop_first=True)`
  exists; PerRide2's legend already documents the "pominięta kategoria
  bazowa" reading.
- **Sample size rule of thumb**: ~10–27 rows per coefficient. Tier 2 with
  ~14 coefficients wants ~150–300 worked hours (≈ 2 months full-time) before
  confidence intervals are useful.

## Pitfalls discovered (and their resolutions)

1. **Selection bias from missing idle hours.** If idle worked hours exist
   but produce no rows, per-bucket averages are inflated exactly where the
   driver most needs honesty (night hours). Resolved here only by the
   empirical claim that such hours don't occur in this dataset. Bucketing
   does NOT fix this — it changes granularity, not selection.
2. **Shift-edge partial hours.** First/last hour of a shift offers < 60
   minutes of exposure; raw sums make those hours look weak. Rejected fixes:
   artificial filler rides (any invented price is fabricated data) and
   shift-anchored windows (still leaves one stump per shift). Fix: rate
   target + fill weight.
3. **Exposure as additive feature.** Putting fill factor in as a regular
   feature mis-specifies: exposure scales earnings multiplicatively, not
   additively. Fix: divide the target by fill (normalization), don't add a
   column.
4. **Regressing on outcomes.** Earnings ≈ base·rides + rate·km by Bolt's
   own price list; regressing zł/h on rides/h and km/h just rediscovers the
   cennik with R² ≈ 1 and strangles the lever coefficients (weekend
   coefficient → 0 because its causal channel — more/longer rides — is held
   fixed). Rule: features = what the driver sees *before* the hour.
5. **Amplified noise in short rows.** Rate normalization turns a lucky
   15 zł ride in a 10-minute stump into a 90 zł/h row. Fix: WLS weights.
6. **Dummy trap with shares.** See math section — drop one baseline column
   per categorical group.
7. **Constant-zero columns.** A driver who never left centrum has all other
   district share columns identically 0 → singular matrix, OLS throws or
   returns NaN. Guard: drop all-zero columns before fitting (verify whether
   the analytics service already does this).
8. **Categorical encodings that hide structure.** Encoding the district mix
   as a single categorical string (e.g. "101") makes near-identical hours
   look totally unrelated. Multi-column shares keep the geometry.

## Open items

- WLS support in the Python analytics service (`AnalyticsClient.olsRegression`
  currently has no weights parameter — needs adding).
- Fill-factor computation: worked minutes per hour inferred from ride time
  spans; decide attribution of idle minutes between rides (suggestion:
  attribute to the district of the *next* pickup, since that's where the
  driver positioned).
- Daylight flag definition (fixed hours vs. actual sunrise/sunset —
  Open-Meteo already provides sunrise/sunset if wanted).