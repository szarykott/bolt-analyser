module Bolt.Infrastructure.Tests.ScrapeMetadataTests

open System
open System.IO
open Xunit
open Bolt.Infrastructure.Repository
open Bolt.Models.BoltApi
open Bolt.Infrastrucutre.storage.Storage

let private withTempRoot (f: unit -> unit) =
    let temp = Path.Combine(Path.GetTempPath(), "bolt-tests-" + Guid.NewGuid().ToString "N")
    Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", temp)
    try f ()
    finally
        Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", null)
        if Directory.Exists temp then Directory.Delete(temp, true)

[<Fact>]
let ``isFresh is false when no metadata exists`` () =
    withTempRoot (fun () ->
        Assert.False(ScrapeMetadataRepository.isFresh DateTimeOffset.UtcNow (TimeSpan.FromDays 14.0) "a@b.pl"))

[<Fact>]
let ``isFresh is true within the window`` () =
    withTempRoot (fun () ->
        let now = DateTimeOffset.UtcNow
        ScrapeMetadataRepository.save "a@b.pl" { ScrapedAt = now.AddDays -2.0; SkippedOrders = None; Diagnostics = None }
        Assert.True(ScrapeMetadataRepository.isFresh now (TimeSpan.FromDays 14.0) "a@b.pl"))

[<Fact>]
let ``isFresh is false outside the window`` () =
    withTempRoot (fun () ->
        let now = DateTimeOffset.UtcNow
        ScrapeMetadataRepository.save "a@b.pl" { ScrapedAt = now.AddDays -15.0; SkippedOrders = None; Diagnostics = None }
        Assert.False(ScrapeMetadataRepository.isFresh now (TimeSpan.FromDays 14.0) "a@b.pl"))

[<Fact>]
let ``metadata without diagnostics remains readable`` () =
    withTempRoot (fun () ->
        JsonStorage.writeProfile "a@b.pl" "metadata.json" {| ScrapedAt = DateTimeOffset.UtcNow |}
        let loaded = (ScrapeMetadataRepository.get "a@b.pl").Value
        Assert.True(loaded.Diagnostics.IsNone)
        Assert.True(loaded.SkippedOrders.IsNone))

[<Fact>]
let ``metadata preserves diagnostic bodies without an order identifier`` () =
    withTempRoot (fun () ->
        let diagnostic =
            { RideError.create None "http_response" (InvalidOperationException "invalid response") with
                ResponseBody = Some ""
                Endpoint = Some "/orderDriver/v1/getPreviousOrder" }
        ScrapeMetadataRepository.save "a@b.pl"
            { ScrapedAt = DateTimeOffset.UtcNow; SkippedOrders = None; Diagnostics = Some [| diagnostic |] }
        let actual = (ScrapeMetadataRepository.get "a@b.pl").Value.Diagnostics.Value[0]
        Assert.Equal(diagnostic, actual))
