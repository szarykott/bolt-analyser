module Bolt.Scraper.Krakow.Districts

open System
open System.Net.Http
open System.Text.Json.Serialization
open System.Threading
open Bolt.Infrastrucutre.Serialization
open Bolt.Infrastrucutre.ces.TaskResultBuilder
open Bolt.Models.Geo
open Bolt.Scraper.Http.RequestBuilder

type private Properties = {
    [<JsonPropertyName("nazwa")>]
    Name: string
}

type private Coordinate = float32 array

type private Geometry = {
    [<JsonPropertyName("coordinates")>]
    Coordinates: Coordinate array array array
}

type private Feature = {
    [<JsonPropertyName("properties")>]
    Properties: Properties
    [<JsonPropertyName("geometry")>]
    Geometry: Geometry
}

type private DistrictsResponse = {
    [<JsonPropertyName("features")>]
    Features: Feature array
}

let getKrakowDistricts (ct: CancellationToken) =
    let url = "https://msip3.um.krakow.pl/server/services/Pobieranie/Dzielnice/MapServer/WFSServer?service=WFS&version=2.0.0&request=GetFeature&typeName=Dzielnice:dzielnice&outputFormat=GEOJSON"
    
    let coordinateToPoint (coord: Coordinate) =
        match coord with
        | [| ln; lt |] -> { Latitude = lt; Longitude = ln  }
        | _ -> failwith "Invalid number of elements of array; expected 2"
        
    
    let responseToDistrict (resp : Feature) =
        let name = resp.Properties.Name
        let points : Point array =
            resp.Geometry.Coordinates
                |> Array.collect id
                |> Array.collect id
                |> Array.map coordinateToPoint
        
        District.create name points
    
    let maybeDistricts =
        task {
            use client = new HttpClient(Timeout = TimeSpan.FromSeconds(30.0))
            try
                let request = newRequest HttpMethod.Get (Uri(url))
                use! httpResponseMessage = client.SendAsync(request, ct)
                let! body = httpResponseMessage.Content.ReadAsStringAsync(ct)
                return Ok(Json.deserialize<DistrictsResponse> body)
            with
            | ex -> return Error(ex.Message)
        }
    
    taskResult {
        let! districts = maybeDistricts
        let mappedDistricts =
            districts.Features
            |> Array.map responseToDistrict
        return mappedDistricts
    }
