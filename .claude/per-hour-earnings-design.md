# Per-Hour Earnings Analysis — Design Notes

Result of a design discussion (2026-07-19, tier ladder refined same day) for
a per-hour earnings regression,
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

## Features (top rung = full model)

All features are **conditions visible before the hour starts** — never
outcomes the hour produces.

- **District shares** — one numeric column per *named* district (Kraków has
  17 + `unknown`), value = fraction of the hour's worked time spent in that
  district. Columns sum to 1.0 per row. `dzielnica_centrum` dropped as
  baseline (most data, and the natural driver question is "should I leave
  centrum?"). Districts without enough exposure are merged into one
  `inne_dzielnice` column (see ladder). **Decided: the manual 8-way
  `getGeneralDistrict` fold from PerRide2 is NOT reused here** — no
  hand-curated intermediate grouping; the exposure floor does the folding
  per driver instead.
- **Temperature buckets** — dummy columns from `TemperatureBucket`,
  **refactored to 6 buckets** (decided; PerRide2 uses the same split):
  boundaries 0 / 10 / 18 / 25 / 30 °C. Baseline `18–25°C` (Mild) dropped.
  **Display labels use the bucket boundary values, not names**: `≤0°C`,
  `0–10°C`, `10–18°C`, `25–30°C`, `>30°C`; the merged low-rung flag is
  `≤0°C LUB >30°C`. Values are more informative for the driver than
  adjectives; labels come from `TemperatureBucket.label`, kept next to
  `fromCelcius` so they can never drift out of sync with the actual
  bucketing.
- **Booleans (0/1)** — rain, snow, rush hour, weekend, daylight.

**Banned as features:** distance (km) and ride count. They are outcomes of
the hour, not conditions. See pitfall 4.

## Tier ladder (coarse-to-fine)

Replaces the earlier two-tier design. Motivation: with two fixed tiers a
driver with 50 hours gets *nothing* — even tier 1 (~8 coefficients) is
underpowered, everything lands in the insignificant note. The ladder gives
an inexperienced driver a useful answer from day one and refines it as data
accumulates.

### Formal shape: partition refinement tree

Every categorical feature is a tree of partitions; climbing a rung splits
one node. Fine models are strict supersets of coarse ones (nested), so each
rung is the previous model with one constraint relaxed.

```
Temperatura:  {wszystkie}
              ├── ≤0°C LUB >30°C                     ← rung 2 (merged extremes)
              │   ├── ≤0°C                           ← rung 4
              │   └── >30°C
              └── baseline {0–30°C}
                  ├── 0–18°C                         ← rung 3
                  │   ├── 0–10°C                     ← rung 4
                  │   └── 10–18°C
                  └── baseline {18–30°C}
                      ├── baseline {18–25°C}         ← rung 4 (final baseline)
                      └── 25–30°C

Dzielnice:    {wszystkie} → {centrum (baseline) | poza centrum}
                          → {centrum | named districts with enough exposure | inne dzielnice}

Opady:        {zła pogoda = deszcz ∪ śnieg} → {deszcz | śnieg}
```

**Baseline chain is fixed down the ladder** (decided):
`{0–30°C} → {18–30°C} → {18–25°C}` — each rung's baseline is a subset of the
previous one, so the "względem przyjemnej pogody" reading stays intuitive
across rung changes. Accepted caveat: 18–30°C has less mass in Kraków than
0–18°C, so a thin baseline widens all temperature CIs somewhat; chosen
anyway for interpretability. Note the dummy trap here: splitting the
baseline node adds only *one* column per split (the other half becomes the
new baseline) — rung 3 adds the `0–18°C` column, `18–30°C` is absorbed by
the intercept.

Known caveat of the merged temperature flag: it merges both ends of the
demand U-curve. Effects are expected same-direction (both raise demand), but
if they disagreed in sign they would cancel inside the merged flag — a ≈0
coefficient can mean "no effect" or "offsetting effects". Rung 4 resolves
the ambiguity. Accepted at low n.

### Rungs (~15 rows per coefficient)

| Rung | Adds | ~Coefs | Unlocks at |
| --- | --- | --- | --- |
| 0 | grouped-means table (weekend / dzień-noc), no regression | — | 0 h |
| 1 | weekend, rush hour, zła pogoda | 4 | ~60 h |
| 2 | + daylight, `≤0°C LUB >30°C`, poza centrum | 7 | ~105 h |
| 3 | + `0–18°C` (baseline shrinks to `18–30°C`), deszcz/śnieg split | 9 | ~135 h |
| 4 | + full 6-bucket temperature split (`≤0°C`, `>30°C`, `0–10°C`, `10–18°C`, `25–30°C`; baseline `18–25°C`) and named district shares — each district peels out of `inne dzielnice` individually once its exposure floor is met | 12 + unlocked districts (up to ~29) | dynamic, per district |

Hour values in the table are *illustrative*, not hardcoded. **Unlock
thresholds are computed dynamically at runtime**: a rung unlocks when
`n ≥ 15 × (coefficients of the candidate rung)`, where the candidate
coefficient count itself depends on the driver's data (which districts pass
the exposure floor). Rung 4 is not a single unlock moment — the full
temperature split unlocks with the global budget, while the district section
grows district-by-district as exposure accumulates, so its teaser is
per-district: „dzielnica Podgórze odblokuje się po ~12 godzinach jazdy w
Podgórzu". The 6-way temperature split deliberately lands in rung 4 rather
than its own rung — no need to tease the driver with too many intermediate
steps.

