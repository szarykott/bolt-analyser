// For more information see https://aka.ms/fsharp-console-apps
open Bolt.Infrastrucutre.storage.Storage
open Bolt.Models.PreviousOrder
open Bolt.Models.Shared

let totalRides orders =
    orders
    |> Seq.filter (fun f -> f.State = OrderState.Finished)
    |> Seq.length

let averagePrice (orders: PreviousOrder seq) =
    let total =
        orders
        |> Seq.filter _.Price.IsSome
        |> Seq.map _.Price.Value.Value
        |> Seq.sum
    
    total / decimal (totalRides orders) 

let previousOrders : PreviousOrder seq option = JsonStorage.read "previousOrders.json"
match previousOrders with
| None -> printfn "Could not find previousOrders.json"
| Some orders ->
    printfn $"Liczba twoich ukończnych przejazdów to %i{totalRides orders}"
    printfn $"Średnia cena kursu to %.2f{averagePrice orders} zł"
    
