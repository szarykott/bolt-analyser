namespace Bolt.ETL.Analytics

module AnalyticsClient =

    open System
    open System.Net.Http
    open System.Text
    open Bolt.Infrastrucutre.Serialization

    let mutable private baseUrl =
        Environment.GetEnvironmentVariable "BOLT_ANALYTICS_URL"
        |> Option.ofObj
        |> Option.defaultValue "http://localhost:8000"

    /// Must be called before the first request; the HttpClient is created lazily.
    let configure (url: string) = baseUrl <- url

    let private client = lazy (new HttpClient(BaseAddress = Uri baseUrl))

    let isHealthy () : bool =
        try
            use response = client.Value.GetAsync("/health").GetAwaiter().GetResult()
            response.IsSuccessStatusCode
        with _ ->
            false

    let private post<'req, 'resp> (path: string) (request: 'req) : 'resp =
        use content = new StringContent(Json.serialize request, Encoding.UTF8, "application/json")
        use response = client.Value.PostAsync(path, content).GetAwaiter().GetResult()
        response.EnsureSuccessStatusCode() |> ignore
        response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        |> Json.deserialize<'resp>

    let stDbscan (request: StDbscanRequest) : StDbscanResponse =
        post "/cluster/st-dbscan" request

    let olsRegression (request: OlsRequest) : OlsResponse =
        post "/regression/ols" request

    let mirrorCheck (request: MirrorCheckRequest) : MirrorCheckResponse =
        post "/diagnostics/mirror-check" request
