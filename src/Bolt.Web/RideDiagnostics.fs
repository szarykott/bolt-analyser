module Bolt.Web.RideDiagnostics

open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json
open Bolt.Models.BoltApi

let private options = JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower)

let toJson (error: RideError) = JsonSerializer.Serialize(error, options)

let instructions supportEmail =
    let contact =
        match supportEmail with
        | Some email -> $"Jeśli chcesz zgłosić problem, wyślij ten ZIP na adres {email}."
        | None -> "Jeśli chcesz zgłosić problem, przekaż ten ZIP autorowi aplikacji."
    "Archiwum zawiera wyłącznie kursy pominięte z powodu błędów danych.\n"
    + "Każdy plik JSON zawiera oryginalne odpowiedzi API dla jednego kursu, etap błędu, komunikat i stack trace.\n"
    + contact + "\nPlik nie jest wysyłany automatycznie. Przed wysłaniem możesz sprawdzić jego zawartość.\n"

let zip supportEmail (errors: RideError[]) =
    use output = new MemoryStream()
    do
        use archive = new ZipArchive(output, ZipArchiveMode.Create, true)
        let write name text =
            let entry = archive.CreateEntry(name, CompressionLevel.Optimal)
            use stream = entry.Open()
            use writer = new StreamWriter(stream, UTF8Encoding(false))
            writer.Write(text: string)
        write "README.txt" (instructions supportEmail)
        errors |> Array.iteri (fun index error -> write $"ride-{index + 1}-{error.OrderId}.json" (toJson error))
    output.ToArray()
