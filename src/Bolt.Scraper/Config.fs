module Bolt.Scraper.Config

open System.IO
open System.Text.Json
open System.Text.Json.Serialization

module AppConfig =
    type Configuration = {
        [<JsonPropertyName("email")>]
        Email: string option
    }
    
    let read file =
        let contents = File.ReadAllText(file)
        JsonSerializer.Deserialize<Configuration>(contents)
