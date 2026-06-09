namespace Bolt

open System
open System.Linq
open System.Text.RegularExpressions
open System.Threading
open System.Web

type MagicLinkToken = MagicLinkToken of string
type AccessToken = AccessToken of value: string * expiresAt: DateTimeOffset
type RefreshToken = RefreshToken of string

type Tokens = {
    Access: AccessToken
    Refresh: RefreshToken
}

type TokenStore = {
    mutable Current: Tokens
    Lock: SemaphoreSlim
}

module TokenStore = 
    let create (initial: Tokens) = 
        { Current = initial; Lock = new SemaphoreSlim(1,1) }

    let store (s: TokenStore) (accessToken: AccessToken) (refreshToken: RefreshToken) =
        s.Current <- { Access = accessToken; Refresh = refreshToken }
    
    let storeAccessToken (s: TokenStore) (accessToken: AccessToken) =
        s.Current <- { Access = accessToken; Refresh = s.Current.Refresh }


    let snapshot (s: TokenStore) = s.Current
    
module MagicLink =
    open Bolt.App.ResultBuilder
    
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