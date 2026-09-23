namespace Bolt.ETL.Analysis

module RideClustering =

    open System
    open System.Threading.Tasks
    open Bolt.ETL
    open Bolt.ETL.Analytics
    open Bolt.Models

    type RideRow = {
        Latitude: float
        Longitude: float
        Time: DateTimeOffset
    }

    type RidesDataSource = {
        Rows: RideRow array
    }

    type PickupPoint = {
        Latitude: float
        Longitude: float
        Hour: float
    }

    type Cluster = {
        Id: int
        Size: int
        CentroidLatitude: float
        CentroidLongitude: float
        MeanHour: float
    }

    type AnalysisResult = {
        Points: PickupPoint array
        Labels: int array
        NoiseCount: int
        Clusters: Cluster array
    }

    module RideRow =
        let fromRide (ride: FinishedRide) : RideRow =
            let pickup = ride.Route.Stops[0].Location
            {
                Latitude = pickup.Latitude
                Longitude = pickup.Longitude
                Time = ride.Times.CreatedTimestamp
            }

    let prepareRideAnalysisSource (rides: FinishedRide[]) : RidesDataSource =
        { Rows = rides |> Array.map RideRow.fromRide }

    let toPoints (data: RidesDataSource) : PickupPoint array =
        data.Rows
        |> Array.map (fun r -> {
            Latitude = r.Latitude
            Longitude = r.Longitude
            Hour = float r.Time.Hour + float r.Time.Minute / 60.0
        })

    let fromAnalyticsResponse (points: PickupPoint array) (response: StDbscanResponse) : AnalysisResult =
        { Points = points
          Labels = response.Labels
          NoiseCount = response.NNoise
          Clusters = response.Clusters |> Array.map (fun c ->
            { Id = c.Id
              Size = c.Size
              CentroidLatitude = c.CentroidLatitude
              CentroidLongitude = c.CentroidLongitude
              MeanHour = c.MeanHour }) }

    let run (source: RidesDataSource) : Task<AnalysisResult> =
        task {
            let points = toPoints source

            let! response =
                AnalyticsClient.stDbscan {
                    Points = points |> Array.map (fun p ->
                        { Latitude = p.Latitude; Longitude = p.Longitude; Hour = p.Hour })
                    EpsKm = 0.7
                    EpsHours = 1
                    MinSamples = 5
                }

            return fromAnalyticsResponse points response
        }
