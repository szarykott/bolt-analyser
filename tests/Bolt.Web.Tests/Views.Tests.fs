module Bolt.Web.Tests.ViewsTests

open Xunit
open Bolt.Web
open Bolt.Web.Tests.ReportFixture

[<Fact>]
let ``fragments are rooted in the panel div`` () =
    Assert.StartsWith("<div id=\"panel\">", Views.Processing.progressFragment "Scraping…" "12/40")
    Assert.StartsWith("<div id=\"panel\">", Views.Input.magicLinkFragment "a@b.pl" None)
    Assert.StartsWith("<div id=\"panel\">", Views.Processing.errorFragment "a@b.pl" "scraping" "boom")

[<Fact>]
let ``magic link fragment carries email and shows error`` () =
    let html = Views.Input.magicLinkFragment "a@b.pl" (Some "bad <token>")
    Assert.Contains("value=\"a@b.pl\"", html)
    Assert.Contains("magic-link", html)
    Assert.Contains("bad &lt;token&gt;", html)
    Assert.Contains("Zaloguj się", html)

[<Fact>]
let ``report renders basic statistics before the cluster chart`` () =
    let html = Views.Report.reportFragment report
    let basic = html.IndexOf("id=\"basic-statistics\"")
    let clusters = html.IndexOf("id=\"ride-clusters\"")
    Assert.True(basic >= 0 && basic < clusters)
    Assert.DoesNotContain("price-regression", html)
    Assert.DoesNotContain("per-hour-earnings", html)
    Assert.Contains("data-plotly-target=\"chart-ride-clusters-0\"", html)
    Assert.Contains("Skupiska (punkty: 1, poza skupiskami: 0)", html)
    Assert.Contains("downloadReport()", html)
    Assert.Contains("42 przejazd&#243;w", html)

[<Fact>]
let ``report lists skipped orders and escapes their reasons`` () =
    let withSkipped = { report with SkippedOrders = [| { OrderId = 7L; Reason = "<error>" } |] }
    let html = Views.Report.reportFragment withSkipped
    Assert.Contains("Kurs 7: &lt;error&gt;", html)
    Assert.Contains("Pominięte kursy (1)", html)

[<Fact>]
let ``report shows completed ride count earnings distance and four hourly groups`` () =
    let html = Views.Report.reportFragment report
    Assert.Contains("class=\"summary-grid\"", html)
    Assert.Contains("Liczba przejazd&#243;w</span><strong class=\"metric-value\">42", html)
    Assert.Contains("Przejechany dystans</span><strong class=\"metric-value\">57,25 km", html)
    Assert.Contains("Zarobek kierowcy</span><strong class=\"metric-value\">", html)
    Assert.Contains("234,50 zł", html)
    Assert.Contains("57,25 km", html)
    Assert.Contains("Zapłacone przez pasażer&#243;w", html)
    Assert.Contains("W tym got&#243;wką", html)
    Assert.Contains("W tym cyfrowo", html)
    Assert.Contains("Napiwki", html)
    Assert.Contains("Prowizja Bolt", html)
    Assert.Contains("13,94%", html)
    Assert.Contains("Zarobek kierowcy", html)
    Assert.Contains("Najdłuższy przejazd", html)
    Assert.Contains("Najkr&#243;tszy przejazd", html)
    Assert.Contains("Największy zarobek na przejeździe", html)
    Assert.Contains("Najmniejszy zarobek na przejeździe", html)
    Assert.Contains("Aleja Długa → Rynek Gł&#243;wny", html)
    Assert.Contains("dzień roboczy, dzień", html)
    Assert.Contains("<td>80,00</td>", html)
    Assert.Contains("<td>1,50</td>", html)
    Assert.Contains("dzień roboczy, noc", html)
    Assert.Contains("weekend, dzień", html)
    Assert.Contains("weekend, noc", html)
    Assert.Contains("<td>–</td><td>0,00</td>", html)

[<Fact>]
let ``highlight addresses are HTML encoded`` () =
    let longest = { report.BasicStatistics.LongestRide with FromAddress = Some "<adres>" }
    let stats = { report.BasicStatistics with LongestRide = longest }
    let html = Views.Report.reportFragment { report with BasicStatistics = stats }
    Assert.Contains("&lt;adres&gt;", html)
    Assert.DoesNotContain("<adres>", html)

[<Fact>]
let ``hourly groups stay visible without observations`` () =
    let stats = { report.BasicStatistics with HourlyAverages = [||] }
    let html = Views.Report.reportFragment { report with BasicStatistics = stats }
    Assert.Equal(4, html.Split("<td>–</td><td>0,00</td>").Length - 1)

[<Fact>]
let ``index page wires htmx websocket and panel`` () =
    let html = Views.Input.indexPage ()
    Assert.StartsWith("<!DOCTYPE html>", html)
    Assert.Contains("hx-ext=\"ws\"", html)
    Assert.Contains("ws-connect=\"/ws\"", html)
    Assert.Contains("<div id=\"panel\">", html)
    Assert.Contains("start-analysis", html)
    Assert.Contains("/app.js", html)
    Assert.Contains("lang=\"pl\"", html)
    Assert.Contains("Analiza przejazd&#243;w Bolt", html)
    Assert.Contains("@media (max-width: 650px)", html)

[<Fact>]
let ``report wraps wide tables and lets the chart size to its container`` () =
    let html = Views.Report.reportFragment report
    Assert.Contains("class=\"table-scroll wide\"", html)
    Assert.Contains("id=\"chart-ride-clusters-0\" class=\"chart-container\"", html)
    Assert.DoesNotContain("width:100%;height:800px", html)

[<Fact>]
let ``email attribute is single-encoded by the view engine`` () =
    let html = Views.Input.magicLinkFragment "a&b@x.pl" None
    Assert.Contains("value=\"a&amp;b@x.pl\"", html)
    Assert.DoesNotContain("a&amp;amp;b", html)
