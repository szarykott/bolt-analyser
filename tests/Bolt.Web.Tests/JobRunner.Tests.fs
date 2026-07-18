module Bolt.Web.Tests.JobRunnerTests

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Xunit
open Bolt.ETL.Analysis
open Bolt.Web
open Bolt.Web.Jobs

let private report: AnalysisReport = {
    Email = "a@b.pl"
    GeneratedAt = DateTimeOffset.UtcNow
    RideCount = 1
    DateRange = (DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
    Sections = []
}

let private cap = DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
let private ok () : Task<Result<unit, string>> = Task.FromResult(Ok())
let private err e : Task<Result<unit, string>> = Task.FromResult(Error e)

/// Happy-path deps over a unit session and int data (the ride count);
/// individual tests override fields.
let private baseDeps: PipelineDeps<unit, int> = {
    LoadCached = fun _ -> None
    CreateSession = fun _ -> ()
    HasTokens = fun _ -> false
    RefreshTokens = fun _ _ -> ok ()
    RequestMagicLink = fun _ _ -> ok ()
    AuthenticateWithUrl = fun _ _ _ -> ok ()
    ScrapeRides = fun _ _ _ -> Task.FromResult(Ok 3)
    EnsureMeteo = fun _ _ -> Task.FromResult(Ok cap)
    RunAnalysis = fun _ _ -> Task.FromResult(Ok report)
    RideCountOf = id
}

let private runToEnd deps (feed: string list) =
    let states = ConcurrentQueue<JobState>()
    let channel = Channel.CreateUnbounded<string>()
    for url in feed do channel.Writer.TryWrite url |> ignore
    let notify state = states.Enqueue state; Task.CompletedTask
    (JobRunner.run deps "a@b.pl" channel.Reader notify CancellationToken.None)
        .GetAwaiter()
        .GetResult()
    states |> List.ofSeq

[<Fact>]
let ``cached data skips auth and scraping`` () =
    let states = runToEnd { baseDeps with LoadCached = fun _ -> Some 3 } []
    Assert.Equal<JobState list>(
        [ CheckingCache; FetchingMeteo; RunningAnalysis; Done report ], states)

[<Fact>]
let ``no tokens goes through magic link then scrapes`` () =
    let states = runToEnd baseDeps [ "https://link" ]
    Assert.Contains(AwaitingMagicLink None, states)
    Assert.Contains(FetchingMeteo, states)
    Assert.Equal(Done report, List.last states)

[<Fact>]
let ``invalid magic link keeps awaiting with error`` () =
    let attempts = ConcurrentQueue<string>()
    let deps =
        { baseDeps with
            AuthenticateWithUrl = fun _ url _ ->
                attempts.Enqueue url
                if url = "bad" then err "bad token" else ok () }
    let states = runToEnd deps [ "bad"; "good" ]
    Assert.Contains(AwaitingMagicLink(Some "bad token"), states)
    Assert.Equal(Done report, List.last states)
    Assert.Equal(2, attempts.Count)

[<Fact>]
let ``valid saved tokens skip magic link`` () =
    let deps = { baseDeps with HasTokens = fun _ -> true }
    let states = runToEnd deps []
    Assert.DoesNotContain(AwaitingMagicLink None, states)
    Assert.Equal(Done report, List.last states)

[<Fact>]
let ``scrape failure ends in Failed`` () =
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            ScrapeRides = fun _ _ _ -> Task.FromResult(Error "boom") }
    let states = runToEnd deps []
    Assert.Equal(Failed("pobieranie przejazdów", "boom"), List.last states)

[<Fact>]
let ``zero scraped rides fail at the scraping step`` () =
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            ScrapeRides = fun _ _ _ -> Task.FromResult(Ok 0) }
    let states = runToEnd deps []
    Assert.Equal(
        Failed("pobieranie przejazdów", "Nie znaleziono przejazdów dla tego konta"), List.last states)

[<Fact>]
let ``weather cap flows from EnsureMeteo into RunAnalysis`` () =
    let received = ConcurrentQueue<DateTimeOffset>()
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            RunAnalysis = fun _ c -> received.Enqueue c; Task.FromResult(Ok report) }
    runToEnd deps [] |> ignore
    Assert.Equal<DateTimeOffset list>([ cap ], List.ofSeq received)

[<Fact>]
let ``meteo failure ends in Failed at the weather step`` () =
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            EnsureMeteo = fun _ _ -> Task.FromResult(Error "boom") }
    let states = runToEnd deps []
    Assert.Equal(Failed("pobieranie danych pogodowych", "boom"), List.last states)

[<Fact>]
let ``cancellation while awaiting magic link produces no terminal state`` () =
    let states = ConcurrentQueue<JobState>()
    let channel = Channel.CreateUnbounded<string>()
    use cts = new CancellationTokenSource()
    let notify state = states.Enqueue state; Task.CompletedTask
    let running = JobRunner.run baseDeps "a@b.pl" channel.Reader notify cts.Token
    // Give the runner a moment to reach the await, then cancel.
    Task.Delay(100).GetAwaiter().GetResult()
    cts.Cancel()
    running.GetAwaiter().GetResult()
    Assert.DoesNotContain(states, fun s -> match s with Done _ | Failed _ -> true | _ -> false)
