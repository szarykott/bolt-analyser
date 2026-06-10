module Bolt.App.bolt.LowLevelApi

open System
open System.Net.Http
open System.Net
open System.Text.Json.Serialization
open Bolt.App.bolt.Models
open Bolt.App.bolt.Tokens

type SuccessResponse<'T> =
    { [<JsonPropertyName("message")>]
      Message: string
      [<JsonPropertyName("data")>]
      Data: 'T }

type ValidationError =
    { [<JsonPropertyName("error")>]
      Error: string
      [<JsonPropertyName("property")>]
      Property: string }

type ErrorResponse =
    { [<JsonPropertyName("error")>]
      Code: int
      [<JsonPropertyName("message")>]
      Message: string
      [<JsonPropertyName("error_hint")>]
      ErrorHint: string option
      [<JsonPropertyName("validation_errors")>]
      ValidationErrors: ValidationError list option }

type ApiError =
    | BoltError of ErrorResponse
    | NetworkError of exn
    | DeserializationError of exn
    | AuthError of string
    | HttpError of status: HttpStatusCode * body: string

module LowLevelApi =
    open System.Threading.Tasks
    open System.Threading
    open System.Text.Json
    open TaskResultBuilder
    open Bolt.App.bolt.Http

    type ResponseCode =
        { [<JsonPropertyName("code")>]
          Code: int }

    let private rawSend<'T>
        (cfg: ApiConfig)
        (ct: CancellationToken)
        (req: HttpRequestMessage)
        : Task<Result<'T, ApiError>> =
        task {
            try
                use! resp = cfg.HttpClient.SendAsync(req, ct)

                let! body = resp.Content.ReadAsStringAsync ct

                if resp.IsSuccessStatusCode then
                    try
                        let boltCode = JsonSerializer.Deserialize<ResponseCode> body

                        match boltCode.Code with
                        | n when n = 0 -> return Ok (JsonSerializer.Deserialize<SuccessResponse<'T>> body).Data
                        | n when n = 503 -> return Error(AuthError "NOT_AUTHORIZED")
                        | _ -> return Error(BoltError(JsonSerializer.Deserialize<ErrorResponse> body))
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
                |> RequestBuilder.withUrlFromEncodedBody
                    [| ("email", email)
                       ("device_uid", cfg.RubbishData.DeviceUid)
                       ("device_name", cfg.RubbishData.DeviceName)
                       ("device_os_version", cfg.RubbishData.DeviceOsVersion) |]
                |> rawSend<unit> cfg ct
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
                |> RequestBuilder.withUrlFromEncodedBody
                    [| ("token", v)
                       ("device_uid", cfg.RubbishData.DeviceUid)
                       ("device_name", cfg.RubbishData.DeviceName)
                       ("device_os_version", cfg.RubbishData.DeviceOsVersion) |]
                |> rawSend<RefreshTokenResponse> cfg ct

            return RefreshToken resp.RefreshToken
        }


    type AccessTokenResponse =
        { [<JsonPropertyName("access_token")>]
          AccessToken: string
          [<JsonPropertyName("expires_timestamp")>]
          ExpiresTimestamp: int64
          ExpiresInSeconds: int32
          NextUpdateInSeconds: int32
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
                |> RequestBuilder.withUrlFromEncodedBody [| ("refresh_token", v) |]
                |> rawSend<AccessTokenResponse> cfg ct

            return AccessToken(resp.AccessToken, resp.ExpiresTimestamp |> DateTimeOffset.FromUnixTimeSeconds)
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
        task {
            match! request |> withAuthentication cfg |> rawSend<'T> cfg ct with
            | Error(AuthError _) ->
                match! refreshTokens cfg ct with
                | Ok _ -> return! request |> withAuthentication cfg |> rawSend<'T> cfg ct // retry once
                | Error e -> return Error e
            | other -> return other
        }
