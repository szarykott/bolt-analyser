open Bolt
open System
open System.Net.Http
open System.Threading

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

let x = Api.sendMagicLink cfg "radoslaw.edward.kot@gmail.com" CancellationToken.None
match x.Result with
| Ok _ -> printfn "all good"
| Error e -> printfn $"{e}"