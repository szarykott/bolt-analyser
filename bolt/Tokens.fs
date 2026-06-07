namespace Bolt

open System
open System.Threading

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

    let store (s: TokenStore) (accessToken: AccessToken) =
        s.Current <- { Access = accessToken; Refresh = s.Current.Refresh }


    let snapshot (s: TokenStore) = s.Current