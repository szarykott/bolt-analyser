"""FastAPI statistical service: clustering, OLS regression, multicollinearity
diagnostics. Structured JSON only — plotting stays a CLI concern."""

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from analytics.api.models import (
    MirrorCheckRequest,
    MirrorCheckResponse,
    OlsRequest,
    OlsResponse,
    StDbscanRequest,
    StDbscanResponse,
)
from analytics.core.clustering import run_st_dbscan
from analytics.core.diagnostics import run_mirror_check
from analytics.core.frames import to_dataframe
from analytics.core.regression import run_ols

app = FastAPI(title="Bolt Analytics")


@app.exception_handler(ValueError)
async def value_error_handler(_: Request, exc: ValueError) -> JSONResponse:
    return JSONResponse(status_code=400, content={"detail": str(exc)})


@app.get("/health")
def health() -> dict:
    return {"status": "ok"}


@app.post("/cluster/st-dbscan")
def cluster_st_dbscan(request: StDbscanRequest) -> StDbscanResponse:
    result = run_st_dbscan(
        [p.latitude for p in request.points],
        [p.longitude for p in request.points],
        [p.hour for p in request.points],
        eps_km=request.eps_km,
        eps_hours=request.eps_hours,
        min_samples=request.min_samples,
    )
    return StDbscanResponse(**result)


@app.post("/regression/ols")
def regression_ols(request: OlsRequest) -> OlsResponse:
    df = to_dataframe(request.rows)
    result = run_ols(df, target=request.target, drop_columns=request.drop_columns,
                     categorical_columns=request.categorical_columns,
                     standardize=request.standardize)
    return OlsResponse(**result)


@app.post("/diagnostics/mirror-check")
def diagnostics_mirror_check(request: MirrorCheckRequest) -> MirrorCheckResponse:
    df = to_dataframe(request.rows)
    result = run_mirror_check(
        df,
        target_columns=request.target_columns,
        group_by=request.group_means.by if request.group_means else None,
        group_value=request.group_means.value if request.group_means else None,
    )
    return MirrorCheckResponse(**result)
