module Bolt.Scraper.Tests.MagicLinkTests

open Xunit
open Bolt.Scraper.BoltApi.Tokens

[<Fact>]
let ``extracts token from awstrack tracking url`` () =
    let url = "https://awstrack.me/L0/https:%2F%2Fpartners.bolt.eu%2Fmagic-login%3Ftoken%3DABC123/1/0100019/xyz="
    Assert.Equal(Ok(MagicLinkToken "ABC123"), MagicLink.getMagicLinkTokenFromTrackingUrl url)

[<Fact>]
let ``extracts token from plain magic-login url`` () =
    let url = "https://partners.bolt.eu/magic-login?token=XYZ789&lang=pl"
    Assert.Equal(Ok(MagicLinkToken "XYZ789"), MagicLink.getMagicLinkTokenFromTrackingUrl url)

[<Fact>]
let ``fails when url has no token`` () =
    let url = "https://partners.bolt.eu/magic-login?lang=pl"
    Assert.True(MagicLink.getMagicLinkTokenFromTrackingUrl url |> Result.isError)
