# Views.fs → Giraffe.ViewEngine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace hand-rolled string concatenation in `src/Bolt.Web/Views.fs` with the Giraffe.ViewEngine HTML DSL, keeping the public API and rendered semantics identical.

**Architecture:** `Views.fs` keeps its five public `string`-returning functions; internally each builds an `XmlNode` tree and renders with `RenderView.AsString`. Consumers (`WebSockets.fs`, `Program.fs`) are untouched. This is a green-to-green refactor: existing tests define the contract and must pass before and after each task.

**Tech Stack:** F# / .NET 10, ASP.NET Core minimal API + Kestrel (unchanged), `Giraffe.ViewEngine` NuGet package (standalone DSL — NOT the Giraffe web framework), xUnit.

**Spec:** `docs/superpowers/specs/2026-07-18-views-giraffe-viewengine-design.md`

## Global Constraints

- Only `src/Bolt.Web/Bolt.Web.fsproj` gets the new package reference; no other project changes.
- Public signatures unchanged: `indexPage: unit -> string`, `progressFragment: string -> string -> string`, `magicLinkFragment: string -> string option -> string`, `errorFragment: string -> string -> string -> string`, `reportFragment: AnalysisReport -> string`.
- Every fragment's root element renders as `<div id="panel">` (htmx swap target).
- Plotly figure JSON is embedded raw (`rawText`) but passed through `scriptSafeJson` (`</` → `<\/`).
- **Giraffe.ViewEngine gotcha:** `str` HTML-encodes text nodes, but attribute values are rendered raw. Any user-controlled attribute value (the email in hidden inputs) must go through `escapeAttr` (= `WebUtility.HtmlEncode`).
- Do not weaken existing test assertions; only adjust them if rendering formatting genuinely differs while semantics hold.
- Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Add package, convert progress / magic-link / error fragments

**Files:**
- Modify: `src/Bolt.Web/Bolt.Web.fsproj` (via `dotnet add`)
- Modify: `src/Bolt.Web/Views.fs` (header + three fragment functions; `indexPage` and report helpers stay string-based for now)
- Test: `tests/Bolt.Web.Tests/Views.Tests.fs` (existing tests, unchanged)

**Interfaces:**
- Consumes: `Bolt.ETL.Analysis` types (already referenced).
- Produces for later tasks: private helpers `escapeAttr: string -> string`, `render: XmlNode -> string` (= `RenderView.AsString.htmlNode`), `panel: XmlNode list -> XmlNode` (= `div [ _id "panel" ]`), and unchanged private `scriptSafeJson: string -> string`. Tasks 2 and 3 use these exact names.

- [ ] **Step 1: Baseline — run existing view tests, confirm green**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~ViewsTests"`
Expected: PASS (4 tests). If red, stop — fix the baseline first, don't refactor on red.

- [ ] **Step 2: Add the package**

```bash
dotnet add src/Bolt.Web/Bolt.Web.fsproj package Giraffe.ViewEngine
```

Expected: `PackageReference Include="Giraffe.ViewEngine"` appears in `Bolt.Web.fsproj`. This is the standalone view DSL; it does not pull in the Giraffe web framework.

- [ ] **Step 3: Rewrite the file header and the three simple fragments**

In `src/Bolt.Web/Views.fs`, replace everything from `module Bolt.Web.Views` down to and including the `let escape ...` and `scriptSafeJson` lines with:

```fsharp
module Bolt.Web.Views

open System.Net
open Bolt.ETL.Analysis
open Giraffe.ViewEngine

// Still used by the string-based report helpers; deleted in Task 2.
let escape (s: string) = WebUtility.HtmlEncode s

// Giraffe.ViewEngine encodes text nodes (str), but renders attribute
// values raw — user-controlled attribute values go through here.
let private escapeAttr (s: string) = WebUtility.HtmlEncode s

// JSON that lands inside a <script> block must not terminate it early.
let private scriptSafeJson (json: string) = json.Replace("</", "<\\/")

let private render = RenderView.AsString.htmlNode

let private panel = div [ _id "panel" ]
```

