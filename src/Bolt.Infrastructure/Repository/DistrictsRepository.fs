namespace Bolt.Infrastructure.Repository

open Bolt.Infrastrucutre.storage.Storage
open Bolt.Models.Geo

module DistrictsRepository =
    let private fileName = "krakowDistricts.json" 
    
    let save (data: District seq) =
        JsonStorage.write fileName data
        
    let get () : District seq option =
        JsonStorage.read fileName