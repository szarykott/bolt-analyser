module Bolt.ETL.Tests.Geo.DistrictAssignmentTests

open Xunit
open Bolt.Models.Geo.District
open Bolt.Models.Geo
open Bolt.ETL.Geo.DistrictAssignment

// Helpers

let private pt lat lon : Point = { Latitude = lat; Longitude = lon }

let private district name coords = create name coords

// Unit square: (lat 0..1, lon 0..1), wound counter-clockwise
//
//   (1,0)----(1,1)
//     |        |
//   (0,0)----(0,1)
let private unitSquare =
    district "Square" [|
        pt 0.0f 0.0f
        pt 0.0f 1.0f
        pt 1.0f 1.0f
        pt 1.0f 0.0f
        pt 0.0f 0.0f   // closed ring
    |]

// Triangle with vertices (0,0) (0,4) (4,0)
let private triangle =
    district "Triangle" [|
        pt 0.0f 0.0f
        pt 0.0f 4.0f
        pt 4.0f 0.0f
        pt 0.0f 0.0f
    |]

// Two non-overlapping squares side by side on the longitude axis:
//   Left  : lat 0..1, lon 0..1
//   Right : lat 0..1, lon 2..3
let private leftSquare =
    district "Left" [|
        pt 0.0f 0.0f
        pt 0.0f 1.0f
        pt 1.0f 1.0f
        pt 1.0f 0.0f
        pt 0.0f 0.0f
    |]

let private rightSquare =
    district "Right" [|
        pt 0.0f 2.0f
        pt 0.0f 3.0f
        pt 1.0f 3.0f
        pt 1.0f 2.0f
        pt 0.0f 2.0f
    |]

// --- Single district ---

[<Fact>]
let ``point clearly inside unit square returns Some`` () =
    let result = assignCoordinatesToDistrict [ unitSquare ] (pt 0.5f 0.5f)
    Assert.Equal(Some unitSquare, result)

[<Fact>]
let ``point clearly outside unit square returns None`` () =
    let result = assignCoordinatesToDistrict [ unitSquare ] (pt 1.5f 1.5f)
    Assert.Equal(None, result)

[<Fact>]
let ``point at origin corner returns None when no district matches`` () =
    let result = assignCoordinatesToDistrict [] (pt 0.5f 0.5f)
    Assert.Equal(None, result)

// --- Triangle ---

[<Fact>]
let ``point inside triangle returns Some`` () =
    let result = assignCoordinatesToDistrict [ triangle ] (pt 0.5f 0.5f)
    Assert.Equal(Some triangle, result)

[<Fact>]
let ``point outside triangle (opposite side of hypotenuse) returns None`` () =
    // (3,3) is above the hypotenuse lat+lon=4
    let result = assignCoordinatesToDistrict [ triangle ] (pt 3.0f 3.0f)
    Assert.Equal(None, result)

// --- Multiple districts ---

[<Fact>]
let ``point in left square is assigned to left square`` () =
    let result = assignCoordinatesToDistrict [ leftSquare; rightSquare ] (pt 0.5f 0.5f)
    Assert.Equal(Some leftSquare, result)

[<Fact>]
let ``point in right square is assigned to right square`` () =
    let result = assignCoordinatesToDistrict [ leftSquare; rightSquare ] (pt 0.5f 2.5f)
    Assert.Equal(Some rightSquare, result)

[<Fact>]
let ``point between the two squares returns None`` () =
    // lon 1.5 is in the gap between left (0..1) and right (2..3)
    let result = assignCoordinatesToDistrict [ leftSquare; rightSquare ] (pt 0.5f 1.5f)
    Assert.Equal(None, result)
