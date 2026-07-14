module Bolt.ETL.Tests.AnalysisPipelineTests

open System
open Xunit
open Bolt.ETL
open Bolt.Models

let private rideAt (created: DateTimeOffset) : FinishedRide = {
    Payment =
        { PaymentMetadata = { PaymentType = "cash"; PaymentMethodType = "cash" }
          Paid = [||]
          Earned = [||] }
    Route = { RideDistance = 1.0<km>; Stops = [||] }
    Times =
        { CreatedTimestamp = created
          AcceptedTimestamp = created
          RideStart = created
          RideEnd = created }
    State = "finished"
}

[<Fact>]
let ``weatherEligible keeps rides at or before the cap`` () =
    let cap = DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
    let before = rideAt (cap.AddDays -1.0)
    let atCap = rideAt cap
    let after = rideAt (cap.AddDays 1.0)
    Assert.Equal<FinishedRide[]>(
        [| before; atCap |],
        AnalysisPipeline.weatherEligible cap [| before; atCap; after |])
