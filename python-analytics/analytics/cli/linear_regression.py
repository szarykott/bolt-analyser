"""OLS regression over a ';' delimited CSV: one target column, remaining
columns are features (categoricals one-hot encoded, numerics standardized)."""

import argparse
from pathlib import Path

import pandas as pd

from analytics.core.regression import run_ols

DEFAULT_CSV = Path.home() / ".config/.bolt-app/ridesDataSource2.csv"


def parse_args():
    p = argparse.ArgumentParser(description="OLS regression over a ';' delimited CSV")
    p.add_argument("csv_path", type=Path, nargs="?", default=DEFAULT_CSV)
    p.add_argument("--target", default="price_pln")
    p.add_argument("--drop", nargs="*", default=[], help="extra columns to drop")
    p.add_argument("--no-standardize", action="store_true")
    return p.parse_args()


def main():
    args = parse_args()
    df = pd.read_csv(args.csv_path, delimiter=";")
    result = run_ols(df, target=args.target, drop_columns=args.drop,
                     standardize=not args.no_standardize)

    print(f"n={result['n_observations']}  "
          f"R²={result['r_squared']:.4f}  adj R²={result['adj_r_squared']:.4f}  "
          f"F={result['f_statistic']:.2f} (p={result['f_pvalue']:.3g})")
    table = pd.DataFrame(result["coefficients"]).set_index("name")
    with pd.option_context("display.max_columns", None, "display.width", None):
        print(table)


if __name__ == "__main__":
    main()
