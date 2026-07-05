module Bolt.Models.Meteo

open System
open Bolt.Models.Geo

[<Measure>]
type celcius

[<Measure>]
type mm

[<Measure>]
type cm

type WeatherDataPoint =
    { Temperature: float<celcius>
      Rain: float<mm>
      Snow: float<cm> }

type private HourUnitTimestamp = int64

type Weather =
    { Center: GeoPoint
      Data: Map<HourUnitTimestamp, WeatherDataPoint> }

module Weather =
    let private roundToHourTimestamp (date: DateTimeOffset) : HourUnitTimestamp =
        let exactTimestamp = date.ToUnixTimeSeconds()
        let roundedDown = (exactTimestamp / int64 3600) * int64 3600

        if exactTimestamp - roundedDown < 1800 then
            roundedDown 
        else
            roundedDown + int64 3600

    let create (center: GeoPoint) (data: (DateTimeOffset * WeatherDataPoint) seq) =
        { Center = center
          Data =
            data
            |> Seq.map (fun (time, weather) -> (roundToHourTimestamp time, weather))
            |> Map.ofSeq }

    let getDataPoint weather time =
        let timestamp = roundToHourTimestamp time
        weather.Data[timestamp] 