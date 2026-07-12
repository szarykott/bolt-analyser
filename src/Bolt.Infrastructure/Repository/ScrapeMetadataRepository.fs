namespace Bolt.Infrastructure.Repository

open System
open Bolt.Infrastrucutre.storage.Storage

module ScrapeMetadataRepository =
    type ScrapeMetadata = { ScrapedAt: DateTimeOffset }

    let private fileName = "metadata.json"

    let save email (metadata: ScrapeMetadata) =
        JsonStorage.writeProfile email fileName metadata

    let get email : ScrapeMetadata option =
        JsonStorage.readProfile email fileName

    let isFresh (now: DateTimeOffset) (maxAge: TimeSpan) email =
        get email
        |> Option.map (fun m -> now - m.ScrapedAt < maxAge)
        |> Option.defaultValue false
