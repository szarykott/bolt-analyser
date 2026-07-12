"""ST-DBSCAN clustering of ride events in space + hour-of-day.

Input CSV (';' delimited): latitude, longitude (decimal degrees), time (HH:MM).

Outputs: <input stem>-clustered.csv (input + cluster column, -1 = noise),
<input stem>-clusters.png, console summary.
"""

import argparse
import sys
from pathlib import Path

import matplotlib
import pandas as pd

matplotlib.use("Agg")
import matplotlib.pyplot as plt

from analytics.core.clustering import circular_mean_hour, run_st_dbscan

# dataviz palette (light mode, validated): 8 fixed categorical slots
SERIES_COLORS = ["#2a78d6", "#1baf7a", "#eda100", "#008300",
                 "#4a3aa7", "#e34948", "#e87ba4", "#eb6834"]
NOISE_COLOR = "#8a8984"
SURFACE = "#fcfcfb"
INK_PRIMARY = "#1a1a19"
INK_SECONDARY = "#5f5e58"


def parse_args():
    p = argparse.ArgumentParser(description="ST-DBSCAN over lat/lon + hour-of-day")
    p.add_argument("csv_path", type=Path, help="input CSV (';' delimited)")
    p.add_argument("--eps-km", type=float, default=0.5, help="spatial radius, km")
    p.add_argument("--eps-hours", type=float, default=0.5,
                   help="temporal radius, hours (cyclic over midnight)")
    p.add_argument("--min-samples", type=int, default=5)
    p.add_argument("--out", type=Path, default=None,
                   help="output CSV path (default: <input stem>-clustered.csv)")
    return p.parse_args()


def load(csv_path: Path) -> pd.DataFrame:
    df = pd.read_csv(csv_path, delimiter=";")
    required = ["latitude", "longitude", "time"]
    missing = [c for c in required if c not in df.columns]
    if missing:
        sys.exit(f"error: missing columns {missing}, found {list(df.columns)}")

    df["latitude"] = pd.to_numeric(df["latitude"], errors="coerce")
    df["longitude"] = pd.to_numeric(df["longitude"], errors="coerce")
    parsed = pd.to_datetime(df["time"], format="%H:%M", errors="coerce")
    df["hour"] = parsed.dt.hour + parsed.dt.minute / 60.0

    bad = df[["latitude", "longitude", "hour"]].isna().any(axis=1)
    if bad.any():
        print(f"warning: dropped {bad.sum()} row(s) with unparseable values")
        df = df[~bad].reset_index(drop=True)
    if df.empty:
        sys.exit("error: no valid rows after parsing")
    return df


def summarize(df):
    labels = df["cluster"]
    ids = sorted(set(labels) - {-1})
    noise = int((labels == -1).sum())
    print(f"\nclusters: {len(ids)}   noise: {noise}/{len(df)} "
          f"({100 * noise / len(df):.1f}%)")
    for cid in ids:
        c = df[labels == cid]
        h = circular_mean_hour(c["hour"].to_numpy())
        minutes = int(round(h * 60)) % (24 * 60)
        print(f"  cluster {cid}: {len(c):4d} pts   "
              f"centroid ({c['latitude'].mean():.5f}, {c['longitude'].mean():.5f})   "
              f"~{minutes // 60:02d}:{minutes % 60:02d}")


def plot(df, png_path: Path):
    fig, ax = plt.subplots(figsize=(9, 7), facecolor=SURFACE)
    ax.set_facecolor(SURFACE)

    noise = df[df["cluster"] == -1]
    ax.scatter(noise["longitude"], noise["latitude"], s=14, c=NOISE_COLOR,
               alpha=0.5, linewidths=0, label=f"noise ({len(noise)})")

    # fixed slot order by cluster size; 9th+ fold into "other" (never cycle hues)
    ids = sorted(set(df["cluster"]) - {-1},
                 key=lambda cid: -(df["cluster"] == cid).sum())
    other = pd.DataFrame()
    for rank, cid in enumerate(ids):
        c = df[df["cluster"] == cid]
        if rank >= len(SERIES_COLORS):
            other = pd.concat([other, c])
            continue
        ax.scatter(c["longitude"], c["latitude"], s=22,
                   c=SERIES_COLORS[rank], linewidths=0,
                   label=f"cluster {cid} ({len(c)})")
        # direct label at centroid: relief for low-contrast palette slots
        ax.annotate(str(cid), (c["longitude"].mean(), c["latitude"].mean()),
                    color=INK_PRIMARY, fontsize=10, fontweight="bold",
                    ha="center", va="center",
                    bbox=dict(boxstyle="circle,pad=0.25", fc=SURFACE,
                              ec=INK_SECONDARY, lw=0.5, alpha=0.85))
    if not other.empty:
        ax.scatter(other["longitude"], other["latitude"], s=14, c=INK_SECONDARY,
                   alpha=0.6, linewidths=0,
                   label=f"other {len(ids) - len(SERIES_COLORS)} clusters")

    ax.set_xlabel("longitude", color=INK_SECONDARY)
    ax.set_ylabel("latitude", color=INK_SECONDARY)
    ax.set_title("ST-DBSCAN ride clusters", color=INK_PRIMARY, loc="left")
    ax.tick_params(colors=INK_SECONDARY, labelsize=8)
    for spine in ax.spines.values():
        spine.set_visible(False)
    ax.grid(True, color="#e8e7e3", linewidth=0.5)
    ax.set_axisbelow(True)
    if len(ids) >= 1:
        ax.legend(loc="upper left", bbox_to_anchor=(1.01, 1), frameon=False,
                  fontsize=8, labelcolor=INK_PRIMARY)
    fig.tight_layout()
    fig.savefig(png_path, dpi=150, facecolor=SURFACE)
    print(f"plot: {png_path}")


def main():
    args = parse_args()
    df = load(args.csv_path)

    result = run_st_dbscan(df["latitude"].to_numpy(), df["longitude"].to_numpy(),
                           df["hour"].to_numpy(), eps_km=args.eps_km,
                           eps_hours=args.eps_hours, min_samples=args.min_samples)
    df["cluster"] = result["labels"]

    out = args.out or args.csv_path.with_name(args.csv_path.stem + "-clustered.csv")
    df.drop(columns=["hour"]).to_csv(out, sep=";", index=False)
    print(f"labeled csv: {out}")

    summarize(df)
    plot(df, args.csv_path.with_name(args.csv_path.stem + "-clusters.png"))


if __name__ == "__main__":
    main()
