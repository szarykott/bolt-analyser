module Bolt.App.Tests.bolt.TokensTests

open Bolt.App.bolt.Tokens

open Xunit

[<Theory>]
[<InlineData("https://3zf1wp45.r.eu-central-1.awstrack.me/L0/https:%2F%2Fpartners.bolt.eu%2Fdriverapp%2Fmagic-login.html%3Fplatform=iOS%26token=AZxuIwee8RHRsLjeoyr5A6R7m9v12UUbciQjJGfjPJSOKjbxHJH7AM4Q6Tc2iP4s6AersIH6p5BANA0IxfYGk3qXS0ruK7Hp0AabdqD5lZolnsHp54Anx7dQApegS83czWLFJ2aEB8dexq6BeKUIoRdSCS9bXvke1BfhPcUWxjpxQ1oDetRqnFjZcg5b5WQMMQrXaDPRyOF2QfhN7vDaROtZr9ZX2u3F2QKYif8aHGQ3w0CULJVrE3OH61eMnC/1/0107019d2a93f346-4092a030-b3d4-4d51-9813-175ea3518366-000000/Df7OV4D460JnwTkVA5pQuBaB5aQ=252",
             "AZxuIwee8RHRsLjeoyr5A6R7m9v12UUbciQjJGfjPJSOKjbxHJH7AM4Q6Tc2iP4s6AersIH6p5BANA0IxfYGk3qXS0ruK7Hp0AabdqD5lZolnsHp54Anx7dQApegS83czWLFJ2aEB8dexq6BeKUIoRdSCS9bXvke1BfhPcUWxjpxQ1oDetRqnFjZcg5b5WQMMQrXaDPRyOF2QfhN7vDaROtZr9ZX2u3F2QKYif8aHGQ3w0CULJVrE3OH61eMnC")>]
[<InlineData("https://example.com/magic-login.html?platform=iOS&token=TOKEN123", "TOKEN123")>]
let ``extractUrlFromTracking_should_extractToken`` trackingUrl token =
    match MagicLink.getMagicLinkTokenFromTrackingUrl trackingUrl with
    | Error e -> Assert.Fail(e)
    | Ok(MagicLinkToken v) -> Assert.Equal(token, v)
