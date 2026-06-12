module Bolt.App.Serialization

open System
open System.Globalization
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization

/// Bolt's "created" fields use "yyyy.MM.dd HH:mm" with no offset marker; values are UTC
/// (verified against the sibling created_timestamp unix field).
type BoltDateTimeOffsetConverter() =
    inherit JsonConverter<DateTimeOffset>()

    static let format = "yyyy.MM.dd HH:mm"

    override _.Read(reader, _, _) =
        DateTimeOffset.ParseExact(
            reader.GetString(),
            format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal
        )

    override _.Write(writer, value, _) =
        writer.WriteStringValue(value.ToUniversalTime().ToString(format, CultureInfo.InvariantCulture))

/// Unix-seconds timestamp on the wire, DateTimeOffset in the model.
/// Distinct type so its converter can't collide with the global
/// DateTimeOffset converter used for "yyyy.MM.dd HH:mm" strings.
[<Struct>]
type UnixTime =
    | UnixTime of DateTimeOffset

    member this.Value =
        let (UnixTime v) = this
        v

type UnixTimeConverter() =
    inherit JsonConverter<UnixTime>()

    override _.Read(reader, _, _) =
        UnixTime(DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64()))

    override _.Write(writer, UnixTime value, _) =
        writer.WriteNumberValue(value.ToUnixTimeSeconds())

module Json =


    let private serializerSettings =
        let opts =
            JsonFSharpOptions
                .Default()
                .WithSkippableOptionFields(SkippableOptionFields.Always, deserializeNullAsNone = true)
                .WithUnionUnwrapFieldlessTags()
                .ToJsonSerializerOptions()
        opts.NumberHandling <- JsonNumberHandling.AllowReadingFromString
        // global because FSharp.SystemTextJson's record converter ignores
        // [<JsonConverter>] on record fields; applies to every DateTimeOffset
        opts.Converters.Add(BoltDateTimeOffsetConverter())
        // Insert(0): UnixTime is an F# union, so the JsonFSharpConverter factory
        // (already in Converters) would claim it first; ours must precede it.
        opts.Converters.Insert(0, UnixTimeConverter())
        opts

    let serialize x =
        JsonSerializer.Serialize(x, serializerSettings)

    let deserialize<'a> (x: string) =
        JsonSerializer.Deserialize<'a>(x, serializerSettings)
        
    let deserializeNode<'a> (x: JsonNode) =
        JsonSerializer.Deserialize<'a>(x, serializerSettings)
