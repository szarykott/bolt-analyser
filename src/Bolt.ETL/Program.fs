module Bolt.ETL.Program

open Bolt.ETL.Analysis

PerRide1.prepareRideAnalysisSource ()
|> PerRide1.saveRidesDataSourceToCsv

PerRide2.prepareRideAnalysisSource ()
|> PerRide2.saveRidesDataSourceToCsv

RideClustering.prepareRideAnalysisSource ()
|> RideClustering.saveRidesDataSourceToCsv