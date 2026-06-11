module Bolt.App.bolt.Http

open System
open System.Collections.Generic
open System.Net.Http

module RequestBuilder =
    let newRequest method (uri : Uri) =
        new HttpRequestMessage(method, uri)
    
    let withUrlFormEncodedBody data (request: HttpRequestMessage) =
        let pairs = data |> Seq.map (fun (x,y) -> KeyValuePair(x,y)) 
        request.Content <- new FormUrlEncodedContent(pairs)
        request
       