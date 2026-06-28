module Bolt.Reporter.Plotting

open Plotly.NET
open Plotly.NET.ImageExport
open Plotly.NET.LayoutObjects

let linePlot dataPoints =
    Chart.Line(xy = dataPoints)

let linePlotStyle 
    title 
    xTitle 
    yTitle 
    chart =
    chart
    |> Chart.withTitle (Title.init(Text = title))
    |> Chart.withXAxis (LinearAxis.init(
        DTick = 1, 
        Title = Title.init(Text = xTitle)))
    |> Chart.withYAxis (LinearAxis.init(
        Title = Title.init(Text = yTitle)))
    


let svgString width height chart = 
    Chart.toSVGString (Width = width, Height = height) chart 
