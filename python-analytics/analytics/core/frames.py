"""Build DataFrames from JSON-shaped rows (list of dicts).

Dtype mapping mirrors what pd.read_csv produces on the existing CSV exports,
so service results match the CLI baseline: JSON numbers -> float64,
JSON bools -> bool (stays a single 0/1 column through get_dummies, exactly
like read_csv's bool inference), JSON strings -> object (one-hot encoded).
"""

import pandas as pd


def to_dataframe(rows: list[dict], categorical_columns: list[str] | None = None) -> pd.DataFrame:
    if not rows:
        raise ValueError("rows must not be empty")
    df = pd.DataFrame(rows)
    if categorical_columns:
        missing = [c for c in categorical_columns if c not in df.columns]
        if missing:
            raise ValueError(f"categorical_columns not in data: {missing}")
        for column in categorical_columns:
            df[column] = df[column].astype(str)
    return df
