module Bolt.Web.Views.Shared

open Giraffe.ViewEngine

let render = RenderView.AsString.htmlNode
let panel = div [ _id "panel" ]
