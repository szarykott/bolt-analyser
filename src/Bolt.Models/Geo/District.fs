module Bolt.Models.Geo.District

type Point = {
    Latitude: float32
    Longitude: float32
}

type District = {
    Name: string
    Coordinates: Point array
}