"""/regression/wls endpoint: weights required (422 without it, 400 when the
column is absent from rows). /regression/ols is untouched — same request
works there without weights (guards the no-branching separation)."""

from fastapi.testclient import TestClient

from analytics.api.main import app

client = TestClient(app)

ROWS = [
    {"y": 30.0 + 8.0 * (i % 10) / 10.0 - 5.0 * (i % 2), "x": (i % 10) / 10.0,
     "flag": bool(i % 2), "w": 0.5 + (i % 5) / 10.0}
    for i in range(60)
]


def test_wls_happy_path():
    response = client.post("/regression/wls", json={
        "rows": ROWS, "target": "y", "weights": "w", "standardize": False})
    assert response.status_code == 200
    body = response.json()
    assert body["n_observations"] == 60
    names = [c["name"] for c in body["coefficients"]]
    assert "const" in names and "x" in names
    assert "w" not in names


def test_wls_without_weights_field_is_422():
    response = client.post("/regression/wls", json={
        "rows": ROWS, "target": "y", "standardize": False})
    assert response.status_code == 422


def test_wls_missing_weights_column_is_400():
    rows = [{k: v for k, v in row.items() if k != "w"} for row in ROWS]
    response = client.post("/regression/wls", json={
        "rows": rows, "target": "y", "weights": "w", "standardize": False})
    assert response.status_code == 400
    assert "weights" in response.json()["detail"]


def test_ols_endpoint_unchanged_no_weights_needed():
    rows = [{k: v for k, v in row.items() if k != "w"} for row in ROWS]
    response = client.post("/regression/ols", json={
        "rows": rows, "target": "y", "standardize": False})
    assert response.status_code == 200
    assert response.json()["n_observations"] == 60
