namespace Bolt.Web.Views

open Giraffe.ViewEngine

type ResultTable = {
    Title: string
    Headers: string list
    Rows: string list list
    /// Legend / interpretation lines rendered under the table.
    Notes: string list
}

module ResultTable =
    let asHtml (t: ResultTable) = [
        h4 [] [ str t.Title ]
        table [] [
            thead [] [ tr [] [ for h in t.Headers -> th [] [ str h ] ] ]
            tbody [] [ for r in t.Rows -> tr [] [ for c in r -> td [] [ str c ] ] ]
        ]
        if not (List.isEmpty t.Notes) then
            ul [ _class "notes" ] [ for n in t.Notes -> li [] [ str n ] ]
    ]