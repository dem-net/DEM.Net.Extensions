using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

namespace DEM.Net.Extension.Osm.Buildings;

/// <summary>The roof shapes drawn; every OSM <c>roof:shape</c> maps to one of them (<see cref="RoofGeometry.ParseShape"/>).</summary>
public enum RoofKind
{
    Flat,
    Skillion,
    Gabled,
    Hipped,
    Pyramidal,
}

/// <summary>
/// A building footprint in model coordinates (x = longitude field, y = latitude field), open rings.
/// Convex footprints without holes (most houses) are handled without NetTopologySuite, which is far slower.
/// </summary>
public sealed class RoofFootprint
{
    private static readonly GeometryFactory Factory = new();
    private Polygon? _polygon;
    private bool _polygonBuilt;

    public IReadOnlyList<(double X, double Y)> Shell { get; }
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> Holes { get; }

    /// <summary>Area, holes removed.</summary>
    public double Area { get; }

    /// <summary>Convex and without holes: roof faces are clipped and triangulated directly.</summary>
    public bool IsSimpleConvex { get; }

    public RoofFootprint(IReadOnlyList<(double X, double Y)> shell, IReadOnlyList<IReadOnlyList<(double X, double Y)>>? holes = null)
    {
        Shell = shell;
        Holes = holes ?? [];
        Area = Math.Abs(SignedArea(shell)) - Holes.Sum(h => Math.Abs(SignedArea(h)));
        IsSimpleConvex = Holes.Count == 0 && IsConvex(shell);
    }

    public static RoofFootprint From(Polygon p)
    {
        static List<(double, double)> Open(LineString r) => [.. r.Coordinates.Take(r.NumPoints - 1).Select(c => (c.X, c.Y))];
        return new RoofFootprint(Open(p.Shell), [.. p.Holes.Select(h => (IReadOnlyList<(double X, double Y)>)Open(h))]);
    }

    /// <summary>The NetTopologySuite polygon, null when it isn't valid. Built on first use only.</summary>
    public Polygon? Polygon
    {
        get
        {
            if (_polygonBuilt) return _polygon;
            _polygonBuilt = true;
            try
            {
                LinearRing Ring(IReadOnlyList<(double X, double Y)> r)
                    => Factory.CreateLinearRing([.. r.Select(p => new Coordinate(p.X, p.Y)), new Coordinate(r[0].X, r[0].Y)]);
                var p = Factory.CreatePolygon(Ring(Shell), [.. Holes.Where(h => h.Count >= 3).Select(Ring)]);
                _polygon = p.IsValid && p.Area > 0 ? p : null;
            }
            catch (ArgumentException) { _polygon = null; }
            return _polygon;
        }
    }

