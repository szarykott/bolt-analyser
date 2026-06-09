namespace Bolt.Tokens

open System
open System.Threading

type Tokens = {
    Access: string
    Refresh: string
    ExpiresAt: DateTimeOffset
}

type TokenStore = {
    mutable Current: Tokens
    Lock: SemaphoreSlim
}

module TokenStore = 
    let create (initial: Tokens) = 
        { Current = initial; Lock = new SemaphoreSlim(1,1) }

    let snapshot (s: TokenStore) = s.Current