namespace Bolt.Web.Views

type ResultChart = {
    Title: string
    /// Plotly figure JSON ({"data": …, "layout": …}), rendered client-side.
    PlotlyFigureJson: string
}