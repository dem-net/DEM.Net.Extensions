using System;
using System.Collections.Generic;
using System.Linq;
using DEM.Net.Core;
using DEM.Net.Extension.Osm.Buildings;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Xunit;

namespace DEM.Net.Extension.Tests;

public class RoofTests
{
    private static readonly GeometryFactory Factory = new();

    // x = longitude field, y = latitude field, model units = metres (default transform)
    private static List<GeoPoint> Rect(double w, double h, double z = 0)
        => [new(0, 0, z), new(0, w, z), new(h, w, z), new(h, 0, z), new(0, 0, z)];

    private static Polygon Footprint(double w, double h)
        => Factory.CreatePolygon([new(0, 0), new(w, 0), new(w, h), new(0, h), new(0, 0)]);

    private static BuildingModel House(double w, double h, string? roofShape = null, double? height = null, string? type = null, bool part = false)
        => new(Rect(w, h), null) { RoofShape = roofShape, Height = height, Type = type, IsPart = part, Id = "1" };

    private static OsmBuildingProcessor Processor()
    {
        var processor = new OsmBuildingProcessor(GeoTransformPipeline.Default, withBuildingsColors: false, defaultBuildingsColor: null);
        processor.Init(null!, null!, new MeshService(), null!, NullLogger.Instance);
        return processor;
    }

    [Fact]
    public void GabledRidgeFollowsTheLongSide()
    {
        var roof = RoofGeometry.Create(RoofKind.Gabled, Footprint(20, 10), height: 3)!;

        Assert.Equal(3, roof.HeightAt(10, 5), 9);   // ridge along x, at y = 5
        Assert.Equal(3, roof.HeightAt(1, 5), 9);
        Assert.Equal(0, roof.HeightAt(10, 0), 9);   // eaves on the long sides
        Assert.Equal(1.5, roof.HeightAt(10, 2.5), 9);
        Assert.Equal(2, roof.Faces(Footprint(20, 10)).Count);
        Assert.Equal(200, roof.Faces(Footprint(20, 10)).Sum(f => f.Area), 6);

        // the short sides cross the ridge: one break each, at the middle
        Assert.Equal([0.5], roof.Breaks(0, 0, 0, 10));
        Assert.Empty(roof.Breaks(0, 0, 20, 0));

        // across: the ridge along y
        var across = RoofGeometry.Create(RoofKind.Gabled, Footprint(20, 10), height: 3, across: true)!;
        Assert.Equal(3, across.HeightAt(10, 1), 9);
        Assert.Equal(0, across.HeightAt(0, 5), 9);
    }

    [Fact]
    public void HippedAndPyramidalRoofs()
    {
        var hipped = RoofGeometry.Create(RoofKind.Hipped, Footprint(20, 10), height: 5)!;
        Assert.Equal(4, hipped.Faces(Footprint(20, 10)).Count);
        Assert.Equal(5, hipped.HeightAt(10, 5), 9);   // ridge
        Assert.Equal(0, hipped.HeightAt(0, 5), 9);    // the short ends slope too
        Assert.Equal(2.5, hipped.HeightAt(2.5, 5), 9); // same slope as the sides

        var pyramid = RoofGeometry.Create(RoofKind.Pyramidal, Footprint(10, 10), height: 4)!;
        Assert.Equal(4, pyramid.HeightAt(5, 5), 9);
        Assert.Equal(0, pyramid.HeightAt(0, 5), 9);
        Assert.Equal(4, pyramid.Faces(Footprint(10, 10)).Count);
    }

    [Fact]
    public void SkillionSlopesTowardItsDirection()
    {
        // roof:direction = 90: the slope faces east, so the high side is west
        var roof = RoofGeometry.Create(RoofKind.Skillion, Footprint(10, 10), height: 2, directionDegrees: 90)!;
        Assert.Equal(2, roof.HeightAt(0, 5), 9);
        Assert.Equal(0, roof.HeightAt(10, 5), 9);
        Assert.Single(roof.Faces(Footprint(10, 10)));
    }

    [Fact]
    public void DefaultRoofHeightUsesThePitch()
    {
        double h = RoofGeometry.DefaultHeight(RoofKind.Gabled, Footprint(20, 10));
        Assert.Equal(5 * Math.Tan(Math.PI / 6), h, 9);
    }

