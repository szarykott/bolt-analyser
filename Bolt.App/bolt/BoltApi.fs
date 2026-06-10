module Bolt.App.bolt.BoltApi

open System
open System.Net.Http
open System.Text.Json
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
            let query = let d = config.RubbishData in $"version={d.Version}&country={d.Version}&language={d.Language}&deviceType={d.DeviceType}&deviceId={d.DeviceUid}&device_os_version={d.DeviceOsVersion}"
            let! response : JsonElement =
                RequestBuilder.newRequest HttpMethod.Get (Uri(config.BaseUrl, $"driver/getDriverProfile?{query}"))
                |> LowLevelApi.send config CancellationToken.None
            
            return response
        }
        
    let getOrderHistoryPaginated bolt =
        failwith "not yet implemented"
        
    let getPreviousOrder bolt =
        failwith "not yet implemented"
        
    let getPastOrderDetails bolt =
        failwith "not yet implemented"
        
    let getActivityHours bolt =
        failwith "not yet implemented"
