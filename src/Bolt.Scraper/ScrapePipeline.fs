module Bolt.Scraper.ScrapePipeline

open System
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Bolt.Infrastructure.Repository
open Bolt.Infrastrucutre.ces.TaskResultBuilder
open Bolt.Infrastrucutre.Serialization
open Bolt.Models.BoltApi
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
    | ScrapingOrderFailed of current: int * total: int * orderId: int64 * reason: string

type ScrapeSession = {
    Config: ApiConfig
    Email: string
}

let private mapApiError (t: Task<Result<'a, ApiError>>) : Task<Result<'a, string>> =
    task {
        let! r = t
        return Result.mapError (fun e -> e.ToString()) r
    }

let private describeApiError = function
    | HttpError(status, _) -> $"HTTP {int status}"
    | BoltError response -> response.Message
    | NetworkError ex
    | DeserializationError ex -> ex.Message
    | AuthError message -> message

let internal fetchOrderDetails
    (config: ApiConfig)
    (handles: HistoryOrderHandle[])
    (report: ScrapeProgress -> Task)
    (ct: CancellationToken)
    : Task<Result<(int * JsonElement * JsonElement)[] * SkippedOrder[], ApiError>> =
    task {
        let bolt = BoltClient.fromConfig config
        use _cancelRequests = ct.Register(fun () -> config.HttpClient.CancelPendingRequests())
        let results = Array.create handles.Length None
        let failures = Array.create handles.Length None
        let mutable completed = 0
        use progressLock = new SemaphoreSlim(1, 1)

        let fetchWithRetry (handle: HistoryOrderHandle) (token: CancellationToken) =
            task {
                let mutable attempt = 0
                let mutable outcome: Result<JsonElement * JsonElement, ApiError> option = None
                while attempt < 3 && outcome.IsNone do
                    token.ThrowIfCancellationRequested()
                    let! result = taskResult {
                        let! p = BoltClient.getPreviousOrder bolt handle
                        token.ThrowIfCancellationRequested()
                        let! d = BoltClient.getPastOrderDetails bolt handle
                        return p, d
                    }
                    attempt <- attempt + 1
                    match result with
                    | Ok _ -> outcome <- Some result
                    | Error _ when attempt = 3 -> outcome <- Some result
                    | _ -> ()
                return outcome.Value
            }

        let fetch (index, handle: HistoryOrderHandle) (token: CancellationToken) =
            ValueTask(task {
                let! result = fetchWithRetry handle token

                match result with
                | Error e ->
                    failures[index] <- Some { OrderId = handle.OrderHandle.OrderId; Reason = describeApiError e }
                | Ok(p, d) -> results[index] <- Some(index, p, d)

                do! progressLock.WaitAsync token
                try
                    completed <- completed + 1
                    match result with
                    | Error e ->
                        do! report (ScrapingOrderFailed(completed, handles.Length, handle.OrderHandle.OrderId, describeApiError e))
                    | Ok _ -> do! report (ScrapingOrderDetails(completed, handles.Length))
                finally
                    progressLock.Release() |> ignore
            })

        do! Parallel.ForEachAsync(
            Array.indexed handles,
            ParallelOptions(MaxDegreeOfParallelism = 10, CancellationToken = ct),
            Func<_, _, _>(fetch))

        ct.ThrowIfCancellationRequested()
        return Ok(Array.choose id results, Array.choose id failures)
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

/// Scrapes everything for the session's account and returns it in memory.
/// DEBUG builds also persist the raw responses to disk (same files the
/// debugging tools read); production writes nothing.
let scrapeRides
    (session: ScrapeSession)
    (report: ScrapeProgress -> Task)
    (ct: CancellationToken)
    : Task<Result<ScrapedData, string>> =
    let bolt = BoltClient.fromConfig session.Config
    let email = session.Email

    task {
        let! scraped =
            mapApiError (taskResult {
                do! report ScrapingProfile
                let! profile = BoltClient.getDriverProfile bolt

                do! report ScrapingActivityHours
                let! activity = BoltClient.getActivityHours bolt

                do! report ScrapingOrderHistory
                let! handles, history = BoltClient.getOrderHistory bolt

                let handles = handles |> Array.ofSeq
                let history = Array.ofSeq history
                let! successful, skipped = fetchOrderDetails session.Config handles report ct
                let history = successful |> Array.map (fun (index, _, _) -> history[index])
                let previous = successful |> Array.map (fun (_, p, _) -> p)
                let details = successful |> Array.map (fun (_, _, d) -> d)

                return (profile, activity, history, previous, details, skipped)
            })

        match scraped with
        | Error e -> return Error e
        | Ok(_, _, _, previous, _, skipped) when previous.Length = 0 && skipped.Length > 0 ->
            return Error $"Nie udało się pobrać żadnego kursu: pominięto {skipped.Length} po trzech próbach. Błąd: {skipped[0].Reason}"
        | Ok(profile, activity, history, previous, details, skipped) ->
            try
                let data =
                    { Email = email
                      Profile = profile
                      ActivityHours = activity
                      OrderHistory = history
                      PreviousOrders = previous |> Array.map Json.deserializeElement<PreviousOrder>
                      PastOrderDetails = details |> Array.map Json.deserializeElement<PastOrderDetail>
                      SkippedOrders = skipped }
#if DEBUG
                DriverProfileRepository.saveUnstructuredDangerous email profile
                ActivityHoursRepository.saveUnstructuredDangerous email activity
                OrderHistoryRepository.saveUnstructuredDangerous email history
                PreviousOrderRepository.saveUnstructuredDangerous email previous
                PastOrderDetailRepository.saveUnstructuredDangerous email details
                ScrapeMetadataRepository.save email { ScrapedAt = DateTimeOffset.UtcNow; SkippedOrders = Some skipped }
#endif
                return Ok data
            with ex ->
                return Error $"Could not parse scraped data: {ex.Message}"
    }

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
