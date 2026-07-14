module Bolt.Scraper.Tests.MeteoCoverageTests

open System
open Xunit
open Bolt.Scraper.ScrapePipeline

let private at (iso: string) = DateTimeOffset.Parse iso

[<Fact>]
let ``no existing data means one full range`` () =
    let ranges = MeteoCoverage.missingRanges (at "2026-01-01Z", at "2026-02-01Z") None
    Assert.Equal<(DateTimeOffset * DateTimeOffset) list>([ at "2026-01-01Z", at "2026-02-01Z" ], ranges)

[<Fact>]
let ``fully covered means no ranges`` () =
    let ranges =
        MeteoCoverage.missingRanges
            (at "2026-01-05Z", at "2026-01-20Z")
            (Some(at "2026-01-01Z", at "2026-02-01Z"))
    Assert.Empty ranges

[<Fact>]
let ``gap before and after existing coverage`` () =
    let ranges =
        MeteoCoverage.missingRanges
            (at "2026-01-01Z", at "2026-03-01Z")
            (Some(at "2026-01-10Z", at "2026-02-10Z"))
    Assert.Equal<(DateTimeOffset * DateTimeOffset) list>(
        [ (at "2026-01-01Z", at "2026-01-10Z"); (at "2026-02-10Z", at "2026-03-01Z") ],
        ranges)

[<Fact>]
let ``cappedMax returns rideMax when the archive already covers it`` () =
    Assert.Equal(at "2026-07-01Z", MeteoCoverage.cappedMax (at "2026-07-13Z") (at "2026-07-01Z"))

[<Fact>]
let ``cappedMax clamps to the archive lag before now`` () =
    Assert.Equal(at "2026-07-08Z", MeteoCoverage.cappedMax (at "2026-07-13Z") (at "2026-07-12Z"))
