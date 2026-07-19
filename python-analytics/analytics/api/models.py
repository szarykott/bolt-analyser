"""Request/response models. Row values use a str | bool | float union —
pydantic's smart mode keeps bool distinct from float, so false != 0.0."""

from pydantic import BaseModel

RowValue = str | bool | float | None
Row = dict[str, RowValue]


class Point(BaseModel):
    latitude: float
    longitude: float
    hour: float  # fractional hour-of-day, e.g. 14.5 == 14:30


class StDbscanRequest(BaseModel):
    points: list[Point]
    eps_km: float = 0.5
    eps_hours: float = 0.5
    min_samples: int = 5


class ClusterStats(BaseModel):
    id: int
    size: int
    centroid_latitude: float
    centroid_longitude: float
    mean_hour: float


class StDbscanResponse(BaseModel):
    labels: list[int]  # index-aligned with request points, -1 = noise
    n_points: int
    n_clusters: int
    n_noise: int
    clusters: list[ClusterStats]


class OlsRequest(BaseModel):
    rows: list[Row]
    target: str
    drop_columns: list[str] = []
    categorical_columns: list[str] | None = None
    standardize: bool = True


class WlsRequest(OlsRequest):
    weights: str  # required: name of the weights column inside rows


class Coefficient(BaseModel):
    name: str
    coef: float | None
    std_err: float | None
    t_value: float | None
    p_value: float | None
    ci_low: float | None
    ci_high: float | None


class OlsResponse(BaseModel):
    n_observations: int
    r_squared: float | None
    adj_r_squared: float | None
    f_statistic: float | None
    f_pvalue: float | None
    coefficients: list[Coefficient]


class GroupMeansSpec(BaseModel):
    by: str
    value: str


class MirrorCheckRequest(BaseModel):
    rows: list[Row]
    target_columns: list[str]
    group_means: GroupMeansSpec | None = None


class CorrelationMatrix(BaseModel):
    columns: list[str]
    matrix: list[list[float | None]]


class VifEntry(BaseModel):
    feature: str
    vif: float | None


class GroupMean(BaseModel):
    group: str
    mean: float | None


class MirrorCheckResponse(BaseModel):
    numeric_correlation: CorrelationMatrix
    encoded_correlation: CorrelationMatrix
    vif: list[VifEntry]
    group_means: list[GroupMean] | None
