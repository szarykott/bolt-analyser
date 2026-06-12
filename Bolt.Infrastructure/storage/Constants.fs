module Bolt.Infrastrucutre.storage.Constants

open System
open System.IO

module Paths =
    let private home = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)

    let private storageFolder = ".bolt-app"
    
    let ensureStorageExists () =
        Directory.CreateDirectory(Path.Combine(home, storageFolder))
    
    let getQualifiedStorageLocation filename = Path.Combine(home, storageFolder, filename)