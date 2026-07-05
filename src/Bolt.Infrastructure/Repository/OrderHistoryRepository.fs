namespace Bolt.Infrastructure.Repository


module OrderHistoryRepository =
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models.BoltApi

    let private fileName = "orderHistory.json"
    
    let saveUnstructuredDangerous x =
        JsonStorage.write fileName x
    
    let save (pastOrderDetails: OrderHistory seq) =
        JsonStorage.write fileName pastOrderDetails
    
    let get () : OrderHistory seq option =
        JsonStorage.read fileName