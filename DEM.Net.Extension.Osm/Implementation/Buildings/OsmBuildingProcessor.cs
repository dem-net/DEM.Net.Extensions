using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using DEM.Net.Core;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;

namespace DEM.Net.Extension.Osm.Buildings;


public class OsmBuildingProcessor(GeoTransformPipeline? transformPipeline, bool withBuildingsColors, string? defaultBuildingsColor) : OsmProcessorStage<BuildingModel>(transformPipeline)
{

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

        gltfModel = _gltfService.AddMesh(gltfModel, glTFNodeName, new IndexedTriangulation(triangulation), null, null, doubleSided: true);

        return gltfModel;

    }

    protected override IEnumerable<BuildingModel> ComputeModelElevationsAndTransform(IEnumerable<BuildingModel> modelsEnum, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
    {
        List<GeoPoint> transformedPoints;

        //// georeference

        List<BuildingModel> models = DropOutlinesWithParts([.. modelsEnum]);
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

            foreach (var building in buildingModels)
            {
                numTotal++;
                numWithHeight += building.HasHeightInformation ? 1 : 0;
                numWithColor += (building.Color.HasValue || building.RoofColor.HasValue) ? 1 : 0;

                var triangulation = this.Triangulate(building);

                var positionsVec3 = triangulation.Positions.ToVector3GlTFSpace().ToList();
                var buildingNormals = _meshService.ComputeMeshNormals(positionsVec3, triangulation.Indices);

                int initialPositionsCount = positions.Count;

                positions.AddRange(positionsVec3);
                foreach (int index in triangulation.Indices)
                    indices.Add(index + initialPositionsCount);
                colors.AddRange(triangulation.Colors!);
                normals.AddRange(buildingNormals);
            }

            _logger.LogInformation($"Building heights: {numWithHeight:N0}/{numTotal:N0} ({numWithHeight / (float)numTotal:P}) with height information.");
            _logger.LogInformation($"Building colors: {numWithColor:N0}/{numTotal:N0} ({numWithColor / (float)numTotal:P}) with color information.");
        }


        return new TriangulationNormals(positions, indices, normals, colors);
    }
    /// <summary>
    /// Flat-roofed building: the footprint triangulation as the roof, and the walls from the floor to the roof.
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
        TriangulationList<GeoPoint> triangulation = _meshService.Tesselate(rings[0], rings.Skip(1));
        for (int i = 0; i < triangulation.Positions.Count; i++)
        {
            triangulation.Positions[i] = triangulation.Positions[i] with { Elevation = building.ComputedRoofAltitude };
        }

        Vector4 defaultColor = Vector4.One;
        Vector4 wallColor = building.Color ?? defaultColor;
        Vector4 roofColor = building.RoofColor ?? wallColor;
        triangulation.Colors = [.. triangulation.Positions.Select(_ => roofColor)];

        //==========================
        // Walls, ring by ring
        //
        foreach (var ring in rings)
        {
            AddRingWalls(triangulation, ring, building, wallColor);
        }

        Debug.Assert(triangulation.Colors.Count == triangulation.Positions.Count);

        return triangulation;
    }

    /// <summary>
    /// Outline turns below this angle share their wall vertices (smooth normals across the corner)
    /// </summary>
    internal const double SmoothWallAngleDegrees = 30;

    private void AddRingWalls(TriangulationList<GeoPoint> triangulation, List<GeoPoint> ring, BuildingModel building, Vector4 color)
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
            triangulation.Positions.Add(p with { Elevation = building.ComputedRoofAltitude });
            triangulation.Positions.Add(p with { Elevation = building.ComputedFloorAltitude ?? p.Elevation });
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
    /// Without <c>min_height</c>, the walls go down to the terrain at each vertex.
    /// </summary>
    internal BuildingModel ComputeBuildingHeightMeters(BuildingModel building)
    {
        if (building.ComputedRoofAltitude.HasValue)
            return building;

        building.HasHeightInformation = building.Levels.HasValue || building.Height.HasValue || building.MinHeight.HasValue;

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
        double? minHeight = building.MinHeight is > 0 ? building.MinHeight : null;
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
