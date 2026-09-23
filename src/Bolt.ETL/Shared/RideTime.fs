namespace Bolt.ETL.Shared

module RideTime =
    open System

    let isWeekend (date: DateTimeOffset) =
        match date.DayOfWeek, date.Hour with
        | DayOfWeek.Saturday, _
        | DayOfWeek.Sunday, _ -> true
        | DayOfWeek.Friday, h when h >= 18 -> true
        | DayOfWeek.Monday, h when h < 6 -> true
        | _ -> false

    let isRushHour (date: DateTimeOffset) =
        if isWeekend date then
            date.Hour >= 22 || date.Hour < 3
        else
            (date.Hour >= 6 && date.Hour < 9)
            || (date.Hour >= 15 && date.Hour < 19)
