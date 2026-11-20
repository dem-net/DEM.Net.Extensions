#!/usr/bin/env python3
"""
Builds the roof prior read by DEM.Net.Extension.Osm (RoofPrior, OsmElevationOptions.RoofPriorFilePath): for each
place on Earth and each kind of building, the odds of each roof shape. Buildings without a mapped roof:shape get
a roof drawn from these odds.

Source: the roof:shape of OpenStreetMap buildings, through the Overture Maps buildings release (about 9 million
tagged buildings out of 2.5 billion), counted per 0.25° cell and per stratum:
  size   footprint area: < 50, < 150, < 300, < 1000, >= 1000 m²
  height height, else floors x 3: unknown, <= 8, <= 15, <= 30, > 30 m
  use    residential or other, non-residential (commercial, industrial...: RoofPrior.NonResidentialTypes)
Sparse cells borrow from their 1° and 5° neighbourhoods, then from the average of their climate zone
(Köppen-Geiger 1991-2020, Beck et al. 2023, CC BY 4.0), so every land cell has odds.

The output is a database derived from OpenStreetMap: ODbL, credit "© OpenStreetMap contributors, Overture Maps
Foundation", and Beck et al. (2023) for the climate zones.

    python3 -m venv venv && venv/bin/pip install duckdb numpy
    # Köppen-Geiger: koppen_geiger_tif.zip from https://figshare.com/articles/dataset/21789074, then
    #   unzip koppen_geiger_tif.zip 1991_2020/koppen_geiger_0p1.tif
    #   gdal_translate -of ENVI 1991_2020/koppen_geiger_0p1.tif koppen_0p1.bin      (3600 x 1800 bytes)
    venv/bin/python build_roof_prior.py aggregate 2026-11-18.0 work/      # ~1 h, resumable, reads ~10 GB
    venv/bin/python build_roof_prior.py build work/ koppen_0p1.bin roof-prior.bin [--flat-bias zone=factor,...]

File format (little endian, gzip), version 1:
  "DRP1", u8 sizes, u8 heights, u8 uses, u8 kinds (flat, gabled, hipped, pyramidal, skillion)
  climate grid: u16 cols, u16 rows, f32 cell degrees, cols x rows u8 Köppen class (0 = sea), rows from the north
  u8 classes, then classes x strata x kinds u8 odds (round(p x 255))
  u8 levels; per level (finest first): f32 cell degrees, u32 count, count x (u32 row x cols + col, strata x kinds u8)
  A stratum's index is (size x heights + height) x uses + use.
"""
import concurrent.futures as cf
import gzip
import os
import struct
import sys
import time
import urllib.request
import xml.etree.ElementTree as ET

SIZES, HEIGHTS, USES = 5, 5, 2
KINDS = ["flat", "gabled", "hipped", "pyramidal", "skillion"]
STRATA = SIZES * HEIGHTS * USES
LEVELS = [0.25, 1.0, 5.0]
MIN_TAGS = 30   # a cell is stored when it has at least this many tagged buildings
BLEND = 20      # pseudo-count of the parent odds mixed into each stratum
CLASSES = 31

# Same lists as RoofPrior.cs
NON_RESIDENTIAL = ["commercial", "industrial", "retail", "warehouse", "office", "supermarket", "service", "garage",
                   "garages", "parking", "carport", "hangar", "factory", "manufacture", "hospital", "school",
                   "university", "college", "train_station", "transportation", "stadium"]
KIND_OF = {
    "flat": "flat",
    "gabled": "gabled", "saltbox": "gabled", "gambrel": "gabled", "round": "gabled",
    "hipped": "hipped", "half_hipped": "hipped", "half-hipped": "hipped", "side_hipped": "hipped", "mansard": "hipped",
    "pyramidal": "pyramidal", "dome": "pyramidal", "onion": "pyramidal", "cone": "pyramidal", "spherical": "pyramidal",
    "skillion": "skillion", "lean_to": "skillion",
}

