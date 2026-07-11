namespace Bolt.ETL.Analysis

module RideClustering =

    open System
    open System.Globalization
    open Bolt.ETL
    open Bolt.Infrastructure.Repository
    open Bolt.Infrastrucutre.storage.Storage
    open Bolt.Models

    type RideRow = {
        Latitude: float
        Longitude: float
        Time: DateTimeOffset
    }

    type RidesDataSource = {
        Rows: RideRow array
    }

    module RideRow =
        let fromRide (ride: FinishedRide) : RideRow =
            let pickup = ride.Route.Stops[0].Location
            {
                Latitude = pickup.Latitude
                Longitude = pickup.Longitude
                Time = ride.Times.CreatedTimestamp
            }

    let prepareRideAnalysisSource () : RidesDataSource =
        let previousRides = (PreviousOrderRepository.get ()).Value |> Array.ofSeq
        let pastOrders = (PastOrderDetailRepository.get ()).Value |> Array.ofSeq

        let finishedRide (ride: Ride) : FinishedRide option =
            match ride.Data with
            | Finished r -> Some r
            | _ -> None

        let data =
            Array.zip previousRides pastOrders
            |> Array.map (fun (pr, pod) -> RideFactory.getRide pr pod)
            |> Array.choose finishedRide
            |> Array.map RideRow.fromRide

        { Rows = data }

    let saveRidesDataSourceToCsv (data: RidesDataSource) =
        let headers = [|
            "latitude"
            "longitude"
            "time"
        |]

        let data =
            data.Rows
            |> Array.map(fun r -> [|
                r.Latitude.ToString("F5", CultureInfo.InvariantCulture)
                r.Longitude.ToString("F5", CultureInfo.InvariantCulture)
                r.Time.ToString("HH:mm", CultureInfo.InvariantCulture)
            |])

        CsvStorage.write "rideClusteringDataSource.csv" { Headers = headers; Rows = data }
