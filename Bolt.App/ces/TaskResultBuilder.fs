module TaskResultBuilder

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

let taskResult = TaskResultBuilder()
