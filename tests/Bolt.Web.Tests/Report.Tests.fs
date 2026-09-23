module Bolt.Web.Tests.ReportTests

open System
open System.Text.Json
open Xunit
open Bolt.Models
open Bolt.Models.BoltApi
open Bolt.Web

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
let ``basic statistics sum completed ride earnings and distance`` () =
    let date = DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)
    let first =
        { rideAt date with
            Payment =
                { (rideAt date).Payment with
                    Earned = [| { Title = "course"; Value = 20m }; { Title = "bonus"; Value = 5m } |] }
            Route = { (rideAt date).Route with RideDistance = 2.5<km> } }
    let second =
        { rideAt (date.AddHours 1.0) with
            Payment = { (rideAt date).Payment with Earned = [| { Title = "course"; Value = 30m } |] }
            Route = { (rideAt date).Route with RideDistance = 3.0<km> } }
    let stats = Report.basicStatistics [| first; second |]
    Assert.Equal(55m, stats.TotalEarnings)
    Assert.Equal(5.5, stats.TotalDistanceKm, 3)

[<Fact>]
let ``basic statistics reconcile passenger payments tips commission and ride records`` () =
    let date = DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.FromHours 2.0)
    let stop address =
        { Address = address
          Location = { Latitude = 50.0; Longitude = 20.0 }
          Timestamp = None }
    let makeRide offset paymentType paid distance fromAddress toAddress earned =
        let ride = rideAt (date.AddHours offset)
        { ride with
            Payment =
                { ride.Payment with
                    PaymentMetadata = { ride.Payment.PaymentMetadata with PaymentType = paymentType }
                    Paid = [| { Title = "zapłacone"; Value = paid } |]
                    Earned = earned }
            Route =
                { RideDistance = distance * 1.0<km>
                  Stops = [| stop fromAddress; stop toAddress |] } }
    let rides =
        [| makeRide 0.0 "cash" 43m 5.0 "Pierwszy" "Drugi"
               [| { Title = "Prowizja Bolt"; Value = -8m }
                  { Title = "Kurs"; Value = 40m }
                  { Title = "Napiwek"; Value = 3m } |]
           makeRide 1.0 "in_app" 60m 1.0 "Trzeci" "Czwarty"
               [| { Title = "Prowizja Bolt"; Value = -12m }
                  { Title = "Kurs"; Value = 60m } |]
           makeRide 2.0 "voucher" 10m 3.0 "Piąty" "Szósty"
               [| { Title = "Prowizja Bolt"; Value = -2m }
                  { Title = "Kurs"; Value = 10m } |] |]
    let stats = Report.basicStatistics rides
    Assert.Equal(113m, stats.TotalPaid)
    Assert.Equal(43m, stats.PaidCash)
    Assert.Equal(70m, stats.PaidDigital)
    Assert.Equal(stats.TotalPaid, stats.PaidCash + stats.PaidDigital)
    Assert.Equal(3m, stats.TotalTips)
    Assert.Equal(22m, stats.TotalCommission)
    Assert.Equal(91m, stats.TotalEarnings)
    Assert.Equal(22m / 113m, stats.CommissionRate)
    Assert.Equal(5.0, stats.LongestRide.DistanceKm)
    Assert.Equal(Some "Pierwszy", stats.LongestRide.FromAddress)
    Assert.Equal(1.0, stats.ShortestRide.DistanceKm)
    Assert.Equal(48m, stats.HighestEarningRide.Earnings)
    Assert.Equal(8m, stats.LowestEarningRide.Earnings)

[<Fact>]
let ``report rejects history without completed rides`` () =
    let data: ScrapedData =
        { Email = "driver@example.com"
          Profile = JsonDocument.Parse("{}").RootElement
          ActivityHours = JsonDocument.Parse("{}").RootElement
          OrderHistory = [||]
          PreviousOrders = [||]
          PastOrderDetails = [||]
          SkippedOrders = [||] }
    let result = Report.run data |> fun task -> task.GetAwaiter().GetResult()
    Assert.Equal(Error "Nie znaleziono zakończonych przejazdów dla driver@example.com", result)
