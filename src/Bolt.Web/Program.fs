module Bolt.Web.Program

open System
open Bolt.ETL.Analytics
#if DEBUG
open Bolt.Infrastrucutre.storage.Constants
#endif
open Bolt.Models.BoltApi
open Bolt.Scraper.ScrapePipeline
open Bolt.Web.Jobs
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection

/// Marker for WebApplicationFactory in integration tests.
type BoltWebMarker() = class end

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    builder.Services.AddSingleton<PipelineDeps<ScrapeSession, ScrapedData>>(Pipeline.realDeps) |> ignore
    let app = builder.Build()

#if DEBUG
    Paths.ensureStorageExists () |> ignore
#endif

    app.Configuration.GetValue<string>("Analytics:BaseUrl", "http://localhost:8000")
    |> AnalyticsClient.configure

#if !DEBUG
    app.UseHsts() |> ignore
    app.UseHttpsRedirection() |> ignore
#endif

    app.UseStaticFiles() |> ignore

    app.UseWebSockets(WebSocketOptions(KeepAliveInterval = TimeSpan.FromSeconds 30.0)) |> ignore

    app.Map(
        "/ws",
        Func<HttpContext, Threading.Tasks.Task>(fun ctx ->
            let deps = ctx.RequestServices.GetRequiredService<PipelineDeps<ScrapeSession, ScrapedData>>()
            WebSockets.handle deps ctx)
    )
    |> ignore

    app.MapGet(
        "/",
        Func<HttpContext, Threading.Tasks.Task>(fun ctx ->
            ctx.Response.ContentType <- "text/html; charset=utf-8"
            ctx.Response.WriteAsync(Views.Input.indexPage ()))
    )
    |> ignore

    app.MapGet(
        "/start",
        Func<HttpContext, Threading.Tasks.Task>(fun ctx ->
            ctx.Response.ContentType <- "text/html; charset=utf-8"
            ctx.Response.WriteAsync(Views.Input.emailPage ()))
    )
    |> ignore

    app.MapGet(
        "/health",
        Func<Threading.Tasks.Task<IResult>>(fun () ->
            task {
                let! healthy = AnalyticsClient.isHealthy ()
                return Results.Json {| status = "ok"; analytics = healthy |}
            })
    )
    |> ignore

    app.Run()
    0
