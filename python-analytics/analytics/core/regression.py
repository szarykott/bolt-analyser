"""OLS regression: categoricals one-hot encoded (drop_first), numeric features
optionally standardized, intercept added. Results come from model attributes,
not the summary() text."""

import pandas as pd
import statsmodels.api as sm

from analytics.core.sanitize import finite_or_none


def run_ols(df: pd.DataFrame, target: str, drop_columns: list[str] = (),
            categorical_columns: list[str] | None = None,
            standardize: bool = True) -> dict:
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

    model = sm.OLS(y, X).fit()
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
