namespace Bolt.ETL.Analysis

/// Per-hour earnings analysis: WLS regression of zł per worked hour on
/// conditions visible before the hour starts (time, weather, district mix),
/// with a tier ladder that unlocks finer models as data accumulates.
/// Modeling authority: .claude/per-hour-earnings-design.md;
/// implementation spec: docs/superpowers/specs/2026-07-19-per-hour-earnings-design.md.
module PerHour =

    open System
    open System.Threading.Tasks
    open Bolt.ETL.Shared
    open Bolt.ETL.Geo
    open Bolt.ETL.Geo.DistrictAssignment
    open Bolt.ETL.Meteo.Model
    open Bolt.ETL.Analytics
    open Bolt.Models
    open Bolt.Models.Geo
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

    type HourlyAverage = {
        IsWeekend: bool
        IsNight: bool
        Rate: float
        HourCount: int
    }

    type BasicHourlyAverage = {
        IsWeekend: bool
        IsNight: bool
        Earnings: float
        WorkedHours: float
    }

    type HourlyEffect = {
        Feature: string
        Estimate: float option
        PValue: float option
        CiLow: float option
        CiHigh: float option
    }

    type HourlyModel = {
        Level: int
        ObservationCount: int
        RSquared: float option
        AdjustedRSquared: float option
        Effects: HourlyEffect array
        FoldedDistricts: string array
    }

    type AnalysisResult = {
        Averages: HourlyAverage array
        Models: HourlyModel list
    }

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

        let overlapMinutes (aStart: DateTimeOffset) (aEnd: DateTimeOffset) (bStart: DateTimeOffset) (bEnd: DateTimeOffset) =
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
        let earningsInHour (rides: FinishedRide[]) (hour: DateTimeOffset) : float =
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
                        IsWeekend = RideTime.isWeekend hour
                        IsRushHour = RideTime.isRushHour hour
                    })
            |> Array.ofList

    let prepareHourlySource (meteo: Weather) (districts: District seq) (rides: FinishedRide[]) : HourlyDataSource =
        let weatherProvider t = (Weather.getNearestDataPoint meteo t).Value
        let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts
        let districtOf (ride: FinishedRide) =
            districtProvider ride.Route.Stops[0].Location
            |> Option.defaultValue (DistrictName "unknown")
            |> _.Value
        { Rows = Dataset.buildRows weatherProvider districtOf rides }

    /// A regression column: display-ready Polish name, value in [0,1] per row
    /// (0/1 for dummies, share of the hour for district columns).
    type ColumnSpec = {
        Name: string
        Value: HourRow -> float
    }

    /// Rows-per-coefficient budget for unlocking a rung.
    let RowsPerCoefficient = 15.0

    /// Minimum effective hours (Σ value·fill) for a column to stand alone.
    let ExposureFloorHours = 20.0

    module Ladder =

        type Rung = {
            Level: int
            Columns: ColumnSpec list
            /// Non-baseline districts folded into inne_dzielnice (rung 4 only).
            FoldedDistricts: string array
        }

        let exposure (rows: HourRow array) (spec: ColumnSpec) : float =
            rows |> Array.sumBy (fun r -> r.Fill * spec.Value r)

        let private passesFloor rows spec = exposure rows spec >= ExposureFloorHours

        let private dummy name (pred: HourRow -> bool) =
            { Name = name; Value = fun r -> if pred r then 1.0 else 0.0 }

        let private shareOf district (r: HourRow) =
            r.Shares |> Map.tryFind district |> Option.defaultValue 0.0

        let private weekend = dummy "weekend" _.IsWeekend
        let private rushHour = dummy "godziny_szczytu" _.IsRushHour
        let private night = dummy "noc" _.IsNight
        let private badWeather = dummy "zła_pogoda" (fun r -> r.Rain || r.Snow)
        let private rain = dummy "deszcz" _.Rain
        let private snow = dummy "śnieg" _.Snow

        let private tempIn buckets = fun (r: HourRow) -> List.contains r.Temperature buckets
        let private extremeTemp = dummy "≤0°C LUB >30°C" (tempIn [ Frost; Hot ])
        let private cold0to18 = dummy "0–18°C" (tempIn [ Cold; Cool ])
        let private frost = dummy (TemperatureBucket.label Frost) (tempIn [ Frost ])
        let private hot = dummy (TemperatureBucket.label Hot) (tempIn [ Hot ])
        let private cold = dummy (TemperatureBucket.label Cold) (tempIn [ Cold ])
        let private cool = dummy (TemperatureBucket.label Cool) (tempIn [ Cool ])
        let private warm = dummy (TemperatureBucket.label Warm) (tempIn [ Warm ])

        let private ifFloor rows col = if passesFloor rows col then [ col ] else []

        /// A split replaces its merged parent only when both children stand
        /// on their own; otherwise the parent column stays (floor permitting).
        let private splitOrMerged rows (children: ColumnSpec list) (parent: ColumnSpec) =
            if children |> List.forall (passesFloor rows)
            then children
            else ifFloor rows parent

        let private pozaCentrum =
            { Name = "poza_centrum"; Value = fun r -> 1.0 - shareOf BaselineDistrict r }

        /// Catch-alls never block and take no floor — included whenever inhabited.
        let private ifInhabited rows col = if exposure rows col > 0.0 then [ col ] else []

        let private districtColumns (rows: HourRow array) =
            let named =
                rows
                |> Array.collect (fun r -> r.Shares |> Map.toArray |> Array.map fst)
                |> Array.distinct
                |> Array.filter (fun d -> d <> BaselineDistrict)
                |> Array.sort
            let unlocked, folded =
                named
                |> Array.partition (fun d ->
                    passesFloor rows { Name = d; Value = shareOf d })
            let namedColumns =
                unlocked
                |> Array.map (fun d -> { Name = $"dzielnica_{d}"; Value = shareOf d })
                |> List.ofArray
            let otherColumn =
                { Name = "inne_dzielnice"
                  Value = fun r -> folded |> Array.sumBy (fun d -> shareOf d r) }
            namedColumns @ ifInhabited rows otherColumn, folded

        let rungColumns (rows: HourRow array) (level: int) : Rung =
            let baseColumns = ifFloor rows weekend @ ifFloor rows rushHour
            match level with
            | 1 ->
                { Level = 1
                  Columns = baseColumns @ ifFloor rows badWeather
                  FoldedDistricts = [||] }
            | 2 ->
                { Level = 2
                  Columns =
                    baseColumns @ ifFloor rows badWeather @ ifFloor rows night
                    @ ifFloor rows extremeTemp @ ifInhabited rows pozaCentrum
                  FoldedDistricts = [||] }
            | 3 ->
                { Level = 3
                  Columns =
                    baseColumns @ splitOrMerged rows [ rain; snow ] badWeather
                    @ ifFloor rows night @ ifFloor rows extremeTemp
                    @ ifFloor rows cold0to18 @ ifInhabited rows pozaCentrum
                  FoldedDistricts = [||] }
            | 4 ->
                let districts, folded = districtColumns rows
                { Level = 4
                  Columns =
                    baseColumns @ splitOrMerged rows [ rain; snow ] badWeather
                    @ ifFloor rows night
                    @ splitOrMerged rows [ frost; hot ] extremeTemp
                    @ splitOrMerged rows [ cold; cool ] cold0to18
                    @ ifFloor rows warm @ districts
                  FoldedDistricts = folded }
            | _ -> invalidArg (nameof level) "rung level must be 1..4"

        /// Global budget gate: n ≥ 15 × (columns + intercept).
        let isUnlocked (rows: HourRow array) (rung: Rung) =
            float rows.Length >= RowsPerCoefficient * float (rung.Columns.Length + 1)

        /// Drops rungs with no columns (intercept-only regression is meaningless)
        /// and, among rungs sharing an identical column-name list, keeps only
        /// the highest level — ascending order preserved.
        let unlockedRungs (rows: HourRow array) : Rung list =
            [ 1 .. 4 ]
            |> List.map (rungColumns rows)
            |> List.filter (fun r -> not (List.isEmpty r.Columns))
            |> List.filter (isUnlocked rows)
            |> List.rev
            |> List.distinctBy (fun r -> r.Columns |> List.map _.Name)
            |> List.rev

    /// Weighted grouped means, always available without a service call.
    let hourlyAverages (rows: HourRow array) : HourlyAverage array =
        rows
        |> Array.groupBy (fun r -> r.IsWeekend, r.IsNight)
        |> Array.map (fun ((isWeekend, isNight), group) ->
            let effectiveHours = group |> Array.sumBy _.Fill
            let earnings = group |> Array.sumBy (fun r -> r.Rate * r.Fill)
            { IsWeekend = isWeekend
              IsNight = isNight
              Rate = earnings / effectiveHours
              HourCount = group.Length })
        |> Array.sortByDescending _.Rate

    /// The report's grouped earnings use the same worked spans and hourly
    /// earnings allocation as the regression, without weather or districts.
    let basicHourlyAverages (rides: FinishedRide[]) : BasicHourlyAverage array =
        let segments = Dataset.workedSegments (fun _ -> "") rides
        Dataset.eligibleHours rides
        |> List.choose (fun hour ->
            let hourEnd = hour.AddHours 1.0
            let workedHours =
                segments
                |> List.sumBy (fun segment ->
                    Dataset.overlapMinutes segment.SegStart segment.SegEnd hour hourEnd / 60.0)
            if workedHours <= 0.0 then None
            else
                Some(RideTime.isWeekend hour, hour.Hour >= 18 || hour.Hour < 6,
                     Dataset.earningsInHour rides hour, workedHours))
        |> List.groupBy (fun (weekend, night, _, _) -> weekend, night)
        |> List.map (fun ((weekend, night), hours) ->
            { IsWeekend = weekend
              IsNight = night
              Earnings = hours |> List.sumBy (fun (_, _, earnings, _) -> earnings)
              WorkedHours = hours |> List.sumBy (fun (_, _, _, worked) -> worked) })
        |> List.toArray

    let toAnalyticsRows (rung: Ladder.Rung) (rows: HourRow array) : Map<string, obj> array =
        rows
        |> Array.map (fun r ->
            Map.ofList
                (("stawka_pln_h", box r.Rate)
                 :: ("waga", box r.Fill)
                 :: (rung.Columns |> List.map (fun c -> c.Name, box (c.Value r)))))

    let modelFromResponse (rung: Ladder.Rung) (response: OlsResponse) : HourlyModel =
        { Level = rung.Level
          ObservationCount = response.NObservations
          RSquared = response.RSquared
          AdjustedRSquared = response.AdjRSquared
          FoldedDistricts = rung.FoldedDistricts
          Effects =
            response.Coefficients
            |> Array.filter (fun c -> c.Name <> "const")
            |> Array.map (fun c ->
                { Feature = c.Name
                  Estimate = c.Coef
                  PValue = c.PValue
                  CiLow = c.CiLow
                  CiHigh = c.CiHigh }) }

    let run (source: HourlyDataSource) : Task<AnalysisResult> =
        task {
            let rows = source.Rows
            let rungs = Ladder.unlockedRungs rows

            let models = ResizeArray<HourlyModel>()
            for rung in rungs do
                let! response =
                    AnalyticsClient.wlsRegression {
                        Rows = toAnalyticsRows rung rows
                        Target = "stawka_pln_h"
                        Weights = "waga"
                        DropColumns = [||]
                        CategoricalColumns = None
                        Standardize = false
                    }
                models.Add(modelFromResponse rung response)

            return
                { Averages = hourlyAverages rows
                  Models = List.ofSeq models }
        }
