module Bolt.Infrastructure.Tests.ScrapeMetadataTests

open System
open System.IO
open Xunit
open Bolt.Infrastructure.Repository

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
        ScrapeMetadataRepository.save "a@b.pl" { ScrapedAt = now.AddDays -2.0; SkippedOrders = None }
        Assert.True(ScrapeMetadataRepository.isFresh now (TimeSpan.FromDays 14.0) "a@b.pl"))

[<Fact>]
let ``isFresh is false outside the window`` () =
    withTempRoot (fun () ->
        let now = DateTimeOffset.UtcNow
        ScrapeMetadataRepository.save "a@b.pl" { ScrapedAt = now.AddDays -15.0; SkippedOrders = None }
        Assert.False(ScrapeMetadataRepository.isFresh now (TimeSpan.FromDays 14.0) "a@b.pl"))
