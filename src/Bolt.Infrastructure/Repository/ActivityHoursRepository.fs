namespace Bolt.Infrastructure.Repository


module ActivityHoursRepository =
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models.ActivityHours

    let private fileName = "activityHours.json"
    
    let saveUnstructuredDangerous x =
        JsonStorage.write fileName x
    
    let save (pastOrderDetails: ActivityHours) =
        JsonStorage.write fileName pastOrderDetails
    
    let get () : ActivityHours option =
        JsonStorage.read fileName