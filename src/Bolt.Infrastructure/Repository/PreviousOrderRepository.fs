namespace Bolt.Infrastructure.Repository

module PreviousOrderRepository =
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models.BoltApi

    let private fileName = "previousOrders.json"

    let saveUnstructuredDangerous email x =
        JsonStorage.writeProfile email fileName x

    let save email (pastOrderDetails: PreviousOrder seq) =
        JsonStorage.writeProfile email fileName pastOrderDetails

    let get email : PreviousOrder seq option =
        JsonStorage.readProfile email fileName
