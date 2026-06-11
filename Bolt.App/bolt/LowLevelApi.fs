module Bolt.App.bolt.LowLevelApi

open System
open System.Net.Http
open System.Text.Json.Serialization
open Bolt.App.bolt.Models
open Bolt.App.bolt.Tokens
open Bolt.App.Serialization
open Bolt.App.Logging

module LowLevelApi =
    open System.Threading.Tasks
    open System.Threading
    open TaskResultBuilder
    open Bolt.App.bolt.Http

    type ResponseCode =
        { [<JsonPropertyName("code")>]
          Code: int }

    let private logRequest (request: HttpRequestMessage) =
        Logger.debug $"[{request.Method}] {request.RequestUri}"
        
    let private rawSend<'T>
        (cfg: ApiConfig)
        (ct: CancellationToken)
        (req: HttpRequestMessage)
        : Task<Result<'T, ApiError>> =
        logRequest req    
        
        task {
            try
                use! resp = cfg.HttpClient.SendAsync(req, ct)

                let! body = resp.Content.ReadAsStringAsync ct

                Logger.debug $"[{resp.StatusCode}] {body}"
                
                if resp.IsSuccessStatusCode then
                    try
                        match (Json.deserialize body).Code with
                        | 0 -> return Ok (Json.deserialize<'T> body)
                        | 503 -> return Error(AuthError "NOT_AUTHORIZED")
                        | _ -> return Error(BoltError(Json.deserialize<ErrorResponse> body))
                    with ex ->
                        return Error(DeserializationError ex)
                else
                    return Error(HttpError(resp.StatusCode, body))
            with ex ->
                return Error(NetworkError ex)
        }

    let private withAuthentication (config: ApiConfig) (request: HttpRequestMessage) : HttpRequestMessage =
        let tokens = TokenStore.snapshot config.Tokens
        let (AccessToken(v, _)) = tokens.Access
        request.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", v)
        request

    let private sendMagicLink (cfg: ApiConfig) (email: string) (ct: CancellationToken) : Task<Result<unit, ApiError>> =
        taskResult {
            do!
                RequestBuilder.newRequest
                    HttpMethod.Post
                    (Uri(cfg.BaseUrl, $"driver/sendMagicLink?version={cfg.RubbishData.Version}"))
                |> RequestBuilder.withUrlFormEncodedBody
                    [| ("email", email)
                       ("device_uid", cfg.RubbishData.DeviceUid)
                       ("device_name", cfg.RubbishData.DeviceName)
                       ("device_os_version", cfg.RubbishData.DeviceOsVersion) |]
                |> rawSend<SuccessResponse> cfg ct
        }

    type RefreshTokenResponse =
        { [<JsonPropertyName("refresh_token")>]
          RefreshToken: string }

    let private authenticateWithMagicLink
        (cfg: ApiConfig)
        (token: MagicLinkToken)
        (ct: CancellationToken)
        : Task<Result<RefreshToken, ApiError>> =
        taskResult {
            let (MagicLinkToken v) = token

            let! resp =
                RequestBuilder.newRequest
                    HttpMethod.Post
                    (Uri(cfg.BaseUrl, $"driver/authenticateWithMagicLink?version={cfg.RubbishData.Version}"))
                |> RequestBuilder.withUrlFormEncodedBody
                    [| ("token", v)
                       ("device_uid", cfg.RubbishData.DeviceUid)
                       ("device_name", cfg.RubbishData.DeviceName)
                       ("device_os_version", cfg.RubbishData.DeviceOsVersion) |]
                |> rawSend<SuccessResponse<RefreshTokenResponse>> cfg ct

            return RefreshToken resp.Data.RefreshToken
        }


    type AccessTokenResponse =
        { [<JsonPropertyName("access_token")>]
          AccessToken: string
          [<JsonPropertyName("expires_timestamp")>]
          ExpiresTimestamp: int64
          [<JsonPropertyName("expires_in_seconds")>]
          ExpiresInSeconds: int32
          [<JsonPropertyName("next_update_in_seconds")>]
          NextUpdateInSeconds: int32
          [<JsonPropertyName("next_update_give_up_timestamp")>]
          NextUpdateGiveUpTimestamp: int64 }

    let private getAccessToken
        (cfg: ApiConfig)
        (token: RefreshToken)
        (ct: CancellationToken)
        : Task<Result<AccessToken, ApiError>> =
        taskResult {
            let (RefreshToken v) = token

            let! resp =
                RequestBuilder.newRequest HttpMethod.Post (Uri(cfg.BaseUrl, "driver/getAccessToken"))
                |> RequestBuilder.withUrlFormEncodedBody [| ("refresh_token", v) |]
                |> rawSend<SuccessResponse<AccessTokenResponse>> cfg ct

            return AccessToken(resp.Data.AccessToken, resp.Data.ExpiresTimestamp |> DateTimeOffset.FromUnixTimeSeconds)
        }


    let private refreshTokens (cfg: ApiConfig) (ct: CancellationToken) : Task<Result<unit, ApiError>> =
        taskResult {
            do! cfg.Tokens.Lock.WaitAsync ct

            try
                // Double-check: another caller may have refreshed while we waited.
                let { Access = AccessToken(_, expires)
                      Refresh = refresh } =
                    cfg.Tokens.Current

                if expires < DateTimeOffset.UtcNow then
                    let! newToken = getAccessToken cfg refresh ct
                    newToken |> TokenStore.storeAccessToken cfg.Tokens

            finally
                cfg.Tokens.Lock.Release() |> ignore
        }

    let initialize
        (config: ApiConfig)
        (email: string)
        (trackingUrlCallback: unit -> string)
        (ct: CancellationToken)
        : Task<Result<unit, ApiError>> =
        if TokenStore.isEmpty config.Tokens then
            taskResult {
                do! sendMagicLink config email ct
    
                let! token =
                    trackingUrlCallback ()
                    |> MagicLink.getMagicLinkTokenFromTrackingUrl
                    |> Result.mapError AuthError
    
                let! refreshToken = authenticateWithMagicLink config token ct
                let! accessToken = getAccessToken config refreshToken ct
                TokenStore.store config.Tokens accessToken refreshToken
            }
        else
            refreshTokens config ct

    let send<'T> cfg ct request: Task<Result<'T, ApiError>> =
        let attempt () = request |> withAuthentication cfg |> rawSend<SuccessResponse<'T>> cfg ct
        task {
            match! attempt () with
            | Ok response -> return Ok response.Data
            | Error(AuthError _) ->
                match! refreshTokens cfg ct with
                | Ok _ ->
                    match! attempt () with
                    | Ok response -> return Ok response.Data
                    | Error e -> return Error e
                | Error e -> return Error e
            | Error e -> return Error e
        }
