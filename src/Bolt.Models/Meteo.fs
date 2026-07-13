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

    let hourRange (w: Weather) : (DateTimeOffset * DateTimeOffset) option =
        if Map.isEmpty w.Data then None
        else
            let keys = w.Data |> Map.toSeq |> Seq.map fst
            Some(
                DateTimeOffset.FromUnixTimeSeconds(Seq.min keys),
                DateTimeOffset.FromUnixTimeSeconds(Seq.max keys)
            )

    /// Union of both datasets; on hour collisions the right ("newer") side wins.
    let merge (a: Weather) (b: Weather) : Weather =
        { Center = a.Center
          Data = (a.Data, b.Data) ||> Map.fold (fun acc k v -> Map.add k v acc) }

    /// Exact hour if covered, otherwise the closest covered hour.
    /// None only when the dataset is empty.
    let getNearestDataPoint (w: Weather) (time: DateTimeOffset) : WeatherDataPoint option =
        if Map.isEmpty w.Data then None
        else
            let ts = roundToHourTimestamp time
            match Map.tryFind ts w.Data with
            | Some p -> Some p
            | None ->
                w.Data
                |> Map.toSeq
                |> Seq.minBy (fun (k, _) -> abs (k - ts))
                |> snd
                |> Some