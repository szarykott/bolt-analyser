namespace Bolt.ETL.Analytics

module AnalyticsClient =

    open System
    open System.Net.Http
    open System.Text
    open System.Threading.Tasks
    open Bolt.Infrastrucutre.Serialization

    let mutable private baseUrl =
        Environment.GetEnvironmentVariable "BOLT_ANALYTICS_URL"
        |> Option.ofObj
        |> Option.defaultValue "http://localhost:8000"

    /// Must be called before the first request; the HttpClient is created lazily.
    let configure (url: string) = baseUrl <- url

    let private client = lazy (new HttpClient(BaseAddress = Uri baseUrl))

    let isHealthy () : Task<bool> =
        task {
            try
                use! response = client.Value.GetAsync "/health"
                return response.IsSuccessStatusCode
            with _ ->
                return false
        }

    let private post<'req, 'resp> (path: string) (request: 'req) : Task<'resp> =
        task {
            use content = new StringContent(Json.serialize request, Encoding.UTF8, "application/json")
            use! response = client.Value.PostAsync(path, content)
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync()
            return Json.deserialize<'resp> body
        }

    let stDbscan (request: StDbscanRequest) : Task<StDbscanResponse> =
        post "/cluster/st-dbscan" request

    let olsRegression (request: OlsRequest) : Task<OlsResponse> =
        post "/regression/ols" request

    let wlsRegression (request: WlsRequest) : Task<OlsResponse> =
        post "/regression/wls" request

    let mirrorCheck (request: MirrorCheckRequest) : Task<MirrorCheckResponse> =
        post "/diagnostics/mirror-check" request
