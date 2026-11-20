"""
Validation 1/3 (Germany): ground truth from the North Rhine-Westphalia LoD2 building model (open data, dl-de/zero-2-0,
https://www.opengeodata.nrw.de/produkte/geobasis/3dg/lod2_gml/): the ADV roof type of each building (its largest
part's), in random 1 km tiles of some 0.25° cells. 1000 is flat; 2100-4000 pitched; mixed (5000) and other (9999)
are left out. Same CSV as validate_1_sample_3dbag.py, for validate_2_join_overture.py.
    python3 validate_1b_sample_nrw.py work/nrw.csv [tiles per cell]
"""
import csv
import json
import math
import random
import sys
import urllib.request
import xml.etree.ElementTree as ET

BASE = "https://www.opengeodata.nrw.de/produkte/geobasis/3dg/lod2_gml/lod2_gml/"
CELLS = {  # name: (lon0, lat0) of the 0.25° cell
    'Köln': (6.75, 50.75), 'Düsseldorf': (6.75, 51.0), 'Essen': (7.0, 51.25), 'Münster': (7.5, 51.75),
    'Bielefeld': (8.5, 51.75), 'Sauerland rural': (8.25, 51.0), 'Eifel rural': (6.5, 50.5)}
NS = {"bldg": "http://www.opengis.net/citygml/building/1.0", "gml": "http://www.opengis.net/gml"}
FLAT, PITCHED = {"1000"}, {"2100", "2200", "3100", "3200", "3300", "3400", "3500", "3600", "3700", "3800", "3900", "4000"}


def utm32_to_lonlat(e, n):
    """Inverse transverse Mercator, zone 32 north, GRS80 (ETRS89)."""
    a, f, k0 = 6378137.0, 1 / 298.257222101, 0.9996
    e2 = f * (2 - f); ep2 = e2 / (1 - e2)
    x, y = e - 500000.0, n
    m = y / k0
    mu = m / (a * (1 - e2 / 4 - 3 * e2 ** 2 / 64 - 5 * e2 ** 3 / 256))
    e1 = (1 - math.sqrt(1 - e2)) / (1 + math.sqrt(1 - e2))
    phi1 = (mu + (3 * e1 / 2 - 27 * e1 ** 3 / 32) * math.sin(2 * mu) + (21 * e1 ** 2 / 16 - 55 * e1 ** 4 / 32) * math.sin(4 * mu)
            + (151 * e1 ** 3 / 96) * math.sin(6 * mu))
    c1 = ep2 * math.cos(phi1) ** 2; t1 = math.tan(phi1) ** 2
    n1 = a / math.sqrt(1 - e2 * math.sin(phi1) ** 2); r1 = a * (1 - e2) / (1 - e2 * math.sin(phi1) ** 2) ** 1.5
    d = x / (n1 * k0)
    lat = phi1 - (n1 * math.tan(phi1) / r1) * (d ** 2 / 2 - (5 + 3 * t1 + 10 * c1 - 4 * c1 ** 2 - 9 * ep2) * d ** 4 / 24
                                             + (61 + 90 * t1 + 298 * c1 + 45 * t1 ** 2 - 252 * ep2 - 3 * c1 ** 2) * d ** 6 / 720)
    lon = (d - (1 + 2 * t1 + c1) * d ** 3 / 6 + (5 - 2 * c1 + 28 * t1 - 3 * c1 ** 2 + 8 * ep2 + 24 * t1 ** 2) * d ** 5 / 120) / math.cos(phi1)
    return 9 + math.degrees(lon), math.degrees(lat)


def ground_rings(el):
    for gs in el.iterfind(".//bldg:GroundSurface", NS):
        for pl in gs.iterfind(".//gml:posList", NS):
            v = [float(t) for t in pl.text.split()]
            dim = int(pl.get("srsDimension", "3"))
            yield [(v[i], v[i + 1]) for i in range(0, len(v), dim)]


def area(ring):
    return abs(sum(ring[i][0] * ring[(i + 1) % len(ring)][1] - ring[(i + 1) % len(ring)][0] * ring[i][1] for i in range(len(ring)))) / 2


def main():
    out_path = sys.argv[1]
    per_cell = int(sys.argv[2]) if len(sys.argv) > 2 else 4
    files = json.load(urllib.request.urlopen(BASE + "index.json"))["datasets"][0]["files"]
    tiles = {}
    for f in files:
        _, _, e, n, *_ = f["name"].split("_")
        if int(f["size"]) < 100_000: continue  # empty tiles
        lon, lat = utm32_to_lonlat(int(e) * 1000 + 500, int(n) * 1000 + 500)
        tiles.setdefault((math.floor((lon + 180) * 4), math.floor((lat + 90) * 4)), []).append(f["name"])
    random.seed(1)
    w = csv.writer(open(out_path, "w", newline=""))
    w.writerow(["cell", "cx", "cy", "id", "roof", "area", "height", "lon", "lat"])
    for name, (lon0, lat0) in CELLS.items():
        cx, cy = int((lon0 + 180) * 4), int((lat0 + 90) * 4)
        chosen = random.sample(tiles.get((cx, cy), []), min(per_cell, len(tiles.get((cx, cy), []))))
        count = 0
        for t in chosen:
            for _, b in ET.iterparse(urllib.request.urlopen(BASE + t, timeout=300), events=("end",)):
                if b.tag != "{%s}Building" % NS["bldg"]: continue
                parts = [b] + list(b.iterfind(".//bldg:BuildingPart", NS))
                best, rings = None, []
                for p in parts:
                    rt = p.findtext("bldg:roofType", namespaces=NS)
                    pr = list(ground_rings(p)) if p is not b else [r for r in ground_rings(p)]
                    a = sum(area(r) for r in pr)
                    if rt and (best is None or a > best[1]): best = (rt, a)
                rings = list(ground_rings(b))
                if not best or not rings or (best[0] not in FLAT and best[0] not in PITCHED): b.clear(); continue
                pts = [p for r in rings for p in r[:-1]]
                ex = sum(p[0] for p in pts) / len(pts); ny = sum(p[1] for p in pts) / len(pts)
                lon, lat = utm32_to_lonlat(ex, ny)
                h = b.findtext("bldg:measuredHeight", namespaces=NS)
                w.writerow([name, cx, cy, b.get("{%s}id" % NS["gml"]), "horizontal" if best[0] in FLAT else "slanted",
                            round(sum(area(r) for r in rings), 1), h, lon, lat])
                count += 1
                b.clear()
        print(name, len(chosen), "tiles", count, "buildings", flush=True)


if __name__ == "__main__":
    main()
