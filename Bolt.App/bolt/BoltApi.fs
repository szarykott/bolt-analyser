module Bolt.App.bolt.BoltApi

open System
open System.Linq
open System.Net.Http
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Bolt.App.bolt.Models
open Bolt.App.bolt.Http
open Bolt.App.bolt.LowLevelApi
open TaskResultBuilder

module BoltClient =
    [<RequireQualifiedAccess>]
    type Bolt = Instance of ApiConfig

    let private getConfig (Bolt.Instance config) = config
    
    let createBolt config email callback ct=
        taskResult {
            do! LowLevelApi.initialize config email callback ct
            return Bolt.Instance config
        }
        
    let getDriverProfile bolt =
        let config = getConfig bolt
        taskResult {
            let query = let d = config.RubbishData in $"version={d.Version}&country={d.Country}&language={d.Language}&deviceType={d.DeviceType}&deviceId={d.DeviceUid}&device_os_version={d.DeviceOsVersion}"
            let! response : JsonElement =
                RequestBuilder.newRequest HttpMethod.Get (Uri(config.BaseUrl, $"driver/getDriverProfile?{query}"))
                |> LowLevelApi.send config CancellationToken.None
            
            return response
        }
    
    let rec getOrderHistoryPage bolt cursor=
        let config = getConfig bolt
        taskResult {
            let query = let d = config.RubbishData in $"version={d.Version}&country={d.Country}&language={d.Language}&deviceType={d.DeviceType}&deviceId={d.DeviceUid}&device_os_version={d.DeviceOsVersion}"
            
            let query = cursor
                        |> Option.map (fun f -> query + $"&cursor={f}")
                        |> Option.defaultValue query
            
            let! response : {| cursor: int32 option; orders: JsonArray  |} =
                RequestBuilder.newRequest HttpMethod.Get (Uri(config.BaseUrl, $"orderDriver/v1/getOrderHistoryPaginated?{query}"))
                |> LowLevelApi.send config CancellationToken.None
            
            return response
        }
    
    let rec getOrderHistory bolt =
        taskResult {
            let mutable cont = true
            let mutable acc = Seq.empty
            let mutable cursor = None
            
            while cont do
                let! page : {| cursor: int32 option; orders: JsonArray  |} = getOrderHistoryPage bolt cursor
                
                cursor <- page.cursor
                acc <- page.orders |> Seq.append acc
                
                if cursor.IsNone then
                    cont <- false

        
            return acc
        }
        
            
    
    let getPreviousOrder bolt =
        failwith "not yet implemented"
        
    let getPastOrderDetails bolt =
        failwith "not yet implemented"
        
    let getActivityHours bolt =
        failwith "not yet implemented"
