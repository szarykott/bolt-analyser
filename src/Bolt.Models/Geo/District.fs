module Bolt.Models.Geo

type GeoPoint = {
    Latitude: float
    Longitude: float
}
    
type Edge = GeoPoint * GeoPoint

type District = private {
    Name: string
    Coordinates: GeoPoint array
    EdgeRepresentation : Edge array
}

module District =
    let private toEdgeRepresentation points=
        points |> Array.pairwise
    
    let create name points =
        {
            Name = name
            Coordinates = points
            EdgeRepresentation = toEdgeRepresentation points
        }