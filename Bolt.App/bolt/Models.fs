module Bolt.App.bolt.Models

open System
open System.Net
open System.Net.Http
open System.Text.Json.Serialization
open Bolt.App.bolt.Tokens

type RubbishBoltData =
    { DeviceUid: string
      DeviceName: string
      DeviceOsVersion: string
      DeviceType: string
      Version: string
      Country: string
      Language: string
       }

type ApiConfig =
    { BaseUrl: Uri
      HttpClient: HttpClient
      Tokens: TokenStore
      RubbishData: RubbishBoltData }
    
type SuccessResponse<'T> =
    { [<JsonPropertyName("message")>]
      Message: string
      [<JsonPropertyName("data")>]
      Data: 'T }

type SuccessResponse =
    { [<JsonPropertyName("message")>]
      Message: string }

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