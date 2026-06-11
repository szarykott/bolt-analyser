module Bolt.App.Logging

module Logger =
    type Logger = {
        Info: string -> unit
        Warn: string -> unit
        Debug: string -> unit
        Error:  exn -> string -> unit
    }
    
    let consoleLogger = {
        Info = fun msg -> printfn $"[INFO] %s{msg}"
        Warn = fun msg -> printfn $"[WARN] %s{msg}"
        Debug = fun msg -> printfn $"[DEBG] %s{msg}"
        Error = fun ex  msg -> printfn $"[ERR]  %s{msg} {ex}"
    }
    
    let mutable private impl = consoleLogger
    
    let info msg = impl.Info msg
    let warn msg = impl.Warn msg
    let debug msg = impl.Debug msg
    let error ex msg = impl.Error ex msg