module Bolt.Models.Meteo

open System
open Bolt.Models.Geo

[<Measure>] type celcius
[<Measure>] type mm
[<Measure>] type cm 

type WeatherDataPoint = {
    Temperature: float<celcius>
    Rain: float<mm>
    Snow: float<cm>
}

type Weather = {
    Center: GeoPoint
    Data: Map<DateTimeOffset, WeatherDataPoint>
}