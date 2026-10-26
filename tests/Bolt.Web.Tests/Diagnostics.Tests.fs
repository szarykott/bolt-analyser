module Bolt.Web.Tests.DiagnosticsTests

open System.IO
open System.IO.Compression
open System.Net
open System.Text.Json
open Xunit
open Bolt.Web
open Bolt.Web.Tests.ReportFixture

[<Fact>]
let ``zip contains one JSON per failed ride with original payloads and full trace`` () =
    let errors = [| rideError; { rideError with OrderId = 100L } |]
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
