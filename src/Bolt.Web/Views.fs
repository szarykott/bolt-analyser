module Bolt.Web.Views

open Bolt.Web.Report
open Giraffe.ViewEngine

let private render = RenderView.AsString.htmlNode

let private panel = div [ _id "panel" ]

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

let errorFragment (email: string) (step: string) (message: string) =
    panel [
        p [ _class "error" ] [ str $"Analiza nie powiodła się na etapie: {step}. {message}" ]
        form [ flag "ws-send" ] [
            input [ _type "hidden"; _name "msgType"; _value "start-analysis" ]
            input [ _type "hidden"; _name "email"; _value email ]
            button [ _type "submit" ] [ str "Spróbuj ponownie" ]
        ]
    ]
    |> render

let reportFragment (report: AnalysisReport) =
    let fromDate, toDate = report.DateRange

    let header = [
        h1 [] [ str $"Analiza przejazdów Bolt — {report.Email}" ]
        p [] [
            str (
                $"""{report.RideCount} przejazdów między {fromDate.ToString "yyyy-MM-dd"} a {toDate.ToString "yyyy-MM-dd"}, """
                + $"""raport wygenerowano {report.GeneratedAt.ToString "yyyy-MM-dd HH:mm"} UTC."""
            )
        ]
        if not (Array.isEmpty report.SkippedOrders) then
            section [] [
                h2 [] [ str $"Pominięte kursy ({report.SkippedOrders.Length})" ]
                p [] [ str "Nie udało się pobrać szczegółów tych kursów po trzech próbach. Pominięto je w analizie." ]
                ul [] [
                    for order in report.SkippedOrders do
                        li [] [ str $"Kurs {order.OrderId}: {order.Reason}" ]
                ]
            ]
    ]

    panel [
        div [ _id "report-content" ] (header @ ReportViews.sections report)
        button [ _onclick "downloadReport()" ] [ str "Pobierz raport" ]
    ]
    |> render