AGG_SQL = """
COPY (
  SELECT floor((ST_X(c) + 180) * 4)::INT cx, floor((ST_Y(c) + 90) * 4)::INT cy,
    CASE WHEN a < 50 THEN 0 WHEN a < 150 THEN 1 WHEN a < 300 THEN 2 WHEN a < 1000 THEN 3 ELSE 4 END size,
    CASE WHEN h IS NULL THEN 0 WHEN h <= 8 THEN 1 WHEN h <= 15 THEN 2 WHEN h <= 30 THEN 3 ELSE 4 END height,
    CASE WHEN class IN ({nonres}) THEN 1 ELSE 0 END use,
    roof_shape, count(*)::INT n
  FROM (
    SELECT ST_Centroid(geometry) c, class, roof_shape, coalesce(height, num_floors * 3) h,
      ST_Area(geometry) * 111320 * 110540 * cos(radians(ST_Y(ST_Centroid(geometry)))) a
    FROM read_parquet('s3://overturemaps-us-west-2/{key}')
    WHERE roof_shape IS NOT NULL)
  GROUP BY ALL
) TO '{out}' (FORMAT parquet)
"""


def list_files(release):
    prefix = f"release/{release}/theme=buildings/type=building/"
    keys, after = [], ""
    ns = {"s3": "http://s3.amazonaws.com/doc/2006-03-01/"}
    while True:
        url = f"https://overturemaps-us-west-2.s3.amazonaws.com/?list-type=2&prefix={prefix}" + (f"&start-after={after}" if after else "")
        root = ET.fromstring(urllib.request.urlopen(url).read())
        page = [k.text for k in root.findall("s3:Contents/s3:Key", ns)]
        keys += page
        if root.findtext("s3:IsTruncated", namespaces=ns) != "true" or not page:
            return keys
        after = page[-1]


def aggregate(release, work):
    import duckdb
    os.makedirs(f"{work}/parts", exist_ok=True)
    keys = list_files(release)
    nonres = ", ".join(f"'{t}'" for t in NON_RESIDENTIAL)

    def run(i):
        out = f"{work}/parts/part-{i:04d}.parquet"
        if os.path.exists(out):
            return
        con = duckdb.connect()
        con.sql("INSTALL httpfs; LOAD httpfs; INSTALL spatial; LOAD spatial; SET s3_region='us-west-2'; SET threads=2; SET geometry_always_xy=true;")
        con.sql(AGG_SQL.format(key=keys[i], out=out + ".tmp", nonres=nonres))
        os.replace(out + ".tmp", out)

    t0 = time.time()
    with cf.ThreadPoolExecutor(8) as ex:
        for k, _ in enumerate(ex.map(run, range(len(keys)))):
            if k % 32 == 0:
                print(f"{k}/{len(keys)} files, {time.time() - t0:.0f} s", flush=True)
    print(f"{len(keys)} files in {time.time() - t0:.0f} s")


