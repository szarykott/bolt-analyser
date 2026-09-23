module Bolt.Web.Views.Input

open Giraffe.ViewEngine
open Bolt.Web.Views.Shared

let private css = """
body { font-family: sans-serif; max-width: 1100px; margin: 2rem auto; padding: 0 1rem; }
table { border-collapse: collapse; margin: 1rem 0; }
th, td { border: 1px solid #ccc; padding: 0.3rem 0.7rem; text-align: left; }
.error { color: #b00; }
.notes { font-size: 0.85rem; color: #555; max-width: 80ch; }
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
            h1 [] [ str "Analiza przejazdów Bolt" ]
            panel [
                form [ flag "ws-send" ] [
                    input [ _type "hidden"; _name "msgType"; _value "start-analysis" ]
                    label [] [
                        str "Adres e-mail kierowcy Bolt: "
                        input [ _type "email"; _name "email"; _required; _placeholder "kierowca@przyklad.pl" ]
                    ]
                    button [ _type "submit" ] [ str "Przygotuj analizę" ]
                ]
            ]
        ]
    ]
    |> RenderView.AsString.htmlDocument

let magicLinkFragment (email: string) (error: string option) =
    let errorNode =
        error |> Option.map (fun e -> p [ _class "error" ] [ str e ]) |> Option.toList

    panel [
        yield p [] [ str "Sprawdź swoją skrzynkę — Bolt wysłał wiadomość z linkiem do logowania. Wklej ten link poniżej." ]
        yield! errorNode
        yield form [ flag "ws-send" ] [
            input [ _type "hidden"; _name "msgType"; _value "magic-link" ]
            input [ _type "hidden"; _name "email"; _value email ]
            label [] [ str "Link z wiadomości: "; input [ _type "text"; _name "url"; _required ] ]
            button [ _type "submit" ] [ str "Zaloguj się" ]
        ]
    ]
    |> render
