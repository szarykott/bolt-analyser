module Bolt.Web.Tests.JobRunnerTests

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Xunit
open Bolt.Web
open Bolt.Web.Jobs
open Bolt.Web.Tests.ReportFixture

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
    RunAnalysis = fun _ -> Task.FromResult(Ok report)
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
        [ CheckingCache; RunningAnalysis; Done report ], states)

[<Fact>]
let ``no tokens goes through magic link then scrapes`` () =
    let states = runToEnd baseDeps [ "https://link" ]
    Assert.Contains(AwaitingMagicLink None, states)
    Assert.Contains(RunningAnalysis, states)
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
let ``scraped data flows directly into analysis`` () =
    let received = ConcurrentQueue<int>()
    let deps =
        { baseDeps with
            HasTokens = fun _ -> true
            RunAnalysis = fun data -> received.Enqueue data; Task.FromResult(Ok report) }
    runToEnd deps [] |> ignore
    Assert.Equal<int list>([ 3 ], List.ofSeq received)

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
