using System.Collections.Generic;
using System.Linq;
using DEM.Net.Core;
using DEM.Net.Extension.Osm;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Extension.Osm.Highways;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DEM.Net.Extension.Tests;

/// <summary>Buildings, bridges, tunnels and road widths (offline)</summary>
public class OsmAccuracyTests
{
    /// <summary>Closed ring (last point = first), in model coordinates: (x, y) horizontal, z elevation</summary>
    private static List<GeoPoint> Ring(params (double x, double y, double z)[] pts)
        => [.. pts.Append(pts[0]).Select(p => new GeoPoint(p.y, p.x, p.z))];

    private static List<GeoPoint> Box(double x, double y, double size, double z = 0)
        => Ring((x, y, z), (x + size, y, z), (x + size, y + size, z), (x, y + size, z));

    private static BuildingModel Building(List<GeoPoint> ring, double? height = null, double? minHeight = null, bool part = false, string id = "1")
        => new(ring, null) { Height = height, MinHeight = minHeight, IsPart = part, Id = id };

    private static OsmBuildingProcessor Processor(GeoTransformPipeline? transform = null)
    {
        var processor = new OsmBuildingProcessor(transform ?? GeoTransformPipeline.Default, withBuildingsColors: false, defaultBuildingsColor: null);
        processor.Init(null!, null!, new MeshService(), null!, NullLogger.Instance);
        return processor;
    }

    // ---- Buildings

    [Fact]
    public void MinHeightIsMeasuredFromTheGround()
    {
        // Empire State Building upper part: height 443 m, min_height 381 m, on a 10-12 m footprint
        var part = Building(Ring((0, 0, 10), (50, 0, 12), (50, 50, 11), (0, 50, 10)), height: 443, minHeight: 381, part: true);

        Processor().ComputeBuildingHeightMeters(part);

        Assert.Equal(10 + 381, part.ComputedFloorAltitude);
        Assert.Equal(10 + 443, part.ComputedRoofAltitude);
    }

    [Fact]
    public void WithoutMinHeightTheWallsGoDownToTheTerrain()
    {
        var building = Building(Ring((0, 0, 10), (50, 0, 14), (50, 50, 12), (0, 50, 10)), height: 20);

        var triangulation = Processor().Triangulate(building);

        Assert.Null(building.ComputedFloorAltitude);
        Assert.Equal(30, building.ComputedRoofAltitude); // lowest ground + height: no wall floats on the slope
        Assert.Contains(triangulation.Positions, p => p.Elevation == 14); // the floor follows the terrain
    }

    [Fact]
    public void HeightsKeepTheirProportionsInWebMercator()
    {
        // without true scale, model units are Web Mercator metres: 1/cos(lat) per metre
        var bbox = new BoundingBox(-73.99, -73.98, 40.745, 40.755);
        var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_GEODETIC, Reprojection.SRID_PROJECTED_MERCATOR, true, 1f, false);
        var building = Building(Box(0, 0, 50), height: 100, minHeight: 50);

        Processor(transform).ComputeBuildingHeightMeters(building);

