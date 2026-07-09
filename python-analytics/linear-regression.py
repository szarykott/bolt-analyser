import pandas as pd
import statsmodels.api as sm

df = pd.read_csv("~/.config/.bolt-app/ridesDataSource2.csv", delimiter=';')
y = df["price_pln"]                                # one target per run
drop = ["price_pln"]                            # + mirrors removed in STEP 2
X = df.drop(columns=drop)

X = pd.get_dummies(X, drop_first=True).astype(float)    # categoricals → 0/1
num = ["distance_km"]                       # numeric cols only
X[num] = (X[num] - X[num].mean()) / X[num].std()         # standardize → comparable
X = sm.add_constant(X)                                   # intercept b0

model = sm.OLS(y, X).fit()
print(model.summary())