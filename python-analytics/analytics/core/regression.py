"""OLS/WLS regression: categoricals one-hot encoded (drop_first), numeric
features optionally standardized, intercept added. Results come from model
attributes, not the summary() text. WLS is a separate entry point — weights
are required there and absent from OLS (no branching between the two)."""

import pandas as pd
import statsmodels.api as sm

from analytics.core.sanitize import finite_or_none


def _design(df: pd.DataFrame, target: str, drop_columns: list[str],
            categorical_columns: list[str] | None,
            standardize: bool) -> tuple[pd.Series, pd.DataFrame]:
    missing = [c for c in [target, *drop_columns] if c not in df.columns]
    if missing:
        raise ValueError(f"columns not in data: {missing}")

    if categorical_columns:
        df = df.copy()
        for column in categorical_columns:
            if column not in df.columns:
                raise ValueError(f"categorical_columns not in data: [{column!r}]")
            df[column] = df[column].astype(str)

    y = pd.to_numeric(df[target], errors="raise")
    X = df.drop(columns=[target, *drop_columns])
    numeric = X.select_dtypes(include="number").columns.tolist()  # bool excluded

    X = pd.get_dummies(X, drop_first=True).astype(float)  # categoricals -> 0/1
    if standardize and numeric:
        X[numeric] = (X[numeric] - X[numeric].mean()) / X[numeric].std()
    X = sm.add_constant(X)
    return y, X


def _fit_result(model) -> dict:
    conf_int = model.conf_int()
    coefficients = [
        {
            "name": name,
            "coef": finite_or_none(model.params[name]),
            "std_err": finite_or_none(model.bse[name]),
            "t_value": finite_or_none(model.tvalues[name]),
            "p_value": finite_or_none(model.pvalues[name]),
            "ci_low": finite_or_none(conf_int.loc[name, 0]),
            "ci_high": finite_or_none(conf_int.loc[name, 1]),
        }
        for name in model.params.index
    ]
    return {
        "n_observations": int(model.nobs),
        "r_squared": finite_or_none(model.rsquared),
        "adj_r_squared": finite_or_none(model.rsquared_adj),
        "f_statistic": finite_or_none(model.fvalue),
        "f_pvalue": finite_or_none(model.f_pvalue),
        "coefficients": coefficients,
    }


def run_ols(df: pd.DataFrame, target: str, drop_columns: list[str] = (),
            categorical_columns: list[str] | None = None,
            standardize: bool = True) -> dict:
    y, X = _design(df, target, drop_columns, categorical_columns, standardize)
    model = sm.OLS(y, X).fit()
    return _fit_result(model)


def run_wls(df: pd.DataFrame, target: str, weights: str,
            drop_columns: list[str] = (),
            categorical_columns: list[str] | None = None,
            standardize: bool = True) -> dict:
    if weights not in df.columns:
        raise ValueError(f"weights column not in data: [{weights!r}]")
    w = pd.to_numeric(df[weights], errors="raise")
    y, X = _design(df.drop(columns=[weights]), target, list(drop_columns),
                   categorical_columns, standardize)
    model = sm.WLS(y, X, weights=w).fit()
    return _fit_result(model)