def build(work, koppen_path, out_path, flat_bias=None):
    import duckdb
    import numpy as np

    con = duckdb.connect()
    rows = con.sql(f"SELECT cx, cy, size, height, use, roof_shape, sum(n)::BIGINT n FROM '{work}/parts/*.parquet' GROUP BY ALL").fetchall()

    # counts[cx, cy] -> strata x kinds, at 0.25°
    fine = {}
    for cx, cy, size, height, use, shape, n in rows:
        kind = KIND_OF.get(shape)
        if kind is None:
            continue
        s = (size * HEIGHTS + height) * USES + use
        fine.setdefault((cx, cy), np.zeros((STRATA, len(KINDS))))[s, KINDS.index(kind)] += n

    # climate class at each 0.25° cell centre (nearest 0.1° pixel), rows from the north
    kg = np.fromfile(koppen_path, dtype=np.uint8).reshape(1800, 3600)
    cols, rws = 1440, 720
    lat = 90 - (np.arange(rws) + 0.5) * 0.25
    lon = -180 + (np.arange(cols) + 0.5) * 0.25
    climate = kg[np.clip(((90 - lat) * 10).astype(int), 0, 1799)][:, np.clip(((lon + 180) * 10).astype(int), 0, 3599)]

    def climate_of(cx, cy):  # cx, cy: 0.25° from (-180, -90)
        return int(climate[rws - 1 - cy, cx])

    # climate priors: counts by class, blended with the global odds per stratum, then by stratum-free odds
    by_class = np.zeros((CLASSES, STRATA, len(KINDS)))
    for (cx, cy), cnt in fine.items():
        by_class[climate_of(cx, cy)] += cnt
    total = by_class.sum(axis=0)
    overall = total.sum(axis=0) / total.sum()
    glob = (total + BLEND * overall) / (total.sum(axis=1, keepdims=True) + BLEND)
    class_odds = (by_class + BLEND * glob) / (by_class.sum(axis=2, keepdims=True) + BLEND)
    if flat_bias:
        for zone, factor in flat_bias.items():
            for k in zone_classes(zone):
                class_odds[k] = apply_flat_bias(class_odds[k], factor)

    # coarser levels: sum of the 0.25° counts
    def coarse(deg):
        f = int(deg / 0.25)
        agg = {}
        for (cx, cy), cnt in fine.items():
            key = (cx // f, cy // f)
            agg[key] = agg.get(key, 0) + cnt
        return agg

    levels = {0.25: fine, 1.0: coarse(1.0), 5.0: coarse(5.0)}

    def blended(deg, key, parent):
        cnt = levels[deg][key]
        return (cnt + BLEND * parent) / (cnt.sum(axis=1, keepdims=True) + BLEND)

    def centre_class(deg, key):
        f = int(deg / 0.25)
        return climate_of(min(cols - 1, key[0] * f + f // 2), min(rws - 1, key[1] * f + f // 2))

    stored = {deg: {} for deg in LEVELS}
    for key in levels[5.0]:
        stored[5.0][key] = blended(5.0, key, class_odds[centre_class(5.0, key)])
    for key in levels[1.0]:
        stored[1.0][key] = blended(1.0, key, stored[5.0][(key[0] // 5, key[1] // 5)])
    for key in levels[0.25]:
        stored[0.25][key] = blended(0.25, key, stored[1.0][(key[0] // 4, key[1] // 4)])
    for deg in LEVELS:  # only cells with enough tags; others fall back to their parent at run time
        stored[deg] = {k: v for k, v in stored[deg].items() if levels[deg][k].sum() >= MIN_TAGS}

    def odds_bytes(p):
        return np.clip(np.round(p * 255), 0, 255).astype(np.uint8).tobytes()

    with gzip.open(out_path, "wb") as f:
        f.write(b"DRP1" + bytes([SIZES, HEIGHTS, USES, len(KINDS)]))
        f.write(struct.pack("<HHf", cols, rws, 0.25))
        f.write(climate.astype(np.uint8).tobytes())
        f.write(bytes([CLASSES]))
        for k in range(CLASSES):
            f.write(odds_bytes(class_odds[k]))
        f.write(bytes([len(LEVELS)]))
        for deg in LEVELS:
            lc = int(360 / deg)
            lr = int(180 / deg)
            f.write(struct.pack("<fI", deg, len(stored[deg])))
            for (cx, cy), p in sorted(stored[deg].items()):
                f.write(struct.pack("<I", (lr - 1 - cy) * lc + cx))
                f.write(odds_bytes(p))
    print(out_path, os.path.getsize(out_path), "bytes;", {deg: len(stored[deg]) for deg in LEVELS}, "cells per level")
    return stored, class_odds


ZONES = {"A": range(1, 4), "B": range(4, 8), "C": range(8, 17), "D": range(17, 29), "E": range(29, 31)}


def zone_classes(zone):
    return ZONES[zone] if zone in ZONES else [int(zone)]


def apply_flat_bias(odds, factor):
    """Multiply the flat odds by factor (mapped flat roofs are under-tagged: no tag means flat), renormalise."""
    o = odds.copy()
    o[:, 0] *= factor
    return o / o.sum(axis=1, keepdims=True)


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "aggregate":
        aggregate(sys.argv[2], sys.argv[3])
    elif cmd == "build":
        bias = None
        if "--flat-bias" in sys.argv:
            spec = sys.argv[sys.argv.index("--flat-bias") + 1]
            bias = {z: float(v) for z, v in (p.split("=") for p in spec.split(","))}
        build(sys.argv[2], sys.argv[3], sys.argv[4], bias)
    else:
        print(__doc__)
        sys.exit(1)
