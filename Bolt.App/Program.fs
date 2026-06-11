open System
open System.Net.Http
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Bolt.App.Logging
open Bolt.App.storage
open TaskResultBuilder
open Bolt.App.bolt.Models
open Bolt.App.bolt.Tokens
open Bolt.App.bolt.BoltApi
open Bolt.App.Config
open Bolt.App.Constants

let serialize element =
  let options = JsonSerializerOptions(WriteIndented = true)
  JsonSerializer.Serialize(element, options)

let config = AppConfig.read "appsettings.json"

printfn "Welcome to Bolt scraper and data analyser!"

Paths.ensureStorageExists ()
|> fun d -> Logger.info $"Using storage location: {d.FullName}"

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
      RubbishData = {
        DeviceName = "Google Pixel 9"
        DeviceUid = "7d86eace-a407-46e1-bb98-73824c818dee"
        DeviceOsVersion = "Android16"
        DeviceType = "android"
        Version = "DI.116.0"
        Country = "pl"
        Language = "pl"
      }
    }

let tokenCallback = fun () ->
    printf "Input tracking URL from email you just received: "
    Console.ReadLine()

let result = taskResult {
  let! bolt = BoltClient.createBolt cfg email tokenCallback CancellationToken.None
  
  do! BoltClient.getDriverProfile bolt
      |>! JsonStorage.write "driverProfile.json"
  
  do! BoltClient.getOrderHistory bolt
      |>! JsonStorage.write "orderHistory.json"
}

match result.Result with
| Ok _ -> printfn "Program finished."
| Error e -> printfn $"{e}"