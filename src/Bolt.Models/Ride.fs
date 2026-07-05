namespace Bolt.Models

open System
open Bolt.Models.Geo

[<Measure>] type km

type RidePaymentMetadata =
    { PaymentType: string
      PaymentMethodType: string }

type MoneyElement =
    { Title: string
      Value: decimal }

type Payment =
    { PaymentMetadata: RidePaymentMetadata
      Paid: MoneyElement array
      Earned: MoneyElement array }

type TripStop = {
    Address: string
    Location: GeoPoint
    Timestamp: DateTimeOffset option
}

type RideRoute =
    { RideDistance: float<km>
      Stops: TripStop array }

type RideTimes =
    { CreatedTimestamp: DateTimeOffset
      AcceptedTimestamp: DateTimeOffset
      RideStart: DateTimeOffset
      RideEnd: DateTimeOffset }

type FinishedRide =
    { Payment: Payment
      Route: RideRoute
      Times: RideTimes
      State: string }

type RideThatDidNotHappen =
    { Created: DateTimeOffset
      Stops: TripStop array
      State: string }

type RideType =
    | Finished of FinishedRide
    | DidNotHappen of RideThatDidNotHappen

type Ride = {
    Data: RideType
}