    [Theory]
    [InlineData("gabled", RoofKind.Gabled)]
    [InlineData("saltbox", RoofKind.Gabled)]
    [InlineData("half-hipped", RoofKind.Hipped)]
    [InlineData("mansard", RoofKind.Hipped)]
    [InlineData("dome", RoofKind.Pyramidal)]
    [InlineData("lean_to", RoofKind.Skillion)]
    [InlineData("flat", RoofKind.Flat)]
    [InlineData("something", RoofKind.Flat)]
    [InlineData(null, RoofKind.Flat)]
    public void RoofShapesMapToDrawnKinds(string? shape, RoofKind expected)
        => Assert.Equal(expected, RoofGeometry.ParseShape(shape));

    [Theory]
    [InlineData("90", 90d)]
    [InlineData("-90", 270d)]
    [InlineData("NE", 45d)]
    [InlineData("ssw", 202.5d)]
    [InlineData("up", null)]
    public void RoofDirectionParsesDegreesAndCardinals(string value, double? expected)
        => Assert.Equal(expected, BuildingValidator.ParseDirection(value));

    [Fact]
    public void MappedHeightIncludesTheRoof()
    {
        var house = House(20, 10, "gabled", height: 10);
        house.RoofHeight = 3;

        var triangulation = Processor().Triangulate(house);

        double top = triangulation.Positions.Max(p => p.Elevation!.Value);
        Assert.Equal(10, top, 9);
        // eaves at 7: the long walls stop there
        Assert.Contains(triangulation.Positions, p => p.Latitude == 0 && p.Longitude == 20 && Math.Abs(p.Elevation!.Value - 7) < 1e-9);
        // gable ends: a wall vertex at the ridge, up to the top
        Assert.Contains(triangulation.Positions, p => p.Longitude == 0 && Math.Abs(p.Latitude - 5) < 1e-9 && Math.Abs(p.Elevation!.Value - 10) < 1e-9);
    }

    [Fact]
    public void AnEstimatedHeightIsTheWallHeight()
    {
        // Microsoft ML heights are low (4–5 m for Alpine houses): the roof goes on top
        var house = House(20, 10, "gabled", height: 5);
        house.HeightIsEstimated = true;
        house.RoofHeight = 3;

        var triangulation = Processor().Triangulate(house);

        Assert.Equal(8, triangulation.Positions.Max(p => p.Elevation!.Value), 9);
        Assert.True(OsmBuildingProcessor.HasDefaultGabledRoof(new BuildingModel(Rect(12, 9), null) { Height = 14, HeightIsEstimated = true }, Footprint(12, 9), 1));
    }

    [Theory]
    [InlineData("""[{"property":"","dataset":"OpenStreetMap","provider":"osm"},{"property":"/properties/height","dataset":"Microsoft ML Buildings","provider":"microsoft"}]""", true)]
    [InlineData("""[{"property":"","dataset":"Microsoft ML Buildings","provider":"microsoft"}]""", true)]
    [InlineData("""[{"property":"","dataset":"OpenStreetMap","provider":"osm"}]""", false)]
    [InlineData("""[{"property":"","provider":"microsoft"},{"property":"/properties/height","provider":"osm"}]""", false)]
    [InlineData("not json", false)]
    public void EstimatedHeightsAreReadFromTheSources(string sources, bool estimated)
        => Assert.Equal(estimated, DEM.Net.Extension.Osm.PMTiles.OvertureSchema.HeightIsEstimated(new Dictionary<string, object> { ["sources"] = sources }));

    [Fact]
    public void WithoutAHeightTheRoofGoesOnTopOfTheWalls()
    {
        var house = House(20, 10, "hipped");
        house.Levels = 2;
        house.RoofHeight = 3;

        var triangulation = Processor().Triangulate(house);

        Assert.Equal(6 + 3, triangulation.Positions.Max(p => p.Elevation!.Value), 9);
    }

