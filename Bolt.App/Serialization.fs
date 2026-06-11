module Bolt.App.Serialization

module Json =
    open System.Text.Json
    open System.Text.Json.Serialization


    let private serializerSettings =
        let opts =
            JsonFSharpOptions.Default().WithSkippableOptionFields().ToJsonSerializerOptions()
        opts.NumberHandling <- JsonNumberHandling.AllowReadingFromString
        opts

    let serialize x =
        JsonSerializer.Serialize(x, serializerSettings)

    let deserialize<'a> (x: string) =
        JsonSerializer.Deserialize<'a>(x, serializerSettings)
