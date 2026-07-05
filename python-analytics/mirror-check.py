import pandas as pd
from statsmodels.stats.outliers_influence import variance_inflation_factor

df = pd.read_csv("~/.config/.bolt-app/ridesDataSource.csv", delimiter=';')            # or hours.csv
pd.set_option("display.max_columns", None, "display.width", None)
targets = ["price_pln", "price_per_km"]
feat = df.drop(columns=targets)

# 2a — numeric mirrors (Pearson)
print(feat.corr(numeric_only=True))

# 2b — categorical mirrors: one-hot, then corr (catches 0/1 vs numeric)
enc = pd.get_dummies(feat, drop_first=True).astype(float)
print(enc.corr())

# 2c — VIF: catch-all, each column vs all others (numeric + dummy)
vif = pd.DataFrame({
    "feature": enc.columns,
    "VIF": [variance_inflation_factor(enc.values, i) for i in range(enc.shape[1])]
}).sort_values("VIF", ascending=False)
print(vif)

# 2d — multi-level category vs numeric (eyeball group means)
print(df.groupby("pickup_district")["distance_km"].mean())