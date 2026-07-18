namespace Bolt.ETL.Analysis

open System

type ResultTable = {
    Title: string
    Headers: string list
    Rows: string list list
    /// Legend / interpretation lines rendered under the table.
    Notes: string list
}

type ResultChart = {
    Title: string
    /// Plotly figure JSON ({"data": …, "layout": …}), rendered client-side.
    PlotlyFigureJson: string
}

type AnalysisSection = {
    Id: string
    Title: string
    Description: string
    Charts: ResultChart list
    Tables: ResultTable list
}

type AnalysisReport = {
    Email: string
    GeneratedAt: DateTimeOffset
    RideCount: int
    DateRange: DateTimeOffset * DateTimeOffset
    Sections: AnalysisSection list
}
