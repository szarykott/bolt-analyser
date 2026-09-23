# Repository Guidelines

## Project Structure & Module Organization

`Bolt.slnx` groups the .NET 10 F# projects. `src/Bolt.Models` defines domain records; `Bolt.Infrastructure` handles storage and repositories; `Bolt.Scraper` fetches Bolt and weather data; `Bolt.ETL` builds analyses; `Bolt.Reporter` produces reports; and `Bolt.Web` hosts the UI and WebSocket workflow. Matching xUnit projects live under `tests/`. The separate Python service lives in `python-analytics/`; follow its own `AGENTS.md` for package-specific guidance. `bruno/` holds API collections, `docs/superpowers/` holds design notes and plans, and `src/Bolt.Web/wwwroot/` holds browser assets.

## Build, Test, and Development Commands

- `dotnet build Bolt.slnx` compiles all F# projects from the repository root.
- `dotnet test Bolt.slnx` runs all .NET tests; `dotnet test tests/Bolt.ETL.Tests` runs one project.
- `dotnet run --project src/Bolt.Web` starts the web app. Start analytics separately with `cd python-analytics && uv sync && uv run uvicorn analytics.api.main:app --port 8000` when testing the full workflow.
- `docker compose up --build` builds and starts both applications in Docker.
- `cd python-analytics && uv run pytest` runs Python tests.

## Coding Style & Naming Conventions

Match nearby F# formatting: four-space indentation, `PascalCase` modules and types, and `camelCase` functions and values. Keep F# source order aligned with `<Compile Include>` entries in each `.fsproj`; compilation depends on that order. Name test files `*.Tests.fs`. Python code uses four spaces, `snake_case` functions and modules, and `PascalCase` models. No repository-wide formatter or linter is configured; keep edits consistent with surrounding code.

## Testing Guidelines

Use xUnit `[<Fact>]` tests in `tests/Bolt.*.Tests/` and pytest `test_*.py` files in `python-analytics/tests/`. Add focused tests for changed behavior, especially analysis calculations, serialization, and HTTP contracts. Run affected test projects, then `dotnet test Bolt.slnx`; run Python tests when analytics code or its F# client changes. No coverage threshold is configured.

## Commit & Pull Request Guidelines

Recent commits usually use short subjects such as `feat: add ...`, `fix: correct ...`, `docs: update ...`, or `refactor: extract ...`. Keep commits focused. In pull requests, explain behavior and affected components, link related issues when available, list test commands and results, and include screenshots for UI changes.

## Configuration & Secrets

`src/Bolt.Web/appsettings.json` is ignored by Git. Keep local service URLs and credentials out of commits. The web app reads `Analytics:BaseUrl`; the Python service defaults to port 8000 in local instructions.
