module Bolt.Infrastructure.Tests.SerializationTests

open System
open System.IO
open System.Text.Json
open Xunit
open Bolt.Infrastructure.Repository
open Bolt.Infrastrucutre.Serialization

[<Fact>]
let ``deserializeElement uses the shared converter settings`` () =
    // BoltDateTimeOffsetConverter parses "yyyy.MM.dd HH:mm" as UTC — only the
    // shared settings know that format.
    use doc = JsonDocument.Parse """{"name": "x", "created": "2026.07.01 12:30"}"""
    let value = Json.deserializeElement<{| name: string; created: DateTimeOffset |}> doc.RootElement
    Assert.Equal("x", value.name)
    Assert.Equal(DateTimeOffset(2026, 7, 1, 12, 30, 0, TimeSpan.Zero), value.created)

let private withTempRoot (f: unit -> unit) =
    let temp = Path.Combine(Path.GetTempPath(), "bolt-tests-" + Guid.NewGuid().ToString "N")
    Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", temp)
    try f ()
    finally
        Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", null)
        if Directory.Exists temp then Directory.Delete(temp, true)

[<Fact>]
let ``getUnstructured round-trips raw driver profile JSON`` () =
    withTempRoot (fun () ->
        // Compact form: storage re-serializes on write, so insignificant
        // whitespace does not survive the round-trip.
        use doc = JsonDocument.Parse """{"driver":{"id":7}}"""
        DriverProfileRepository.saveUnstructuredDangerous "a@b.pl" doc.RootElement
        let loaded = (DriverProfileRepository.getUnstructured "a@b.pl").Value
        Assert.Equal(doc.RootElement.GetRawText(), loaded.GetRawText()))

[<Fact>]
let ``getUnstructured is None when nothing was saved`` () =
    withTempRoot (fun () ->
        Assert.True((DriverProfileRepository.getUnstructured "a@b.pl").IsNone))
