module Bolt.ETL.Meteo.Model

open Bolt.Models.Meteo

type TemperatureBucket =
    | Frost
    | Cold
    | Mild
    | Hot    

module TemperatureBucket =
    let fromCelcius (v: float<celcius>) =
        match v with
        | x when x <= 0.0<celcius> -> Frost
        | x when x > 0.0<celcius> && x <= 15.0<celcius> -> Cold
        | x when x > 15.0<celcius> && x <= 25.0<celcius> -> Mild
        | x when x > 25.0<celcius> -> Hot
        | x -> failwith $"Temperature {x} not assignable to bucket"
        