"""
Validation 2/3: matches each 3DBAG building to the Overture building containing its centre, for the attributes the
API sees (area, height, floors, class, mapped roof shape).
    venv/bin/python validate_2_join_overture.py work/      (reads work/3dbag.csv, writes work/joined.csv)
"""
import duckdb, sys
S = sys.argv[1]
c = duckdb.connect(); c.sql("INSTALL httpfs; LOAD httpfs; INSTALL spatial; LOAD spatial; SET s3_region='us-west-2'; SET geometry_always_xy=true;")
c.sql(f"CREATE TABLE s AS SELECT *, ST_Point(lon, lat) pt FROM read_csv('{S}/3dbag.csv')")
src = "s3://overturemaps-us-west-2/release/2026-11-18.0/theme=buildings/type=building/*"
boxes = c.sql("SELECT cell, min(lon), max(lon), min(lat), max(lat) FROM s GROUP BY cell").fetchall()
c.sql("CREATE TABLE o (id VARCHAR, height DOUBLE, num_floors INT, class VARCHAR, roof_shape VARCHAR, area DOUBLE, geometry GEOMETRY)")
for cell, x0, x1, y0, y1 in boxes:
    c.sql(f"""INSERT INTO o SELECT id, height, num_floors, class, roof_shape,
        ST_Area(geometry) * 111320 * 110540 * cos(radians({(y0 + y1) / 2})), geometry
        FROM read_parquet('{src}') WHERE bbox.xmin < {x1} AND bbox.xmax > {x0} AND bbox.ymin < {y1} AND bbox.ymax > {y0}""")
    print(cell, c.sql("SELECT count(*) FROM o").fetchall()[0][0], flush=True)
c.sql(f"""COPY (SELECT s.cell, s.cx, s.cy, s.roof, s.area bag_area, o.id, o.area, o.height, o.num_floors, o.class, o.roof_shape
          FROM s JOIN o ON ST_Contains(o.geometry, s.pt)) TO '{S}/joined.csv'""")
print(c.sql(f"SELECT count(*), count(DISTINCT id) FROM '{S}/joined.csv'").fetchall())
