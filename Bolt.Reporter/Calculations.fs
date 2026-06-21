module Bolt.Reporter.Calculations

open Bolt.Reporter.RideReportingSource

let rides<'s> (source: 's) (ridesExtractor: 's -> RideReportingSource seq) = ridesExtractor source