    [Fact]
    public void RoofTrianglesFaceUp()
    {
        var triangulation = Processor().Triangulate(House(20, 10, "hipped", height: 10));

        var pos = triangulation.Positions;
        int up = 0;
        for (int t = 0; t < triangulation.Indices.Count; t += 3)
        {
            var (a, b, c) = (pos[triangulation.Indices[t]], pos[triangulation.Indices[t + 1]], pos[triangulation.Indices[t + 2]]);
            double cross = (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);
            if (Math.Abs(cross) < 1e-9) continue; // a wall
            Assert.True(cross > 0, "roof triangles must be counter-clockwise from above");
            up++;
        }
        Assert.True(up >= 4);
    }

    [Fact]
    public void SmallUnmappedHousesGetAGabledRoof()
    {
        var p = Processor();
        Assert.True(OsmBuildingProcessor.HasDefaultGabledRoof(House(12, 9), Footprint(12, 9), 1));
        Assert.False(OsmBuildingProcessor.HasDefaultGabledRoof(House(12, 9, type: "commercial"), Footprint(12, 9), 1));
        Assert.False(OsmBuildingProcessor.HasDefaultGabledRoof(House(12, 9, part: true), Footprint(12, 9), 1));
        Assert.False(OsmBuildingProcessor.HasDefaultGabledRoof(House(12, 9, roofShape: "flat"), Footprint(12, 9), 1));
        Assert.False(OsmBuildingProcessor.HasDefaultGabledRoof(House(12, 9, height: 20), Footprint(12, 9), 1));
        Assert.False(OsmBuildingProcessor.HasDefaultGabledRoof(House(30, 20), Footprint(30, 20), 1)); // 600 m²
        Assert.False(OsmBuildingProcessor.HasDefaultGabledRoof(House(4, 4), Footprint(4, 4), 1));     // a shed

        // an L-shaped house isn't rectangular enough
        var l = Factory.CreatePolygon([new(0, 0), new(12, 0), new(12, 4), new(4, 4), new(4, 12), new(0, 12), new(0, 0)]);
        Assert.False(OsmBuildingProcessor.HasDefaultGabledRoof(House(12, 12), l, 1));

        // drawn: higher than the flat roof would be, with sloped faces
        var house = House(12, 9);
        var triangulation = p.Triangulate(house);
        Assert.Equal(9 + 4.5 * Math.Tan(Math.PI / 6), triangulation.Positions.Max(q => q.Elevation!.Value), 6);
    }

    [Theory]
    [InlineData("gabled")]
    [InlineData("hipped")]
    [InlineData("pyramidal")]
    [InlineData("skillion")]
    public void ConcaveFootprintsAreCoveredOnce(string shape)
    {
        // an L and a U: some faces fall apart in two pieces
        (double, double)[][] outlines =
        [
            [(0, 0), (20, 0), (20, 6), (6, 6), (6, 14), (0, 14)],
            [(0, 0), (24, 0), (24, 14), (18, 14), (18, 5), (6, 5), (6, 14), (0, 14)],
        ];
        foreach (var outline in outlines)
        {
            var ring = outline.Select(p => new GeoPoint(p.Item2, p.Item1, 0)).ToList();
            ring.Add(ring[0]);
            var house = new BuildingModel(ring, null) { RoofShape = shape, Height = 10, Id = "1" };

            var triangulation = Processor().Triangulate(house);

            var pos = triangulation.Positions;
            double roofArea = 0;
            for (int t = 0; t < triangulation.Indices.Count; t += 3)
            {
                var (a, b, c) = (pos[triangulation.Indices[t]], pos[triangulation.Indices[t + 1]], pos[triangulation.Indices[t + 2]]);
                double cross = (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);
                Assert.True(cross > -1e-9, "roof triangles face up");
                roofArea += cross / 2; // walls are vertical: no area seen from above
            }
            double footprintArea = Math.Abs(RoofFootprint.SignedArea([.. outline]));
            Assert.Equal(footprintArea, roofArea, 6);
            Assert.True(pos.Max(p => p.Elevation!.Value) <= 10 + 1e-9);
        }
    }

    [Fact]
    public void FlatRoofsAreUnchanged()
    {
        var triangulation = Processor().Triangulate(House(30, 20, height: 10));
        Assert.All(triangulation.Positions, q => Assert.True(q.Elevation is 0 or 10));
        Assert.Equal(4 + 16, triangulation.Positions.Count); // the roof's 4 corners, two vertex pairs per sharp wall corner
    }
}
