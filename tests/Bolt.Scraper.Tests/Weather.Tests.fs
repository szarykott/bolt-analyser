module Bolt.Scraper.Tests.WeatherTests

open System
open Xunit
open Bolt.Models.Geo
open Bolt.Models.Meteo

let private center: GeoPoint = { Latitude = 50.06; Longitude = 19.92 }
let private point t r s = { Temperature = t * 1.0<celcius>; Rain = r * 1.0<mm>; Snow = s * 1.0<cm> }
let private at (iso: string) = DateTimeOffset.Parse iso

let private weather (points: (string * WeatherDataPoint) list) =
    Weather.create center (points |> List.map (fun (d, p) -> at d, p))

[<Fact>]
let ``hourRange returns min and max covered hours`` () =
    let w = weather [ "2026-01-01T05:00Z", point 1.0 0.0 0.0; "2026-01-03T15:00Z", point 2.0 0.0 0.0 ]
    Assert.Equal(Some(at "2026-01-01T05:00Z", at "2026-01-03T15:00Z"), Weather.hourRange w)

[<Fact>]
let ``hourRange is None for empty weather`` () =
    Assert.Equal(None, Weather.hourRange (Weather.create center []))

[<Fact>]
let ``merge keeps both sides and right wins collisions`` () =
    let a = weather [ "2026-01-01T05:00Z", point 1.0 0.0 0.0; "2026-01-01T06:00Z", point 2.0 0.0 0.0 ]
    let b = weather [ "2026-01-01T06:00Z", point 9.0 0.0 0.0; "2026-01-01T07:00Z", point 3.0 0.0 0.0 ]
    let m = Weather.merge a b
    Assert.Equal(point 1.0 0.0 0.0, Weather.getDataPoint m (at "2026-01-01T05:00Z"))
    Assert.Equal(point 9.0 0.0 0.0, Weather.getDataPoint m (at "2026-01-01T06:00Z"))
    Assert.Equal(point 3.0 0.0 0.0, Weather.getDataPoint m (at "2026-01-01T07:00Z"))

[<Fact>]
let ``getNearestDataPoint falls back to closest hour`` () =
    let w = weather [ "2026-01-01T05:00Z", point 1.0 0.0 0.0 ]
    Assert.Equal(Some(point 1.0 0.0 0.0), Weather.getNearestDataPoint w (at "2026-01-05T23:00Z"))

[<Fact>]
let ``getNearestDataPoint is None for empty weather`` () =
    Assert.Equal(None, Weather.getNearestDataPoint (Weather.create center []) DateTimeOffset.UtcNow)
