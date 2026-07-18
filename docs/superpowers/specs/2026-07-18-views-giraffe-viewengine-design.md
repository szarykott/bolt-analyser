# Views.fs → Giraffe.ViewEngine

**Date**: 2026-07-18
**Status**: Approved

## Goal

Replace hand-rolled string concatenation in `src/Bolt.Web/Views.fs` with the
Giraffe.ViewEngine HTML DSL. Motivation: type safety and composability —
malformed HTML caught at compile time, views composed as F# functions.

Everything else stays: Kestrel, ASP.NET Core minimal API routing, the
WebSocket handler, pipeline construction, htmx-over-WebSocket protocol.

## Scope

One file rewritten: `src/Bolt.Web/Views.fs`. One package added:
`Giraffe.ViewEngine` (standalone NuGet — **not** the Giraffe web framework),
referenced only by `Bolt.Web.fsproj`. Test assertions in
`tests/Bolt.Web.Tests/Views.Tests.fs` updated where rendering differences
break them.

Out of scope: routing changes, `WebSockets.fs`, `Program.fs`, `Jobs/`,
`wwwroot/`, any other project.

## Public API — unchanged

These functions keep their exact signatures and continue to return `string`:

| Function | Signature |
|---|---|
| `indexPage` | `unit -> string` |
| `progressFragment` | `string -> string -> string` |
| `magicLinkFragment` | `string -> string option -> string` |
| `errorFragment` | `string -> string -> string -> string` |
| `reportFragment` | `AnalysisReport -> string` |

Rendering (`RenderView.AsString.htmlNode` for fragments,
`RenderView.AsString.htmlDocument` for the index page) happens inside
`Views.fs`. Consequence: zero edits in `WebSockets.fs` and `Program.fs`.

The public `escape` function is removed — `str` auto-escapes and no code
outside `Views.fs` uses it.

## Internals

- Helpers (`tableHtml`, `chartHtml`, `sectionHtml`) become
  `XmlNode`-returning functions composed with the DSL.
- htmx attributes, which the DSL has no built-ins for:
  - `flag "ws-send"` (valueless attribute)
  - `attr "hx-ext" "ws"`, `attr "ws-connect" "/ws"`
  - `attr "data-plotly-target" chartId`
- Plotly figure JSON must land in the DOM unescaped:
  `rawText (scriptSafeJson c.PlotlyFigureJson)` inside
  `script [ _type "application/json"; … ]`. The `scriptSafeJson` helper
  (`</` → `<\/`) stays to keep the JSON from terminating the script block.
- CSS: `rawText style` inside a `style` element.
- Index page rendered with `RenderView.AsString.htmlDocument`, which emits
  the `<!DOCTYPE html>` preamble.

## Invariants that must hold

- Every fragment's root element is `<div id="panel">` — htmx swaps target
  this id. Existing tests guard it.
- Report fragment contains `id="report-content"` and the
  `downloadReport()` button (used by `wwwroot/app.js`).
- Chart ids follow `chart-{sectionId}-{index}`, matched by the
  `data-plotly-target` attribute on the adjacent JSON script block.
- All user-controlled text (emails, error messages, titles, table cells) is
  HTML-escaped; Plotly JSON is the only raw passthrough and stays
  script-safe.
- Index page keeps the htmx + ws extension + Plotly + `/app.js` script tags
  and the `hx-ext="ws" ws-connect="/ws"` body attributes.

## Testing

- Existing `Views.Tests.fs` assertions are substring-based and mostly
  survive (Giraffe renders `key="value"` attributes conventionally). Fix
  assertions broken by whitespace/formatting differences; do not weaken what
  they assert.
- Full `Bolt.Web.Tests` suite (health, job runner, WebSocket integration)
  must stay green — it exercises the fragments end-to-end.
- Manual check: run the app, load `/`, confirm htmx swap works and a report
  renders with charts.

## Risk

Low. Single-file rewrite behind a stable API, output guarded by existing
tests.
