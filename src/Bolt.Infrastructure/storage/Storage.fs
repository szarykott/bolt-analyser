module Bolt.Infrastrucutre.storage.Storage

open System
open System.IO
open Bolt.Infrastrucutre.storage.Constants.Paths
open Bolt.Infrastrucutre.Serialization

type FileStorage<'a> = {
    Read: unit -> 'a option
    Write: 'a -> unit
}

module JsonStorage =
    let private fromFile path =
        match File.Exists(path) with
        | true ->
            File.ReadAllText(path)
            |> Json.deserialize
            |> Some
        | false -> None
    
    let private toFile path object =
        let content =
            object
            |> Json.serialize
        
        File.WriteAllText(path, content)
    
    let create file =
        let storageLocation = getQualifiedStorageLocation file
        {
            Read = fun () -> fromFile storageLocation
            Write = toFile storageLocation
        }
        
    let write file data =
        let storage = create file
        storage.Write data
        
    let read file =
        let storage = create file
        storage.Read ()

module CsvStorage =
    type CsvFormat = {
        Headers: string seq
        Rows: string seq seq
    }
    
    let delimiter = ";"
    
    let write file (data: CsvFormat) =
        let headerRow = String.Join(delimiter, data.Headers)
        let dataRows =
            data.Rows
            |> Seq.map (fun r -> String.Join(delimiter, r))
        let allRows = Seq.concat [[| headerRow |]; dataRows |> Array.ofSeq]

        let storageLocation = getQualifiedStorageLocation file
        File.WriteAllLines(storageLocation, allRows)
