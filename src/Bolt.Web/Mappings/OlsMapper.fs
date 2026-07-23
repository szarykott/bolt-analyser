namespace Bolt.Web.Mappings

open System
open System.Globalization
open Bolt.ETL.Analytics
open Bolt.Web.Views

module OlsMapper =
    let private pl = CultureInfo.GetCultureInfo "pl-PL"

    let private fmt2 (v: float) = v.ToString("F2", pl)

    let private fmt2Opt (v: float option) =
        v |> Option.map fmt2 |> Option.defaultValue "–"

    let private fmtPValue (p: float option) =
        match p with
        | Some p when p < 0.001 -> "< 0,001"
        | Some p -> p.ToString("F3", pl)
        | None -> "–"

    let private isSignificant (c: Coefficient) =
        match c.PValue with
        | Some p -> p < 0.05
        | None -> false

    let coefficientsTable (response: OlsResponse) : ResultTable =
        let features = response.Coefficients |> Array.filter (fun c -> c.Name <> "const")

        let significant, insignificant = features |> Array.partition isSignificant

        let rows =
            significant
            |> Array.sortByDescending (fun c -> c.Coef |> Option.map abs |> Option.defaultValue 0.0)
            |> Array.map (fun c ->
                [ c.Name
                  fmt2Opt c.Coef
                  (match c.CiLow, c.CiHigh with
                   | Some lo, Some hi -> $"od {fmt2 lo} do {fmt2 hi}"
                   | _ -> "–")
                  fmtPValue c.PValue ])
            |> List.ofArray

        let insignificantNote =
            if Array.isEmpty insignificant then
                "Wszystkie cechy modelu są istotne statystycznie (p < 0,05)."
            else
                let names = insignificant |> Array.map _.Name |> String.concat ", "
                $"Cechy statystycznie nieistotne (p ≥ 0,05), pominięte w tabeli: {names}."

        { Title = "Wpływ cech na cenę przejazdu"
          Headers = [ "cecha"; "współczynnik [zł]"; "przedział ufności 95%"; "istotność (p)" ]
          Rows = rows
          Notes =
            [ "cecha — zmienna wpływająca na cenę; nazwy w formie dzielnica_… lub temperatura_… oznaczają różnicę względem pominiętej kategorii bazowej."
              "współczynnik [zł] — o ile złotych zmienia się cena przejazdu, gdy dana cecha występuje (dla dystansu: przy wzroście o 1 odchylenie standardowe); im większa wartość bezwzględna, tym silniejszy wpływ; wiersze są posortowane od najsilniejszego wpływu."
              "przedział ufności 95% — zakres, w którym z 95-procentową pewnością mieści się prawdziwa wartość współczynnika."
              "istotność (p) — prawdopodobieństwo, że tak silny efekt pojawiłby się przypadkiem; wartości poniżej 0,05 uznaje się za istotne statystycznie."
              insignificantNote ] }

    let modelStatsTable (response: OlsResponse) : ResultTable =
        let fitNote =
            match response.RSquared with
            | Some r2 ->
                let pct = int (Math.Round(r2 * 100.0))
                [ $"Model wyjaśnia {pct}%% zmienności ceny przejazdu." ]
            | None -> []

        { Title = "Dopasowanie modelu"
          Headers = [ "statystyka"; "wartość" ]
          Rows =
            [ [ "liczba przejazdów"; string response.NObservations ]
              [ "R²"; fmt2Opt response.RSquared ]
              [ "skorygowane R²"; fmt2Opt response.AdjRSquared ] ]
          Notes =
            fitNote
            @ [ "R² — jaka część zmienności ceny jest wyjaśniona przez model (od 0 do 1, wyżej = lepiej); skorygowane R² dodatkowo uwzględnia liczbę cech w modelu." ] }