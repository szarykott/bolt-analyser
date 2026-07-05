module Bolt.ETL.RideAnalysis.Loader

open Bolt.ETL
open Bolt.ETL.Geo
open Bolt.ETL.RideAnalysis.Model
open Bolt.Infrastructure.Repository
open Bolt.Infrastrucutre.storage.Storage
open Bolt.Models
open Bolt.Models.Meteo

let prepareRideAnalysisSource () : RidesDataSource =
    let previousRides = (PreviousOrderRepository.get ()).Value |> Array.ofSeq
    let pastOrders = (PastOrderDetailRepository.get ()).Value |> Array.ofSeq
    let meteo = (MeteoRepository.get ()).Value
    let districts = (DistrictsRepository.get ()).Value

    let weatherProvider = Weather.getDataPoint meteo
    let districtProvider = DistrictAssignment.assignCoordinatesToDistrict districts

    let finishedRide (ride: Ride) : FinishedRide option =
        match ride.Data with
        | Finished r -> Some r
        | _ -> None

    let data =
        Array.zip previousRides pastOrders
        |> Array.map (fun (pr, pod) -> RideFactory.getRide pr pod)
        |> Array.choose finishedRide
        |> Array.map (RideRow.fromRide weatherProvider districtProvider)

    { Rows = data }

let saveRidesDataSourceToCsv (data: RidesDataSource) =
    let headers = [|
        "price_pln"
        "price_per_km"
        "distance_km"
        "part_of_day"
        "day_of_week"
        "pickup_district"
        "rain"
        "snow"
        "temperature_bucket"
        "payment_type"
    |]
    
    let data =
        data.Rows
        |> Array.map(fun r -> [|
            r.PricePln.ToString("F2")
            r.PricePerKm.ToString("F2")
            r.Distance.ToString()
            r.PartOfDay.ToString()
            r.DayOfWeek.ToString()
            r.PickupDistrict.Value.ToString()
            r.Rain.ToString()
            r.Snow.ToString()
            r.Temperature.ToString()
            r.PaymentType
        |])

    CsvStorage.write "ridesDataSource.csv" { Headers = headers; Rows = data }
