module Bolt.App.ResultBuilder

type ResultBuilder() =
    member _.Return(x: 'a) : Result<'a, 'e> = Ok x
    member _.Bind (a: Result<'a, 'e>, f: 'a -> Result<'b, 'e>) : Result<'b, 'e> =
        match a with
        | Error e -> Error e
        | Ok a -> f a

let result = ResultBuilder()