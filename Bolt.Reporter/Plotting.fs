module Bolt.Reporter.Plotting

open System
open System.Text
open ScottPlot

[<RequireQualifiedAccess>]
type Svg = Base64Uri of string with
    member this.Value =
        match this with
        | Base64Uri v -> v


let plot<'k, 'v>
    (data : ('k * 'v) array)
    (fs: 'k * 'v -> float)
    (vs: 'k * 'v -> float) =
    
    let plt = new Plot()
    plt.Add.Scatter(data |> Array.map fs, data |> Array.map vs) |> ignore
    plt

let asMarkdownSvg width height (plot: Plot)=
    plot.GetSvgXml(width, height)
    |> fun s ->
        let i = s.IndexOf("<svg")
        s.Substring(i)
    |> Encoding.UTF8.GetBytes
    |> Convert.ToBase64String
    |> sprintf "![Plot](data:image/svg+xml;base64,%s)"
