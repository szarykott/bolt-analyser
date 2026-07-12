module Bolt.Infrastrucutre.storage.Constants

open System
open System.IO

module Paths =
    // Read per call so tests (and deployments) can override via env var.
    let private root () =
        Environment.GetEnvironmentVariable "BOLT_STORAGE_ROOT"
        |> Option.ofObj
        |> Option.defaultValue (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))

    let private storageFolder = ".bolt-app"

    /// Email → filesystem-safe folder name. Lowercased; anything outside
    /// letters, digits, '.', '@', '-', '+' becomes '_'.
    let sanitizeProfile (profile: string) =
        profile.Trim().ToLowerInvariant()
        |> Seq.map (fun c ->
            if Char.IsLetterOrDigit c || c = '.' || c = '@' || c = '-' || c = '+' then c else '_')
        |> Seq.toArray
        |> String

    let ensureStorageExists () =
        Directory.CreateDirectory(Path.Combine(root (), storageFolder))

    let getQualifiedStorageLocation filename =
        Path.Combine(root (), storageFolder, filename)

    let ensureProfileStorageExists profile =
        Directory.CreateDirectory(Path.Combine(root (), storageFolder, sanitizeProfile profile))

    let getProfileStorageLocation profile filename =
        Path.Combine(root (), storageFolder, sanitizeProfile profile, filename)