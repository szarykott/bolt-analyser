module Bolt.Web.Views.Diagnostics

open System
open Bolt.Models.BoltApi
open Bolt.Web
open Giraffe.ViewEngine

let nodes supportEmail analysisCompleted (errors: RideError[]) = [
    if not (Array.isEmpty errors) then
        section [ _class "report-notice" ] [
            h2 [] [ str $"Kursy z błędami danych ({errors.Length})" ]
            p [] [
                if analysisCompleted then str "Analizę wykonano na podstawie mniejszej liczby kursów. "
                str "Kursy z błędami danych zostały pominięte."
            ]
            p [] [
                a [ _href "/diagnostics.zip"; attr "download" "bolt-errors.zip" ] [ str "Pobierz diagnostykę błędnych kursów (ZIP)" ]
            ]
            p [] [ str "Pobierz ZIP przed zamknięciem strony, aby zachować diagnostykę." ]
            p [] [
                str "ZIP zawiera dane wyłącznie błędnych kursów oraz szczegóły błędów. Możesz je przejrzeć przed wysłaniem. "
                match supportEmail with
                | Some email ->
                    str "Jeśli chcesz zgłosić problem, dołącz pobrany ZIP do wiadomości na adres "
                    a [ _href ($"mailto:{email}?subject=" + Uri.EscapeDataString "Błędy analizy przejazdów Bolt") ] [ str email ]
                    str "."
                | None -> str "Jeśli chcesz zgłosić problem, przekaż pobrany ZIP autorowi aplikacji."
            ]
            for error in errors do
                details [] [
                    summary [] [ str $"Kurs {error.OrderId}: {error.Message}" ]
                    pre [ _class "diagnostic-json" ] [ str (RideDiagnostics.toJson error) ]
                ]
        ]
]
