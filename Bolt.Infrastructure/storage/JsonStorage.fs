module Bolt.Infrastrucutre.storage

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

