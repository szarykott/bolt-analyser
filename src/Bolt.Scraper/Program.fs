open System
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Bolt.Infrastrucutre.Logging
open Bolt.Infrastrucutre.ces.TaskResultBuilder
open Bolt.Infrastrucutre.storage.Storage
open Bolt.Infrastrucutre.storage.Constants
open Bolt.Scraper.BoltApi.ApiModels
open Bolt.Scraper.BoltApi.Tokens
open Bolt.Scraper.BoltApi.bolt.BoltApi
open Bolt.Scraper.Config
open Bolt.Scraper.Krakow.Districts
open Bolt.Scraper.Meteo.OpenMeteo

let serialize element =
    let options = JsonSerializerOptions(WriteIndented = true)
    JsonSerializer.Serialize(element, options)

let config = AppConfig.read "appsettings.json"

let scrapeBoltData () = 
    let email =
        match config.Email with
        | Some email ->
            printfn $"Using {email} from configuration"
            email
        | None ->
            printf "Input email address you are using for Bolt Driver app: "
            Console.ReadLine()
    
    let cfg: ApiConfig =
        { BaseUrl = Uri "https://driver.live.boltsvc.net"
          HttpClient = new HttpClient()
          Tokens = TokenStore.fromPrevious email |> Option.defaultValue (TokenStore.empty email)
          RubbishData =
            { DeviceName = "Google Pixel 9"
              DeviceUid = "7d86eace-a407-46e1-bb98-73824c818dee"
              DeviceOsVersion = "Android16"
              DeviceType = "android"
              Version = "DI.116.0"
              Country = "pl"
              Language = "pl" } }
    
    let tokenCallback =
        fun () ->
            printf "Input tracking URL from email you just received: "
            Console.ReadLine()
    
    let result =
        taskResult {
            let! bolt = BoltClient.createBolt cfg email tokenCallback CancellationToken.None
    
            do! BoltClient.getDriverProfile bolt
                |>! JsonStorage.write "driverProfile.json"
    
            do! BoltClient.getActivityHours bolt
                |>! JsonStorage.write "activityHours.json"
            
            let! handles, history = BoltClient.getOrderHistory bolt
            
            history |> JsonStorage.write "orderHistory.json"
    
            do! handles
                |> Seq.map (BoltClient.getPreviousOrder bolt)
                |> Task.WhenAll
                |> sequenceResults
                |>! JsonStorage.write "previousOrders.json"
    
            do! handles
                |> Seq.map (BoltClient.getPastOrderDetails bolt)
                |> Task.WhenAll
                |> sequenceResults
                |>! JsonStorage.write "pastOrderDetails.json"
        }
    
    match result.Result with
    | Ok _ -> printfn "Scraping Bolt finished."
    | Error e -> printfn $"{e}"

let scrapeKrakowGeoData () =
    let result =
        taskResult {
            do! getKrakowDistricts CancellationToken.None
                |>! JsonStorage.write "krakowDistricts.json" 
        }
    
    match result.Result with
    | Ok _ -> printfn "Scraping Krakow districts finished."
    | Error e -> printfn $"{e}"

let scrapeMeteoData () =
    let result =
        taskResult {
            do! getWeatherData
                    (DateTimeOffset.Parse("2026-03-15"))
                    (DateTimeOffset.Parse("2026-06-30"))
                    { Latitude = 50.06255f; Longitude = 19.923765f  }
                |>! JsonStorage.write "krakowMeteoData.json"
        }
    
    match result.Result with
    | Ok _ -> printfn "Scraping Meteo data finished."
    | Error e -> printfn $"{e}"

//========== PROGRAM ================//

printfn "Welcome to Bolt & stuff scraper and data analyser!"

Paths.ensureStorageExists ()
|> fun d -> Logger.info $"Using storage location: {d.FullName}"

printfn "Select one of following actions: "
printfn "(1) Scrape Bolt data "
printfn "(2) Scrape Krakow district data"
printfn "(3) Scrape Meteo data"

let action = Console.ReadLine()

match action with
| "1" -> scrapeBoltData ()
| "2" -> scrapeKrakowGeoData ()
| "3" -> scrapeMeteoData ()
| _   -> failwith "Unknown action selected"
