"""
Validation 1/3: ground truth from 3DBAG (Netherlands, CC BY 4.0): roof type of the buildings in random 1 km boxes of
seven 0.25° cells. A building is flat when its flat roof area is at least its sloped roof area (b3_dak_type alone
calls a building "slanted" as soon as one surface slopes).
    python3 validate_1_sample_3dbag.py work/3dbag.csv
"""
import json, random, urllib.request, csv, sys
CELLS = {  # name: (lon0, lat0) of the 0.25° cell
  'Amsterdam': (4.75, 52.25), 'Rotterdam': (4.25, 51.75), 'Utrecht': (5.0, 52.0), 'Eindhoven': (5.25, 51.25),
  'Friesland rural': (5.75, 53.0), 'Groningen': (6.5, 53.0), 'Zeeland rural': (3.75, 51.5)}
URL = ("https://data.3dbag.nl/api/BAG3D/wfs?service=WFS&version=2.0.0&request=GetFeature&typeNames=BAG3D:lod22"
       "&count=3000&outputFormat=application/json&propertyName=identificatie,b3_dak_type,b3_opp_grond,b3_h_max,b3_h_maaiveld,b3_opp_dak_plat,b3_opp_dak_schuin,geom&srsName=urn:ogc:def:crs:EPSG::4326"
       "&bbox={s},{w},{n},{e},urn:ogc:def:crs:EPSG::4326")
random.seed(1)
out = csv.writer(open(sys.argv[1], 'w', newline='')); out.writerow(['cell', 'cx', 'cy', 'id', 'roof', 'area', 'height', 'lon', 'lat'])
for name, (lon0, lat0) in CELLS.items():
    seen = set()
    for _ in range(6):
        w = lon0 + random.random() * (0.25 - 0.015); s = lat0 + random.random() * (0.25 - 0.009)
        d = json.load(urllib.request.urlopen(URL.format(s=s, w=w, n=s + 0.009, e=w + 0.015), timeout=120))
        for f in d['features']:
            p = f['properties']
            if p['identificatie'] in seen: continue
            seen.add(p['identificatie'])
            h = (p['b3_h_max'] - p['b3_h_maaiveld']) if p.get('b3_h_max') is not None and p.get('b3_h_maaiveld') is not None else None
            ring = f['geometry']['coordinates'][0] if f['geometry']['type'] == 'Polygon' else f['geometry']['coordinates'][0][0]
            lo = sum(q[0] for q in ring[:-1]) / (len(ring) - 1); la = sum(q[1] for q in ring[:-1]) / (len(ring) - 1)  # the server answers lon, lat
            roof = p['b3_dak_type']
            if roof in ('slanted', 'horizontal', 'multiple horizontal') and p.get('b3_opp_dak_plat') is not None and p.get('b3_opp_dak_schuin') is not None:
                roof = 'horizontal' if p['b3_opp_dak_plat'] >= p['b3_opp_dak_schuin'] else 'slanted'  # the dominant roof surface
            out.writerow([name, int((lon0 + 180) * 4), int((lat0 + 90) * 4), p['identificatie'], roof, p['b3_opp_grond'], h, lo, la])
    print(name, len(seen), flush=True)
