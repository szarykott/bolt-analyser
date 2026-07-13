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

let private ok () : Task<Result<unit, string>> = Task.FromResult(Ok())
let private err e : Task<Result<unit, string>> = Task.FromResult(Error e)

/// Happy-path deps over a unit session; individual tests override fields.
let private baseDeps: PipelineDeps<unit> = {
    IsFresh = fun _ -> false
    CreateSession = fun _ -> ()
    HasTokens = fun _ -> false
    RefreshTokens = fun _ _ -> ok ()
    RequestMagicLink = fun _ _ -> ok ()
    AuthenticateWithUrl = fun _ _ _ -> ok ()
    ScrapeRides = fun _ _ _ -> ok ()
    EnsureMeteo = fun _ _ -> ok ()
    RunAnalysis = fun _ -> Task.FromResult(Ok report)
}

let private runToEnd deps initialLink (feed: string list) =
    let states = ConcurrentQueue<JobState>()
    let channel = Channel.CreateUnbounded<string>()
    for url in feed do channel.Writer.TryWrite url |> ignore
    let notify state = states.Enqueue state; Task.CompletedTask
    (JobRunner.run deps "a@b.pl" initialLink channel.Reader notify CancellationToken.None)
        .GetAwaiter()
        .GetResult()
    states |> List.ofSeq

[<Fact>]
let ``fresh cache short-circuits to analysis`` () =
    let states = runToEnd { baseDeps with IsFresh = fun _ -> true } None []
    Assert.Equal<JobState list>([ CheckingCache; RunningAnalysis; Done report ], states)

[<Fact>]
let ``no tokens goes through magic link then scrapes`` () =
    let states = runToEnd baseDeps None [ "https://link" ]
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
    let states = runToEnd deps None [ "bad"; "good" ]
    Assert.Contains(AwaitingMagicLink(Some "bad token"), states)
    Assert.Equal(Done report, List.last states)
    Assert.Equal(2, attempts.Count)

[<Fact>]
let ``initial magic link skips waiting (stateless login)`` () =
    let states = runToEnd baseDeps (Some "https://link") []
    Assert.DoesNotContain(AwaitingMagicLink None, states)
    Assert.Equal(Done report, List.last states)

[<Fact>]
let ``valid saved tokens skip magic link`` () =
    let deps = { baseDeps with HasTokens = fun _ -> true }
    let states = runToEnd deps None []
    Assert.DoesNotContain(AwaitingMagicLink None, states)
    Assert.Equal(Done report, List.last states)

[<Fact>]
let ``scrape failure ends in Failed`` () =
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            ScrapeRides = fun _ _ _ -> err "boom" }
    let states = runToEnd deps None []
    Assert.Equal(Failed("scraping rides", "boom"), List.last states)

[<Fact>]
let ``cancellation while awaiting magic link produces no terminal state`` () =
    let states = ConcurrentQueue<JobState>()
    let channel = Channel.CreateUnbounded<string>()
    use cts = new CancellationTokenSource()
    let notify state = states.Enqueue state; Task.CompletedTask
    let running = JobRunner.run baseDeps "a@b.pl" None channel.Reader notify cts.Token
    // Give the runner a moment to reach the await, then cancel.
    Task.Delay(100).GetAwaiter().GetResult()
    cts.Cancel()
    running.GetAwaiter().GetResult()
    Assert.DoesNotContain(states, fun s -> match s with Done _ | Failed _ -> true | _ -> false)
