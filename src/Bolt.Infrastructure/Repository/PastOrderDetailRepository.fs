namespace Bolt.Infrastructure.Repository

module PastOrderDetailRepository =
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models.PastOrderDetail

    let private fileName = "pastOrderDetails.json"
    
    let saveUnstructuredDangerous x =
        JsonStorage.write fileName x
    
    let save (pastOrderDetails: PastOrderDetail seq) =
        JsonStorage.write fileName pastOrderDetails
    
    let get () : PastOrderDetail seq option =
        JsonStorage.read fileName