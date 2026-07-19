"""run_wls: separate from run_ols (no branching). Weights are required;
uniform weights reproduce OLS, non-uniform weights change the fit."""

import numpy as np
import pandas as pd
import pytest

from analytics.core.regression import run_ols, run_wls


def _frame() -> pd.DataFrame:
    rng = np.random.default_rng(42)
    n = 80
    x = rng.uniform(0, 1, n)
    flag = rng.integers(0, 2, n).astype(float)
    y = 30.0 + 8.0 * x - 5.0 * flag + rng.normal(0, 2, n)
    w = rng.uniform(0.2, 1.0, n)
    return pd.DataFrame({"y": y, "x": x, "flag": flag, "w": w})


def test_missing_weights_column_raises():
    df = _frame().drop(columns=["w"])
    with pytest.raises(ValueError, match="weights"):
        run_wls(df, target="y", weights="w", standardize=False)


def test_uniform_weights_match_ols():
    df = _frame()
    df["w"] = 1.0
    wls = run_wls(df, target="y", weights="w", standardize=False)
    ols = run_ols(df.drop(columns=["w"]), target="y", standardize=False)
    for c_wls, c_ols in zip(wls["coefficients"], ols["coefficients"]):
        assert c_wls["name"] == c_ols["name"]
        assert c_wls["coef"] == pytest.approx(c_ols["coef"], rel=1e-9)


def test_nonuniform_weights_differ_from_ols():
    df = _frame()
    wls = run_wls(df, target="y", weights="w", standardize=False)
    ols = run_ols(df.drop(columns=["w"]), target="y", standardize=False)
    coefs_wls = {c["name"]: c["coef"] for c in wls["coefficients"]}
    coefs_ols = {c["name"]: c["coef"] for c in ols["coefficients"]}
    assert coefs_wls != coefs_ols


def test_weights_column_stays_out_of_design_matrix():
    df = _frame()
    wls = run_wls(df, target="y", weights="w", standardize=False)
    names = [c["name"] for c in wls["coefficients"]]
    assert "w" not in names
    assert set(names) == {"const", "x", "flag"}