Note: the existing `open System` can be dropped only if nothing else in the file uses it — the string-based `indexPage` still present after this task does not, but check before removing. Keep `let private style = ...` and `let private indexBody = ...` and `let indexPage ...` exactly as they are (the `style` string value shadows Giraffe's `style` element function — harmless until Task 3, where it is renamed).

Then replace the three fragment functions (`progressFragment`, `magicLinkFragment`, `errorFragment`) with:

```fsharp
let progressFragment (stateText: string) (detail: string) =
    panel [
        p [] [ strong [] [ str stateText ]; str (" " + detail) ]
        progress [] []
    ]
    |> render

let magicLinkFragment (email: string) (error: string option) =
    let errorNode =
        error |> Option.map (fun e -> p [ _class "error" ] [ str e ]) |> Option.toList

    panel [
        yield p [] [ str "Check your e-mail for a login message from Bolt, then paste the link from it below." ]
        yield! errorNode
        yield form [ flag "ws-send" ] [
            input [ _type "hidden"; _name "msgType"; _value "magic-link" ]
            input [ _type "hidden"; _name "email"; _value (escapeAttr email) ]
            label [] [ str "Magic link URL: "; input [ _type "text"; _name "url"; _required ] ]
            button [ _type "submit" ] [ str "Log in" ]
        ]
    ]
    |> render

let errorFragment (email: string) (step: string) (message: string) =
    panel [
        p [ _class "error" ] [ str $"Analysis failed at {step}: {message}" ]
        form [ flag "ws-send" ] [
            input [ _type "hidden"; _name "msgType"; _value "start-analysis" ]
            input [ _type "hidden"; _name "email"; _value (escapeAttr email) ]
            button [ _type "submit" ] [ str "Retry" ]
        ]
    ]
    |> render
```

DSL notes for the unfamiliar:
- `flag "ws-send"` renders a valueless attribute (`<form ws-send>`), which is what htmx expects.
- `_required` is also a flag-style helper (renders `required`).
- `str` produces an HTML-encoded text node; that is why the old explicit `escape` calls disappear.
- The `yield` / `yield!` forms in `magicLinkFragment` are needed because the error paragraph is zero-or-one nodes.

The report helpers (`tableHtml`, `chartHtml`, `sectionHtml`, `reportFragment`) stay string-based and keep compiling because `escape` still exists.

- [ ] **Step 4: Build and run view tests**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~ViewsTests"`
Expected: PASS (4 tests). The fragment tests assert `StartsWith "<div id=\"panel\">"`, `Contains "value=\"a@b.pl\""`, `Contains "magic-link"`, `Contains "bad &lt;token&gt;"` — Giraffe renders `key="value"` attributes and encodes text nodes with `WebUtility.HtmlEncode`, so all hold.

If a test fails on formatting only (whitespace/attribute order) while the semantic content is present, update the assertion to match the new rendering without weakening what it checks. Anything else failing = investigate, don't paper over.

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.Web/Bolt.Web.fsproj src/Bolt.Web/Views.fs
git commit -m "refactor: progress/magic-link/error fragments via Giraffe.ViewEngine

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Convert the report fragment

**Files:**
- Modify: `src/Bolt.Web/Views.fs` (replace `tableHtml`, `chartHtml`, `sectionHtml`, `reportFragment`; delete `escape`)
- Test: `tests/Bolt.Web.Tests/Views.Tests.fs` (existing tests, unchanged)

**Interfaces:**
- Consumes from Task 1: private `escapeAttr`, `render`, `panel`, `scriptSafeJson` (exact names).
- Produces: `reportFragment: AnalysisReport -> string` (public signature unchanged); private `tableNodes: ResultTable -> XmlNode list`, `chartNodes: string -> int -> ResultChart -> XmlNode list`, `sectionNode: AnalysisSection -> XmlNode`.

- [ ] **Step 1: Replace the report helpers and fragment**

In `src/Bolt.Web/Views.fs`, delete `let escape ...` (its last users go away in this step), then replace `tableHtml`, `chartHtml`, `sectionHtml`, and `reportFragment` with:

```fsharp
let private tableNodes (t: ResultTable) = [
    h4 [] [ str t.Title ]
    table [] [
        thead [] [ tr [] [ for h in t.Headers -> th [] [ str h ] ] ]
        tbody [] [ for r in t.Rows -> tr [] [ for c in r -> td [] [ str c ] ] ]
    ]
]

let private chartNodes (sectionId: string) (index: int) (c: ResultChart) =
    let chartId = $"chart-{sectionId}-{index}"

    [
        h4 [] [ str c.Title ]
        div [ _id chartId; _style "width:100%;height:800px" ] []
        script [ _type "application/json"; attr "data-plotly-target" chartId ] [
            rawText (scriptSafeJson c.PlotlyFigureJson)
        ]
    ]

let private sectionNode (s: AnalysisSection) =
    let charts = s.Charts |> List.mapi (chartNodes s.Id) |> List.concat
    let tables = s.Tables |> List.collect tableNodes

    section [ _id s.Id ]
        ([ h2 [] [ str s.Title ]; p [] [ str s.Description ] ] @ charts @ tables)

let reportFragment (report: AnalysisReport) =
    let fromDate, toDate = report.DateRange

    let header = [
        h1 [] [ str $"Bolt ride analysis — {report.Email}" ]
        p [] [
            str (
                $"""{report.RideCount} rides between {fromDate.ToString "yyyy-MM-dd"} and {toDate.ToString "yyyy-MM-dd"}, """
                + $"""generated {report.GeneratedAt.ToString "yyyy-MM-dd HH:mm"} UTC."""
            )
        ]
    ]

    panel [
        div [ _id "report-content" ] (header @ (report.Sections |> List.map sectionNode))
        button [ _onclick "downloadReport()" ] [ str "Download report" ]
    ]
    |> render
```

Notes:
- The old chart div had `style="width:100%;height:800px"` written as `100%%` only because of interpolated-string escaping; `_style "width:100%;height:800px"` is the same output.
- `rawText` is deliberate and only used for the Plotly JSON (script-safe via `scriptSafeJson`) — everything else is `str`-encoded.
- Section/chart ids come from analysis code, not users; they are used unescaped in attributes, same as before.
- Interpolated strings holding `ToString "yyyy-MM-dd"` calls need the triple-quote `$"""..."""` form (a plain `$"..."` can't contain string literals inside `{}`).

- [ ] **Step 2: Build and run view tests**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~ViewsTests"`
Expected: PASS (4 tests). Key assertions: `Contains "report-content"`, `Contains "data-plotly-target=\"chart-s1-0\""`, raw figure JSON present verbatim, `Contains "Section &lt;1&gt;"`, `Contains "&lt;cell&gt;"`, `Contains "downloadReport()"`, and `DoesNotContain "</script>\"}"` (script-safe JSON). Same formatting-only rule as Task 1 Step 4 applies.

- [ ] **Step 3: Commit**

```bash
git add src/Bolt.Web/Views.fs
git commit -m "refactor: report fragment via Giraffe.ViewEngine

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Convert the index page

**Files:**
- Modify: `src/Bolt.Web/Views.fs` (replace `style`/`indexBody`/`indexPage`; final DSL-only file)
- Modify: `tests/Bolt.Web.Tests/Views.Tests.fs` (add index page test)

**Interfaces:**
- Consumes from Task 1: private `panel`; Giraffe `RenderView.AsString.htmlDocument`.
- Produces: `indexPage: unit -> string` (public signature unchanged).

- [ ] **Step 1: Add a guard test for the index page**

Append to `tests/Bolt.Web.Tests/Views.Tests.fs`:

```fsharp
[<Fact>]
let ``index page wires htmx websocket and panel`` () =
    let html = Views.indexPage ()
    Assert.StartsWith("<!DOCTYPE html>", html)
    Assert.Contains("hx-ext=\"ws\"", html)
    Assert.Contains("ws-connect=\"/ws\"", html)
    Assert.Contains("<div id=\"panel\">", html)
    Assert.Contains("start-analysis", html)
    Assert.Contains("/app.js", html)
```

- [ ] **Step 2: Run it against the OLD string implementation — must pass**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~ViewsTests"`
Expected: PASS (5 tests). This pins the contract before the rewrite. (Refactor variant of TDD: the new test must be green on the old code, and stay green on the new.)

- [ ] **Step 3: Rewrite the index page in the DSL**

In `src/Bolt.Web/Views.fs`: delete `indexBody`, rename the `style` CSS string to `css` (it currently shadows Giraffe's `style` element, which this step starts using), and replace `indexPage`:

```fsharp
let private css = """
body { font-family: sans-serif; max-width: 1100px; margin: 2rem auto; padding: 0 1rem; }
table { border-collapse: collapse; margin: 1rem 0; }
th, td { border: 1px solid #ccc; padding: 0.3rem 0.7rem; text-align: left; }
.error { color: #b00; }
"""

let indexPage () =
    html [ _lang "en" ] [
        head [] [
            meta [ _charset "utf-8" ]
            title [] [ str "Bolt ride analysis" ]
            meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
            script [ _src "https://unpkg.com/htmx.org@1.9.12" ] []
            script [ _src "https://unpkg.com/htmx.org@1.9.12/dist/ext/ws.js" ] []
            script [ _src "https://cdn.plot.ly/plotly-2.32.0.min.js" ] []
            script [ _src "/app.js" ] []
            style [] [ rawText css ]
        ]
        body [ attr "hx-ext" "ws"; attr "ws-connect" "/ws" ] [
            h1 [] [ str "Bolt ride analysis" ]
            panel [
                form [ flag "ws-send" ] [
                    input [ _type "hidden"; _name "msgType"; _value "start-analysis" ]
                    label [] [
                        str "Bolt driver e-mail: "
                        input [ _type "email"; _name "email"; _required; _placeholder "driver@example.com" ]
                    ]
                    button [ _type "submit" ] [ str "Make an analysis for me" ]
                ]
            ]
        ]
    ]
    |> RenderView.AsString.htmlDocument
```

Notes:
- `RenderView.AsString.htmlDocument` emits the `<!DOCTYPE html>` preamble; fragments keep using `render` (`htmlNode`).
- `css` and `indexPage` must be defined before any use; F# compiles top-down. Put `css` where the old `style` string was.
- After this step the file contains no string-built HTML. Check that `open System` (if still present) is actually needed; drop dead opens.

- [ ] **Step 4: Run view tests**

Run: `dotnet test tests/Bolt.Web.Tests --filter "FullyQualifiedName~ViewsTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Bolt.Web/Views.fs tests/Bolt.Web.Tests/Views.Tests.fs
git commit -m "refactor: index page via Giraffe.ViewEngine, drop string templates

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Full verification

**Files:**
- No planned changes; fix-ups only if verification finds breakage.

**Interfaces:**
- Consumes: the finished `Views` module through the whole app (WebSocket integration tests exercise fragments end-to-end).

- [ ] **Step 1: Run the entire solution test suite**

Run: `dotnet test`
Expected: PASS across all projects. `Bolt.Web.Tests` WebSocket integration tests assert loose substrings (`report-content`, `a@b.pl`, `Start the analysis with your e-mail address first.`) that the DSL output still contains; `JobRunner` tests never touch HTML. Failures elsewhere are unrelated to this refactor — investigate before assuming.

- [ ] **Step 2: Boot the app and eyeball the index page**

```bash
SkipStartupDistricts=true dotnet run --project src/Bolt.Web &
sleep 5
curl -s http://localhost:5000 | head -40
kill %1
```

Expected in the output: `<!DOCTYPE html>`, `hx-ext="ws"`, `ws-connect="/ws"`, `<div id="panel">`, the htmx/Plotly/`app.js` script tags. (Port may differ — read the `Now listening on:` line from the run output and adjust the curl.)

- [ ] **Step 3: Commit any verification fix-ups (only if Step 1–2 required changes)**

```bash
git add -A
git commit -m "test: adjust view assertions to Giraffe.ViewEngine rendering

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

If nothing changed, skip the commit.
