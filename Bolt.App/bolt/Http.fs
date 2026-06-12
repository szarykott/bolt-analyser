module Bolt.App.bolt.Http

open System
open System.Collections.Generic
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Http.Json
open System.Net.Mime

module RequestBuilder =
    let newRequest method (uri : Uri) =
        new HttpRequestMessage(method, uri)
    
    let withUrlFormEncodedBody data (request: HttpRequestMessage) =
        let pairs = data |> Seq.map (fun (x,y) -> KeyValuePair(x,y)) 
        request.Content <- new FormUrlEncodedContent(pairs)
        request
        
    let withJsonBody data (request: HttpRequestMessage) =
        request.Content <- JsonContent.Create(data, new MediaTypeHeaderValue(MediaTypeNames.Application.Json))
        request
       