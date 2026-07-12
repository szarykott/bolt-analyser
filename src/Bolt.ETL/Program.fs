module Bolt.ETL.Program

open Bolt.ETL.Analysis

PerRide1.prepareRideAnalysisSource ()
|> PerRide1.saveRidesDataSourceToCsv

let perRideSource = PerRide2.prepareRideAnalysisSource ()
perRideSource |> PerRide2.saveRidesDataSourceToCsv
perRideSource |> PerRide2.runRemoteRegression
perRideSource |> PerRide2.runRemoteMirrorCheck

let clusteringSource = RideClustering.prepareRideAnalysisSource ()
clusteringSource |> RideClustering.saveRidesDataSourceToCsv
clusteringSource |> RideClustering.runRemoteClustering
