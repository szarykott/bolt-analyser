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
.flow-card a, .landing a { color: #a83b11; }
.notes { font-size: 0.875rem; max-width: 80ch; }
.flow-card, .report-section, .metric-card, .benefit-card, .data-card { background: #fff; border: 1px solid #eadccf; border-radius: 1rem; box-shadow: 0 8px 30px rgba(83, 46, 21, 0.05); }
.flow-card { width: 100%; max-width: 640px; padding: clamp(1.5rem, 4vw, 2.5rem); margin: auto; }
.flow-card p:last-child { margin-bottom: 0; }
.landing { width: 100%; max-width: 960px; margin: auto; }
.landing-hero { max-width: 760px; margin-bottom: 3rem; }
.landing-hero h1 { font-size: clamp(2.2rem, 5vw, 4rem); }
.landing-hero p { font-size: 1.15rem; }
.benefit-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1rem; margin-bottom: 3rem; }
.benefit-card, .data-card { padding: clamp(1.5rem, 3vw, 2rem); }
.benefit-card h3 { margin-bottom: 0.75rem; }
.benefit-card p:last-child, .data-card p:last-child { margin-bottom: 0; }
.data-card { max-width: 760px; }
.data-card details { margin: 1.5rem 0; }
.data-card summary { color: #a83b11; font-weight: 700; cursor: pointer; }
.data-card summary:focus-visible { outline: 3px solid #ff5e1e; outline-offset: 2px; }
.data-card details p:first-of-type { margin-top: 1rem; }
.data-card form { margin-top: 1.5rem; }
.debug-footer { margin-top: 2rem; color: #6e5c51; font-size: 0.875rem; line-height: 1.5; }
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
  .landing-hero { margin-bottom: 2rem; }
  .benefit-grid { grid-template-columns: 1fr; margin-bottom: 2rem; }
  .summary-grid { grid-template-columns: 1fr; }
  .report-actions button, .flow-card button, .data-card button { width: 100%; }
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
            script [ _src "app.js" ] []
            style [] [ rawText css ]
        ]
        body bodyAttributes [
            div [ _class "app-shell" ] [
                header [ _class "app-header" ] [
                    span [] [ str "Analiza przejazdów Bolta" ]
                ]
                panel content
#if DEBUG
                footer [ _class "debug-footer" ] [
                    str "Aplikacja działa w trybie deweloperskim (Debug) i zapisuje lokalnie na serwerze pobrane dane oraz tokeny logowania. Informacja o braku zapisu dotyczy wersji publicznej."
                ]
#endif
            ]
        ]
    ]
    |> RenderView.AsString.htmlDocument

let emailPage () =
    page [ attr "hx-ext" "ws"; attr "ws-connect" "ws" ] [
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

let indexPage () =
    page [] [
        main [ _class "landing" ] [
            section [ _class "landing-hero" ] [
                h1 [] [ str "Zobacz, co mówią Twoje przejazdy" ]
                p [ _class "muted" ] [ str "Zamień historię kursów Bolt w czytelny raport o zarobkach, stawkach godzinowych i miejscach odbioru pasażerów." ]
            ]
            section [] [
                h2 [] [ str "Co znajdziesz w raporcie" ]
                div [ _class "benefit-grid" ] [
                    article [ _class "benefit-card" ] [
                        h3 [] [ str "Zarobki w jednym miejscu" ]
                        p [] [ str "Zobacz podsumowanie przejazdów, napiwków, prowizji i zarobku kierowcy." ]
                    ]
                    article [ _class "benefit-card" ] [
                        h3 [] [ str "Porównanie godzin pracy" ]
                        p [] [ str "Sprawdź średnie zarobki na godzinę w dni robocze, weekendy, w dzień i w nocy." ]
                    ]
                    article [ _class "benefit-card" ] [
                        h3 [] [ str "Mapa miejsc odbioru" ]
                        p [] [ str "Odkryj, gdzie i o jakich porach najczęściej odbierasz pasażerów." ]
                    ]
                ]
            ]
            section [ _class "data-card" ] [
                h2 [] [ str "Jak korzystamy z Twoich danych" ]
                p [] [
                    str "Do przygotowania raportu podasz adres e-mail i link z wiadomości Bolt. "
                    str "Aplikacja zaloguje się na Twoje konto kierowcy i pobierze dane potrzebne do analizy."
                ]
                p [] [ str "Dane nie są zapisywane na serwerze, a połączenia z Bolt i z tą stroną są szyfrowane." ]
                details [] [
                    summary [] [ str "Zobacz szczegóły dostępu do konta i danych" ]
                    p [] [
                        str "Zalogowanie przez aplikację spowoduje wylogowanie Cię z aplikacji Bolt. "
                        str "Aplikacja uzyska dostęp do wszystkich danych dostępnych na Twoim koncie kierowcy, nie tylko do danych pokazanych w raporcie."
                    ]
                    p [] [
                        str "Przetwarzane są adres e-mail i link do logowania, dane profilu, godziny aktywności oraz historia i szczegóły przejazdów, "
                        str "w tym lokalizacje i kwoty. Dane służą do przygotowania analizy i pokazania Ci raportu."
                    ]
                    p [] [
                        str "To nie jest oficjalny produkt Bolta. To osobisty projekt stworzony przez kierowcę Bolt dla siebie i innych kierowców. "
                        str "Przejście dalej oznacza duże zaufanie do autora tej strony: serwer otrzyma link do logowania i dostęp do danych konta."
                    ]
                    p [] [
                        str "Aplikacja jest non-profit i otwartoźródłowa. "
                        a [ _href "https://github.com/szarykott/bolt-analyser" ] [ str "Kod źródłowy jest dostępny na GitHubie" ]
                        str "; możesz go sprawdzić i uruchomić aplikację na własnym komputerze."
                    ]
                ]
                form [ _action "start"; _method "get" ] [
                    button [ _type "submit" ] [ str "Przejdź do podania e-maila" ]
                ]
            ]
        ]
    ]

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
            yield img [ _src "bolt-copy-link.png"; _alt "Przykład kopiowania linku z wiadomości Bolt"; _class "link-example" ]
        ]
    ]
    |> render
