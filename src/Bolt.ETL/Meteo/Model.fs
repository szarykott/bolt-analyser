module Bolt.ETL.Meteo.Model

open Bolt.Models.Meteo

type TemperatureBucket =
    | Frost
    | Cold
    | Cool
    | Mild
    | Warm
    | Hot

module TemperatureBucket =
    let fromCelcius (v: float<celcius>) =
        match v with
        | x when x <= 0.0<celcius> -> Frost
        | x when x > 0.0<celcius> && x <= 10.0<celcius> -> Cold
        | x when x > 10.0<celcius> && x <= 18.0<celcius> -> Cool
        | x when x > 18.0<celcius> && x <= 25.0<celcius> -> Mild
        | x when x > 25.0<celcius> && x <= 30.0<celcius> -> Warm
        | x when x > 30.0<celcius> -> Hot
        | x -> failwith $"Temperature {x} not assignable to bucket"

    // Driver-facing labels use the boundary values, not adjectives — values are
    // more informative and cannot drift out of sync with the bucketing above.
    let label (t: TemperatureBucket) =
        match t with
        | Frost -> "≤0°C"
        | Cold -> "0–10°C"
        | Cool -> "10–18°C"
        | Mild -> "18–25°C"
        | Warm -> "25–30°C"
        | Hot -> ">30°C"