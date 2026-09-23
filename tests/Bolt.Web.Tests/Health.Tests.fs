module Bolt.Web.Tests.HealthTests

open System.Net
open Microsoft.AspNetCore.Mvc.Testing
open Microsoft.AspNetCore.Hosting
open Xunit
open Bolt.Web.Program

let private makeFactory () =
    (new WebApplicationFactory<BoltWebMarker>())
        .WithWebHostBuilder(fun b -> b.UseSetting("SkipStartupDistricts", "true") |> ignore)

[<Fact>]
let ``health endpoint responds ok`` () =
    use factory = makeFactory ()
    use client = factory.CreateClient()
    let response = client.GetAsync("/health").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert.Contains("\"status\":\"ok\"", body.Replace(" ", ""))

[<Fact>]
let ``start page serves email form`` () =
    use factory = makeFactory ()
    use client = factory.CreateClient()
    let response = client.GetAsync("/start").GetAwaiter().GetResult()
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert.Contains("name=\"email\"", body)
