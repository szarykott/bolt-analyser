module Bolt.ETL.Shared.Model

open System

type PartOfDay =
    | Night
    | Morning
    | Afternoon
    | Evening

module PartOfDay =
    let fromDate (v: DateTimeOffset) =
        match v.Hour with
        | x when x >= 0 && x <= 5 -> Night
        | x when x >= 6 && x <= 11 -> Morning
        | x when x >= 12 && x <= 15 -> Afternoon
        | x when x >= 16 && x <= 19 -> Evening
        | x when x >= 20 && x <= 23 -> Night
        | x -> failwith $"Hour {x} not assignable to bucket"
