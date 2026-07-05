namespace Bolt.Infrastructure.Repository

module PreviousOrderRepository =
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models.PreviousOrder

    let private fileName = "previousOrders.json" 
    
    let saveUnstructuredDangerous x =
        JsonStorage.write fileName x
    
    let save (pastOrderDetails: PreviousOrder seq) =
        JsonStorage.write fileName pastOrderDetails
    
    let get () : PreviousOrder seq option =
        JsonStorage.read fileName