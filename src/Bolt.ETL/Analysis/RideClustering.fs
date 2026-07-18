namespace Bolt.ETL.Analysis

module RideClustering =

    open System
    open System.Globalization
    open System.Threading.Tasks
    open Bolt.ETL
    open Bolt.ETL.Analytics
    open Bolt.ETL.Plotting
    open Bolt.Models
    open Plotly.NET

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

    let prepareRideAnalysisSource (rides: FinishedRide[]) : RidesDataSource =
        { Rows = rides |> Array.map RideRow.fromRide }

    let toStPoints (data: RidesDataSource) : StPoint array =
        data.Rows
        |> Array.map (fun r -> {
            Latitude = r.Latitude
            Longitude = r.Longitude
            Hour = float r.Time.Hour + float r.Time.Minute / 60.0
        })

    let clusterTable (response: StDbscanResponse) : ResultTable =
        { Title = $"Skupiska (punkty: {response.NPoints}, poza skupiskami: {response.NNoise})"
          Headers = [ "skupisko"; "liczba przejazdów"; "szer. geogr."; "dł. geogr."; "średnia godzina" ]
          Rows =
            response.Clusters
            |> Array.sortByDescending _.Size
            |> Array.map (fun c ->
                [ string c.Id
                  string c.Size
                  c.CentroidLatitude.ToString("F5", CultureInfo.InvariantCulture)
                  c.CentroidLongitude.ToString("F5", CultureInfo.InvariantCulture)
                  c.MeanHour.ToString("F2", CultureInfo.InvariantCulture) ])
            |> List.ofArray
          Notes = [] }

    let buildSection (source: RidesDataSource) : Task<AnalysisSection> =
        task {
            let points = toStPoints source

            let! response =
                AnalyticsClient.stDbscan {
                    Points = points
                    EpsKm = 0.7
                    EpsHours = 1
                    MinSamples = 5
                }

            let chart =
                [ ClusterMap.ridePointsLayer points response
                  ClusterMap.centroidLayer response ]
                |> Chart.combine
                |> ClusterMap.withMapStyle points

            return
                { Id = "ride-clusters"
                  Title = "Skupiska odbiorów pasażerów"
                  Description = "Przestrzenno-czasowe skupiska (ST-DBSCAN) miejsc odbioru pasażerów."
                  Charts = [ { Title = "Mapa skupisk odbiorów"; PlotlyFigureJson = GenericChart.toFigureJson chart } ]
                  Tables = [ clusterTable response ] }
        }
