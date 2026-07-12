"""Multicollinearity mirror check over a ';' delimited CSV: numeric and
one-hot encoded correlations, VIF, optional group means."""

import argparse
from pathlib import Path

import pandas as pd

from analytics.core.diagnostics import run_mirror_check

DEFAULT_CSV = Path.home() / ".config/.bolt-app/ridesDataSource2.csv"


def parse_args():
    p = argparse.ArgumentParser(description="multicollinearity mirror check")
    p.add_argument("csv_path", type=Path, nargs="?", default=DEFAULT_CSV)
    p.add_argument("--targets", nargs="+", default=["price_pln"])
    p.add_argument("--group-by", default="pickup_district")
    p.add_argument("--group-value", default="distance_km")
    return p.parse_args()


def _print_matrix(corr: dict):
    frame = pd.DataFrame(corr["matrix"], index=corr["columns"], columns=corr["columns"])
    print(frame)


def main():
    args = parse_args()
    pd.set_option("display.max_columns", None, "display.width", None)
    df = pd.read_csv(args.csv_path, delimiter=";")
    result = run_mirror_check(df, target_columns=args.targets,
                              group_by=args.group_by, group_value=args.group_value)

    _print_matrix(result["numeric_correlation"])
    _print_matrix(result["encoded_correlation"])
    print(pd.DataFrame(result["vif"]))
    if result["group_means"] is not None:
        print(pd.DataFrame(result["group_means"]).set_index("group"))


if __name__ == "__main__":
    main()
