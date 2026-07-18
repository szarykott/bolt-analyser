module Bolt.Web.Views

open System.Net
open Bolt.ETL.Analysis
open Giraffe.ViewEngine

// Giraffe.ViewEngine encodes text nodes (str), but renders attribute
// values raw — user-controlled attribute values go through here.
let private escapeAttr (s: string) = WebUtility.HtmlEncode s

// JSON that lands inside a <script> block must not terminate it early.
let private scriptSafeJson (json: string) = json.Replace("</", "<\\/")

let private render = RenderView.AsString.htmlNode

let private panel = div [ _id "panel" ]

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
