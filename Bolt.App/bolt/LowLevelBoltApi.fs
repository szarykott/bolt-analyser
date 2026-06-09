namespace Bolt

open System
open System.Net.Http
open System.Net
open System.Text.Json.Serialization

type SuccessResponse<'T> = { Message: string; Data: 'T }

type ValidationError = { Error: string; Property: string }

type ErrorResponse =
    { Code: int
      Message: string
      ErrorHint: string option
      ValidationErrors: ValidationError list option }

type ApiError =
    | BoltError of ErrorResponse
    | NetworkError of exn
    | DeserializationError of exn
    | AuthError of string
    | HttpError of status: HttpStatusCode * body: string

type RubbishBoltData =
    { DeviceUid: string
      DeviceName: string
      DeviceOsVersion: string
      Version: string }

type ApiConfig =
    { BaseUrl: Uri
      HttpClient: HttpClient
      Tokens: TokenStore
      RubbishData: RubbishBoltData }

module LowLevelApi =
    open System.Threading.Tasks
    open System.Threading
    open System.Text.Json
    open System.Collections.Generic
    open TaskResultBuilder

    type ResponseCode = {
        [<JsonPropertyName("code")>]
        Code: int
    }

    let private rawSend<'T>
        (cfg: ApiConfig)
        (req: HttpRequestMessage)
        (ct: CancellationToken)
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

    let sendMagicLink
        (cfg: ApiConfig)
        (email: string)
        (ct: CancellationToken)
        : Task<Result<unit, ApiError>> =
        taskResult {
            let req =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    Uri(cfg.BaseUrl, $"driver/sendMagicLink?version={cfg.RubbishData.Version}")
                )
            
            req.Content <-
                new FormUrlEncodedContent [| KeyValuePair("email", email)
                                             KeyValuePair("device_uid", cfg.RubbishData.DeviceUid)
                                             KeyValuePair("device_name", cfg.RubbishData.DeviceName)
                                             KeyValuePair("device_os_version", cfg.RubbishData.DeviceOsVersion) |]

            let! resp = rawSend<unit> cfg req ct

            resp |> ignore
        }

    type RefreshTokenResponse = {
        [<JsonPropertyName("refresh_token")>]
        RefreshToken: string
    }

    let private authenticateWithMagicLink
        (cfg: ApiConfig)
        (token: MagicLinkToken)
        (ct: CancellationToken)
        : Task<Result<RefreshToken, ApiError>> =
        taskResult {
            let req =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    Uri(cfg.BaseUrl, $"driver/authenticateWithMagicLink?version={cfg.RubbishData.Version}")
                )

            let (MagicLinkToken v) = token

            req.Content <-
                new FormUrlEncodedContent [| KeyValuePair("token", v)
                                             KeyValuePair("device_uid", cfg.RubbishData.DeviceUid)
                                             KeyValuePair("device_name", cfg.RubbishData.DeviceName)
                                             KeyValuePair("device_os_version", cfg.RubbishData.DeviceOsVersion) |]

            let! resp = rawSend<RefreshTokenResponse> cfg req ct

            return RefreshToken resp.RefreshToken
        }


    type AccessTokenResponse =
        {
          [<JsonPropertyName("access_token")>]
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
            let req =
                new HttpRequestMessage(HttpMethod.Post, Uri(cfg.BaseUrl, "driver/getAccessToken"))

            let (RefreshToken v) = token

            req.Content <- new FormUrlEncodedContent [| KeyValuePair("refresh_token", v) |]

            let! resp = rawSend<AccessTokenResponse> cfg req ct

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

    let initialize (config: ApiConfig) (email: string) (trackingUrlCallback: unit -> string) (ct : CancellationToken) : Task<Result<unit, ApiError>> =
        taskResult {
            do! sendMagicLink config email ct
            let! token = trackingUrlCallback () |> MagicLink.getMagicLinkTokenFromTrackingUrl |> Result.mapError AuthError
            let! refreshToken = authenticateWithMagicLink config token ct
            let! accessToken = getAccessToken config refreshToken ct
            TokenStore.store config.Tokens accessToken refreshToken
        }
    
    let send<'T> (cfg: ApiConfig) (request: HttpRequestMessage) ct : Task<Result<'T, ApiError>> =
        task {
            match! rawSend<'T> cfg (request |> withAuthentication cfg) ct with
            | Error(AuthError _) ->
                match! refreshTokens cfg ct with
                | Ok _ -> return! rawSend<'T> cfg (request |> withAuthentication cfg) ct // retry once
                | Error e -> return Error e
            | other -> return other
        }
