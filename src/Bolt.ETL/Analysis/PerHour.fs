namespace Bolt.ETL.Analysis

/// Per-hour earnings analysis: WLS regression of zł per worked hour on
/// conditions visible before the hour starts (time, weather, district mix),
/// with a tier ladder that unlocks finer models as data accumulates.
/// Modeling authority: .claude/per-hour-earnings-design.md;
/// implementation spec: docs/superpowers/specs/2026-07-19-per-hour-earnings-design.md.
module PerHour =

    open System
    open Bolt.ETL.Geo
    open Bolt.ETL.Geo.DistrictAssignment
    open Bolt.ETL.Meteo.Model
    open Bolt.Infrastructure.Repository
    open Bolt.Models
    open Bolt.Models.Meteo

    /// The centrum district, dropped as the regression baseline.
    [<Literal>]
    let BaselineDistrict = "Stare Miasto"

    type HourRow = {
        HourStart: DateTimeOffset
        /// Worked minutes / 60, in (0, 1]. Doubles as the WLS weight.
        Fill: float
        /// zł per fully worked hour = hour earnings / Fill.
        Rate: float
        /// Named district -> share of the hour's worked minutes; sums to 1.
        Shares: Map<string, float>
        Rain: bool
        Snow: bool
        Temperature: TemperatureBucket
        IsNight: bool
        IsWeekend: bool
        IsRushHour: bool
    }

    type HourlyDataSource = { Rows: HourRow array }

    type WorkedSegment = {
        SegStart: DateTimeOffset
        SegEnd: DateTimeOffset
        District: string
    }

    module Dataset =

        /// Non-overlapping worked segments. Ride spans (Accepted -> RideEnd)
        /// are unioned; the gap before each ride becomes a segment attributed
        /// to that ride's pickup district (where the driver positioned).
        let workedSegments (districtOf: FinishedRide -> string) (rides: FinishedRide[]) : WorkedSegment list =
            let step (segments, cursor) (ride: FinishedRide) =
                let s = ride.Times.AcceptedTimestamp
                let e = ride.Times.RideEnd
                let district = districtOf ride
                let gap =
                    match cursor with
                    | Some c when s > c -> [ { SegStart = c; SegEnd = s; District = district } ]
                    | _ -> []
                let clippedStart =
                    match cursor with
                    | Some c -> max s c
                    | None -> s
                let rideSegment =
                    if e > clippedStart
                    then [ { SegStart = clippedStart; SegEnd = e; District = district } ]
                    else []
                let cursor' =
                    match cursor with
                    | Some c -> Some(max c e)
                    | None -> Some e
                segments @ gap @ rideSegment, cursor'

            rides
            |> Array.sortBy _.Times.AcceptedTimestamp
            |> Array.fold step ([], None)
            |> fst

        let hourFloor (t: DateTimeOffset) =
            DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset)

        let private overlapMinutes (aStart: DateTimeOffset) (aEnd: DateTimeOffset) (bStart: DateTimeOffset) (bEnd: DateTimeOffset) =
            let s = max aStart bStart
            let e = min aEnd bEnd
            if e > s then (e - s).TotalMinutes else 0.0

        /// Clock hours with a ride starting or in progress. Fully idle hours
        /// never get a row, so gap minutes falling into them drop out.
        let eligibleHours (rides: FinishedRide[]) : DateTimeOffset list =
            rides
            |> Seq.collect (fun r ->
                seq {
                    let mutable h = hourFloor r.Times.AcceptedTimestamp
                    yield h
                    h <- h.AddHours 1.0
                    while h < r.Times.RideEnd do
                        yield h
                        h <- h.AddHours 1.0
                })
            |> Seq.distinct
            |> Seq.sort
            |> List.ofSeq

        /// Earnings land in hours proportionally to the ride span's overlap
        /// with the hour (a zero-length span pays out in its starting hour).
        let private earningsInHour (rides: FinishedRide[]) (hour: DateTimeOffset) : float =
            let hourEnd = hour.AddHours 1.0
            rides
            |> Array.sumBy (fun r ->
                let earned = r.Payment.Earned |> Array.sumBy _.Value |> float
                let total = (r.Times.RideEnd - r.Times.AcceptedTimestamp).TotalMinutes
                if total <= 0.0 then
                    if hourFloor r.Times.AcceptedTimestamp = hour then earned else 0.0
                else
                    earned * overlapMinutes r.Times.AcceptedTimestamp r.Times.RideEnd hour hourEnd / total)

        let buildRows (weatherProvider: DateTimeOffset -> WeatherDataPoint)
                      (districtOf: FinishedRide -> string)
                      (rides: FinishedRide[]) : HourRow array =
            let segments = workedSegments districtOf rides
            eligibleHours rides
            |> List.choose (fun hour ->
                let hourEnd = hour.AddHours 1.0
                let minutesByDistrict =
                    segments
                    |> List.map (fun s -> s.District, overlapMinutes s.SegStart s.SegEnd hour hourEnd)
                    |> List.filter (fun (_, m) -> m > 0.0)
                    |> List.groupBy fst
                    |> List.map (fun (d, xs) -> d, xs |> List.sumBy snd)
                let workedMinutes = minutesByDistrict |> List.sumBy snd
                if workedMinutes <= 0.0 then None
                else
                    let fill = workedMinutes / 60.0
                    let weather = weatherProvider hour
                    Some {
                        HourStart = hour
                        Fill = fill
                        Rate = earningsInHour rides hour / fill
                        Shares =
                            minutesByDistrict
                            |> List.map (fun (d, m) -> d, m / workedMinutes)
                            |> Map.ofList
                        Rain = weather.Rain <> 0.0<mm>
                        Snow = weather.Snow <> 0.0<cm>
                        Temperature = TemperatureBucket.fromCelcius weather.Temperature
                        IsNight = hour.Hour >= 18 || hour.Hour < 6
                        IsWeekend = PerRide2.RideRow.isWeekend hour
                        IsRushHour = PerRide2.RideRow.isRushHour hour
                    })
            |> Array.ofList

    let prepareHourlySource (rides: FinishedRide[]) : HourlyDataSource =
        let meteo = (MeteoRepository.get ()).Value
        let districts = (DistrictsRepository.get ()).Value
        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
        let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts
        let districtOf (ride: FinishedRide) =
            districtProvider ride.Route.Stops[0].Location
            |> Option.defaultValue (DistrictName "unknown")
            |> _.Value
        { Rows = Dataset.buildRows weatherProvider districtOf rides }
