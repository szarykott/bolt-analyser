module Bolt.Infrastructure.Tests.StorageTests

open System
open System.IO
open Xunit
open Bolt.Infrastrucutre.storage.Constants
open Bolt.Infrastrucutre.storage.Storage

// All BOLT_STORAGE_ROOT manipulation stays inside this one module so xunit's
// per-class serial execution protects the env var.

let private withTempRoot (f: string -> unit) =
    let temp = Path.Combine(Path.GetTempPath(), "bolt-tests-" + Guid.NewGuid().ToString "N")
    Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", temp)
    try f temp
    finally
        Environment.SetEnvironmentVariable("BOLT_STORAGE_ROOT", null)
        if Directory.Exists temp then Directory.Delete(temp, true)

[<Fact>]
let ``sanitizeProfile lowercases and keeps safe characters`` () =
    Assert.Equal("john.doe@example.com", Paths.sanitizeProfile "John.Doe@Example.COM")

[<Fact>]
let ``sanitizeProfile replaces path-hostile characters`` () =
    Assert.Equal("a_b_c@x.pl", Paths.sanitizeProfile "a/b\\c@x.pl")
    Assert.Equal("..__@x.pl", Paths.sanitizeProfile ("  ../ @x.pl  ".Trim()))

[<Fact>]
let ``profile json roundtrip lands in per-profile folder`` () =
    withTempRoot (fun temp ->
        JsonStorage.writeProfile "User@Example.com" "test.json" (Map [ "a", 1 ])
        let read: Map<string, int> option = JsonStorage.readProfile "User@Example.com" "test.json"
        Assert.Equal<Map<string, int> option>(Some(Map [ "a", 1 ]), read)
        Assert.True(File.Exists(Path.Combine(temp, ".bolt-app", "user@example.com", "test.json"))))

[<Fact>]
let ``readProfile returns None when file absent`` () =
    withTempRoot (fun _ ->
        let read: Map<string, int> option = JsonStorage.readProfile "nobody@example.com" "missing.json"
        Assert.Equal<Map<string, int> option>(None, read))
