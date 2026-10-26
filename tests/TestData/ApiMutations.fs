module Bolt.Tests.ApiMutations

open System.Text.Json.Nodes
open Bolt.Tests.BoltApiFixture

// Fixed fixture keys contain neither '/' nor '~'. Paths include the entire response.
let rec paths path (node: JsonNode) =
    seq {
        yield path
        let child key = if path = "" then key else path + "/" + key
        match node with
        | :? JsonObject as obj ->
            for property in obj do yield! paths (child property.Key) property.Value
        | :? JsonArray as array ->
            for index in 0 .. array.Count - 1 do yield! paths (child (string index)) array[index]
        | _ -> ()
    }

let replacements =
    [| None; Some "null"; Some "true"; Some "0"; Some "\"\""; Some "[]"; Some "{}"
       Some "1e999"; Some "253402300799"; Some "\"79228162514264337593543950335 zł\"" |]

let apply path (replacement: string option) (node: JsonNode) =
    if path = "" then replacement |> Option.map JsonNode.Parse |> Option.defaultValue null
    else mutate path replacement node

let singleMutations () =
    seq {
        for previous in [ true; false ] do
            let baseline = if previous then previousOrder () else pastOrderDetail ()
            for path in paths "" baseline do
                for replacement in replacements do
                    let p, d = previousOrder (), pastOrderDetail ()
                    let changed = apply path replacement (if previous then p else d)
                    let raw = if previous then rawOrder 2L changed d else rawOrder 2L p changed
                    let side = if previous then "previous" else "detail"
                    yield sprintf "%s:%s <- %A" side path replacement, raw
    }
