namespace Bolt.Infrastructure.Repository

open Bolt.Infrastrucutre.storage.Storage
open Bolt.Models.Meteo

module MeteoRepository =
    let private fileName = "krakowMeteoData.json" 
    
    let save (data: Weather) =
        JsonStorage.write fileName data
        
    let get () : Weather option =
        JsonStorage.read fileName