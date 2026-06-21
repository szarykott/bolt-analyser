module Bolt.App.Tokens

open System
open System.Text.RegularExpressions
open System.Threading
open System.Web
open Bolt.Infrastrucutre.ces.OptionBuilder
open Bolt.Infrastrucutre.storage

type MagicLinkToken = MagicLinkToken of string
type AccessToken = AccessToken of value: string * expiresAt: DateTimeOffset
type RefreshToken = RefreshToken of string

type Tokens = {
    Access: AccessToken
    Refresh: RefreshToken
}

type TokenStore = {
    User: string
    mutable Current: Tokens
    Lock: SemaphoreSlim
}

module TokenStore =
    open Bolt.Infrastrucutre.storage.Storage
    
    let private storage = JsonStorage.create "tokenStore.json"
    
    let private toFile user tokens : unit =
            maybe {
                let! allPrevious = storage.Read()
                return Map.add user tokens allPrevious
            }
            |> Option.defaultValue (Map [(user, tokens)])
            |> storage.Write
    
    let empty user = 
        { User = user ; Current = {Access = AccessToken("", DateTimeOffset.UtcNow); Refresh = RefreshToken("") }; Lock = new SemaphoreSlim(1,1) }
    
    let isEmpty s =
        let (RefreshToken v) = s.Current.Refresh
        v = ""
    
    let fromPrevious user =
        maybe {
            let! allPrevious = storage.Read()
            let! userPrevious = Map.tryFind user allPrevious
            return { User = user ; Current = userPrevious; Lock = new SemaphoreSlim(1,1) }
        }
    
    let store s accessToken refreshToken =
        s.Current <- { Access = accessToken; Refresh = refreshToken }
        toFile s.User s.Current
    
    let storeAccessToken s accessToken=
        s.Current <- { Access = accessToken; Refresh = s.Current.Refresh }
        toFile s.User s.Current

    let snapshot s = s.Current
    
module MagicLink =
    open Bolt.Infrastrucutre.ces.ResultBuilder
    
    let regex = new Regex(@".*/L0/(.*?)/.*")
    
    let private extractUrlFromTracking (url: string) : Result<string, string> =
        if url.Contains("awstrack.me") then
            match regex.Match(url) with
            | m when m.Success && m.Groups.Count > 1 -> Ok (m.Groups[1].Value)
            | _ -> Error $"Url is not as expected. Got: {url}"
        else 
            Ok url
    
    let private urlDecode (s: string) : string =
        HttpUtility.UrlDecode(s)
    
    let private tokenFromUrl (url: string) : Result<string, string> =
        let pairs =
                url.Split('?')
                |> Array.skip 1
                |> Array.collect _.Split('&')
                |> Array.map (fun x -> let p = x.Split('=') in p[0], p[1])
                |> dict
                
        match pairs.TryGetValue("token") with
        | false, _ -> Error $"Could not find token in {url}"
        | true, token -> Ok token
    
    let getMagicLinkTokenFromTrackingUrl (url: string) : Result<MagicLinkToken, string> =
        result {
            let! internalUrl = extractUrlFromTracking url
            let decodedUrl = urlDecode internalUrl
            let! token = tokenFromUrl decodedUrl
            return MagicLinkToken token
        }