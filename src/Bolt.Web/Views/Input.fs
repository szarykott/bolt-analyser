module Bolt.Web.Views.Input

open Giraffe.ViewEngine
open Bolt.Web.Views.Shared

let private css = """
:root { color-scheme: light; font-family: system-ui, sans-serif; color: #17251e; background: #f4f7f5; }
* { box-sizing: border-box; }
body { margin: 0; }
.app-shell, #report-content { max-width: 1200px; margin: 0 auto; }
.app-shell { padding: 2rem 1.5rem 4rem; }
.exported-report #report-content { padding: 2rem 1.5rem 4rem; }
.app-header { display: flex; align-items: center; gap: 0.75rem; margin-bottom: 2rem; font-weight: 700; }
.brand-mark { display: grid; place-items: center; width: 2.25rem; height: 2.25rem; border-radius: 0.7rem; background: #2ecb70; color: #10251a; }
h1, h2, h3, h4, p { margin-top: 0; }
h1 { font-size: clamp(1.8rem, 4vw, 2.7rem); line-height: 1.15; letter-spacing: -0.035em; }
h2 { font-size: 1.35rem; }
h4 { font-size: 1rem; margin-bottom: 0.75rem; }
p { line-height: 1.6; }
.muted, .notes { color: #53665b; }
.notes { font-size: 0.875rem; max-width: 80ch; }
.flow-card, .report-section, .metric-card { background: #fff; border: 1px solid #dce8df; border-radius: 1rem; box-shadow: 0 8px 30px rgba(18, 49, 30, 0.04); }
.flow-card { max-width: 640px; padding: clamp(1.5rem, 4vw, 2.5rem); }
.flow-card p:last-child { margin-bottom: 0; }
.form-field { display: grid; gap: 0.5rem; margin: 1.5rem 0 1rem; font-weight: 600; }
input { width: 100%; min-height: 2.8rem; padding: 0.65rem 0.85rem; border: 1px solid #aebfb3; border-radius: 0.65rem; background: #fff; color: inherit; font: inherit; }
input:focus-visible, button:focus-visible { outline: 3px solid #197c45; outline-offset: 2px; }
button { min-height: 2.8rem; padding: 0.65rem 1.1rem; border: 0; border-radius: 0.65rem; background: #176b3d; color: #fff; font: inherit; font-weight: 700; cursor: pointer; }
button:hover { background: #10552f; }
.error { color: #a02d28; }
.error-card { border-color: #edc6c4; }
progress { display: block; width: 100%; height: 0.75rem; margin-top: 1.5rem; accent-color: #176b3d; }
.report-actions { display: flex; justify-content: flex-end; margin-bottom: 1rem; }
.report-header { margin-bottom: 1.5rem; }
.report-header h1 { margin-bottom: 0.5rem; }
.summary-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1rem; margin: 1.5rem 0; }
.metric-card { padding: 1.25rem; }
.metric-label { display: block; margin-bottom: 0.5rem; color: #53665b; font-size: 0.9rem; }
.metric-value { display: block; font-size: clamp(1.4rem, 2.5vw, 2rem); font-weight: 750; line-height: 1.2; font-variant-numeric: tabular-nums; }
.report-section { padding: clamp(1.25rem, 3vw, 2rem); margin: 1rem 0; }
.report-section > p { color: #53665b; }
.report-section h4 { margin-top: 1.75rem; }
.report-section h4:first-of-type { margin-top: 1rem; }
.table-scroll { overflow-x: auto; margin: 0 0 1rem; }
table { width: 100%; border-collapse: collapse; font-size: 0.925rem; }
.table-scroll.wide table { min-width: 700px; }
th, td { padding: 0.8rem 0.9rem; border-bottom: 1px solid #e4ece6; text-align: left; vertical-align: top; }
th { color: #53665b; font-weight: 700; background: #f4f8f5; }
tbody tr:last-child td { border-bottom: 0; }
tbody tr:hover { background: #f8fbf9; }
.chart-container { width: 100%; height: clamp(340px, 60vh, 680px); }
.report-notice { padding: 1.25rem; margin-top: 1.5rem; border: 1px solid #edd9aa; border-radius: 0.8rem; background: #fffaf0; }
.report-notice h2 { margin-bottom: 0.5rem; }
.report-notice ul { margin-bottom: 0; padding-left: 1.25rem; }
@media (max-width: 650px) {
  .app-shell { padding: 1.25rem 1rem 2.5rem; }
  .exported-report #report-content { padding: 1.25rem 1rem 2.5rem; }
  .app-header { margin-bottom: 1.5rem; }
  .summary-grid { grid-template-columns: 1fr; }
  .report-actions button, .flow-card button { width: 100%; }
  .metric-card { padding: 1rem 1.25rem; }
}
"""

let indexPage () =
    html [ _lang "pl" ] [
        head [] [
            meta [ _charset "utf-8" ]
            title [] [ str "Analiza przejazdów Bolt" ]
            meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
            script [ _src "https://unpkg.com/htmx.org@1.9.12" ] []
            script [ _src "https://unpkg.com/htmx.org@1.9.12/dist/ext/ws.js" ] []
            script [ _src "https://cdn.plot.ly/plotly-2.32.0.min.js" ] []
            script [ _src "/app.js" ] []
            style [] [ rawText css ]
        ]
        body [ attr "hx-ext" "ws"; attr "ws-connect" "/ws" ] [
            div [ _class "app-shell" ] [
                header [ _class "app-header" ] [
                    span [ _class "brand-mark" ] [ str "B" ]
                    span [] [ str "Bolt · analiza przejazdów" ]
                ]
                panel [
                    div [ _class "flow-card" ] [
                        h1 [] [ str "Analiza przejazdów Bolt" ]
                        p [ _class "muted" ] [ str "Przygotuj podsumowanie swoich przejazdów i zarobków." ]
                        form [ flag "ws-send" ] [
                            input [ _type "hidden"; _name "msgType"; _value "start-analysis" ]
                            label [ _class "form-field" ] [
                                str "Adres e-mail kierowcy Bolt"
                                input [ _type "email"; _name "email"; _required; _placeholder "kierowca@przyklad.pl" ]
                            ]
                            button [ _type "submit" ] [ str "Przygotuj analizę" ]
                        ]
                    ]
                ]
            ]
        ]
    ]
    |> RenderView.AsString.htmlDocument

let magicLinkFragment (email: string) (error: string option) =
    let errorNode =
        error |> Option.map (fun e -> p [ _class "error" ] [ str e ]) |> Option.toList

    panel [
        div [ _class "flow-card" ] [
            yield h1 [] [ str "Dokończ logowanie" ]
            yield p [ _class "muted" ] [ str "Sprawdź swoją skrzynkę — Bolt wysłał wiadomość z linkiem do logowania. Wklej ten link poniżej." ]
            yield! errorNode
            yield form [ flag "ws-send" ] [
                input [ _type "hidden"; _name "msgType"; _value "magic-link" ]
                input [ _type "hidden"; _name "email"; _value email ]
                label [ _class "form-field" ] [ str "Link z wiadomości"; input [ _type "text"; _name "url"; _required ] ]
                button [ _type "submit" ] [ str "Zaloguj się" ]
            ]
        ]
    ]
    |> render
