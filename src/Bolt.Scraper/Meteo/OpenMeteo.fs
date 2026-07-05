namespace Bolt.Scraper.Meteo

open System
open System.Globalization
open System.Net.Http
open System.Text.Json.Serialization
open System.Threading
open Bolt.Infrastrucutre.Serialization
open Bolt.Infrastrucutre.ces.TaskResultBuilder
open Bolt.Models.Geo
open Bolt.Models.Meteo
open Bolt.Scraper.Http.RequestBuilder

module OpenMeteo =
    type private OpenMeteoHourly = {
        [<JsonPropertyName("time")>]
        Time: string array
        [<JsonPropertyName("temperature_2m")>]
        Temperature: float array
        [<JsonPropertyName("rain")>]
        Rain: float array
        [<JsonPropertyName("snowfall")>]
        Snowfall: float array
    }
    
    type private OpenMeteoResponse = {
        [<JsonPropertyName("hourly")>]
        Hourly: OpenMeteoHourly
    }
    
    let zip4 a1 a2 a3 a4 =
        let z3 = Array.zip3 a1 a2 a3
        Array.zip z3 a4
        |> Array.map (fun ((a, b, c), d) -> (a, b, c, d))
    
    let getWeatherData (from: DateTimeOffset) (to' : DateTimeOffset) (coord: GeoPoint) =
        let fromString = from.ToString("yyyy-MM-dd")
        let toString = to'.ToString("yyyy-MM-dd")
        let url = $"https://archive-api.open-meteo.com/v1/archive?latitude={coord.Latitude}&longitude={coord.Longitude}&start_date={fromString}&end_date={toString}&hourly=temperature_2m,rain,snowfall"
        
        let openMeteoResponse =
            task {
                use client = new HttpClient()
                try
                    let request = newRequest HttpMethod.Get (Uri(url))
                    use! httpResponseMessage = client.SendAsync(request, CancellationToken.None)
                    let! body = httpResponseMessage.Content.ReadAsStringAsync()
                    return Ok(Json.deserialize<OpenMeteoResponse> body)
                with
                | ex -> return Error(ex.Message)
            }
    
        taskResult {
            let! data = openMeteoResponse
            return zip4 data.Hourly.Time data.Hourly.Temperature data.Hourly.Rain data.Hourly.Snowfall
            |> Array.map (fun (h, a, b, c) -> (DateTimeOffset.Parse(h, null, DateTimeStyles.AssumeUniversal), { Temperature = a * 1.0<celcius>; Rain = b * 1.0<mm>; Snow = c * 1.0<cm> }))
            |> Map.ofArray
            |> fun f -> {  Center = coord; Data = f }
        }