    internal static double SignedArea(IReadOnlyList<(double X, double Y)> r)
    {
        double a = 0;
        for (int i = 0, n = r.Count; i < n; i++)
        {
            var (p, q) = (r[i], r[(i + 1) % n]);
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }

    /// <summary>All turns the same way (collinear points allowed), and at least 3 points.</summary>
    internal static bool IsConvex(IReadOnlyList<(double X, double Y)> r)
    {
        int n = r.Count, sign = 0;
        if (n < 3) return false;
        for (int i = 0; i < n; i++)
        {
            var (a, b, c) = (r[i], r[(i + 1) % n], r[(i + 2) % n]);
            double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (Math.Abs(cross) < 1e-12) continue;
            int s = Math.Sign(cross);
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return sign != 0;
    }
}

/// <summary>A roof face: a polygon of the footprint where one roof plane is the lowest.</summary>
public sealed record RoofFace(IReadOnlyList<(double X, double Y)> Shell, IReadOnlyList<IReadOnlyList<(double X, double Y)>> Holes, bool IsConvex)
{
    public double Area => Math.Abs(RoofFootprint.SignedArea(Shell)) - Holes.Sum(h => Math.Abs(RoofFootprint.SignedArea(h)));
}

/// <summary>
/// A pitched roof over a building footprint, in model coordinates (x = longitude field, y = latitude field).
/// The roof is the lower envelope of a few planes: its height above the eaves is <c>min_i(h_i(x, y))</c>.
/// The planes are laid out on a frame aligned with the footprint's minimum rectangle (the ridge along its long side),
/// or with <c>roof:direction</c> / <c>roof:orientation</c>:
/// skillion 1 plane, gabled 2 (one ridge), hipped 4 (equal slopes), pyramidal 4 (apex at the centre).
/// Each face is the part of the footprint where its plane is the lowest, so it is flat and its edges are the ridges
/// and hips. Approximations: saltbox, gambrel and round roofs are gabled, half-hipped and mansard roofs hipped,
/// domes, onions and cones pyramidal.
/// </summary>
public sealed class RoofGeometry
{
    /// <summary>Pitch used when the roof height isn't mapped.</summary>
    internal const double DefaultPitchDegrees = 30, DefaultSkillionPitchDegrees = 15;

    private static readonly GeometryFactory Factory = new();

    /// <summary>h(x, y) = A x + B y + C</summary>
    private readonly record struct Plane(double A, double B, double C)
    {
        public double At(double x, double y) => A * x + B * y + C;
    }

    private readonly Plane[] _planes;

    public RoofKind Kind { get; }

    /// <summary>Height of the ridge or apex above the eaves, model units.</summary>
    public double Height { get; }

    private RoofGeometry(RoofKind kind, double height, Plane[] planes)
    {
        Kind = kind;
        Height = height;
        _planes = planes;
    }

    public static RoofKind ParseShape(string? roofShape) => roofShape?.Trim().ToLowerInvariant() switch
    {
        "skillion" or "lean_to" => RoofKind.Skillion,
        "gabled" or "saltbox" or "gambrel" or "round" => RoofKind.Gabled,
        "hipped" or "half-hipped" or "half_hipped" or "side_hipped" or "mansard" => RoofKind.Hipped,
        "pyramidal" or "dome" or "onion" or "cone" or "spherical" => RoofKind.Pyramidal,
        _ => RoofKind.Flat,
    };

    /// <summary>The roof height a pitched roof would have with the default pitch over this footprint, model units.</summary>
    public static double DefaultHeight(RoofKind kind, Polygon footprint, double? directionDegrees = null, bool across = false)
        => Frame(RoofFootprint.From(footprint).Shell, directionDegrees, across) is { } f ? DefaultHeight(kind, f) : 0;

    public static RoofGeometry? Create(RoofKind kind, Polygon footprint, double? height, double? directionDegrees = null, bool across = false)
        => Create(kind, RoofFootprint.From(footprint), height, directionDegrees, across);

    /// <param name="footprint">Model coordinates.</param>
    /// <param name="height">Ridge height above the eaves, model units; null for the default pitch.</param>
    /// <param name="directionDegrees"><c>roof:direction</c>: compass direction the roof slope faces (down-slope), degrees.</param>
    /// <param name="across"><c>roof:orientation=across</c>: ridge across the long side.</param>
    /// <param name="maxHeight">Cap on the roof height, model units.</param>
    /// <returns>null for a flat roof, or a footprint too small to frame.</returns>
    public static RoofGeometry? Create(RoofKind kind, RoofFootprint footprint, double? height, double? directionDegrees = null, bool across = false,
        double maxHeight = double.PositiveInfinity)
    {
        if (kind == RoofKind.Flat) return null;
        if (Frame(footprint.Shell, directionDegrees, across) is not { } f) return null;
        double h = Math.Min(height ?? DefaultHeight(kind, f), maxHeight);
        if (!(h > 0) || !double.IsFinite(h)) return null;

        // u across the ridge (down-slope for a skillion), v along it, both from the frame centre
        var (n, r, cu, cv, w, l) = f;
        Plane Along(double ku, double kv, double d) // h = ku·u + kv·v + d, with u = n·p − cu and v = r·p − cv
            => new(ku * n.X + kv * r.X, ku * n.Y + kv * r.Y, d - ku * cu - kv * cv);

        Plane[] planes = kind switch
        {
            RoofKind.Skillion => [Along(-h / (2 * w), 0, h / 2)],
            RoofKind.Gabled => [Along(-h / w, 0, h), Along(h / w, 0, h)],
            RoofKind.Hipped => [Along(-h / w, 0, h), Along(h / w, 0, h), Along(0, -h / w, h * l / w), Along(0, h / w, h * l / w)],
            RoofKind.Pyramidal => [Along(-h / w, 0, h), Along(h / w, 0, h), Along(0, -h / l, h), Along(0, h / l, h)],
            _ => [],
        };
        return new RoofGeometry(kind, h, planes);
    }

    /// <summary>Roof height above the eaves at a point, model units (0 at the eaves).</summary>
    public double HeightAt(double x, double y) => Math.Clamp(Lowest(x, y), 0, Height);

    private double Lowest(double x, double y)
    {
        double min = double.MaxValue;
        foreach (var p in _planes) min = Math.Min(min, p.At(x, y));
        return min;
    }

    public List<RoofFace> Faces(Polygon footprint) => Faces(RoofFootprint.From(footprint));

    /// <summary>
    /// The roof faces: for each plane, the part of the footprint where it is the lowest. Convex footprints are
    /// clipped directly (convex faces); others go through NetTopologySuite (faces may be concave, with holes).
    /// </summary>
    public List<RoofFace> Faces(RoofFootprint footprint)
    {
        var faces = new List<RoofFace>(_planes.Length);
        if (footprint.Holes.Count == 0)
        {
            // Clipping by half-planes (convex) works on a concave footprint too: a face cut in several pieces keeps
            // them joined by zero-width edges along the cut, which the triangulation turns into no triangle
            double minArea = 1e-9 * footprint.Area;
            for (int i = 0; i < _planes.Length; i++)
            {
                var face = Cell(i, [.. footprint.Shell]);
                if (face.Count >= 3 && Math.Abs(RoofFootprint.SignedArea(face)) > minArea) faces.Add(new RoofFace(face, [], footprint.IsSimpleConvex));
            }
            return faces;
        }

        var polygon = footprint.Polygon ?? throw new InvalidOperationException("invalid footprint");
        var env = polygon.EnvelopeInternal;
        double m = Math.Max(env.Width, env.Height) + 1;
        List<(double X, double Y)> box = [(env.MinX - m, env.MinY - m), (env.MaxX + m, env.MinY - m), (env.MaxX + m, env.MaxY + m), (env.MinX - m, env.MaxY + m)];
        for (int i = 0; i < _planes.Length; i++)
        {
            var region = Cell(i, box);
            if (region.Count < 3) continue;
            var cell = Factory.CreatePolygon([.. region.Select(p => new Coordinate(p.X, p.Y)), new Coordinate(region[0].X, region[0].Y)]);
            Geometry clipped;
            try { clipped = polygon.Intersection(cell); }
            catch (TopologyException) { continue; }
            for (int g = 0; g < clipped.NumGeometries; g++)
                if (clipped.GetGeometryN(g) is Polygon p && !p.IsEmpty && p.Area > 0)
                {
                    var face = RoofFootprint.From(p);
                    faces.Add(new RoofFace(face.Shell, face.Holes, face.IsSimpleConvex));
                }
        }
        return faces;
    }

    // The part of a convex polygon where plane i is the lowest (h_j − h_i ≥ 0 for every other j)
    private List<(double X, double Y)> Cell(int i, List<(double X, double Y)> convex)
    {
        var region = convex;
        for (int j = 0; j < _planes.Length && region.Count >= 3; j++)
        {
            if (j == i) continue;
            var (pi, pj) = (_planes[i], _planes[j]);
            region = ClipHalfPlane(region, pj.A - pi.A, pj.B - pi.B, pj.C - pi.C);
        }
        return region;
    }

    /// <summary>
    /// Positions along a ring segment (0 &lt; t &lt; 1, sorted) where the roof changes face: a wall vertex is needed
    /// there for the wall top to follow the roof (a gable end peaks at the ridge).
    /// </summary>
    public List<double> Breaks(double x0, double y0, double x1, double y1)
    {
        List<double>? ts = null;
        for (int i = 0; i < _planes.Length; i++)
            for (int j = i + 1; j < _planes.Length; j++)
            {
                double d0 = _planes[i].At(x0, y0) - _planes[j].At(x0, y0);
                double d1 = _planes[i].At(x1, y1) - _planes[j].At(x1, y1);
                if (d0 == d1) continue;
                double t = d0 / (d0 - d1);
                if (t <= 1e-9 || t >= 1 - 1e-9) continue;
                // keep it only where these two planes are the roof (the lowest)
                double x = x0 + t * (x1 - x0), y = y0 + t * (y1 - y0);
                if (_planes[i].At(x, y) <= Lowest(x, y) + 1e-9 * Height) (ts ??= []).Add(t);
            }
        if (ts == null) return [];
        ts.Sort();
        return ts;
    }

    public static double Rectangularity(Polygon footprint) => Rectangularity(RoofFootprint.From(footprint));

    /// <summary>Footprint area divided by its minimum rectangle's area: 1 for a rectangle.</summary>
    public static double Rectangularity(RoofFootprint footprint)
    {
        var rect = MinimumRectangle(footprint.Shell);
        return rect is { } r && r.Area > 0 ? footprint.Area / r.Area : 0;
    }

    private readonly record struct RoofFrame((double X, double Y) N, (double X, double Y) R, double CU, double CV, double W, double L);

    private static double DefaultHeight(RoofKind kind, RoofFrame f) => kind == RoofKind.Skillion
        ? 2 * f.W * Math.Tan(DefaultSkillionPitchDegrees * Math.PI / 180)
        : f.W * Math.Tan(DefaultPitchDegrees * Math.PI / 180);

    /// <summary>n across the ridge, r along it (unit vectors), the footprint's centre and half extents on them.</summary>
    private static RoofFrame? Frame(IReadOnlyList<(double X, double Y)> shell, double? directionDegrees, bool across)
    {
        if (shell.Count < 3) return null;

        (double X, double Y) n, r;
        if (directionDegrees is double deg && double.IsFinite(deg))
        {
            double a = deg * Math.PI / 180; // compass: 0 = north (+y), 90 = east (+x)
            n = (Math.Sin(a), Math.Cos(a));
            r = (-n.Y, n.X);
        }
        else
        {
            if (MinimumRectangle(shell) is not { } rect) return null;
            r = rect.Long;
            if (across) r = (-r.Y, r.X);
            n = (-r.Y, r.X);
        }

        double uMin = double.MaxValue, uMax = double.MinValue, vMin = double.MaxValue, vMax = double.MinValue;
        foreach (var p in shell)
        {
            double u = n.X * p.X + n.Y * p.Y, v = r.X * p.X + r.Y * p.Y;
            uMin = Math.Min(uMin, u); uMax = Math.Max(uMax, u);
            vMin = Math.Min(vMin, v); vMax = Math.Max(vMax, v);
        }
        double w = (uMax - uMin) / 2, l = (vMax - vMin) / 2;
        if (!(w > 0) || !(l > 0)) return null;
        return new RoofFrame(n, r, (uMin + uMax) / 2, (vMin + vMax) / 2, w, l);
    }

    /// <summary>
    /// Minimum-area enclosing rectangle (one side lies on a convex hull edge): its area and the unit vector of its
    /// long side.
    /// </summary>
    private static (double Area, (double X, double Y) Long)? MinimumRectangle(IReadOnlyList<(double X, double Y)> shell)
    {
        var hull = ConvexHull(shell);
        if (hull.Count < 3) return null;
        double best = double.MaxValue;
        (double X, double Y) bestLong = default;
        for (int i = 0; i < hull.Count; i++)
        {
            var (a, b) = (hull[i], hull[(i + 1) % hull.Count]);
            double ex = b.X - a.X, ey = b.Y - a.Y, len = Math.Sqrt(ex * ex + ey * ey);
            if (len <= 0) continue;
            ex /= len; ey /= len;
            double uMin = double.MaxValue, uMax = double.MinValue, vMin = double.MaxValue, vMax = double.MinValue;
            foreach (var p in hull)
            {
                double u = ex * p.X + ey * p.Y, v = -ey * p.X + ex * p.Y;
                uMin = Math.Min(uMin, u); uMax = Math.Max(uMax, u);
                vMin = Math.Min(vMin, v); vMax = Math.Max(vMax, v);
            }
            double area = (uMax - uMin) * (vMax - vMin);
            if (area < best)
            {
                best = area;
                bestLong = uMax - uMin >= vMax - vMin ? (ex, ey) : (-ey, ex);
            }
        }
        return best < double.MaxValue && best > 0 ? (best, bestLong) : null;
    }

    // Andrew's monotone chain, counter-clockwise
    private static List<(double X, double Y)> ConvexHull(IReadOnlyList<(double X, double Y)> points)
    {
        var pts = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (pts.Count < 3) return pts;
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        List<(double X, double Y)> Chain(IEnumerable<(double X, double Y)> ordered)
        {
            var chain = new List<(double X, double Y)>();
            foreach (var p in ordered)
            {
                while (chain.Count >= 2 && Cross(chain[^2], chain[^1], p) <= 0) chain.RemoveAt(chain.Count - 1);
                chain.Add(p);
            }
            chain.RemoveAt(chain.Count - 1); // the next chain starts with it
            return chain;
        }
        var hull = Chain(pts);
        hull.AddRange(Chain(Enumerable.Reverse(pts)));
        return hull;
    }

    // Sutherland–Hodgman: the part of a convex polygon where a·x + b·y + c ≥ 0
    private static List<(double X, double Y)> ClipHalfPlane(List<(double X, double Y)> poly, double a, double b, double c)
    {
        var result = new List<(double X, double Y)>(poly.Count + 1);
        for (int k = 0; k < poly.Count; k++)
        {
            var p = poly[k];
            var q = poly[(k + 1) % poly.Count];
            double fp = a * p.X + b * p.Y + c, fq = a * q.X + b * q.Y + c;
            if (fp >= 0) result.Add(p);
            if ((fp >= 0) != (fq >= 0))
            {
                double t = fp / (fp - fq);
                result.Add((p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y)));
            }
        }
        return result;
    }
}
