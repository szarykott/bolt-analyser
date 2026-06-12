module Bolt.Infrastrucutre.ces.OptionBuilder

type OptionBuilder() =
    member _.Bind(opt, binder) =
        match opt with
        | Some value -> binder value
        | None -> None
    
    member _.Return(value) =
        Some value

let maybe = OptionBuilder()
