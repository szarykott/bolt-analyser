module Bolt.Models.Geo

type Point = {
    Latitude: float32
    Longitude: float32
}
    
type Edge = Point * Point 

module District =
    type District = {
        Name: string
        Coordinates: Point array
        EdgeRepresentation : Edge array
    }
    
    let private toEdgeRepresentation points=
        points |> Array.pairwise
    
    let create name points =
        {
            Name = name
            Coordinates = points
            EdgeRepresentation = toEdgeRepresentation points
        }