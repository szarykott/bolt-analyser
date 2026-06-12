module Bolt.App.Serialization

open System.Text.Json.Nodes

module Json =
    open System.Text.Json
    open System.Text.Json.Serialization


    let private serializerSettings =
        let opts =
            JsonFSharpOptions
                .Default()
                .WithSkippableOptionFields(SkippableOptionFields.Always, deserializeNullAsNone = true)
                .WithUnionUnwrapFieldlessTags()
                .ToJsonSerializerOptions()
        opts.NumberHandling <- JsonNumberHandling.AllowReadingFromString
        opts

    let serialize x =
        JsonSerializer.Serialize(x, serializerSettings)

    let deserialize<'a> (x: string) =
        JsonSerializer.Deserialize<'a>(x, serializerSettings)
        
    let deserializeNode<'a> (x: JsonNode) =
        JsonSerializer.Deserialize<'a>(x, serializerSettings)
