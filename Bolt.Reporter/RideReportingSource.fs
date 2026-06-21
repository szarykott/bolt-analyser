module Bolt.Reporter.RideReportingSource

open System
open System.Globalization
open Bolt.Models.PastOrderDetail
open Bolt.Models.PreviousOrder
open Bolt.Models.Shared

type RideTimes =
    { CreatedTimestamp: UnixTime
      AcceptedTimestamp: UnixTime
      RideStart: UnixTime
      RideEnd: UnixTime }

type RidePaymentMetadata =
    { PaymentType: PaymentType
      PaymentMethodType: PaymentMethodType }

type RideRoute =
    { RideDistance: Distance
      Stops: TripStop list }

type MoneyElement =
    { Title: string
      Value: Money }

    static member (+)(m1: MoneyElement, m2: MoneyElement) : MoneyElement =
        { Title = "Summed"
          Value = m1.Value + m2.Value }

    static member Zero = { Title = ""; Value = Money.Zero }

type Payment =
    { PaymentMetadata: RidePaymentMetadata
      Paid: MoneyElement seq
      Earned: MoneyElement seq }

type FinishedRide =
    { Payment: Payment
      Route: RideRoute
      Times: RideTimes
      State: OrderState }

type RideThatDidNotHappen =
    { Created: UnixTime
      Stops: TripStop seq
      State: OrderState }

[<RequireQualifiedAccess>]
type RideType =
    | Finished of FinishedRide
    | DidNotHappen of RideThatDidNotHappen

type RideReportingSource = { Handle: OrderHandle; Data: RideType }

let private toMoney (s: string) =
    let pl = CultureInfo.GetCultureInfo("pl-PL")
    let core = s.Substring(0, s.Length - 3) // strip NBSP + "zł"
    Money(Decimal.Parse(core, NumberStyles.Number, pl))

let private toEarnings (earningsElement: OrderEarnings) =
    let provisionElement =
        { Title = earningsElement.AccordionItems.Tail.Head.Title
          Value = earningsElement.AccordionItems.Tail.Head.Value }

    earningsElement.AccordionItems.Head.Items
    |> Seq.map (fun f -> { Title = f.Title; Value = f.Value })
    |> Seq.append [ provisionElement ]

let private getPayment (previousOrder: PreviousOrder) (pastOrderDetail: PastOrderDetail) =
    { PaymentMetadata =
        { PaymentType = previousOrder.PaymentType
          PaymentMethodType = previousOrder.PaymentMethodType }
      Paid =
        pastOrderDetail.Payment.Value.Items
        |> Seq.map (fun i ->
            { Title = i.Title
              Value = i.TrailingComponent.Text |> toMoney })
      Earned = pastOrderDetail.Earnings.Value |> toEarnings }

let private getRoute (previousOrder: PreviousOrder) =
    { RideDistance = previousOrder.RideDistance.Value
      Stops = previousOrder.Stops }

let private getTimes (previousOrder: PreviousOrder) =
    { CreatedTimestamp = previousOrder.CreatedTimestamp
      AcceptedTimestamp = previousOrder.AcceptedTimestamp.Value
      RideStart = previousOrder.RideStart.Value
      RideEnd = previousOrder.RideEnd.Value }

let buildReportingDataSource (previousOrder: PreviousOrder) pastOrderDetail =
    match previousOrder.State with
    | OrderState.Finished ->
        { Handle = previousOrder.OrderHandle
          Data =
            RideType.Finished(
                { Payment = getPayment previousOrder pastOrderDetail
                  Route = getRoute previousOrder
                  Times = getTimes previousOrder
                  State = previousOrder.State}
            ) }
    | _ ->
        { Handle = previousOrder.OrderHandle
          Data =
            RideType.DidNotHappen(
                { Created = previousOrder.CreatedTimestamp
                  Stops = previousOrder.Stops
                  State = previousOrder.State}
            ) }
