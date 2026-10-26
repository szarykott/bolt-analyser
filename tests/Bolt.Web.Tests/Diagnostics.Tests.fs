module Bolt.Web.Tests.DiagnosticsTests

open System.IO
open System.IO.Compression
open System.Net
open System.Text.Json
open Xunit
open Bolt.Web
open Bolt.Web.Tests.ReportFixture
open Bolt.Models.BoltApi

[<Fact>]
let ``zip contains one JSON per failed ride with original payloads and full trace`` () =
    let errors = [| rideError; { rideError with OrderId = Some 100L } |]
    let bytes = RideDiagnostics.zip (Some "support@example.test") errors
    use stream = new MemoryStream(bytes)
    use zip = new ZipArchive(stream, ZipArchiveMode.Read)
    Assert.Equal(3, zip.Entries.Count)
    use readme = new StreamReader(zip.GetEntry("README.txt").Open())
    Assert.Contains("support@example.test", readme.ReadToEnd())
    use entry = zip.GetEntry("ride-1-99.json").Open()
    use document = JsonDocument.Parse entry
    let json = document.RootElement
    Assert.Equal(99L, json.GetProperty("order_id").GetInt64())
    Assert.Equal("ride", json.GetProperty("stage").GetString())
    Assert.Contains("RideFactory.getPayment", json.GetProperty("stack_trace").GetString())
    Assert.Equal("System.NullReferenceException", json.GetProperty("exception_type").GetString())
    Assert.True(JsonElement.DeepEquals(brokenOrder.PreviousOrder, json.GetProperty("previous_order")))
    Assert.True(JsonElement.DeepEquals(brokenOrder.PastOrderDetail, json.GetProperty("past_order_detail")))
    Assert.False(json.TryGetProperty("email") |> fst)

[<Fact>]
let ``partial report explains exclusions and offers preview zip and mailto`` () =
    let html =
        Views.Report.reportFragmentWithSupport (Some "support@example.test") { report with RideErrors = [| rideError |] }
        |> WebUtility.HtmlDecode
    Assert.Contains("Analizę wykonano na podstawie mniejszej liczby kursów", html)
    Assert.Contains("Kursy z błędami danych (1)", html)
    Assert.Contains("href=\"/diagnostics.zip\"", html)
    Assert.Contains("mailto:support@example.test?subject=", html)
    Assert.Contains("<details>", html)
    Assert.Contains("stack_trace", html)

[<Fact>]
let ``all failed view offers diagnostics without claiming analysis completed`` () =
    let html =
        Views.Processing.errorFragmentWithDiagnostics None [| rideError |] "driver@example.test" "analiza" "Brak poprawnych kursów."
        |> WebUtility.HtmlDecode
    Assert.Contains("/diagnostics.zip", html)
    Assert.DoesNotContain("Analizę wykonano", html)
    Assert.DoesNotContain("mailto:", html)

[<Fact>]
let ``diagnostic summaries encode untrusted exception text`` () =
    let error = { rideError with Message = "<script>alert(1)</script>" }
    let html = Views.Report.reportFragment { report with RideErrors = [| error |] }
    Assert.Contains("&lt;script&gt;", html)
    Assert.DoesNotContain("<script>alert(1)</script>", html)

[<Fact>]
let ``report without parse errors has no diagnostics link`` () =
    let html = Views.Report.reportFragment report
    Assert.DoesNotContain("/diagnostics.zip", html)

[<Theory>]
[<InlineData("")>]
[<InlineData("{")>]
[<InlineData("{\"code\":0,\"data\":[}")>]
[<InlineData("\u0000<script>bad</script>")>]
let ``zip preserves invalid HTTP text exactly with a null order id`` body =
    let ex = Record.Exception(fun () -> JsonDocument.Parse(body: string) |> ignore)
    let diagnostic =
        { RideError.create None "http_response" ex with
            ResponseBody = Some body
            Endpoint = Some "/orderDriver/v1/getPreviousOrder" }
    use stream = new MemoryStream(RideDiagnostics.zip None [| diagnostic |])
    use zip = new ZipArchive(stream, ZipArchiveMode.Read)
    use entry = zip.GetEntry("ride-1-unknown.json").Open()
    use document = JsonDocument.Parse entry
    let json = document.RootElement
    Assert.Equal(JsonValueKind.Null, json.GetProperty("order_id").ValueKind)
    Assert.Equal(body, json.GetProperty("response_body").GetString())
    Assert.Equal("/orderDriver/v1/getPreviousOrder", json.GetProperty("endpoint").GetString())
    Assert.Equal(diagnostic.StackTrace, json.GetProperty("stack_trace").GetString())
    Assert.Equal(JsonValueKind.Null, json.GetProperty("previous_order").ValueKind)
    Assert.Equal(JsonValueKind.Null, json.GetProperty("past_order_detail").ValueKind)

[<Fact>]
let ``zip preserves a malformed history fragment and the view names the missing id`` () =
    use raw = JsonDocument.Parse """{"order_handle":null,"unexpected":"value"}"""
    let ex = Record.Exception(fun () -> Bolt.Infrastrucutre.Serialization.Json.deserializeElement<HistoryOrderHandle> raw.RootElement |> ignore)
    let diagnostic = { RideError.create None "order_history" ex with HistoryEntry = Some(raw.RootElement.Clone()) }
    use stream = new MemoryStream(RideDiagnostics.zip None [| diagnostic |])
    use zip = new ZipArchive(stream, ZipArchiveMode.Read)
    use entry = zip.GetEntry("ride-1-unknown.json").Open()
    use document = JsonDocument.Parse entry
    Assert.True(JsonElement.DeepEquals(raw.RootElement, document.RootElement.GetProperty("history_entry")))
    let html = Views.Report.reportFragment { report with RideErrors = [| diagnostic |] } |> WebUtility.HtmlDecode
    Assert.Contains("Błędy danych (1)", html)
    Assert.Contains("Błąd bez identyfikatora kursu", html)
    Assert.DoesNotContain("Kurs 0:", html)

[<Fact>]
let ``multiple unknown identifiers keep separate diagnostic files`` () =
    let diagnostic body =
        let ex = Record.Exception(fun () -> JsonDocument.Parse(body: string) |> ignore)
        { RideError.create None "http_response" ex with ResponseBody = Some body }
    use stream = new MemoryStream(RideDiagnostics.zip None [| diagnostic "{"; diagnostic "" |])
    use zip = new ZipArchive(stream, ZipArchiveMode.Read)
    for name, body in [ "ride-1-unknown.json", "{"; "ride-2-unknown.json", "" ] do
        use entry = zip.GetEntry(name).Open()
        use json = JsonDocument.Parse entry
        Assert.Equal(body, json.RootElement.GetProperty("response_body").GetString())
