"""Multicollinearity 'mirror check': numeric Pearson correlations, one-hot
encoded correlations (catches 0/1 vs numeric mirrors), VIF per encoded column,
optional group means for eyeballing multi-level category vs numeric."""

import pandas as pd
from statsmodels.stats.outliers_influence import variance_inflation_factor

from analytics.core.sanitize import finite_or_none, matrix_finite_or_none


def _correlation(df: pd.DataFrame, numeric_only: bool = False) -> dict:
    corr = df.corr(numeric_only=numeric_only)
    return {
        "columns": corr.columns.tolist(),
        "matrix": matrix_finite_or_none(corr.to_numpy()),
    }


def run_mirror_check(df: pd.DataFrame, target_columns: list[str],
                     group_by: str | None = None,
                     group_value: str | None = None) -> dict:
    missing = [c for c in target_columns if c not in df.columns]
    if missing:
        raise ValueError(f"target_columns not in data: {missing}")

    feat = df.drop(columns=target_columns)
    enc = pd.get_dummies(feat, drop_first=True).astype(float)

    vif = [
        {"feature": column, "vif": finite_or_none(variance_inflation_factor(enc.values, i))}
        for i, column in enumerate(enc.columns)
    ]
    vif.sort(key=lambda entry: (entry["vif"] is not None, entry["vif"] or 0.0), reverse=True)

    group_means = None
    if group_by is not None:
        if group_value is None:
            raise ValueError("group_means requires both 'by' and 'value'")
        missing = [c for c in (group_by, group_value) if c not in df.columns]
        if missing:
            raise ValueError(f"group_means columns not in data: {missing}")
        means = df.groupby(group_by)[group_value].mean()
        group_means = [
            {"group": str(group), "mean": finite_or_none(mean)}
            for group, mean in means.items()
        ]

    return {
        "numeric_correlation": _correlation(feat, numeric_only=True),
        "encoded_correlation": _correlation(enc),
        "vif": vif,
        "group_means": group_means,
    }
