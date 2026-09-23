namespace Bolt.Web.Views

// Plotly layers for the pickup clustering result shown in the Web report.

module ClusterMap =

    open System
    open Bolt.ETL.Analysis.RideClustering
    open Plotly.NET
    open Plotly.NET.LayoutObjects

    // Plotly qualitative palette, cycled per cluster.
    let private palette = [|
        "#636EFA"; "#EF553B"; "#00CC96"; "#AB63FA"; "#FFA15A"
        "#19D3F3"; "#FF6692"; "#B6E880"; "#FF97FF"; "#FECB52"
    |]

    let private clusterColor (id: int) =
        Color.fromHex palette[id % palette.Length]

    let private formatHour (h: float) =
        let minutes = (int (Math.Round(h * 60.0)) % (24 * 60) + 24 * 60) % (24 * 60)
        sprintf "%02d:%02d" (minutes / 60) (minutes % 60)

    let ridePointsLayer (result: AnalysisResult) : GenericChart =
        let statsById =
            result.Clusters
            |> Array.map (fun c -> c.Id, c)
            |> Map.ofArray

        let traceOf (label: int) (members: PickupPoint array) =
            let name, color, opacity =
                match Map.tryFind label statsById with
                | Some stats ->
                    $"Cluster {label} · {formatHour stats.MeanHour} · n={stats.Size}",
                    clusterColor label,
                    0.8
                | None ->
                    $"Noise · n={result.NoiseCount}",
                    Color.fromHex "#999999",
                    0.35

            let hoverOf (p: PickupPoint) =
                if label < 0 then $"Noise · {formatHour p.Hour}"
                else $"Cluster {label} · {formatHour p.Hour}"

            Chart.PointMapbox(
                longitudes = (members |> Array.map _.Longitude),
                latitudes = (members |> Array.map _.Latitude),
                Name = name,
                Opacity = opacity,
                MultiText = (members |> Array.map hoverOf),
                MarkerColor = color,
                UseDefaults = false
            )

        Array.zip result.Points result.Labels
        |> Array.groupBy snd
        |> Array.sortBy (fun (_, p) -> p.Length)
        |> Array.map (fun (label, members) -> traceOf label (members |> Array.map fst))
        |> Chart.combine

    let centroidLayer (result: AnalysisResult) : GenericChart =
        let sizes = result.Clusters |> Array.map _.Size
        let minSize = if sizes.Length = 0 then 0 else Array.min sizes
        let maxSize = if sizes.Length = 0 then 0 else Array.max sizes

        // Label font grows with cluster size: 10px (smallest) to 24px (biggest).
        let fontSize (size: int) =
            if maxSize = minSize then 14.0
            else 10.0 + 14.0 * float (size - minSize) / float (maxSize - minSize)

        result.Clusters
        |> Array.map (fun c ->
            Chart.PointMapbox(
                longitudes = [ c.CentroidLongitude ],
                latitudes = [ c.CentroidLatitude ],
                Text = formatHour c.MeanHour,
                TextPosition = StyleParam.TextPosition.TopCenter,
                MarkerColor = Color.fromHex "#222222",
                ShowLegend = false,
                UseDefaults = false
            )
            |> GenericChart.mapTrace (
                TraceMapboxStyle.ScatterMapbox(
                    Mode = StyleParam.Mode.Markers_Text,
                    TextFont = Font.init (Size = fontSize c.Size, Color = Color.fromHex "#222222")
                )))
        |> Chart.combine

    let withMapStyle (points: PickupPoint array) (chart: GenericChart) : GenericChart =
        let center =
            if points.Length = 0 then (0.0, 0.0)
            else
                points |> Array.averageBy _.Longitude,
                points |> Array.averageBy _.Latitude

        chart
        |> Chart.withMapbox (
            Mapbox.init (
                Style = StyleParam.MapboxStyle.CartoPositron,
                Center = center,
                Zoom = 11.0
            )
        )
        |> Chart.withMarginSize (Left = 0, Right = 0, Top = 0, Bottom = 0)
