using System.Linq;
using DEM.Net.Core;
using DEM.Net.Extension.Osm;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using Xunit;

namespace DEM.Net.Extension.Tests;

public class OsmFeatureClipperTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);
    private static readonly BoundingBox Bbox = new(0, 10, 0, 10);

    private static Feature Feature(Geometry geom, long id = 1) => new(geom, new AttributesTable { { "osmid", id } });

    private static Polygon Square(double xMin, double yMin, double size) => Factory.CreatePolygon(
    [
        new Coordinate(xMin, yMin), new Coordinate(xMin + size, yMin), new Coordinate(xMin + size, yMin + size),
        new Coordinate(xMin, yMin + size), new Coordinate(xMin, yMin),
    ]);

    [Fact]
    public void KeepsInsideFeaturesAndDropsOutsideOnes()
    {
        var inside = Feature(Square(2, 2, 1));
        var outside = Feature(Square(20, 20, 1));

        var result = OsmFeatureClipper.ClipToBoundingBox([inside, outside], Bbox).ToList();

        Assert.Same(inside, Assert.Single(result));
    }

    [Fact]
    public void CutsPolygonsAtTheEdge()
    {
        var result = OsmFeatureClipper.ClipToBoundingBox([Feature(Square(8, 8, 4), 42)], Bbox).ToList();

        var clipped = Assert.Single(result);
        Assert.Equal(OgcGeometryType.Polygon, clipped.Geometry.OgcGeometryType);
        Assert.Equal(4, clipped.Geometry.Area, 6);
        Assert.Equal(42L, clipped.Attributes["osmid"]);
        Assert.Equal(4326, clipped.Geometry.SRID);
    }

    [Fact]
    public void SplitsLinesLeavingAndReenteringTheBox()
    {
        // in, out through the east edge, back in
        var line = Factory.CreateLineString(
        [
            new Coordinate(5, 2), new Coordinate(15, 2), new Coordinate(15, 8), new Coordinate(5, 8),
        ]);

        var result = OsmFeatureClipper.ClipToBoundingBox([Feature(line)], Bbox).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Equal(OgcGeometryType.LineString, f.Geometry.OgcGeometryType));
        Assert.Equal(10, result.Sum(f => f.Geometry.Length), 6);
        Assert.All(result, f => Assert.True(f.Geometry.EnvelopeInternal.MaxX <= 10));
    }

    [Fact]
    public void KeepsMultiPolygonsAsOneMultiPolygon()
    {
        var multi = Factory.CreateMultiPolygon([Square(1, 1, 2), Square(9, 9, 4)]);

        var clipped = Assert.Single(OsmFeatureClipper.ClipToBoundingBox([Feature(multi)], Bbox));

        Assert.Equal(OgcGeometryType.MultiPolygon, clipped.Geometry.OgcGeometryType);
        Assert.Equal(4 + 1, clipped.Geometry.Area, 6);
    }
}
