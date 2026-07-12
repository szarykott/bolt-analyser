"""ST-DBSCAN over lat/lon + hour-of-day.

Two independent radii: spatial (eps_km, haversine) and temporal (eps_hours,
cyclic over midnight). Implemented as sklearn DBSCAN over a precomputed matrix
where pairs outside the temporal radius get a sentinel distance that can never
be within eps. Dense O(n^2) matrix - fine below ~30k rows.
"""

import numpy as np
from sklearn.cluster import DBSCAN

EARTH_RADIUS_KM = 6371.0
UNREACHABLE_KM = 1e12  # sentinel: farther than any eps, keeps matrix finite


def st_distance_matrix(lat_deg, lon_deg, hours, eps_hours):
    lat = np.radians(lat_deg)[:, None]
    lon = np.radians(lon_deg)[:, None]
    dlat = lat - lat.T
    dlon = lon - lon.T
    a = np.sin(dlat / 2) ** 2 + np.cos(lat) * np.cos(lat.T) * np.sin(dlon / 2) ** 2
    spatial_km = 2 * EARTH_RADIUS_KM * np.arcsin(np.sqrt(np.clip(a, 0, 1)))

    dt = np.abs(hours[:, None] - hours[None, :])
    dt = np.minimum(dt, 24.0 - dt)  # cyclic: 23:50 vs 00:10 = 20 min

    spatial_km[dt > eps_hours] = UNREACHABLE_KM
    return spatial_km


def circular_mean_hour(hours):
    ang = hours / 24.0 * 2 * np.pi
    mean = np.arctan2(np.sin(ang).mean(), np.cos(ang).mean())
    return (mean * 24.0 / (2 * np.pi)) % 24.0


def run_st_dbscan(latitudes, longitudes, hours,
                  eps_km: float = 0.5, eps_hours: float = 0.5,
                  min_samples: int = 5) -> dict:
    latitudes = np.asarray(latitudes, dtype=float)
    longitudes = np.asarray(longitudes, dtype=float)
    hours = np.asarray(hours, dtype=float)
    if not (len(latitudes) == len(longitudes) == len(hours)):
        raise ValueError("latitudes, longitudes and hours must have equal length")
    if len(latitudes) == 0:
        raise ValueError("no points given")

    dist = st_distance_matrix(latitudes, longitudes, hours, eps_hours)
    labels = DBSCAN(eps=eps_km, min_samples=min_samples,
                    metric="precomputed").fit(dist).labels_

    cluster_ids = sorted(set(labels) - {-1})
    clusters = []
    for cid in cluster_ids:
        mask = labels == cid
        clusters.append({
            "id": int(cid),
            "size": int(mask.sum()),
            "centroid_latitude": float(latitudes[mask].mean()),
            "centroid_longitude": float(longitudes[mask].mean()),
            "mean_hour": float(circular_mean_hour(hours[mask])),
        })

    return {
        "labels": [int(l) for l in labels],
        "n_points": int(len(labels)),
        "n_clusters": len(cluster_ids),
        "n_noise": int((labels == -1).sum()),
        "clusters": clusters,
    }
