namespace Bolt.Infrastructure.Repository

module PastOrderDetailRepository =
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models.BoltApi

    let private fileName = "pastOrderDetails.json"

    let saveUnstructuredDangerous email x =
        JsonStorage.writeProfile email fileName x

    let save email (pastOrderDetails: PastOrderDetail seq) =
        JsonStorage.writeProfile email fileName pastOrderDetails

    let get email : PastOrderDetail seq option =
        JsonStorage.readProfile email fileName
