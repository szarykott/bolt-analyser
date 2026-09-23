module Bolt.Web.Views.Input

open Giraffe.ViewEngine
open Bolt.Web.Views.Shared

let private css = """
@import url('https://fonts.googleapis.com/css2?family=Ubuntu+Mono:wght@400;700&display=swap');
:root { color-scheme: light; font-family: 'Ubuntu Mono', monospace; color: #30251f; background: #fff8f2; }
* { box-sizing: border-box; }
body { margin: 0; }
.app-shell, #report-content { max-width: 1200px; margin: 0 auto; }
.app-shell { display: flex; flex-direction: column; min-height: 100vh; min-height: 100svh; padding: 2rem 1.5rem 4rem; }
#panel { display: flex; flex: 1; flex-direction: column; }
#report-content { width: 100%; }
.exported-report #report-content { padding: 2rem 1.5rem 4rem; }
.app-header { margin-bottom: 2rem; color: #a83b11; font-weight: 700; }
h1, h2, h3, h4, p { margin-top: 0; }
h1 { color: #ff5e1e; font-size: clamp(1.8rem, 4vw, 2.7rem); line-height: 1.15; letter-spacing: -0.035em; }
h2 { font-size: 1.35rem; }
h4 { font-size: 1rem; margin-bottom: 0.75rem; }
p { line-height: 1.6; }
.muted, .notes { color: #6e5c51; }
.flow-card a { color: #a83b11; }
.notes { font-size: 0.875rem; max-width: 80ch; }
.flow-card, .report-section, .metric-card { background: #fff; border: 1px solid #eadccf; border-radius: 1rem; box-shadow: 0 8px 30px rgba(83, 46, 21, 0.05); }
.flow-card { width: 100%; max-width: 640px; padding: clamp(1.5rem, 4vw, 2.5rem); margin: auto; }
.flow-card p:last-child { margin-bottom: 0; }
.link-example { display: block; max-width: 100%; height: auto; border: 1px solid #c9ad97; border-radius: 0.65rem; }
.link-example-caption { margin-top: 1.5rem; }
.form-field { display: grid; gap: 0.5rem; margin: 1.5rem 0 1rem; font-weight: 600; }
input { width: 100%; min-height: 2.8rem; padding: 0.65rem 0.85rem; border: 1px solid #c9ad97; border-radius: 0.65rem; background: #fff; color: inherit; font: inherit; }
input:focus-visible, button:focus-visible { outline: 3px solid #ff5e1e; outline-offset: 2px; }
button { min-height: 2.8rem; padding: 0.65rem 1.1rem; border: 0; border-radius: 0.65rem; background: #ff5e1e; color: #30251f; font: inherit; font-weight: 700; cursor: pointer; }
button:hover { background: #ff7842; }
.error { color: #a02d28; }
.error-card { border-color: #edc6c4; }
progress { display: block; width: 100%; height: 0.75rem; margin-top: 1.5rem; accent-color: #ff5e1e; }
.report-actions { display: flex; justify-content: flex-end; margin-bottom: 1rem; }
.report-notice + .report-actions { margin-top: 1rem; }
.report-header { margin-bottom: 1.5rem; }
.report-header h1 { margin-bottom: 0.5rem; }
.summary-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1rem; margin: 1.5rem 0; }
.metric-card { padding: 1.25rem; }
.metric-label { display: block; margin-bottom: 0.5rem; color: #6e5c51; font-size: 0.9rem; }
.metric-value { display: block; font-size: clamp(1.4rem, 2.5vw, 2rem); font-weight: 750; line-height: 1.2; font-variant-numeric: tabular-nums; }
.report-section { padding: clamp(1.25rem, 3vw, 2rem); margin: 1rem 0; }
.report-section > p { color: #6e5c51; }
.report-section h4 { margin-top: 1.75rem; }
.report-section h4:first-of-type { margin-top: 1rem; }
.table-scroll { overflow-x: auto; margin: 0 0 1rem; }
table { width: 100%; border-collapse: collapse; font-size: 0.925rem; }
.table-scroll.wide table { min-width: 700px; }
th, td { padding: 0.8rem 0.9rem; border-bottom: 1px solid #f0e3d7; text-align: left; vertical-align: top; }
th { color: #6e5c51; font-weight: 700; background: #fff5eb; }
tbody tr:last-child td { border-bottom: 0; }
tbody tr:hover { background: #fff9f3; }
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

let private page bodyAttributes content =
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
        body bodyAttributes [
            div [ _class "app-shell" ] [
                header [ _class "app-header" ] [
                    span [] [ str "Analiza przejazdów Bolta" ]
                ]
                panel [ div [ _class "flow-card" ] content ]
            ]
        ]
    ]
    |> RenderView.AsString.htmlDocument

let emailPage () =
    page [ attr "hx-ext" "ws"; attr "ws-connect" "/ws" ] [
        h1 [] [ str "Analiza przejazdów Bolt" ]
        p [ _class "muted" ] [ str "Przygotuj podsumowanie swoich przejazdów i zarobków." ]
#if DEBUG
        p [ _class "notes" ] [ str "Aplikacja działa w trybie Debug." ]
#endif
        form [ flag "ws-send" ] [
            input [ _type "hidden"; _name "msgType"; _value "start-analysis" ]
            label [ _class "form-field" ] [
                str "Adres e-mail kierowcy Bolt"
                input [ _type "email"; _name "email"; _required; _placeholder "kierowca@przyklad.pl" ]
            ]
            button [ _type "submit" ] [ str "Przygotuj analizę" ]
        ]
    ]

let indexPage () =
#if DEBUG
    emailPage ()
#else
    page [] [
        h1 [] [ str "Zanim podasz adres e-mail" ]
        p [ _class "muted" ] [ str "Ta strona analizuje dane z konta kierowcy Bolt. Przeczytaj, jaki dostęp jej powierzasz." ]
        p [] [ str "To nie jest oficjalny produkt Bolta. To osobisty projekt stworzony przez kierowcę Bolt dla siebie i innych kierowców." ]
        h2 [] [ str "Dostęp do konta i ryzyko" ]
        p [] [
            str "Po podaniu adresu e-mail i linku z wiadomości Bolt aplikacja zaloguje się na Twoje konto. "
            str "Spowoduje to wylogowanie Cię z aplikacji Bolt. Uzyska dostęp do wszystkich danych dostępnych na Twoim koncie kierowcy, nie tylko do danych pokazanych w raporcie."
        ]
        p [] [
            str "Przejście dalej oznacza duże zaufanie do autora tej strony: serwer otrzyma link do logowania i dostęp do danych konta. "
            str "Jeśli nie chcesz powierzać ich cudzej stronie, możesz uruchomić aplikację na swoim komputerze."
        ]
        h2 [] [ str "Jakie dane są przetwarzane" ]
        p [] [
            str "Aplikacja przetwarza adres e-mail i link do logowania, dane profilu, godziny aktywności oraz historię i szczegóły przejazdów, "
            str "w tym lokalizacje i kwoty. Dane służą do przygotowania analizy i pokazania Ci raportu. "
            str "Dane nie są zapisywane na serwerze. Połączenia z Bolt i z tą stroną są szyfrowane."
        ]
        h2 [] [ str "Kod źródłowy" ]
        p [] [
            str "Aplikacja jest non-profit i otwartoźródłowa. "
            a [ _href "https://github.com/szarykott/bolt-analyser" ] [ str "Kod źródłowy jest dostępny na GitHubie" ]
            str "; możesz go sprawdzić i uruchomić aplikację na własnym komputerze."
        ]
        form [ _action "/start"; _method "get" ] [
            button [ _type "submit" ] [ str "Przejdź do podania e-maila" ]
        ]
    ]
#endif

let magicLinkFragment (email: string) (error: string option) =
    let errorNode =
        error |> Option.map (fun e -> p [ _class "error" ] [ str e ]) |> Option.toList

    panel [
        div [ _class "flow-card" ] [
            yield h1 [] [ str "Dokończ logowanie" ]
            yield p [ _class "muted" ] [
                str "Otwórz wiadomość od Bolt. Kliknij prawym przyciskiem myszy niebieski link do logowania "
                str "i wybierz opcję skopiowania linku (np. „Copy Link”). Następnie wklej go w pole poniżej."
            ]
            yield! errorNode
            yield form [ flag "ws-send" ] [
                input [ _type "hidden"; _name "msgType"; _value "magic-link" ]
                input [ _type "hidden"; _name "email"; _value email ]
                label [ _class "form-field" ] [ str "Link z wiadomości"; input [ _type "text"; _name "url"; _required ] ]
                button [ _type "submit" ] [ str "Zaloguj się" ]
            ]
            yield p [ _class "muted link-example-caption" ] [ str "Przykład: tak należy skopiować link z wiadomości e-mail od Bolt:" ]
            yield img [ _src "/bolt-copy-link.png"; _alt "Przykład kopiowania linku z wiadomości Bolt"; _class "link-example" ]
        ]
    ]
    |> render
