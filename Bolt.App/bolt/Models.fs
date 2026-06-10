module Bolt.App.bolt.Models

open System
open System.Net.Http
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