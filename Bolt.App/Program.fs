open Bolt
open System
open System.Net.Http
open System.Threading
open TaskResultBuilder

let cfg: ApiConfig =
    { BaseUrl = new Uri "https://driver.live.boltsvc.net"
      HttpClient = new HttpClient()
      Tokens = TokenStore.create
            { Access = AccessToken("", DateTimeOffset.UtcNow)
              Refresh = RefreshToken "" } 
      RubbishData = {
        DeviceName = "Google Pixel 9"
        DeviceUid = "7d86eace-a407-46e1-bb98-73824c818dee"
        DeviceOsVersion = "Android 16"
        Version = "DI.116.0"
      }
    }

printfn "Welcome to Bolt scraper and data analyser!"
printf "Input email address you are using for Bolt Driver app: "

let email = Console.ReadLine()

let tokenCallback = fun () ->
    printf "Input tracking URL from email you just received: "
    Console.ReadLine()

let result = taskResult {
  do! LowLevelApi.initialize cfg email tokenCallback CancellationToken.None
}

match result.Result with
| Ok _ -> printfn "all good"
| Error e -> printfn $"{e}"