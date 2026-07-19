namespace Bolt.ETL.Analytics

// DTOs for the python analytics service. Field names go over the wire as
// snake_case via [<JsonPropertyName>] (FSharp.SystemTextJson honors these on
// record fields; only [<JsonConverter>] is ignored, per Serialization.fs).
// Request rows are Map<string, obj>: boxed float/bool/string serialize by
// runtime type, culture-safe. Nullable response floats are float option
// (deserializeNullAsNone is set globally).

open System.Text.Json.Serialization

type StPoint = {
    [<JsonPropertyName "latitude">] Latitude: float
    [<JsonPropertyName "longitude">] Longitude: float
    [<JsonPropertyName "hour">] Hour: float
}

type StDbscanRequest = {
    [<JsonPropertyName "points">] Points: StPoint array
    [<JsonPropertyName "eps_km">] EpsKm: float
    [<JsonPropertyName "eps_hours">] EpsHours: float
    [<JsonPropertyName "min_samples">] MinSamples: int
}

type ClusterStats = {
    [<JsonPropertyName "id">] Id: int
    [<JsonPropertyName "size">] Size: int
    [<JsonPropertyName "centroid_latitude">] CentroidLatitude: float
    [<JsonPropertyName "centroid_longitude">] CentroidLongitude: float
    [<JsonPropertyName "mean_hour">] MeanHour: float
}

type StDbscanResponse = {
    [<JsonPropertyName "labels">] Labels: int array
    [<JsonPropertyName "n_points">] NPoints: int
    [<JsonPropertyName "n_clusters">] NClusters: int
    [<JsonPropertyName "n_noise">] NNoise: int
    [<JsonPropertyName "clusters">] Clusters: ClusterStats array
}

type OlsRequest = {
    [<JsonPropertyName "rows">] Rows: Map<string, obj> array
    [<JsonPropertyName "target">] Target: string
    [<JsonPropertyName "drop_columns">] DropColumns: string array
    [<JsonPropertyName "categorical_columns">] CategoricalColumns: string array option
    [<JsonPropertyName "standardize">] Standardize: bool
}

/// Same shape as OlsRequest plus the required weights column name; posts to
/// the separate /regression/wls endpoint (weights are never optional there).
type WlsRequest = {
    [<JsonPropertyName "rows">] Rows: Map<string, obj> array
    [<JsonPropertyName "target">] Target: string
    [<JsonPropertyName "weights">] Weights: string
    [<JsonPropertyName "drop_columns">] DropColumns: string array
    [<JsonPropertyName "categorical_columns">] CategoricalColumns: string array option
    [<JsonPropertyName "standardize">] Standardize: bool
}

type Coefficient = {
    [<JsonPropertyName "name">] Name: string
    [<JsonPropertyName "coef">] Coef: float option
    [<JsonPropertyName "std_err">] StdErr: float option
    [<JsonPropertyName "t_value">] TValue: float option
    [<JsonPropertyName "p_value">] PValue: float option
    [<JsonPropertyName "ci_low">] CiLow: float option
    [<JsonPropertyName "ci_high">] CiHigh: float option
}

type OlsResponse = {
    [<JsonPropertyName "n_observations">] NObservations: int
    [<JsonPropertyName "r_squared">] RSquared: float option
    [<JsonPropertyName "adj_r_squared">] AdjRSquared: float option
    [<JsonPropertyName "f_statistic">] FStatistic: float option
    [<JsonPropertyName "f_pvalue">] FPvalue: float option
    [<JsonPropertyName "coefficients">] Coefficients: Coefficient array
}

type GroupMeansSpec = {
    [<JsonPropertyName "by">] By: string
    [<JsonPropertyName "value">] Value: string
}

type MirrorCheckRequest = {
    [<JsonPropertyName "rows">] Rows: Map<string, obj> array
    [<JsonPropertyName "target_columns">] TargetColumns: string array
    [<JsonPropertyName "group_means">] GroupMeans: GroupMeansSpec option
}

type CorrelationMatrix = {
    [<JsonPropertyName "columns">] Columns: string array
    [<JsonPropertyName "matrix">] Matrix: float option array array
}

type VifEntry = {
    [<JsonPropertyName "feature">] Feature: string
    [<JsonPropertyName "vif">] Vif: float option
}

type GroupMean = {
    [<JsonPropertyName "group">] Group: string
    [<JsonPropertyName "mean">] Mean: float option
}

type MirrorCheckResponse = {
    [<JsonPropertyName "numeric_correlation">] NumericCorrelation: CorrelationMatrix
    [<JsonPropertyName "encoded_correlation">] EncodedCorrelation: CorrelationMatrix
    [<JsonPropertyName "vif">] Vif: VifEntry array
    [<JsonPropertyName "group_means">] GroupMeans: GroupMean array option
}
