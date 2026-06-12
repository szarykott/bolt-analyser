module Bolt.Models.ActivityHours

open System
open System.Text.Json.Serialization

type ActivityDay = {
    [<JsonPropertyName("date")>]
    Date: DateOnly
    [<JsonPropertyName("active_seconds")>]
    ActiveSeconds: int
}

type ActivityPeriod = {
    [<JsonPropertyName("key")>]
    Key: string
    [<JsonPropertyName("start_date")>]
    StartDate: DateOnly
    [<JsonPropertyName("end_date")>]
    EndDate: DateOnly
    [<JsonPropertyName("active_seconds")>]
    ActiveSeconds: int
    [<JsonPropertyName("items")>]
    Items: ActivityDay list
}

type ActivityHours = {
    [<JsonPropertyName("description_text")>]
    DescriptionText: string
    [<JsonPropertyName("number_of_months")>]
    NumberOfMonths: int
    [<JsonPropertyName("periods")>]
    Periods: ActivityPeriod list
}