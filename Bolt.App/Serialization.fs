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

/// PLN amount; on the wire "N,NN zł" with a no-break space (U+00A0)
/// before "zł", e.g. "19,20 zł", "-7,03 zł".
[<Struct>]
type Money =
    | Money of decimal

    member this.Value =
        let (Money v) = this
        v

/// Tip amount; on the wire "Napiwek N,NN zł" — plain Money with a
/// constant "Napiwek " prefix.
[<Struct>]
type Tip =
    | Tip of decimal

    member this.Value =
        let (Tip v) = this
        v

/// Distance in kilometres; on the wire "1.1km" or "10km" (dot decimal
/// separator, at most one decimal, no space).
[<Struct>]
type Distance =
    | Distance of decimal

    member this.Value =
        let (Distance v) = this
        v

type MoneyConverter() =
    inherit JsonConverter<Money>()

    static let nbsp = '\u00A0'
    static let pl = CultureInfo.GetCultureInfo("pl-PL")

    override _.Read(reader, _, _) =
        let s = reader.GetString()
        let core = s.Substring(0, s.Length - 3) // strip NBSP + "zł"
        Money(Decimal.Parse(core, NumberStyles.Number, pl))

    override _.Write(writer, Money value, _) =
        writer.WriteStringValue(value.ToString("0.00", pl) + string nbsp + "zł")

type TipConverter() =
    inherit JsonConverter<Tip>()

    static let nbsp = '\u00A0'
    static let pl = CultureInfo.GetCultureInfo("pl-PL")
    static let prefix = "Napiwek "

    override _.Read(reader, _, _) =
        let s = reader.GetString()
        let core = s.Substring(prefix.Length, s.Length - prefix.Length - 3)
        Tip(Decimal.Parse(core, NumberStyles.Number, pl))

    override _.Write(writer, Tip value, _) =
        writer.WriteStringValue(prefix + value.ToString("0.00", pl) + string nbsp + "zł")

type DistanceConverter() =
    inherit JsonConverter<Distance>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()
        Distance(Decimal.Parse(s.Substring(0, s.Length - 2), CultureInfo.InvariantCulture))

    override _.Write(writer, Distance value, _) =
        // "0.#" reproduces both wire forms: 1.1M -> "1.1km", 10M -> "10km"
        writer.WriteStringValue(value.ToString("0.#", CultureInfo.InvariantCulture) + "km")

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
        // Insert(0): these wrappers are F# unions, so the JsonFSharpConverter
        // factory (already in Converters) would claim them first; ours must precede it.
        opts.Converters.Insert(0, UnixTimeConverter())
        opts.Converters.Insert(0, MoneyConverter())
        opts.Converters.Insert(0, TipConverter())
        opts.Converters.Insert(0, DistanceConverter())
        opts

    let serialize x =
        JsonSerializer.Serialize(x, serializerSettings)

    let deserialize<'a> (x: string) =
        JsonSerializer.Deserialize<'a>(x, serializerSettings)
        
    let deserializeNode<'a> (x: JsonNode) =
        JsonSerializer.Deserialize<'a>(x, serializerSettings)
