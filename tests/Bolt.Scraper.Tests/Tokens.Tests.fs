module Bolt.Scraper.Tests.TokensTests

open Bolt.Scraper.BoltApi.Tokens
open Xunit

[<Theory>]
[<InlineData("https://3zf1wp45.r.eu-central-1.awstrack.me/L0/https:%2F%2Fpartners.bolt.eu%2Fdriverapp%2Fmagic-login.html%3Fplatform=iOS%26token=AZxuIwee8RHRsLjeoyr5A6R7m9v12UUbciQjJGfjPJSOKjbxHJH7AM4Q6Tc2iP4s6AersIH6p5BANA0IxfYGk3qXS0ruK7Hp0AabdqD5lZolnsHp54Anx7dQApegS83czWLFJ2aEB8dexq6BeKUIoRdSCS9bXvke1BfhPcUWxjpxQ1oDetRqnFjZcg5b5WQMMQrXaDPRyOF2QfhN7vDaROtZr9ZX2u3F2QKYif8aHGQ3w0CULJVrE3OH61eMnC/1/0107019d2a93f346-4092a030-b3d4-4d51-9813-175ea3518366-000000/Df7OV4D460JnwTkVA5pQuBaB5aQ=252",
             "AZxuIwee8RHRsLjeoyr5A6R7m9v12UUbciQjJGfjPJSOKjbxHJH7AM4Q6Tc2iP4s6AersIH6p5BANA0IxfYGk3qXS0ruK7Hp0AabdqD5lZolnsHp54Anx7dQApegS83czWLFJ2aEB8dexq6BeKUIoRdSCS9bXvke1BfhPcUWxjpxQ1oDetRqnFjZcg5b5WQMMQrXaDPRyOF2QfhN7vDaROtZr9ZX2u3F2QKYif8aHGQ3w0CULJVrE3OH61eMnC")>]
[<InlineData("https://example.com/magic-login.html?platform=iOS&token=TOKEN123", "TOKEN123")>]
[<InlineData("https://3zf1wp45.r.eu-central-1.awstrack.me/L0/https:%2F%2Fpartners.bolt.eu%2Fdriverapp%2Fmagic-login.html%3Fplatform=iOS%26token=00CPievjECT8XREMTzEHAx98HVDvB3YzNXZvcHe93YeQOa3jQARYGkVIw4kF3vh4XPYihwQjylNuAFVbp3TvdiF3NmyrAKa6DAXbvUyTuAtxdfEV2qNUGzBv4FninMU1iu2wB6xWFydArJCw6tbY7cGQv4QOLwqog5vIdxHbTa4rBDH57DoasQb0AzbYvFFG35xCn9LQzYZT5aDxwib03dBL3FVQYCDWJuAzkgo9GRTIuLlupfIzEF51bemfukH/1/010701a0ddaf37d2-d31d768f-7eea-46e9-9397-b5c826853a2d-000000/oD90VtVUqR_P-bzTx5c-ShpdMns=258",
              "00CPievjECT8XREMTzEHAx98HVDvB3YzNXZvcHe93YeQOa3jQARYGkVIw4kF3vh4XPYihwQjylNuAFVbp3TvdiF3NmyrAKa6DAXbvUyTuAtxdfEV2qNUGzBv4FninMU1iu2wB6xWFydArJCw6tbY7cGQv4QOLwqog5vIdxHbTa4rBDH57DoasQb0AzbYvFFG35xCn9LQzYZT5aDxwib03dBL3FVQYCDWJuAzkgo9GRTIuLlupfIzEF51bemfukH")>]
[<InlineData("https://3zf1wp45.r.eu-central-1.awstrack.me/L0/https:%2F%2Fpartners.bolt.eu%2Fdriverapp%2Fmagic-login.html%3Fplatform=iOS%26token=ag6rN2HbXmxQ7sMA95fDhl5p8Zeh4nwiA9EejZ2Vproj3OPea5S4VNbSbyxMOKTXvGedshapDQMhAHJyZ4WSbQCfEpmL959BMmuUNaYbAhGc5i0tMYD0TDlnqG8jlVhGo3LXBIUblBWfch1Ig1HNAlYgjINGrmLxPi9VlOhvxc4fHrKDocQw2ob9pbnyKZDr8iSU6KHpKCn26smKKX6vjJ2zg06mHEKGIpu9nHBzbae0iAYBEFnSJs7iOy7rTxl/1/010701a0f388f879-4811cd4e-cc3f-46f5-87a7-48ed785bdc61-000000/L2QxAhnEt0jh6y3BSiEhKDem5U4=258",
             "ag6rN2HbXmxQ7sMA95fDhl5p8Zeh4nwiA9EejZ2Vproj3OPea5S4VNbSbyxMOKTXvGedshapDQMhAHJyZ4WSbQCfEpmL959BMmuUNaYbAhGc5i0tMYD0TDlnqG8jlVhGo3LXBIUblBWfch1Ig1HNAlYgjINGrmLxPi9VlOhvxc4fHrKDocQw2ob9pbnyKZDr8iSU6KHpKCn26smKKX6vjJ2zg06mHEKGIpu9nHBzbae0iAYBEFnSJs7iOy7rTxl")>]
let ``extractUrlFromTracking should extract token`` trackingUrl token =
    match MagicLink.getMagicLinkTokenFromTrackingUrl trackingUrl with
    | Error e -> Assert.Fail(e)
    | Ok(MagicLinkToken v) -> Assert.Equal(token, v)
