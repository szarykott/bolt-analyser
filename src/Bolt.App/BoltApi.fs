module Bolt.App.bolt.BoltApi

open System
open System.Net.Http
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open Bolt.App.ApiModels
open Bolt.App.Http
open Bolt.App.LowLevel
open Bolt.Infrastrucutre.Serialization
open Bolt.Infrastrucutre.ces.TaskResultBuilder
open Bolt.Models.History

module BoltClient =
    [<RequireQualifiedAccess>]
    type Bolt = Instance of ApiConfig

    let private getConfig (Bolt.Instance config) = config
    
    let createBolt config email callback ct=
        taskResult {
            do! LowLevelApi.initialize config email callback ct
            return Bolt.Instance config
        }
    
    let mandatoryQueryParameters data =
        $"version={data.Version}&country={data.Country}&language={data.Language}&deviceType={data.DeviceType}&deviceId={data.DeviceUid}&device_os_version={data.DeviceOsVersion}"
    
    let getDriverProfile bolt =
        let config = getConfig bolt
        taskResult {
            let! response : JsonElement =
                RequestBuilder.newRequest HttpMethod.Get (Uri(config.BaseUrl, $"driver/getDriverProfile?{config.RubbishData |> mandatoryQueryParameters}"))
                |> LowLevelApi.send config CancellationToken.None
            
            return response
        }
    
    let rec getOrderHistoryPage bolt cursor=
        let config = getConfig bolt
        taskResult {
            let query = config.RubbishData |> mandatoryQueryParameters
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

        
            let handles = acc |> Seq.map Json.deserializeNode<HistoryOrderHandle>
            return (handles, acc)
        }
        
            
    
    let getPreviousOrder bolt orderHandle =
        let config = getConfig bolt
        taskResult {
            let! response : JsonElement =
                RequestBuilder.newRequest HttpMethod.Post (Uri(config.BaseUrl, $"orderDriver/v1/getPreviousOrder?{config.RubbishData |> mandatoryQueryParameters}"))
                |> RequestBuilder.withJsonBody orderHandle
                |> LowLevelApi.send config CancellationToken.None
            
            return response
        }
        
    let getPastOrderDetails bolt orderHandle =
        let config = getConfig bolt
        taskResult {
            let! response : JsonElement =
                RequestBuilder.newRequest HttpMethod.Post (Uri(config.BaseUrl, $"driver/getPastOrderDetails?{config.RubbishData |> mandatoryQueryParameters}"))
                |> RequestBuilder.withJsonBody orderHandle
                |> LowLevelApi.send config CancellationToken.None
            
            return response
        }
        
    let getActivityHours bolt =
        let config = getConfig bolt
        taskResult {
            let! response : JsonElement =
                RequestBuilder.newRequest HttpMethod.Get (Uri(Uri("https://europe-company.taxify.eu"), $"orderDriver/getActivityHours?{config.RubbishData |> mandatoryQueryParameters}&group_by=week"))
                |> LowLevelApi.send config CancellationToken.None
            
            return response
        }
