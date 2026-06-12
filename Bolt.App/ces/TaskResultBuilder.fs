module TaskResultBuilder

open System.Linq
open System.Threading.Tasks

type TaskResultBuilder() =
    member _.Return(x: 'a) : Task<Result<'a, 'e>> = Task.FromResult(Ok x)

    member _.Bind(m: Result<'a, 'e>, f: 'a -> Task<Result<'b, 'e>>) : Task<Result<'b, 'e>> =
        task {
            let r = m

            match r with
            | Ok v -> return! f v
            | Error e -> return Error e
        }
    
    member _.Bind(m: Task<Result<'a, 'e>>, f: 'a -> Task<Result<'b, 'e>>) : Task<Result<'b, 'e>> =
        task {
            let! r = m

            match r with
            | Ok v -> return! f v
            | Error e -> return Error e
        }

    member _.Bind(t: Task, f: unit -> Task<Result<'a,'e>>) : Task<Result<'a,'e>> =
      task { do! t                                                              
             return! f () } 

    member _.TryFinally(expr, finallyExpr) =
        task {
            try
                return! expr()
            finally
                finallyExpr()
        } 

    member _.Zero () : Task<Result<unit,'e>> =
        Task.FromResult(Ok ()) 

    member _.Delay (f: unit -> Task<Result<'a, 'e>>) = f

    member _.Run (f: unit -> Task<Result<'a, 'e>>) = f ()
    
    member _.Combine (m, f) =
        task {
            let! r = m
            match r with
            | Ok () -> return! f ()
            | Error e -> return Error e
        }

    member _.While (guard, body) =
        task {
            let mutable result = Ok()
            while (guard () && Result.isOk result) do
                let! r = body ()
                result <- r
            return result
        }

let taskResult = TaskResultBuilder()

let inline (|>!) (m: Task<Result<'a, 'e>>) f = taskResult { let! x = m in return f x }

let sequenceResults (t: Task<Result<'a, 'e>[]>) : Task<Result<'a[], 'e>> =
    task {
        let! arr = t
        let mutable acc = Ok [||]
        let mutable i = 0
        while acc.IsOk && i < arr.Length do
            match acc, arr[i] with
            | Ok xs, Ok x -> acc <- Ok (Array.append xs [|x|])
            | _, Error e -> acc <- Error e
            | _, _ -> ()
            i <- i + 1
        return acc
    }

let partitionResults (t: Task<Result<'a, 'e>[]>) : Task<'a[] * 'e[]> =
    task {
        let! arr = t
        let mutable okAcc, eAcc, i = [||], [||], 0
        while i < arr.Length do
            match arr[i] with
            | Ok x -> okAcc <- Array.append okAcc [|x|]
            | Error e -> eAcc <- Array.append eAcc [|e|]
            i <- i + 1
        return (okAcc, eAcc)
    }