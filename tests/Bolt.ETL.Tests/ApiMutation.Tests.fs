module Bolt.ETL.Tests.ApiMutationTests

open System
open FsCheck
open FsCheck.FSharp
open global.Xunit
open global.Xunit.Abstractions
open Bolt.Models
open Bolt.Models.BoltApi
open Bolt.ETL
open Bolt.Tests.BoltApiFixture
open Bolt.Tests.ApiMutations

let private verify raw =
    match (RideFactory.parseOrder raw).Data with
    | RideType.Error error ->
        Assert.Equal(Some raw.OrderId, error.OrderId)
        Assert.Contains(error.Stage, [| "previous_order"; "past_order_detail"; "ride" |])
        Assert.False(String.IsNullOrWhiteSpace error.ExceptionType)
        Assert.False(String.IsNullOrWhiteSpace error.Message)
        Assert.False(String.IsNullOrWhiteSpace error.StackTrace)
        Assert.Equal(raw.PreviousOrder.GetRawText(), error.PreviousOrder.Value.GetRawText())
        Assert.Equal(raw.PastOrderDetail.GetRawText(), error.PastOrderDetail.Value.GetRawText())
        "diagnostic"
    | Finished _ -> "finished"
    | DidNotHappen _ -> "not-finished"

type ApiMutationTests(output: ITestOutputHelper) =
    [<Fact>]
    member _.``every field and collection survives exhaustive single mutations with intact diagnostics`` () =
        let outcomes = Collections.Generic.Dictionary<string, int>()
        let mutable count = 0
        for description, raw in singleMutations () do
            try
                let outcome = verify raw
                outcomes[outcome] <- (match outcomes.TryGetValue outcome with true, n -> n | _ -> 0) + 1
                count <- count + 1
            with ex -> failwithf "%s\n%A" description ex
        output.WriteLine(sprintf "Single mutations: %d; outcomes: %A" count (outcomes |> Seq.map (fun kv -> kv.Key, kv.Value) |> Seq.toArray))
        Assert.True(count > 500)
        Assert.True(outcomes["diagnostic"] > 0)
        Assert.True(outcomes["finished"] > 0)

    [<Theory; InlineData(101UL); InlineData(2027UL); InlineData(104729UL)>]
    member _.``adaptive mutation sequences cannot escape parser or lose original JSON`` (seed: uint64) =
        let scenarios =
            gen {
                let! count = Gen.choose(1, 6)
                return! Gen.listOfLength count (gen {
                    let! previous = Gen.elements [ true; false ]
                    let! index = Gen.choose(0, 10000)
                    let! replacement = Gen.choose(0, replacements.Length - 1)
                    return previous, index, replacement
                })
            }
        let shrink choices = seq { if List.length choices > 1 then yield List.tail choices }
        let property =
            Prop.forAll (Arb.fromGenShrink(scenarios, shrink)) (fun choices ->
                let mutable p = previousOrder ()
                let mutable d = pastOrderDetail ()
                for previous, index, replacement in choices do
                    let node = if previous then p else d
                    let available = paths "" node |> Seq.toArray
                    let changed = apply available[index % available.Length] replacements[replacement] node
                    if previous then p <- changed else d <- changed
                let outcome = verify (rawOrder 2L p d)
                true |> Prop.collect outcome)
        let config =
            Config.QuickThrowOnFailure.WithMaxTest(5000)
                .WithReplay(Some { Rnd = Rnd(seed); Size = None })
                .WithRunner(FsCheck.Xunit.TestOutputRunner(output))
        Check.One(config, property)
