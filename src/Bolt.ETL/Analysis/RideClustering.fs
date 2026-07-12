namespace Bolt.ETL.Analysis

module RideClustering =

    open System
    open System.Globalization
    open Bolt.ETL
    open Bolt.ETL.Analytics
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

    let runRemoteClustering (data: RidesDataSource) =
        let points =
            data.Rows
            |> Array.map (fun r -> {
                Latitude = r.Latitude
                Longitude = r.Longitude
                Hour = float r.Time.Hour + float r.Time.Minute / 60.0
            })

        let response =
            AnalyticsClient.stDbscan {
                Points = points
                EpsKm = 0.5
                EpsHours = 0.5
                MinSamples = 5
            }

        JsonStorage.write "rideClustering.result.json" response

        let clusteredRows =
            Array.zip data.Rows response.Labels
            |> Array.map (fun (r, label) -> [|
                r.Latitude.ToString("F5", CultureInfo.InvariantCulture)
                r.Longitude.ToString("F5", CultureInfo.InvariantCulture)
                r.Time.ToString("HH:mm", CultureInfo.InvariantCulture)
                string label
            |])

        CsvStorage.write "rideClusteringDataSource-clustered.csv" {
            Headers = [| "latitude"; "longitude"; "time"; "cluster" |]
            Rows = clusteredRows
        }

        let clusterRows =
            response.Clusters
            |> Array.map (fun c -> [|
                string c.Id
                string c.Size
                c.CentroidLatitude.ToString(CultureInfo.InvariantCulture)
                c.CentroidLongitude.ToString(CultureInfo.InvariantCulture)
                c.MeanHour.ToString(CultureInfo.InvariantCulture)
            |])

        CsvStorage.write "rideClusters.csv" {
            Headers = [| "cluster"; "size"; "centroid_latitude"; "centroid_longitude"; "mean_hour" |]
            Rows = clusterRows
        }
