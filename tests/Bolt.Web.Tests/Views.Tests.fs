module Bolt.Web.Tests.ViewsTests

open System.Text.Json
open Xunit
open Bolt.Web
open Bolt.Web.Tests.ReportFixture
open Plotly.NET

[<Fact>]
let ``cluster map uses OpenFreeMap Positron`` () =
    let chart =
        Views.ClusterMap.ridePointsLayer report.PickupClusters
        |> Views.ClusterMap.withMapStyle report.PickupClusters.Points
    use figure = JsonDocument.Parse(GenericChart.toFigureJson chart)
    let mapbox = figure.RootElement.GetProperty("layout").GetProperty("mapbox")
    Assert.Equal("https://tiles.openfreemap.org/styles/positron", mapbox.GetProperty("style").GetString())

[<Fact>]
let ``cluster hour label uses a font provided by OpenFreeMap`` () =
    use figure = JsonDocument.Parse(Views.ClusterMap.centroidLayer report.PickupClusters |> GenericChart.toFigureJson)
    let trace = figure.RootElement.GetProperty("data")[0]
    Assert.Equal("markers+text", trace.GetProperty("mode").GetString())
    Assert.Equal("08:30", trace.GetProperty("text").GetString())
    Assert.Equal("Noto Sans Regular", trace.GetProperty("textfont").GetProperty("family").GetString())

[<Fact>]
let ``report embeds OpenFreeMap style for browser rendering and download`` () =
    let html = Views.Report.reportFragment report
    Assert.Contains("https://tiles.openfreemap.org/styles/positron", html)
    Assert.Contains("href=\"https://openmaptiles.org\"", html)
    Assert.Contains("href=\"https://www.openstreetmap.org/copyright\"", html)

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
    Assert.Contains("Kliknij prawym przyciskiem myszy", html)
    Assert.Contains("wklej go w pole poniżej", html)
    Assert.Contains("src=\"bolt-copy-link.png\"", html)
    Assert.Contains("alt=\"Przykład kopiowania linku z wiadomości Bolt\"", html)
    let button = html.IndexOf("Zaloguj się")
    let caption = html.IndexOf("Przykład: tak należy skopiować link")
    let image = html.IndexOf("src=\"bolt-copy-link.png\"")
    Assert.True(button >= 0 && button < caption && caption < image)

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
let ``release report asks to save the result before the download button`` () =
    let html = Views.Report.reportFragment report
#if DEBUG
    Assert.DoesNotContain("Dane użyte do przygotowania tej analizy nie zostały zapisane na serwerze", html)
#else
    let notice = html.IndexOf("Dane użyte do przygotowania tej analizy nie zostały zapisane na serwerze")
    let download = html.IndexOf("downloadReport()")
    Assert.True(notice >= 0 && notice < download)
    Assert.Contains("Pobierz raport teraz", html)
    Assert.Contains("bez ponownego oczekiwania na analizę", html)
    Assert.Contains("nie obciążać ponownie serwera", html)
#endif

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
let ``email page wires htmx websocket and panel`` () =
    let html = Views.Input.emailPage ()
    Assert.StartsWith("<!DOCTYPE html>", html)
    Assert.Contains("hx-ext=\"ws\"", html)
    Assert.Contains("ws-connect=\"ws\"", html)
    Assert.Contains("<div id=\"panel\">", html)
    Assert.Contains("start-analysis", html)
    Assert.Contains("src=\"app.js\"", html)
    Assert.Contains("lang=\"pl\"", html)
    Assert.Contains("Analiza przejazd&#243;w Bolt", html)
    Assert.Contains("<header class=\"app-header\"><span>Analiza przejazd&#243;w Bolta</span></header>", html)
    Assert.Contains("@media (max-width: 650px)", html)
    Assert.DoesNotContain("trybie Release", html)
#if DEBUG
    Assert.Contains("Aplikacja działa w trybie Debug.", html)
#else
    Assert.DoesNotContain("trybie Debug", html)
#endif

[<Fact>]
let ``index page warns before asking for email in release`` () =
    let html = Views.Input.indexPage ()
    Assert.DoesNotContain("Release", html)
#if DEBUG
    Assert.Contains("Aplikacja działa w trybie Debug.", html)
    Assert.Contains("name=\"email\"", html)
#else
    Assert.Contains("Zanim podasz adres e-mail", html)
    Assert.Contains("To nie jest oficjalny produkt Bolta", html)
    Assert.Contains("osobisty projekt stworzony przez kierowcę Bolt", html)
    Assert.DoesNotContain("name=\"email\"", html)
    Assert.DoesNotContain("ws-connect", html)
    Assert.Contains("wylogowanie Cię z aplikacji Bolt", html)
    Assert.Contains("wszystkich danych dostępnych na Twoim koncie kierowcy", html)
    Assert.Contains("duże zaufanie do autora tej strony", html)
    Assert.Contains("nie są zapisywane na serwerze", html)
    Assert.Contains("https://github.com/szarykott/bolt-analyser", html)
    Assert.Contains("action=\"start\"", html)
    Assert.Contains("Przejdź do podania e-maila", html)
#endif

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
