module Bolt.Web.Tests.ViewsTests

open Xunit
open Bolt.Web
open Bolt.Web.Tests.ReportFixture

[<Fact>]
let ``fragments are rooted in the panel div`` () =
    Assert.StartsWith("<div id=\"panel\">", Views.progressFragment "Scraping…" "12/40")
    Assert.StartsWith("<div id=\"panel\">", Views.magicLinkFragment "a@b.pl" None)
    Assert.StartsWith("<div id=\"panel\">", Views.errorFragment "a@b.pl" "scraping" "boom")

[<Fact>]
let ``magic link fragment carries email and shows error`` () =
    let html = Views.magicLinkFragment "a@b.pl" (Some "bad <token>")
    Assert.Contains("value=\"a@b.pl\"", html)
    Assert.Contains("magic-link", html)
    Assert.Contains("bad &lt;token&gt;", html)
    Assert.Contains("Zaloguj się", html)

[<Fact>]
let ``report renders three analyses in order with the cluster chart`` () =
    let html = Views.reportFragment report
    let price = html.IndexOf("id=\"price-regression\"")
    let hourly = html.IndexOf("id=\"per-hour-earnings\"")
    let clusters = html.IndexOf("id=\"ride-clusters\"")
    Assert.True(price >= 0 && price < hourly && hourly < clusters)
    Assert.Contains("data-plotly-target=\"chart-ride-clusters-0\"", html)
    Assert.Contains("Skupiska (punkty: 1, poza skupiskami: 0)", html)
    Assert.Contains("downloadReport()", html)
    Assert.Contains("42 przejazd&#243;w", html)

[<Fact>]
let ``report lists skipped orders and escapes their reasons`` () =
    let withSkipped = { report with SkippedOrders = [| { OrderId = 7L; Reason = "<error>" } |] }
    let html = Views.reportFragment withSkipped
    Assert.Contains("Kurs 7: &lt;error&gt;", html)
    Assert.Contains("Pominięte kursy (1)", html)

[<Fact>]
let ``price regression sorts significant effects and explains omitted effects`` () =
    let html = Views.reportFragment report
    let rain = html.IndexOf("<td>deszcz</td>")
    let distance = html.IndexOf("<td>dystans_km</td>")
    Assert.True(rain >= 0 && rain < distance)
    Assert.Contains("<td>-4,25</td>", html)
    Assert.Contains("<td>&lt; 0,001</td>", html)
    Assert.DoesNotContain("<td>śnieg</td>", html)
    Assert.Contains("pominięte w tabeli: śnieg", html)
    Assert.Contains("Model wyjaśnia 42%", html)

[<Fact>]
let ``hourly earnings render averages and unlocked model only`` () =
    let html = Views.reportFragment report
    Assert.Contains("dzień roboczy, dzień", html)
    Assert.Contains("<td>80,00</td>", html)
    Assert.Contains("poziom 1", html)
    Assert.Contains("<td>weekend</td>", html)
    Assert.DoesNotContain("<td>zła_pogoda</td>", html)
    Assert.Contains("pominięte w tabeli: zła_pogoda", html)
    Assert.Contains("liczba godzin", html)

[<Fact>]
let ``hourly earnings without unlocked models keeps only averages and guidance`` () =
    let hourly = { report.HourlyEarnings.Value with Models = [] }
    let html = Views.reportFragment { report with HourlyEarnings = Some hourly }
    Assert.Contains("Średnie zarobki na godzinę pracy", html)
    Assert.DoesNotContain("poziom 1", html)
    Assert.Contains("za mało godzin jazdy", html)

[<Fact>]
let ``coefficient names are HTML encoded`` () =
    let price = report.PriceRegression.Value
    let effect = { price.Effects[0] with Feature = "<cecha>" }
    let html = Views.reportFragment { report with PriceRegression = Some { price with Effects = [| effect |] } }
    Assert.Contains("&lt;cecha&gt;", html)
    Assert.DoesNotContain("<cecha>", html)

[<Fact>]
let ``missing weather yields empty analysis sections with explanation`` () =
    let html = Views.reportFragment { report with PriceRegression = None; HourlyEarnings = None }
    Assert.Contains("Żaden przejazd nie mieści się w zakresie danych pogodowych.", html)
    Assert.DoesNotContain("Wpływ cech na cenę przejazdu", html)
    Assert.DoesNotContain("Średnie zarobki na godzinę pracy", html)
    Assert.Contains("Mapa skupisk odbior&#243;w", html)

[<Fact>]
let ``index page wires htmx websocket and panel`` () =
    let html = Views.indexPage ()
    Assert.StartsWith("<!DOCTYPE html>", html)
    Assert.Contains("hx-ext=\"ws\"", html)
    Assert.Contains("ws-connect=\"/ws\"", html)
    Assert.Contains("<div id=\"panel\">", html)
    Assert.Contains("start-analysis", html)
    Assert.Contains("/app.js", html)
    Assert.Contains("lang=\"pl\"", html)
    Assert.Contains("Analiza przejazd&#243;w Bolt", html)

[<Fact>]
let ``email attribute is single-encoded by the view engine`` () =
    let html = Views.magicLinkFragment "a&b@x.pl" None
    Assert.Contains("value=\"a&amp;b@x.pl\"", html)
    Assert.DoesNotContain("a&amp;amp;b", html)
