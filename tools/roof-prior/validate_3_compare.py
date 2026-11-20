"""
Validation 3/3: the prior's flat-roof odds (same lookup as RoofPrior.cs) against 3DBAG, by cell, size and height,
for the buildings without a mapped roof shape.
    venv/bin/python validate_3_compare.py roof-prior.bin work/joined.csv
"""
import csv, gzip, struct, sys
from collections import defaultdict
import numpy as np

prior_path, sample_path = sys.argv[1], sys.argv[2]

# --- read the prior like RoofPrior.cs does
with gzip.open(prior_path) as f:
    data = f.read()
assert data[:4] == b"DRP1"
sizes, heights, uses, kinds = data[4:8]
strata = sizes * heights * uses
sb = strata * kinds
o = 8
cols, rows, deg = struct.unpack_from("<HHf", data, o); o += 8
climate = np.frombuffer(data, np.uint8, cols * rows, o).reshape(rows, cols); o += cols * rows
classes = data[o]; o += 1
class_odds = np.frombuffer(data, np.uint8, classes * sb, o).reshape(classes, strata, kinds); o += classes * sb
levels = []
nlev = data[o]; o += 1
for _ in range(nlev):
    ldeg, count = struct.unpack_from("<fI", data, o); o += 8
    lc = round(360 / ldeg)
    cells = {}
    for i in range(count):
        idx, = struct.unpack_from("<I", data, o); o += 4
        cells[idx] = np.frombuffer(data, np.uint8, sb, o).reshape(strata, kinds); o += sb
    levels.append((ldeg, lc, cells))


def odds(lon, lat, s):
    for ldeg, lc, cells in levels:
        lr = round(180 / ldeg)
        x = min(lc - 1, int((lon + 180) // ldeg)); y = min(lr - 1, int((90 - lat) // ldeg))
        c = cells.get(y * lc + x)
        if c is not None:
            return c[s], f"{ldeg}°"
    x = min(cols - 1, int((lon + 180) // deg)); y = min(rows - 1, int((90 - lat) // deg))
    return class_odds[climate[y, x]][s], "climate"


def stratum(area, h):
    size = 0 if area < 50 else 1 if area < 150 else 2 if area < 300 else 3 if area < 1000 else 4
    hc = 0 if h is None else 1 if h <= 8 else 2 if h <= 15 else 3 if h <= 30 else 4
    return (size * heights + hc) * uses


NONRES = {"commercial", "industrial", "retail", "warehouse", "office", "supermarket", "service", "garage", "garages",
          "parking", "carport", "hangar", "factory", "manufacture", "hospital", "school", "university", "college",
          "train_station", "transportation", "stadium"}


def runtime_stratum(r):
    area = float(r["area"])
    h = float(r["height"]) if r["height"] else (int(r["num_floors"]) * 3 if r["num_floors"] else None)
    size = 0 if area < 50 else 1 if area < 150 else 2 if area < 300 else 3 if area < 1000 else 4
    hc = 0 if h is None else 1 if h <= 8 else 2 if h <= 15 else 3 if h <= 30 else 4
    return (size * heights + hc) * uses + (1 if r["class"] in NONRES else 0), size, hc


per_cell = defaultdict(lambda: [0, 0.0, 0, ""])
per_size = defaultdict(lambda: [0, 0.0, 0])
per_h = defaultdict(lambda: [0, 0.0, 0])
mapped = [0, 0]
for r in csv.DictReader(open(sample_path)):
    if r["roof"] not in ("slanted", "horizontal", "multiple horizontal"):
        continue
    truth = r["roof"] != "slanted"
    if r["roof_shape"]:
        mapped[0] += 1; mapped[1] += (r["roof_shape"] == "flat") == truth
        continue
    lon = (int(r["cx"]) + 0.5) / 4 - 180; lat = (int(r["cy"]) + 0.5) / 4 - 90
    s, size, hc = runtime_stratum(r)
    o5, level = odds(lon, lat, s)
    p = o5[0] / max(1, int(o5.sum()))
    for d, key in ((per_cell, r["cell"]), (per_size, size), (per_h, hc)):
        d[key][0] += 1; d[key][1] += p; d[key][2] += truth
    per_cell[r["cell"]][3] = level

print(f"mapped roof shapes: {mapped[0]}, flat/pitched agrees with the truth for {100 * mapped[1] / max(1, mapped[0]):.0f}%")
print(f"{'cell':18} {'n':>6} {'level':>8} {'prior flat':>11} {'truth flat':>11}")
for k, (n, p, t, lv) in per_cell.items():
    print(f"{k:18} {n:6} {lv:>8} {100 * p / n:10.1f}% {100 * t / n:10.1f}%")
names = ["< 50 m²", "50-150", "150-300", "300-1000", ">= 1000"]
print(f"\n{'size':18} {'n':>6} {'prior flat':>11} {'truth flat':>11}")
for k in sorted(per_size):
    n, p, t = per_size[k]; print(f"{names[k]:18} {n:6} {100 * p / n:10.1f}% {100 * t / n:10.1f}%")
hn = ["unknown", "<= 8 m", "<= 15 m", "<= 30 m", "> 30 m"]
print(f"\n{'height (Overture)':18} {'n':>6} {'prior flat':>11} {'truth flat':>11}")
for k in sorted(per_h):
    n, p, t = per_h[k]; print(f"{hn[k]:18} {n:6} {100 * p / n:10.1f}% {100 * t / n:10.1f}%")
N = sum(v[0] for v in per_cell.values())
print(f"\nall {N}: prior {100 * sum(v[1] for v in per_cell.values()) / N:.1f}% flat, truth {100 * sum(v[2] for v in per_cell.values()) / N:.1f}% flat")
