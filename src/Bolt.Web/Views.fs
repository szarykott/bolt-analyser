module Bolt.Web.Views

open System
open Bolt.ETL.Analysis

let escape (s: string) = System.Net.WebUtility.HtmlEncode s

// JSON that lands inside a <script> block must not terminate it early.
let private scriptSafeJson (json: string) = json.Replace("</", "<\\/")

let private style = """
body { font-family: sans-serif; max-width: 1100px; margin: 2rem auto; padding: 0 1rem; }
table { border-collapse: collapse; margin: 1rem 0; }
th, td { border: 1px solid #ccc; padding: 0.3rem 0.7rem; text-align: left; }
.error { color: #b00; }
"""

let private indexBody = """
<h1>Bolt ride analysis</h1>
<div id="panel">
  <form ws-send>
    <input type="hidden" name="msgType" value="start-analysis">
    <label>Bolt driver e-mail:
      <input type="email" name="email" required placeholder="driver@example.com">
    </label>
    <button type="submit">Make an analysis for me</button>
  </form>
</div>
"""

let indexPage () =
    "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>Bolt ride analysis</title>\n"
    + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
    + "<script src=\"https://unpkg.com/htmx.org@1.9.12\"></script>\n"
    + "<script src=\"https://unpkg.com/htmx.org@1.9.12/dist/ext/ws.js\"></script>\n"
    + "<script src=\"https://cdn.plot.ly/plotly-2.32.0.min.js\"></script>\n"
    + "<script src=\"/app.js\"></script>\n"
    + $"<style>{style}</style>\n</head>\n"
    + "<body hx-ext=\"ws\" ws-connect=\"/ws\">\n"
    + indexBody
    + "\n</body>\n</html>"

let progressFragment (stateText: string) (detail: string) =
    $"""<div id="panel"><p><strong>{escape stateText}</strong> {escape detail}</p><progress></progress></div>"""

let magicLinkFragment (email: string) (error: string option) =
    let errorHtml =
        error
        |> Option.map (fun e -> $"""<p class="error">{escape e}</p>""")
        |> Option.defaultValue ""

    $"""<div id="panel">
<p>Check your e-mail for a login message from Bolt, then paste the link from it below.</p>
{errorHtml}
<form ws-send>
  <input type="hidden" name="msgType" value="magic-link">
  <input type="hidden" name="email" value="{escape email}">
  <label>Magic link URL: <input type="text" name="url" required></label>
  <button type="submit">Log in</button>
</form>
</div>"""

let errorFragment (email: string) (step: string) (message: string) =
    $"""<div id="panel">
<p class="error">Analysis failed at {escape step}: {escape message}</p>
<form ws-send>
  <input type="hidden" name="msgType" value="start-analysis">
  <input type="hidden" name="email" value="{escape email}">
  <button type="submit">Retry</button>
</form>
</div>"""

let private tableHtml (t: ResultTable) =
    let ths = t.Headers |> List.map (fun h -> $"<th>{escape h}</th>") |> String.concat ""

    let rows =
        t.Rows
        |> List.map (fun r ->
            "<tr>" + (r |> List.map (fun c -> $"<td>{escape c}</td>") |> String.concat "") + "</tr>")
        |> String.concat "\n"

    $"""<h4>{escape t.Title}</h4><table><thead><tr>{ths}</tr></thead><tbody>{rows}</tbody></table>"""

let private chartHtml (sectionId: string) (index: int) (c: ResultChart) =
    let chartId = $"chart-{sectionId}-{index}"

    $"""<h4>{escape c.Title}</h4>
<div id="{chartId}" style="width:100%%;height:800px"></div>
<script type="application/json" data-plotly-target="{chartId}">{scriptSafeJson c.PlotlyFigureJson}</script>"""

let private sectionHtml (s: AnalysisSection) =
    let charts = s.Charts |> List.mapi (chartHtml s.Id) |> String.concat "\n"
    let tables = s.Tables |> List.map tableHtml |> String.concat "\n"

    $"""<section id="{s.Id}">
<h2>{escape s.Title}</h2>
<p>{escape s.Description}</p>
{charts}
{tables}
</section>"""

let reportFragment (report: AnalysisReport) =
    let sections = report.Sections |> List.map sectionHtml |> String.concat "\n"
    let fromDate, toDate = report.DateRange

    $"""<div id="panel">
<div id="report-content">
<h1>Bolt ride analysis — {escape report.Email}</h1>
<p>{report.RideCount} rides between {fromDate.ToString "yyyy-MM-dd"} and {toDate.ToString "yyyy-MM-dd"}, generated {report.GeneratedAt.ToString "yyyy-MM-dd HH:mm"} UTC.</p>
{sections}
</div>
<button onclick="downloadReport()">Download report</button>
</div>"""
