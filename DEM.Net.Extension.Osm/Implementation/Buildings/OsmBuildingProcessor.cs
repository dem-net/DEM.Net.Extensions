using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using DEM.Net.Core;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;

namespace DEM.Net.Extension.Osm.Buildings;


public class OsmBuildingProcessor(GeoTransformPipeline? transformPipeline, bool withBuildingsColors, string? defaultBuildingsColor, RoofPrior? roofPrior = null) : OsmProcessorStage<BuildingModel>(transformPipeline)
{
    private readonly RoofPrior? _roofPrior = roofPrior;

    const double LevelHeightMeters = 3;
    private readonly bool withBuildingsColors = withBuildingsColors;
    private readonly string? defaultBuildingsColor = defaultBuildingsColor;

    public override IOsmDataSettings DataSettings { get; set; } = new BuildingsDataFilter();

    public override OsmModelFactory<BuildingModel> ModelFactory => new BuildingValidator(base._logger, withBuildingsColors, defaultBuildingsColor);
    public override string glTFNodeName => "Buildings";

    protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IReadOnlyList<BuildingModel> models)
    {
        TriangulationNormals triangulation = this.Triangulate(models);

        if (!triangulation.Positions.Any())
        {
            _logger.LogWarning($"{this.GetType().Name} triangulation has 0 positions. No data written to model");
            return gltfModel;
        }

        // All white (the default): no vertex colors, the material is white. Saves 16 bytes per vertex.
        if (triangulation.Colors.All(c => c == Vector4.One))
        {
            triangulation = new TriangulationNormals(triangulation.Positions, triangulation.Indices, triangulation.Normals, []);
        }

        // With the computed normals: without them, viewers shade every triangle flat (round walls look faceted)
        gltfModel = _gltfService.AddMesh(gltfModel, glTFNodeName, new IndexedTriangulation(triangulation), triangulation.Normals, null, doubleSided: true);

        return gltfModel;

    }

    protected override IEnumerable<BuildingModel> ComputeModelElevationsAndTransform(IEnumerable<BuildingModel> modelsEnum, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
    {
        List<GeoPoint> transformedPoints;

        //// georeference

        List<BuildingModel> models = DropOutlinesWithParts([.. modelsEnum]);
        foreach (var m in models) // longitude and latitude, for the roof prior
        {
            var ring = m.ExteriorRing;
            int n = Math.Max(1, ring.Count - 1);
            double lon = 0, lat = 0;
            for (int i = 0; i < n; i++) { lon += ring[i].Longitude; lat += ring[i].Latitude; }
            m.Centre = (lon / n, lat / n);
        }
        using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug))
        {
            // Select all points (outer ring) + (inner rings)
            // Elevation and transforms keep the points order: they are mapped back to their building by position
            var allBuildingPoints = models
                .SelectMany(b => b.Points);

            // Compute elevations if requested (the elevated points are in the dataset SRID, as the transform expects)
            IEnumerable<GeoPoint> geoPoints = computeElevations ? _elevationService.GetPointsElevation(allBuildingPoints // files downloaded by RunAsync
                                                                    , dataSet
                                                                    , behavior: NoDataBehavior.SetToZero)!
                                                                : ToDataSetSrid(allBuildingPoints, dataSet);

            transformedPoints = [.. Transform.TransformPoints(geoPoints)];

            //.ZScale(zScale)
            //.ReprojectGeodeticToCartesian(pointCount)
            //.CenterOnOrigin(bbox3857)

        }

        using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Remap points", _logger, LogLevel.Debug))
        {
            int index = 0;
            foreach (var buiding in models)
            {
                // same order as BuildingModel.Points
                foreach (var ring in buiding.InteriorRings.Prepend(buiding.ExteriorRing))
                {
                    for (int i = 0; i < ring.Count; i++)
                        ring[i] = transformedPoints[index++];
                }

                yield return buiding;
            }
            Debug.Assert(index == transformedPoints.Count);
            transformedPoints = null!; // releases the points while the caller enumerates
        }

    }


    public TriangulationNormals Triangulate(IEnumerable<BuildingModel> buildingModels)
    {

        List<Vector3> positions = [];
        List<Vector4> colors = [];
        List<int> indices = [];
        List<Vector3> normals = [];

        using (TimeSpanBlock timer = new TimeSpanBlock(nameof(Triangulate), _logger, LogLevel.Information))
        {
            // Get highest base point
            // Retrieve building size
            int numWithHeight = 0;
            int numWithColor = 0;
            int numTotal = 0;

            // Buildings are independent: triangulated in parallel, then appended in their order
            var buildings = buildingModels as IReadOnlyList<BuildingModel> ?? [.. buildingModels];
            var parts = new (List<Vector3> Positions, List<int> Indices, List<Vector3> Normals, List<Vector4> Colors)[buildings.Count];
            Action<int> body = i =>
            {
                var triangulation = this.Triangulate(buildings[i]);
                var positionsVec3 = triangulation.Positions.ToVector3GlTFSpace().ToList();
                parts[i] = (positionsVec3, triangulation.Indices, [.. _meshService.ComputeMeshNormals(positionsVec3, triangulation.Indices)], triangulation.Colors!);
            };
            Parallel.For(0, buildings.Count, body);

            foreach (var (building, part) in buildings.Zip(parts))
            {
                // after Triangulate, which computes HasHeightInformation
                numTotal++;
                numWithHeight += building.HasHeightInformation ? 1 : 0;
                numWithColor += (building.Color.HasValue || building.RoofColor.HasValue) ? 1 : 0;

                int initialPositionsCount = positions.Count;
                positions.AddRange(part.Positions);
                foreach (int index in part.Indices)
                    indices.Add(index + initialPositionsCount);
                colors.AddRange(part.Colors);
                normals.AddRange(part.Normals);
            }

            _logger.LogInformation($"Building heights: {numWithHeight:N0}/{numTotal:N0} ({numWithHeight / (float)numTotal:P}) with height information.");
            _logger.LogInformation($"Building colors: {numWithColor:N0}/{numTotal:N0} ({numWithColor / (float)numTotal:P}) with color information.");
        }


        return new TriangulationNormals(positions, indices, normals, colors);
    }
    /// <summary>
    /// Building: the roof (flat, or pitched, see <see cref="RoofGeometry"/>) and the walls from the floor up to it.
    /// </summary>
    /// <remarks>
    /// The roof has its own vertices, so its normals are not blended with the walls'. Wall vertices are shared
    /// between two consecutive wall faces only where the outline turns by less than
    /// <see cref="SmoothWallAngleDegrees"/> (curved walls stay smooth, corners are sharp).
    /// </remarks>
    public TriangulationList<GeoPoint> Triangulate(BuildingModel building)
    {
        building = this.ComputeBuildingHeightMeters(building);

        //==========================
        // Roof: footprint triangulation at the roof altitude
        //
        // In GeoJson, ring last point == first point, we must filter the last point out
        var rings = building.InteriorRings.Prepend(building.ExteriorRing).Select(r => r.Take(r.Count - 1).ToList()).ToList();
        Vector4 defaultColor = Vector4.One;
        Vector4 wallColor = building.Color ?? defaultColor;
        Vector4 roofColor = building.RoofColor ?? wallColor;

        if (PitchedRoof(building, rings) is { } pitched)
        {
            try
            {
                return TriangulatePitched(building, rings, pitched.Roof, pitched.Footprint, pitched.Eave, wallColor, roofColor);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is NetTopologySuite.Geometries.TopologyException)
            {
                _logger.LogDebug($"Building {building.Id}: pitched roof failed, flat roof instead. {ex.Message}");
            }
        }

        TriangulationList<GeoPoint> triangulation = _meshService.Tesselate(rings[0], rings.Skip(1));
        for (int i = 0; i < triangulation.Positions.Count; i++)
        {
            triangulation.Positions[i] = triangulation.Positions[i] with { Elevation = building.ComputedRoofAltitude };
        }
        triangulation.Colors = [.. triangulation.Positions.Select(_ => roofColor)];

        //==========================
        // Walls, ring by ring
        //
        foreach (var ring in rings)
        {
            AddRingWalls(triangulation, ring, _ => building.ComputedRoofAltitude!.Value, building.ComputedFloorAltitude, wallColor);
        }

        Debug.Assert(triangulation.Colors.Count == triangulation.Positions.Count);

        return triangulation;
    }

    /// <summary>
    /// Outline turns below this angle share their wall vertices (smooth normals across the corner)
    /// </summary>
    internal const double SmoothWallAngleDegrees = 30;

    private static readonly HashSet<string> FlatRoofTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "commercial", "industrial", "retail", "warehouse", "office", "supermarket", "service", "garage", "garages",
        "parking", "carport", "roof", "hangar", "stadium", "train_station", "transportation", "factory", "manufacture",
        "hospital", "school", "university", "college", "civic", "government", "public",
    };

    /// <summary>Largest footprint, square metres, given a gabled roof when its roof shape isn't mapped.</summary>
    internal const double DefaultRoofMaxAreaM2 = 300;

    /// <summary>
    /// The building's pitched roof, or null for a flat one. Mapped shapes are drawn as mapped; an unmapped shape comes
    /// from <see cref="DefaultRoof"/>. A mapped <c>height</c> includes the roof (OSM), so the eaves go down by the roof
    /// height; without a height, or with an estimated one (machine learning, low: taken as the wall height), the roof
    /// goes on top of the walls. A roof never takes more than half the mapped height, or more than the wall height.
    /// </summary>
    internal (RoofGeometry Roof, RoofFootprint Footprint, double Eave)? PitchedRoof(BuildingModel building, List<List<GeoPoint>> rings)
    {
        RoofKind kind = RoofGeometry.ParseShape(building.RoofShape);
        if (kind == RoofKind.Flat && building.RoofShape != null) return null;
        if (rings[0].Count < 3) return null;

        static List<(double X, double Y)> Xy(List<GeoPoint> r) => r.ConvertAll(p => (p.Longitude, p.Latitude));
        var footprint = new RoofFootprint(Xy(rings[0]), [.. rings.Skip(1).Where(r => r.Count >= 3).Select(Xy)]);
        if (!(footprint.Area > 0)) return null;
        if (footprint.Holes.Count > 0 && footprint.Polygon == null) return null; // invalid: flat roof

        double unitsPerMeter = Transform?.ModelUnitsPerMeter ?? 1d;
        if (kind == RoofKind.Flat)
        {
            kind = DefaultRoof(building, footprint, unitsPerMeter);
            if (kind == RoofKind.Flat) return null;
        }

        double top = building.ComputedRoofAltitude!.Value;
        double floor = building.ComputedFloorAltitude ?? building.Points.Min(p => p.Elevation ?? 0);
        double walls = top - floor;
        if (!(walls > 0)) return null;

        double? mapped = building.RoofHeight is > 0 ? building.RoofHeight * unitsPerMeter : null;
        bool heightIncludesRoof = building.Height.HasValue && !building.HeightIsEstimated;
        var roof = RoofGeometry.Create(kind, footprint, mapped, building.RoofDirection, building.RoofAcross, maxHeight: heightIncludesRoof ? walls / 2 : walls);
        if (roof == null) return null;

        double eave = heightIncludesRoof ? top - roof.Height : top;
        return (roof, footprint, eave);
    }

    /// <summary>Footprints below this (area / minimum rectangle) keep a flat roof: the roof planes follow a rectangle.</summary>
    internal const double MinRectangularity = 0.75;

    internal RoofKind DefaultRoof(BuildingModel building, NetTopologySuite.Geometries.Polygon footprint, double unitsPerMeter)
        => DefaultRoof(building, RoofFootprint.From(footprint), unitsPerMeter);

    /// <summary>
    /// The roof of a building without a mapped roof shape: drawn from the roof prior when there is one (by place,
    /// footprint size, height and use), else a gabled roof on small, nearly rectangular, low houses.
    /// </summary>
    internal RoofKind DefaultRoof(BuildingModel building, RoofFootprint footprint, double unitsPerMeter)
    {
        if (_roofPrior == null || building.Centre is not (double lon, double lat))
            return HasDefaultGabledRoof(building, footprint, unitsPerMeter) ? RoofKind.Gabled : RoofKind.Flat;
        if (building.IsPart || footprint.Holes.Count > 0) return RoofKind.Flat;
        if (building.MinHeight is > 0 || building.MinLevel is > 0) return RoofKind.Flat; // overhangs, bridges

        double areaM2 = footprint.Area / (unitsPerMeter * unitsPerMeter);
        double? heightM = building.Height ?? building.Levels * LevelHeightMeters;
        bool nonResidential = building.Type != null && RoofPrior.NonResidentialTypes.Contains(building.Type);
        var kind = _roofPrior.Choose(lon, lat, RoofPrior.Stratum(areaM2, heightM, nonResidential), RoofPrior.Hash(building.Id));
        if (kind != RoofKind.Flat && RoofGeometry.Rectangularity(footprint) < MinRectangularity) return RoofKind.Flat;
        return kind;
    }

    internal static bool HasDefaultGabledRoof(BuildingModel building, NetTopologySuite.Geometries.Polygon footprint, double unitsPerMeter)
        => HasDefaultGabledRoof(building, RoofFootprint.From(footprint), unitsPerMeter);

    internal static bool HasDefaultGabledRoof(BuildingModel building, RoofFootprint footprint, double unitsPerMeter)
    {
        if (building.RoofShape != null || building.IsPart || footprint.Holes.Count > 0) return false;
        if (building.Type != null && FlatRoofTypes.Contains(building.Type)) return false;
        if ((!building.HeightIsEstimated && building.Height is > 12) || building.Levels is > 3 || building.MinHeight is > 0 || building.MinLevel is > 0) return false;
        double areaM2 = footprint.Area / (unitsPerMeter * unitsPerMeter);
        return areaM2 is >= 25 and <= DefaultRoofMaxAreaM2 && RoofGeometry.Rectangularity(footprint) >= 0.8;
    }

    private TriangulationList<GeoPoint> TriangulatePitched(BuildingModel building, List<List<GeoPoint>> rings, RoofGeometry roof,
        RoofFootprint footprint, double eave, Vector4 wallColor, Vector4 roofColor)
    {
        double RoofAt(double x, double y) => eave + roof.HeightAt(x, y);
        GeoPoint OnRoof((double X, double Y) c) => new(c.Y, c.X, RoofAt(c.X, c.Y));

        var triangulation = new TriangulationList<GeoPoint> { Colors = [] };

        // Roof faces, each on its own vertices (sharp ridges), triangles counter-clockwise from above (normals up)
        foreach (var face in roof.Faces(footprint))
        {
            int offset = triangulation.Positions.Count;
            if (face.IsConvex)
            {
                // a fan, in counter-clockwise order
                bool ccw = RoofFootprint.SignedArea(face.Shell) > 0;
                int n = face.Shell.Count;
                for (int k = 0; k < n; k++) triangulation.Positions.Add(OnRoof(face.Shell[ccw ? k : n - 1 - k]));
                for (int k = 1; k + 1 < n; k++)
                {
                    triangulation.Indices.Add(offset);
                    triangulation.Indices.Add(offset + k);
                    triangulation.Indices.Add(offset + k + 1);
                }
            }
            else
            {
                var faceTriangulation = _meshService.Tesselate(face.Shell.Select(OnRoof).ToList(), face.Holes.Select(h => (IEnumerable<GeoPoint>)h.Select(OnRoof).ToList()));
                triangulation.Positions.AddRange(faceTriangulation.Positions);
                var idx = faceTriangulation.Indices;
                for (int t = 0; t + 2 < idx.Count; t += 3)
                {
                    var (a, b, c) = (faceTriangulation.Positions[idx[t]], faceTriangulation.Positions[idx[t + 1]], faceTriangulation.Positions[idx[t + 2]]);
                    double cross = (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);
                    triangulation.Indices.Add(offset + idx[t]);
                    triangulation.Indices.Add(offset + (cross >= 0 ? idx[t + 1] : idx[t + 2]));
                    triangulation.Indices.Add(offset + (cross >= 0 ? idx[t + 2] : idx[t + 1]));
                }
            }
            for (int k = offset; k < triangulation.Positions.Count; k++) triangulation.Colors.Add(roofColor);
        }
        if (triangulation.Indices.Count == 0)
            throw new InvalidOperationException("no roof face");

        // Walls up to the roof line: a vertex wherever the roof changes face (gable ends peak at the ridge)
        foreach (var ring in rings)
        {
            var dense = new List<GeoPoint>(ring.Count + 4);
            for (int i = 0; i < ring.Count; i++)
            {
                var (p, q) = (ring[i], ring[(i + 1) % ring.Count]);
                dense.Add(p);
                foreach (double t in roof.Breaks(p.Longitude, p.Latitude, q.Longitude, q.Latitude))
                {
                    double? z = p.Elevation.HasValue && q.Elevation.HasValue ? p.Elevation + t * (q.Elevation - p.Elevation) : p.Elevation;
                    dense.Add(new GeoPoint(p.Latitude + t * (q.Latitude - p.Latitude), p.Longitude + t * (q.Longitude - p.Longitude), z));
                }
            }
            AddRingWalls(triangulation, dense, p => RoofAt(p.Longitude, p.Latitude), building.ComputedFloorAltitude, wallColor);
        }

        Debug.Assert(triangulation.Colors.Count == triangulation.Positions.Count);
        return triangulation;
    }

    private void AddRingWalls(TriangulationList<GeoPoint> triangulation, List<GeoPoint> ring, Func<GeoPoint, double> top, double? floor, Vector4 color)
    {
        int n = ring.Count;
        if (n < 3) return;

        double cosSmooth = Math.Cos(SmoothWallAngleDegrees * Math.PI / 180);
        Vector2 Edge(int i)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            var e = new Vector2((float)(b.Longitude - a.Longitude), (float)(b.Latitude - a.Latitude));
            return e.LengthSquared() > 0 ? Vector2.Normalize(e) : e;
        }
        bool IsSharp(int i) // corner at vertex i, between edges i-1 and i
            => Vector2.Dot(Edge((i - 1 + n) % n), Edge(i)) < cosSmooth;

        // a (top, bottom) vertex pair at ring vertex i, returns the top index (bottom is +1)
        int AddPair(int i)
        {
            int index = triangulation.Positions.Count;
            var p = ring[i];
            triangulation.Positions.Add(p with { Elevation = top(p) });
            triangulation.Positions.Add(p with { Elevation = floor ?? p.Elevation });
            triangulation.Colors!.Add(color);
            triangulation.Colors.Add(color);
            return index;
        }
        void AddFace(int a, int b) // wall quad from pair a to pair b
        {
            triangulation.Indices.Add(a);
            triangulation.Indices.Add(a + 1);
            triangulation.Indices.Add(b);

            triangulation.Indices.Add(a + 1);
            triangulation.Indices.Add(b + 1);
            triangulation.Indices.Add(b);
        }

        // Start at a sharp corner when there is one, so the ring closes on a split
        int start = Enumerable.Range(0, n).FirstOrDefault(IsSharp, -1);
        bool allSmooth = start < 0;
        if (allSmooth) start = 0;

        int first = AddPair(start);
        int previous = first;
        for (int k = 1; k < n; k++)
        {
            int i = (start + k) % n;
            int end = AddPair(i);
            AddFace(previous, end);
            previous = IsSharp(i) ? AddPair(i) : end;
        }
        // close the ring: on the first pair when it is smooth all around, on a new pair at the start corner otherwise
        AddFace(previous, allSmooth ? first : AddPair(start));
    }

    /// <summary>
    /// Simple 3D Buildings: a building outline that contains building parts is not drawn, its parts are.
    /// </summary>
    /// <remarks>A part belongs to the outline that contains one of its interior points.</remarks>
    internal List<BuildingModel> DropOutlinesWithParts(List<BuildingModel> models)
    {
        var parts = models.Where(m => m.IsPart).ToList();
        if (parts.Count == 0)
            return models;

        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
        NetTopologySuite.Geometries.Polygon ToPolygon(BuildingModel m)
            => factory.CreatePolygon([.. m.ExteriorRing.Select(p => new NetTopologySuite.Geometries.Coordinate(p.Longitude, p.Latitude))]);

        var outlines = new NetTopologySuite.Index.Strtree.STRtree<(BuildingModel model, NetTopologySuite.Geometries.Polygon polygon)>();
        foreach (var outline in models.Where(m => !m.IsPart))
        {
            try
            {
                var polygon = ToPolygon(outline);
                outlines.Insert(polygon.EnvelopeInternal, (outline, polygon));
            }
            catch (ArgumentException) { } // invalid ring: kept as is
        }

        var dropped = new HashSet<BuildingModel>(ReferenceEqualityComparer.Instance);
        foreach (var part in parts)
        {
            NetTopologySuite.Geometries.Point inside;
            try
            {
                inside = ToPolygon(part).InteriorPoint;
            }
            catch (ArgumentException) { continue; }
            if (inside == null || inside.IsEmpty) continue;

            foreach (var (outline, polygon) in outlines.Query(inside.EnvelopeInternal))
            {
                if (!dropped.Contains(outline) && polygon.Contains(inside))
                    dropped.Add(outline);
            }
        }

        if (dropped.Count > 0)
            _logger.LogInformation($"Buildings: {dropped.Count:N0} outlines replaced by their {parts.Count:N0} parts.");
        return dropped.Count == 0 ? models : [.. models.Where(m => !dropped.Contains(m))];
    }

    /// <summary>
    /// Roof and floor altitudes, in model units. The ground is the lowest footprint point, so no wall floats on a slope.
    /// <c>height</c> and <c>min_height</c> are measured from the ground (OSM), and scaled by
    /// <see cref="GeoTransformPipeline.ModelUnitsPerMeter"/> so the building keeps its real proportions.
    /// Without <c>min_height</c>, <c>building:min_level</c> floors are skipped, and without either the walls go down
    /// to the terrain at each vertex.
    /// </summary>
    internal BuildingModel ComputeBuildingHeightMeters(BuildingModel building)
    {
        if (building.ComputedRoofAltitude.HasValue)
            return building;

        building.HasHeightInformation = building.Levels.HasValue || building.Height.HasValue || building.MinHeight.HasValue || building.MinLevel.HasValue;

        if (building.Levels.HasValue && building.Height.HasValue)
        {
            if (building.Levels.Value * LevelHeightMeters > building.Height.Value)
            {
                _logger.LogWarning($"Conflicting height info (Levels:{building.Levels.Value} and Height:{building.Height}), height will be used.");
            }
        }

        double unitsPerMeter = Transform?.ModelUnitsPerMeter ?? 1d;
        double ground = building.Points.Min(p => p.Elevation ?? 0);
        double height = building.Height ?? (building.Levels ?? 3) * LevelHeightMeters;
        double? minHeight = building.MinHeight is > 0 ? building.MinHeight
            : building.MinLevel is > 0 ? building.MinLevel * LevelHeightMeters : null;
        if (minHeight >= height)
        {
            _logger.LogDebug($"Building {building.Id}: min_height {minHeight} is not below its height {height}, ignored.");
            minHeight = null;
        }

        building.ComputedRoofAltitude = ground + height * unitsPerMeter;
        building.ComputedFloorAltitude = minHeight.HasValue ? ground + minHeight.Value * unitsPerMeter : null;

        return building;
    }

}
