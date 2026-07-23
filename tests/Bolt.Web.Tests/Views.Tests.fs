module Bolt.Web.Tests.HtmlTests

open System
open Xunit
open Bolt.ETL.Analysis
open Bolt.Web

[<Fact>]
let ``fragments are rooted in the panel div`` () =
    Assert.StartsWith("<div id=\"panel\">", Html.progressFragment "Scraping…" "12/40")
    Assert.StartsWith("<div id=\"panel\">", Html.magicLinkFragment "a@b.pl" None)
    Assert.StartsWith("<div id=\"panel\">", Html.errorFragment "a@b.pl" "scraping" "boom")

[<Fact>]
let ``magic link fragment carries email and shows error`` () =
    let html = Html.magicLinkFragment "a@b.pl" (Some "bad <token>")
    Assert.Contains("value=\"a@b.pl\"", html)
    Assert.Contains("magic-link", html)
    Assert.Contains("bad &lt;token&gt;", html)
    Assert.Contains("Zaloguj się", html)

[<Fact>]
let ``report fragment embeds figure json and tables`` () =
    let report = {
        Email = "a@b.pl"
        GeneratedAt = DateTimeOffset.Parse "2026-07-12T10:00Z"
        RideCount = 42
        DateRange = (DateTimeOffset.Parse "2026-01-01Z", DateTimeOffset.Parse "2026-06-30Z")
        Sections =
            [ { Id = "s1"
                Title = "Section <1>"
                Description = "desc"
                Charts = [ { Title = "Map"; PlotlyFigureJson = """{"data":[],"layout":{}}""" } ]
                Tables = [ { Title = "T"; Headers = [ "h" ]; Rows = [ [ "<cell>" ] ]; Notes = [] } ] } ]
    }
    let html = Html.reportFragment report
    Assert.Contains("report-content", html)
    Assert.Contains("data-plotly-target=\"chart-s1-0\"", html)
    Assert.Contains("""{"data":[],"layout":{}}""", html)
    Assert.Contains("Section &lt;1&gt;", html)
    Assert.Contains("&lt;cell&gt;", html)
    Assert.Contains("downloadReport()", html)
    Assert.Contains("Pobierz raport", html)
    Assert.Contains("42 przejazd&#243;w", html)

[<Fact>]
let ``script-bound json escapes closing tags`` () =
    let report = {
        Email = "a@b.pl"
        GeneratedAt = DateTimeOffset.UtcNow
        RideCount = 1
        DateRange = (DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        Sections =
            [ { Id = "s1"; Title = "t"; Description = ""
                Charts = [ { Title = "c"; PlotlyFigureJson = """{"a":"</script>"}""" } ]
                Tables = [] } ]
    }
    Assert.DoesNotContain("</script>\"}", Html.reportFragment report)

[<Fact>]
let ``index page wires htmx websocket and panel`` () =
    let html = Html.indexPage ()
    Assert.StartsWith("<!DOCTYPE html>", html)
    Assert.Contains("hx-ext=\"ws\"", html)
    Assert.Contains("ws-connect=\"/ws\"", html)
    Assert.Contains("<div id=\"panel\">", html)
    Assert.Contains("start-analysis", html)
    Assert.Contains("/app.js", html)
    Assert.Contains("lang=\"pl\"", html)
    // The view engine emits U+00A0–U+00FF as numeric entities: ó → &#243;
    Assert.Contains("Analiza przejazd&#243;w Bolt", html)

[<Fact>]
let ``table notes render as legend list`` () =
    let report = {
        Email = "a@b.pl"
        GeneratedAt = DateTimeOffset.UtcNow
        RideCount = 1
        DateRange = (DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        Sections =
            [ { Id = "s1"; Title = "t"; Description = ""
                Charts = []
                Tables = [ { Title = "T"; Headers = [ "h" ]; Rows = []
                             Notes = [ "objaśnienie <a>" ] } ] } ]
    }
    let html = Html.reportFragment report
    Assert.Contains("<ul class=\"notes\">", html)
    Assert.Contains("objaśnienie &lt;a&gt;", html)

[<Fact>]
let ``email attribute is single-encoded by the view engine`` () =
    let html = Html.magicLinkFragment "a&b@x.pl" None
    Assert.Contains("value=\"a&amp;b@x.pl\"", html)
    Assert.DoesNotContain("a&amp;amp;b", html)
