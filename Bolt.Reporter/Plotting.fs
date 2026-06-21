module Bolt.Reporter.Plotting

open System
open System.Text
open ScottPlot

let scatterPlot<'k, 'v> (kvm: 'k * 'v -> float * float) (data: ('k * 'v) seq)=
    let arrayed = data |> Seq.map kvm |> Array.ofSeq
    let plt = new Plot()
    plt.Add.Scatter(arrayed |> Array.map fst, arrayed |> Array.map snd) |> ignore
    plt

let asMarkdownSvg width height (plot: Plot) =
    plot.GetSvgXml(width, height)
    |> fun s ->
        let i = s.IndexOf("<svg")
        s.Substring(i)
    |> Encoding.UTF8.GetBytes
    |> Convert.ToBase64String
    |> sprintf "![Plot](data:image/svg+xml;base64,%s)"
