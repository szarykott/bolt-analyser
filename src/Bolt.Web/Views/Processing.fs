module Bolt.Web.Views.Processing

open Giraffe.ViewEngine
open Bolt.Web.Views.Shared

let progressFragment (stateText: string) (detail: string) =
    panel [
        div [ _class "flow-card" ] [
            h1 [] [ str stateText ]
            if detail <> "" then p [ _class "muted" ] [ str detail ]
            progress [] []
        ]
    ]
    |> render
let errorFragmentWithDiagnostics supportEmail errors (email: string) (step: string) (message: string) =
    panel [
        div [ _class "flow-card error-card" ] [
            h1 [] [ str "Nie udało się przygotować analizy" ]
            p [ _class "error" ] [ str $"Analiza nie powiodła się na etapie: {step}. {message}" ]
            yield! Bolt.Web.Views.Diagnostics.nodes supportEmail false errors
            form [ flag "ws-send" ] [
                input [ _type "hidden"; _name "msgType"; _value "start-analysis" ]
                input [ _type "hidden"; _name "email"; _value email ]
                button [ _type "submit" ] [ str "Spróbuj ponownie" ]
            ]
        ]
    ]
    |> render

let errorFragment email step message = errorFragmentWithDiagnostics None [||] email step message

let renderFailureFragment (email: string) hasDiagnostics =
    panel [
        div [ _class "flow-card error-card" ] [
            h1 [] [ str "Nie udało się wyświetlić raportu" ]
            p [] [ str "Wystąpił błąd podczas wyświetlania wyniku. Możesz spróbować ponownie przygotować analizę." ]
            if hasDiagnostics then
                p [] [
                    str "Informacje o błędnych kursach zostały zachowane. "
                    a [ _href "/diagnostics.zip"; attr "download" "bolt-errors.zip" ] [ str "Pobierz diagnostykę (ZIP)" ]
                ]
            form [ flag "ws-send" ] [
                input [ _type "hidden"; _name "msgType"; _value "start-analysis" ]
                input [ _type "hidden"; _name "email"; _value email ]
                button [ _type "submit" ] [ str "Spróbuj ponownie" ]
            ]
        ]
    ]
    |> render
