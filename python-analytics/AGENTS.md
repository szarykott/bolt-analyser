# Repository Guidelines

## Project Structure & Module Organization

`analytics/core/` contains the shared data-frame, regression, clustering, and diagnostic logic. `analytics/api/` defines FastAPI routes and Pydantic request/response models; `analytics/cli/` contains the three command-line entry points. Keep statistical work in `core` so the API and CLIs remain thin adapters. Tests live in `tests/` and currently cover weighted regression at the core and HTTP layers. There are no committed asset files; the clustering CLI writes CSV and PNG output beside its input.

## Build, Test, and Development Commands

- `uv sync` installs the Python 3.12+ project and development dependencies from `uv.lock`.
- `uv run pytest` runs the test suite.
- `uv run uvicorn analytics.api.main:app --port 8000` starts the service; check `GET /health` for a quick smoke test.
- `uv run st-dbscan path/to/rides.csv`, `uv run linear-regression path/to/rides.csv`, and `uv run mirror-check path/to/rides.csv` run the installed CLIs. Consult `README.md` for options and CSV defaults.
- `uv build` builds the distributable package through Hatchling.

## Coding Style & Naming Conventions

Use four-space indentation, `snake_case` for modules/functions/variables, and `PascalCase` for Pydantic models. Follow the existing type-hint and docstring style. Put input contracts in `analytics/api/models.py`, HTTP handling in `analytics/api/main.py`, and reusable computation in `analytics/core/`. No formatter or linter is configured in `pyproject.toml`; keep changes consistent with nearby code.

## Testing Guidelines

Use pytest. Name files `test_*.py` and test functions `test_*`, as in `tests/test_regression_wls.py`. Add core tests for numerical behavior and API tests for request validation and response status when changing an endpoint. Run `uv run pytest` before submitting. No coverage threshold is configured.

## Commit & Pull Request Guidelines

Recent commits use short, imperative subjects prefixed with `feat:`, `fix:`, or `docs:`; follow that pattern. In pull requests, describe the behavior changed, mention any affected endpoint or CLI, and include test results. Link an issue when one exists; include screenshots only for visible UI or plot changes.
