namespace Bolt.Infrastructure.Repository


module ActivityHoursRepository =
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models.BoltApi

    let private fileName = "activityHours.json"

    let saveUnstructuredDangerous email x =
        JsonStorage.writeProfile email fileName x

    let save email (pastOrderDetails: ActivityHours) =
        JsonStorage.writeProfile email fileName pastOrderDetails

    let get email : ActivityHours option =
        JsonStorage.readProfile email fileName
