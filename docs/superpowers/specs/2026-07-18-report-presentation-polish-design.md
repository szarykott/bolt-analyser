# Report presentation cleanup + Polish UI — design

## Context

The web report (Bolt ride analysis) is noisy. The user cares about two results only: the price-per-ride OLS regression and the ride clustering. Today the report renders three sections — "Ride statistics" (PerRide1), "Price regression" (PerRide2, four raw statistical tables), "Pickup clusters" — all in English. The regression dumps every coefficient (significant or not, unsorted, 7 columns) plus VIF and group-means diagnostics that mean nothing to the end user. The site targets Polish users, so the whole web UI must be in Polish.

**Constraint: display-only change.** The python analytics service and the OLS/clustering computation style stay untouched. All shaping happens where display tables are built (F# section builders) and in `Views.fs`. One data-shape exception, requested explicitly: the column names (and temperature-bucket values) sent to the analytics service become Polish, so coefficient names return Polish without any display-side mapping. The computation itself is unaffected.

## Decisions (confirmed during brainstorming)

- Drop the PerRide1 "Ride statistics" section from the report (code stays, just not called).
- Regression section keeps two tables: cleaned coefficients + model fit. VIF and group-means tables are removed (and the now-unused `mirrorCheck` call with them — its output would be dead).
- Coefficients table: only statistically significant rows (p < 0.05), intercept (`const`) excluded, sorted by |coef| descending. Insignificant features listed by (Polish) name in one sentence below the table.
- Columns: `cecha | współczynnik [zł] | przedział ufności 95% | istotność (p)` (CI as one "od–do" column). Drop std err and t.
- Column explanations: always-visible legend under the table (definition list), not tooltips.
- Whole UI in Polish, `lang="pl"`. Regression feature names are Polish at the source: `toAnalyticsRows` emits Polish column keys (and Polish temperature-bucket values), so the OLS response returns Polish coefficient names — no display-side mapping dictionary.
- Numbers displayed with Polish decimal comma (`pl-PL` culture); p-values shown as `< 0,001` when tiny, else 3 decimals; coefficients 2 decimals.

## Interpretation facts

Verified in `python-analytics/analytics/core/regression.py`: the target (`cena_pln`) is NOT standardized, only numeric features are (here: `dystans_km` only; bools and dummies stay 0/1). So every coefficient is in PLN; distance's coefficient is PLN per 1 standard deviation of distance; bool/dummy coefficients are PLN difference vs. the baseline category. Categoricals are one-hot encoded with `pd.get_dummies(drop_first=True)` — pandas `column_value` names, alphabetically first category dropped as baseline. The legend must say this.

## Changes by file

### `src/Bolt.ETL/Analysis/Report.fs`
Add `Notes: string list` to `ResultTable` (legend lines rendered under the table). All existing table constructions get `Notes = []` unless stated below.

### `src/Bolt.ETL/Analysis/PerRide2.fs`
- Polish names at the source, in `toAnalyticsRows` (no display-side name mapper):
  - column keys: `price_pln` → `cena_pln`, `distance_km` → `dystans_km`, `is_rush_hour` → `godziny_szczytu`, `is_weekend` → `weekend`, `rain` → `deszcz`, `snow` → `śnieg`, `pickup_district` → `dzielnica` (district values already Polish), `temperature_bucket` → `temperatura`;
  - temperature values Polish via a `TemperatureBucket → string` function (`src/Bolt.ETL/Meteo/Model.fs` cases): `Frost` → `mróz`, `Cold` → `zimno`, `Mild` → `umiarkowanie`, `Hot` → `gorąco`;
  - OLS request `Target = "cena_pln"`.
  - The service one-hot encodes via `pd.get_dummies` (pandas `column_value` naming, unicode-safe), so coefficient names come back as e.g. `dystans_km`, `dzielnica_centrum`, `temperatura_mróz` and are displayed as returned.
- Rewrite `coefficientsTable`:
  - exclude `const`; partition by `PValue < 0.05` (treat `None` p-value as insignificant);
  - significant rows sorted by `abs Coef` descending; 4 columns as decided; CI as `od–do` string;
  - `Notes`: one line per column explaining meaning + how to read it, the standardization/PLN interpretation note (incl. that `dzielnica_…` / `temperatura_…` rows are differences vs. the omitted baseline category), and the sentence `Cechy statystycznie nieistotne (p ≥ 0,05): …` (or a "none" variant when all are significant).
- Rewrite `modelStatsTable` → model fit table: `liczba przejazdów`, `R²`, `skorygowane R²`; note translating R² to plain Polish ("model wyjaśnia X% zmienności ceny").
- Delete `vifTable` and `groupMeansTable`; in `buildSection` drop the `AnalyticsClient.mirrorCheck` call; section returns the two tables. Title `Regresja ceny przejazdu`, Polish description.
- Number formatting via `CultureInfo("pl-PL")` for display values (existing `fmtOpt` reworked; `–` placeholder for missing stays).

### `src/Bolt.ETL/Analysis/RideClustering.fs`
Strings only, logic untouched: section title `Skupiska odbiorów pasażerów`, Polish description, table title/headers (`skupisko | liczba przejazdów | szer. geogr. | dł. geogr. | średnia godzina`), chart title in Polish. Coordinate values may keep invariant dot formatting (they are coordinates, not prose numbers) — acceptable either way; prefer pl-PL consistency where cheap.

### `src/Bolt.ETL/AnalysisPipeline.fs`
- Remove the `perRide1` build; `Sections = [ perRide2; clustering ]`. `PerRide1.fs` module stays in the project.
- Translate the empty-weather fallback description suffix and error messages that reach the UI (`No rides found for …`, `Analysis failed: …`) to Polish.

### `src/Bolt.Web/Views.fs`
- `_lang "pl"`, `<title>` and `h1`: `Analiza przejazdów Bolt`.
- All fragments Polish: form label (`Adres e-mail kierowcy Bolt:`), submit button, magic-link instructions + `Zaloguj się`, error fragment (`Analiza nie powiodła się na etapie …` + `Spróbuj ponownie`), report header (`X przejazdów między … a …, wygenerowano … UTC`), download button (`Pobierz raport`).
- `tableNodes` renders `t.Notes` as a small-print list (`<ul class="notes">` or `<dl>`) under the table; add minimal CSS for it.
- Check callers of `progressFragment` (`Pipeline.fs` / `WebSockets.fs`) for English state/detail strings reaching the UI — translate those call-site strings too (display strings only).

### Tests
- `tests/Bolt.ETL.Tests/PerRide2.Tests.fs`: rewrite for new behavior — Polish column keys and bucket values in `toAnalyticsRows`, filtering by p, |coef| sort order, intercept exclusion, insignificant-features note, model-fit table content; delete VIF/group-means tests.
- `tests/Bolt.Web.Tests/Views.Tests.fs`: update expected strings (Polish), add Notes-rendering assertion.
- `tests/Bolt.ETL.Tests/RideClustering.Tests.fs`: update expected strings.

## Verification

1. `dotnet build` clean.
2. `dotnet test` — updated unit tests pass (table shaping is fully unit-testable with canned `OlsResponse`).
3. End-to-end eyeball: run Bolt.Web + python analytics service, generate a report, confirm: only 2 sections, coefficient table sorted/filtered with legend, insignificant features named in a sentence, everything Polish, decimal commas.