        Assert.Equal(100 * transform.ModelUnitsPerMeter, building.ComputedRoofAltitude!.Value, 6);
        Assert.Equal(50 * transform.ModelUnitsPerMeter, building.ComputedFloorAltitude!.Value, 6);
    }

    [Fact]
    public void MinLevelSetsTheFloorWithoutMinHeight()
    {
        // an overhang starting at the 4th floor (building:min_level=3, Overture min_floor)
        var part = Building(Ring((0, 0, 10), (50, 0, 12), (50, 50, 11), (0, 50, 10)), height: 30, part: true);
        part.MinLevel = 3;

        Processor().ComputeBuildingHeightMeters(part);

        Assert.Equal(10 + 9, part.ComputedFloorAltitude);
        Assert.True(part.HasHeightInformation);

        // min_height wins over min_level
        var measured = Building(Box(0, 0, 50), height: 30, minHeight: 12);
        measured.MinLevel = 3;
        Processor().ComputeBuildingHeightMeters(measured);
        Assert.Equal(12, measured.ComputedFloorAltitude);
    }

    [Fact]
    public void MinHeightAboveTheHeightIsIgnored()
    {
        var building = Building(Box(0, 0, 50), height: 10, minHeight: 20);

        Processor().ComputeBuildingHeightMeters(building);

        Assert.Null(building.ComputedFloorAltitude);
    }

    [Fact]
    public void OutlinesWithPartsAreDropped()
    {
        var outline = Building(Box(0, 0, 100), id: "outline");
        var lonely = Building(Box(500, 0, 100), id: "lonely");
        var parts = new[]
        {
            Building(Box(10, 10, 80), height: 300, part: true, id: "base"),
            Building(Box(30, 30, 40), height: 400, minHeight: 300, part: true, id: "tower"),
        };

        var kept = Processor().DropOutlinesWithParts([outline, lonely, .. parts]);

        Assert.Equal(["lonely", "base", "tower"], kept.Select(b => b.Id));
    }

    [Fact]
    public void RoofHasItsOwnVerticesAndBoxWallsAreFlat()
    {
        var building = Building(Box(0, 0, 50), height: 20);

        var triangulation = Processor().Triangulate(building);

        // 4 roof vertices, and 4 walls of 4 vertices each: no vertex is shared between the roof and a wall,
        // or between two walls at a right-angle corner
        Assert.Equal(4 + 4 * 4, triangulation.Positions.Count);
        Assert.Equal(2 * 3 + 4 * 2 * 3, triangulation.Indices.Count);
        var roofIndices = triangulation.Indices.Take(6).ToHashSet();
        var wallIndices = triangulation.Indices.Skip(6).ToHashSet();
        Assert.Empty(roofIndices.Intersect(wallIndices));
        Assert.All(roofIndices, i => Assert.Equal(20, triangulation.Positions[i].Elevation));
        Assert.Equal(triangulation.Positions.Count, triangulation.Colors!.Count);
    }

    [Fact]
    public void CurvedWallsShareTheirVertices()
    {
        // 32-sided round tower: 11.25° turns, smooth
        var circle = Enumerable.Range(0, 32).Select(i => (System.Math.Cos(i * System.Math.PI / 16) * 20, System.Math.Sin(i * System.Math.PI / 16) * 20, 0d)).ToArray();
        var building = Building(Ring(circle), height: 20);

        var triangulation = Processor().Triangulate(building);

        Assert.Equal(32 + 32 * 2, triangulation.Positions.Count); // roof, and one top/bottom pair per vertex
    }

    // ---- Bridges, tunnels, widths

    [Fact]
    public void BridgeDeckIsInterpolatedBetweenItsEnds()
    {
        // a bridge over a 20 m deep valley, ends at 100 m and 110 m
        var demPoints = Enumerable.Range(0, 11).Select(i => new GeoPoint(45, 6 + i * 0.001, i is 0 or 10 ? 100 + i : 80)).ToList();

        var deck = OsmLineElevation.Elevate(demPoints, bridge: true);

        for (int i = 0; i < deck.Count; i++)
            Assert.Equal(100 + i + OsmLineElevation.HeightAboveTerrainMeters, deck[i].Elevation!.Value, 6);
    }

    [Fact]
    public void BridgeDeckStaysAboveTheTerrain()
    {
        // a hump in the middle (a DSM sampling the deck itself, or a ridge)
        var demPoints = Enumerable.Range(0, 5).Select(i => new GeoPoint(45, 6 + i * 0.001, i == 2 ? 130 : 100)).ToList();

        var deck = OsmLineElevation.Elevate(demPoints, bridge: true);

        Assert.Equal(130 + OsmLineElevation.HeightAboveTerrainMeters, deck[2].Elevation!.Value, 6);
        Assert.Equal(100 + OsmLineElevation.HeightAboveTerrainMeters, deck[1].Elevation!.Value, 6);
    }

    [Fact]
    public void RoadsFollowTheTerrain()
    {
        var demPoints = Enumerable.Range(0, 5).Select(i => new GeoPoint(45, 6 + i * 0.001, 100 + i * i)).ToList();

        var road = OsmLineElevation.Elevate(demPoints, bridge: false);

        Assert.Equal(demPoints.Select(p => p.Elevation!.Value + OsmLineElevation.HeightAboveTerrainMeters), road.Select(p => p.Elevation!.Value));
    }

    [Theory]
    [InlineData("tunnel", "yes", false)]
    [InlineData("bridge", "yes", true)]
    [InlineData("name", "Rue Test", true)]
    public void TunnelsAreNotDrawn(string tag, string value, bool drawn)
    {
        var road = new HighwayModel([new GeoPoint(45, 6), new GeoPoint(45, 6.01)])
        {
            Tags = new Dictionary<string, object> { ["highway"] = "primary", [tag] = value },
        };

        Assert.Equal(drawn, new HighwayValidator(NullLogger.Instance).ParseTags(road));
    }

    [Theory]
    [InlineData("motorway", null, 14)]
    [InlineData("residential", null, 6)]
    [InlineData("footway", null, 2)]
    [InlineData("primary", 4, 14)]
    [InlineData("unknown", null, 4)]
    public void RoadWidthsDependOnTheType(string type, int? lanes, float width)
    {
        Assert.Equal(width, OsmLineElevation.RoadWidthMeters(type, lanes));
    }
}