Order logic: balanced booleans first (weekend splits data ~30/70, fastest to
significance); rare buckets and many-category groups last. Rung 2 already
answers the biggest driver question ("czy opłaca się wyjeżdżać z centrum?")
at ~105 h.

### Promotion rule — two gates, both ex-ante

1. **Global budget**: `n ≥ 15 × number of coefficients` of the candidate rung.
2. **Per-column exposure floor**: each new column needs own support —
   dummies: `Σ fill` within the bucket ≥ ~20 effective hours; shares:
   `Σ share·fill` ≥ ~20. Without this a 300-hour centrum-only driver would
   unlock district columns backed by 2 h in Mokotów.

A column failing the exposure floor does not block the rung — it stays
merged in its parent. For districts, under-floor districts collapse into a
single `inne dzielnice` column. **This also means the coefficient count is
naturally variable per driver** (a driver who never visits some districts
simply has them folded into `inne dzielnice`), and it subsumes pitfall 7:
a never-visited district has zero exposure, gets merged, and can never
produce a singular matrix.

Rejected alternative: promoting on an F-test (does the split improve fit).
Statistically principled but answers the wrong question — "does the split
help fit", not "is the estimate stable enough to show the driver" — and
makes unlock thresholds unpredictable, killing the roadmap display.

### Coefficient stability across rungs

Climbing a rung changes coefficient meanings (weekend +15 zł/h at rung 1,
absorbing the centrum premium, may become +9 at rung 4). This generalizes
the previously accepted tier-1 limitation: coarse-rung coefficients absorb
correlated effects of not-yet-unlocked features; they are directional, not
exact, and that is worst exactly when the new driver relies on them.
Handling:

- Show only the current rung's coefficients — never side-by-side with
  historical values.
- On a rung change, a note **explains why the numbers moved** (the model now
  separates an effect that was previously blended in) **and states at how
  many hours each rung starts being significant** — the unlock table,
  computed dynamically from this driver's data, so the driver sees where
  they are on the ladder.
- Dataset is **all-history** (decided), so n only grows and rungs only
  unlock — no demotion, no hysteresis machinery needed.

### Display rules

- **Run all rungs every time** (decided); display gating is separate from
  computation.
- **Everything already unlocked stays visible** (decided): the rung-0 table
  is kept permanently alongside the regressions (grouped means are
  complementary, not inferior), and lower unlocked rungs remain available.
- The p ≥ 0.05 filter (as in `PerRide2.coefficientsTable`) still gates
  display *within* a rung — the ladder gates which columns enter the model,
  significance gates which coefficients are presented as reliable.
- Locked rungs are shown as a teaser: „analiza dzielnic odblokuje się za
  ~55 godzin". **All copy explaining locks and coefficient shifts must pass
  the five-year-old test**: e.g. „mamy za mało godzin z mrozem, żeby uczciwie
  policzyć, ile mróz dodaje — na razie liczymy mróz i upał razem". No
  statistics vocabulary in driver-facing explanations.

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
- **Sample size rule of thumb**: ~10–27 rows per coefficient (ladder uses
  ~15). The top rung with ~12–29 coefficients (depending on unlocked
  districts) wants ~180–450 worked hours before all confidence intervals are
  useful — but the per-district unlocking means the driver never waits for
  the full set at once.

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
   returns NaN. Subsumed by the ladder's per-column exposure floor: a
   zero-exposure district never enters as its own column, it stays merged in
   `inne dzielnice` (or in `poza centrum` at lower rungs).
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