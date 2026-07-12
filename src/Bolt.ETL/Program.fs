module Bolt.ETL.Program

open Bolt.ETL.Analysis
open Bolt.ETL.Plotting
open Plotly.NET

PerRide1.prepareRideAnalysisSource ()
|> PerRide1.saveRidesDataSourceToCsv

let perRideSource = PerRide2.prepareRideAnalysisSource ()
perRideSource |> PerRide2.saveRidesDataSourceToCsv
perRideSource |> PerRide2.runRemoteRegression
perRideSource |> PerRide2.runRemoteMirrorCheck

let clusteringSource = RideClustering.prepareRideAnalysisSource ()
clusteringSource |> RideClustering.saveRidesDataSourceToCsv

let clusterPoints = RideClustering.toStPoints clusteringSource
let clustering = RideClustering.runRemoteClustering clusteringSource

[ ClusterMap.ridePointsLayer clusterPoints clustering
  ClusterMap.centroidLayer clustering ]
|> Chart.combine
|> ClusterMap.withMapStyle clusterPoints
|> ClusterMap.saveHtml "rideClusters"
