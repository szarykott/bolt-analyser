namespace Bolt.Models.BoltApi

open System.Text.Json.Nodes
open System.Text.Json.Serialization

type ActionLink = {
    [<JsonPropertyName("text")>]
    Text: string
    [<JsonPropertyName("url")>]
    Url: string
    [<JsonPropertyName("appearance")>]
    Appearance: string
    [<JsonPropertyName("analytics_event")>]
    AnalyticsEvent: AnalyticsEvent
}

type Profile = {
    [<JsonPropertyName("profile_image")>]
    ProfileImage: Image
    [<JsonPropertyName("first_name")>]
    FirstName: string
    [<JsonPropertyName("last_name")>]
    LastName: string
    [<JsonPropertyName("profile_badge")>]
    ProfileBadge: JsonNode option
}

type TileLayout = {
    [<JsonPropertyName("row")>]
    Row: int
    [<JsonPropertyName("column")>]
    Column: int
    [<JsonPropertyName("height")>]
    Height: int
    [<JsonPropertyName("width")>]
    Width: int
}

type TilePayload = {
    [<JsonPropertyName("title")>]
    Title: string
    [<JsonPropertyName("value_text")>]
    ValueText: string
    [<JsonPropertyName("action_url")>]
    ActionUrl: string option
    [<JsonPropertyName("content")>]
    Content: JsonNode list
    [<JsonPropertyName("analytics_event")>]
    AnalyticsEvent: AnalyticsEvent
}

type ProfileTile = {
    [<JsonPropertyName("type")>]
    Type: string
    [<JsonPropertyName("layout")>]
    Layout: TileLayout
    [<JsonPropertyName("payload")>]
    Payload: TilePayload
}

type Loyalty = {
    [<JsonPropertyName("progress")>]
    Progress: float
    [<JsonPropertyName("tier_icon")>]
    TierIcon: Image
    [<JsonPropertyName("tier_color")>]
    TierColor: Color
    [<JsonPropertyName("tier_text")>]
    TierText: ActionLink
}

type ProfileHeader = {
    [<JsonPropertyName("profile")>]
    Profile: Profile
    [<JsonPropertyName("items")>]
    Items: ProfileTile list
    [<JsonPropertyName("background_color")>]
    BackgroundColor: Color
    [<JsonPropertyName("loyalty")>]
    Loyalty: Loyalty
}

type Compliment = {
    [<JsonPropertyName("id")>]
    Id: int64
    [<JsonPropertyName("text")>]
    Text: string
}

type DriverProfile = {
    [<JsonPropertyName("header")>]
    Header: ProfileHeader
    [<JsonPropertyName("compliments")>]
    Compliments: Compliment list
    [<JsonPropertyName("compliments_preview_count")>]
    ComplimentsPreviewCount: int
    [<JsonPropertyName("link")>]
    Link: ActionLink
    [<JsonPropertyName("see_more_link")>]
    SeeMoreLink: ActionLink
    [<JsonPropertyName("sections")>]
    Sections: JsonNode list
}