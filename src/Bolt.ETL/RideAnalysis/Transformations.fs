namespace Bolt.ETL.RideAnalysis

open System
open System.Globalization
open Bolt.Models
open Bolt.Models.BoltApi

module Transformations =
    let private toMoney (s: string) =
        let pl = CultureInfo.GetCultureInfo("pl-PL")
        let core = s.Substring(0, s.Length - 3) // strip NBSP + "zł"
        Decimal.Parse(core, NumberStyles.Number, pl)

    let private toEarnings (earningsElement: OrderEarnings) =
        let provisionElement =
            { Title = earningsElement.AccordionItems.Tail.Head.Title
              Value = earningsElement.AccordionItems.Tail.Head.Value.Value }

        earningsElement.AccordionItems.Head.Items
        |> Array.map (fun f ->
            { Title = f.Title
              Value = f.Value.Value })
        |> Array.append [| provisionElement |]

    let private getPayment (previousOrder: PreviousOrder) (pastOrderDetail: PastOrderDetail) =
        { PaymentMetadata =
            { PaymentType = previousOrder.PaymentType
              PaymentMethodType = previousOrder.PaymentMethodType }
          Paid =
            pastOrderDetail.Payment.Value.Items
            |> Array.map (fun i ->
                { Title = i.Title
                  Value = i.TrailingComponent.Text |> toMoney })
          Earned = pastOrderDetail.Earnings.Value |> toEarnings }

    let private getStops (stop: TripStop) : Bolt.Models.TripStop =
        { Address = stop.Address
          Timestamp = stop.Timestamp |> Option.map _.Value
          Location =
            { Latitude = stop.Lat
              Longitude = stop.Lng } }

    let private getRoute (previousOrder: PreviousOrder) =
        { RideDistance = previousOrder.RideDistance.Value.Value * 1.0<km>
          Stops = previousOrder.Stops |> Array.map getStops }

    let private getTimes (previousOrder: PreviousOrder) =
        { CreatedTimestamp = previousOrder.CreatedTimestamp.Value
          AcceptedTimestamp = previousOrder.AcceptedTimestamp.Value.Value
          RideStart = previousOrder.RideStart.Value.Value
          RideEnd = previousOrder.RideEnd.Value.Value }

    let toRide (previousOrder: PreviousOrder) (pastOrderDetail: PastOrderDetail) : Ride =
        match OrderState.isFinished previousOrder.State with
        | true ->
            { Data =
                RideType.Finished
                    { Payment = getPayment previousOrder pastOrderDetail
                      Route = getRoute previousOrder
                      Times = getTimes previousOrder
                      State = previousOrder.State } }
        | _ ->
            { Data =
                RideType.DidNotHappen
                    { Created = previousOrder.CreatedTimestamp.Value
                      Stops = previousOrder.Stops |> Array.map getStops
                      State = previousOrder.State } }
