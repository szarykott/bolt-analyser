namespace Bolt.ETL

open System
open System.Globalization
open Bolt.Models
open Bolt.Models.BoltApi
open Bolt.Infrastrucutre.Serialization

module RideFactory =
    let private warsawTz = TimeZoneInfo.FindSystemTimeZoneById "Europe/Warsaw"                                                                                                                                                                                                                                       
    let private toWarsaw (dt: DateTimeOffset) = TimeZoneInfo.ConvertTime(dt, warsawTz)    
    
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
        { CreatedTimestamp = previousOrder.CreatedTimestamp.Value |> toWarsaw
          AcceptedTimestamp = previousOrder.AcceptedTimestamp.Value.Value |> toWarsaw
          RideStart = previousOrder.RideStart.Value.Value |> toWarsaw
          RideEnd = previousOrder.RideEnd.Value.Value |> toWarsaw }

    let getRide (previousOrder: PreviousOrder) (pastOrderDetail: PastOrderDetail) : Ride =
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
                    { Created = previousOrder.CreatedTimestamp.Value |> toWarsaw
                      Stops = previousOrder.Stops |> Array.map getStops
                      State = previousOrder.State } }

    /// Pairs previous orders with their details positionally (both are scraped
    /// from the same handle list, in order) and keeps only finished rides.
    let getFinishedRides
        (previousOrders: PreviousOrder[])
        (pastOrderDetails: PastOrderDetail[])
        : FinishedRide[] =
        Array.zip previousOrders pastOrderDetails
        |> Array.map (fun (pr, pod) -> getRide pr pod)
        |> Array.choose (fun ride ->
            match ride.Data with
            | Finished r -> Some r
            | _ -> None)

    /// Keep the original response for just this order when parsing or conversion fails.
    let parseOrder (order: RawOrder) : Ride =
        let mutable stage = "previous_order"
        try
            let previous = Json.deserializeElement<PreviousOrder> order.PreviousOrder
            stage <- "past_order_detail"
            let detail = Json.deserializeElement<PastOrderDetail> order.PastOrderDetail
            stage <- "ride"
            let ride = getRide previous detail
            match ride.Data with
            | Finished finished ->
                if Array.isEmpty finished.Route.Stops then
                    invalidOp "Kurs nie zawiera przystanku odbioru."
                if not (Double.IsFinite(float finished.Route.RideDistance)) then
                    invalidOp "Dystans kursu nie jest skończoną liczbą."
            | _ -> ()
            ride
        with ex ->
            { Data = RideType.Error
                { OrderId = order.OrderId
                  Stage = stage
                  ExceptionType = ex.GetType().FullName
                  Message = ex.Message
                  StackTrace = ex.ToString()
                  PreviousOrder = order.PreviousOrder.Clone()
                  PastOrderDetail = order.PastOrderDetail.Clone() } }
