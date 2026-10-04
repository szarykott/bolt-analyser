module Bolt.Web.Program

open System
open Bolt.ETL.Analytics
#if DEBUG
open Bolt.Infrastrucutre.storage.Constants
#endif
open Bolt.Models.BoltApi
open Bolt.Scraper.ScrapePipeline
open Bolt.Web.Jobs
open Bolt.Web.JobSessions
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

/// Marker for WebApplicationFactory in integration tests.
type BoltWebMarker() = class end

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    builder.Services.AddSingleton<PipelineDeps<ScrapeSession, ScrapedData>>(Pipeline.realDeps) |> ignore
    builder.Services.AddSingleton<JobRegistry>(fun sp ->
        JobRegistry(TimeSpan.FromMinutes 3.0, sp.GetRequiredService<ILogger<JobRegistry>>()))
    |> ignore
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
            let registry = ctx.RequestServices.GetRequiredService<JobRegistry>()
            WebSockets.handle deps registry ctx)
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
            let registry = ctx.RequestServices.GetRequiredService<JobRegistry>()
            let existing =
                match ctx.Request.Cookies.TryGetValue cookieName with
                | true, value -> Some value
                | _ -> None
            let sessionId, created = registry.GetOrCreate existing
            if created then
                let cookie = CookieOptions()
                cookie.HttpOnly <- true
                cookie.Secure <- ctx.Request.IsHttps
                cookie.SameSite <- SameSiteMode.Strict
                cookie.Path <- "/"
                ctx.Response.Cookies.Append(cookieName, sessionId, cookie)
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
