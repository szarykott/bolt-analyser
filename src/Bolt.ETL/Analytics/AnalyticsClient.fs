namespace Bolt.ETL.Analytics

module AnalyticsClient =

    open System
    open System.Net.Http
    open System.Text
    open Bolt.Infrastrucutre.Serialization

    let private baseUrl =
        Environment.GetEnvironmentVariable "BOLT_ANALYTICS_URL"
        |> Option.ofObj
        |> Option.defaultValue "http://localhost:8000"

    let private client = new HttpClient(BaseAddress = Uri baseUrl)

    let private post<'req, 'resp> (path: string) (request: 'req) : 'resp =
        use content = new StringContent(Json.serialize request, Encoding.UTF8, "application/json")
        use response = client.PostAsync(path, content).GetAwaiter().GetResult()
        response.EnsureSuccessStatusCode() |> ignore
        response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        |> Json.deserialize<'resp>

    let stDbscan (request: StDbscanRequest) : StDbscanResponse =
        post "/cluster/st-dbscan" request

    let olsRegression (request: OlsRequest) : OlsResponse =
        post "/regression/ols" request

    let mirrorCheck (request: MirrorCheckRequest) : MirrorCheckResponse =
        post "/diagnostics/mirror-check" request
