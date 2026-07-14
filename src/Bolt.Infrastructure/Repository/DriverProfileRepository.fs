namespace Bolt.Infrastructure.Repository

module DriverProfileRepository =
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models.BoltApi

    let private fileName = "driverProfile.json"

    let saveUnstructuredDangerous email x =
        JsonStorage.writeProfile email fileName x

    let getUnstructured email : System.Text.Json.JsonElement option =
        JsonStorage.readProfile email fileName

    let save email (pastOrderDetails: OrderHistory seq) =
        JsonStorage.writeProfile email fileName pastOrderDetails

    let get email : OrderHistory seq option =
        JsonStorage.readProfile email fileName
