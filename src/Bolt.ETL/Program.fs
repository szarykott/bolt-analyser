module Bolt.ETL.Program

open RideAnalysis.Loader

prepareRideAnalysisSource ()
|> saveRidesDataSourceToCsv
