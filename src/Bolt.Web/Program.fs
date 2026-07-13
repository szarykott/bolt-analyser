module Bolt.Web.Program

open System
open System.Threading
open Bolt.ETL.Analytics
open Bolt.Infrastructure.Repository
open Bolt.Infrastrucutre.storage.Constants
open Bolt.Scraper.Krakow.Districts
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration

/// Marker for WebApplicationFactory in integration tests.
type BoltWebMarker() = class end

// Kraków districts are shared, static geo data: make sure they are on disk
// before the first analysis needs them.
let ensureDistricts () =
    match DistrictsRepository.get () with
    | Some _ -> ()
    | None ->
        match (getKrakowDistricts CancellationToken.None).GetAwaiter().GetResult() with
        | Ok districts -> DistrictsRepository.save districts
        | Error e -> failwith $"Could not load Kraków districts at startup: {e}"

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let app = builder.Build()

    Paths.ensureStorageExists () |> ignore

    app.Configuration.GetValue<string>("Analytics:BaseUrl", "http://localhost:8000")
    |> AnalyticsClient.configure

    if not (app.Configuration.GetValue<bool>("SkipStartupDistricts", false)) then
        ensureDistricts ()

#if !DEBUG
    app.UseHsts() |> ignore
    app.UseHttpsRedirection() |> ignore
#endif

    app.UseStaticFiles() |> ignore

    app.MapGet(
        "/",
        Func<HttpContext, Threading.Tasks.Task>(fun ctx ->
            ctx.Response.ContentType <- "text/html; charset=utf-8"
            ctx.Response.WriteAsync(Views.indexPage ()))
    )
    |> ignore

    app.MapGet(
        "/health",
        Func<IResult>(fun () ->
            Results.Json {| status = "ok"; analytics = AnalyticsClient.isHealthy () |})
    )
    |> ignore

    app.Run()
    0
