module Bolt.Scraper.ScrapePipeline

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Bolt.Infrastructure.Repository
open Bolt.Infrastrucutre.ces.TaskResultBuilder
open Bolt.Models.Geo
open Bolt.Models.Meteo
open Bolt.Scraper.BoltApi.ApiModels
open Bolt.Scraper.BoltApi.LowLevel
open Bolt.Scraper.BoltApi.Tokens
open Bolt.Scraper.BoltApi.bolt.BoltApi
open Bolt.Scraper.Meteo.OpenMeteo

type ScrapeProgress =
    | ScrapingProfile
    | ScrapingActivityHours
    | ScrapingOrderHistory
    | ScrapingOrderDetails of current: int * total: int

type ScrapeSession = {
    Config: ApiConfig
    Email: string
}

let private mapApiError (t: Task<Result<'a, ApiError>>) : Task<Result<'a, string>> =
    task {
        let! r = t
        return Result.mapError (fun e -> e.ToString()) r
    }

module ScrapeSession =
    let krakowCenter: GeoPoint = { Latitude = 50.06255; Longitude = 19.923765 }

    let create (email: string) : ScrapeSession =
        { Email = email
          Config =
            { BaseUrl = Uri "https://driver.live.boltsvc.net"
              HttpClient = new HttpClient()
              // fromPrevious hits the disk cache only in DEBUG builds
              Tokens = TokenStore.fromPrevious email |> Option.defaultValue (TokenStore.empty email)
              RubbishData =
                { DeviceName = "Google Pixel 9"
                  DeviceUid = "7d86eace-a407-46e1-bb98-73824c818dee"
                  DeviceOsVersion = "Android16"
                  DeviceType = "android"
                  Version = "DI.116.0"
                  Country = "pl"
                  Language = "pl" } } }

    let hasTokens (s: ScrapeSession) = not (TokenStore.isEmpty s.Config.Tokens)

    let requestMagicLink (s: ScrapeSession) (ct: CancellationToken) : Task<Result<unit, string>> =
        LowLevelApi.sendMagicLink s.Config s.Email ct |> mapApiError

    let authenticateWithUrl (s: ScrapeSession) (trackingUrl: string) (ct: CancellationToken) : Task<Result<unit, string>> =
        LowLevelApi.loginWithMagicLinkUrl s.Config trackingUrl ct |> mapApiError

    let refreshTokens (s: ScrapeSession) (ct: CancellationToken) : Task<Result<unit, string>> =
        LowLevelApi.refreshTokens s.Config ct |> mapApiError

module MeteoCoverage =
    /// Ranges that must be fetched so weather data covers [rideMin, rideMax].
    let missingRanges
        (rideMin: DateTimeOffset, rideMax: DateTimeOffset)
        (existing: (DateTimeOffset * DateTimeOffset) option)
        : (DateTimeOffset * DateTimeOffset) list =
        match existing with
        | None -> [ (rideMin, rideMax) ]
        | Some (haveMin, haveMax) ->
            [ if rideMin < haveMin then yield (rideMin, haveMin)
              if rideMax > haveMax then yield (haveMax, rideMax) ]

    /// The open-meteo archive lags a few days behind real time.
    let archiveLag = TimeSpan.FromDays 5.0

    /// Latest ride date the weather archive can cover right now.
    let cappedMax (now: DateTimeOffset) (rideMax: DateTimeOffset) : DateTimeOffset =
        min rideMax (now - archiveLag)

// FIXME: scrapeRides should not persist files to disk, but keep them in memory and return for further analysis
// RunAnalysis step should take them as argument
let scrapeRides
    (session: ScrapeSession)
    (report: ScrapeProgress -> unit)
    (ct: CancellationToken)
    : Task<Result<unit, string>> =
    let bolt = BoltClient.fromConfig session.Config
    let email = session.Email

    mapApiError (taskResult {
        report ScrapingProfile
        do! BoltClient.getDriverProfile bolt
            |>! DriverProfileRepository.saveUnstructuredDangerous email

        report ScrapingActivityHours
        do! BoltClient.getActivityHours bolt
            |>! ActivityHoursRepository.saveUnstructuredDangerous email

        report ScrapingOrderHistory
        let! handles, history = BoltClient.getOrderHistory bolt
        history |> OrderHistoryRepository.saveUnstructuredDangerous email

        let handles = handles |> Array.ofSeq
        let total = handles.Length
        let previous = ResizeArray()
        let details = ResizeArray()
        let mutable i = 0

        do! taskResult {
            while i < total do
                ct.ThrowIfCancellationRequested()
                report (ScrapingOrderDetails(i + 1, total))
                let! p = BoltClient.getPreviousOrder bolt handles[i]
                let! d = BoltClient.getPastOrderDetails bolt handles[i]
                previous.Add p
                details.Add d
                i <- i + 1
        }

        PreviousOrderRepository.saveUnstructuredDangerous email (previous.ToArray())
        PastOrderDetailRepository.saveUnstructuredDangerous email (details.ToArray())
        ScrapeMetadataRepository.save email { ScrapedAt = DateTimeOffset.UtcNow }
        return ()
    })

// Meteo file is shared across users; serialize read-merge-save.
let private meteoLock = obj ()

/// Fetches whatever weather data is missing for [rideMin, rideMax] and returns
/// the effective coverage cap: rides created after it have no weather data and
/// must be excluded from weather-dependent analyses.
let ensureMeteoCoverage
    (rideMin: DateTimeOffset, rideMax: DateTimeOffset)
    (ct: CancellationToken)
    : Task<Result<DateTimeOffset, string>> =
    task {
        let cap = MeteoCoverage.cappedMax DateTimeOffset.UtcNow rideMax
        let existingRange = MeteoRepository.get () |> Option.bind Weather.hourRange
        let ranges = MeteoCoverage.missingRanges (rideMin, cap) existingRange

        let mutable result = Ok cap
        for from, to' in ranges do
            if Result.isOk result && from < to' then
                ct.ThrowIfCancellationRequested()
                match! getWeatherData from to' ScrapeSession.krakowCenter with
                | Error e -> result <- Error $"Weather fetch failed: {e}"
                | Ok fetched ->
                    lock meteoLock (fun () ->
                        let merged =
                            match MeteoRepository.get () with
                            | Some existing -> Weather.merge existing fetched
                            | None -> fetched
                        MeteoRepository.save merged)
        return result
    }
