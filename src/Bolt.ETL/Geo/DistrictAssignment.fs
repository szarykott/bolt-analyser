module Bolt.ETL.Geo.DistrictAssignment

open Bolt.Models.Geo
open Bolt.Models.Geo.District

// https://www.youtube.com/watch?v=RSXM9bgqxJM
// https://gist.github.com/inside-code-yt/7064d1d1553a2ee117e60217cfd1d099
let doesRayCrossEdge (point: Point) (edge: Edge) =
    let { Latitude = latitude; Longitude = longitude } = point
    let { Latitude = lt1; Longitude = ln1 }, { Latitude = lt2; Longitude = ln2 } = edge

    let latitudeCondition = (latitude < lt1) <> (latitude < lt2)

    let longitudeCondition =
        longitude < ln1 + ((latitude - lt1) / (lt2 - lt1)) * (ln2 - ln1)

    latitudeCondition && longitudeCondition

let isWithinDistrict (coord: Point) (district: District) : bool =
    district.EdgeRepresentation
    |> Array.map (doesRayCrossEdge coord)
    |> Array.sumBy (fun r -> if r = true then 1 else 0)
    |> fun r -> r % 2 = 1
    
let assignCoordinatesToDistrict (districts: District seq) (coord: Point) : District option =
    districts |> Seq.tryFind (isWithinDistrict coord)
