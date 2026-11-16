using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DEM.Net.Core;
using DEM.Net.Extension.Osm;
using DEM.Net.Extension.Osm.PMTiles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using Xunit;

namespace DEM.Net.Extension.Tests;

public class PMTilesTests
{
    const int Z = 15, TX = 16000, TY = 11000; // two adjacent tiles: (TX, TY) and (TX + 1, TY)

    [Theory]
    [InlineData(0, 0, 0, 0UL)]
    [InlineData(1, 0, 0, 1UL)]
    [InlineData(1, 0, 1, 2UL)]
    [InlineData(1, 1, 1, 3UL)]
    [InlineData(1, 1, 0, 4UL)]
    [InlineData(2, 0, 0, 5UL)]
    [InlineData(12, 3423, 1763, 19078479UL)]
    public void TileId_MatchesSpec(int z, int x, int y, ulong expected)
    {
        Assert.Equal(expected, PMTilesTileId.FromZxy(z, x, y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_ReadsTilesFromRootAndLeafDirectories(bool useLeafDirectory)
    {
        string path = WriteTestArchive(useLeafDirectory);
        try
        {
            using var reader = new PMTilesReader(path);
            Assert.Equal(PMTilesTileType.Mvt, reader.Header.TileType);
            Assert.Equal(15, reader.Header.MaxZoom);
            Assert.NotNull(reader.GetTile(Z, TX, TY));
            Assert.NotNull(reader.GetTile(Z, TX + 1, TY));
            Assert.Null(reader.GetTile(Z, TX + 2, TY));
            Assert.Null(reader.GetTile(Z, TX, TY + 1));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DataService_StitchesBuildingsAcrossTileSeam()
    {
        string path = WriteTestArchive(useLeafDirectory: true);
        try
        {
            var service = CreateService(path);
            var features = service.GetOsmDataAsGeoJson(TestBbox(), new TestFilter("buildings")).ToList();

            // one building fully inside tile A + one building split across the seam => 2 buildings
            Assert.Equal(2, features.Count);
            Assert.All(features, f => Assert.Equal("yes", f.Attributes["building"]));
            Assert.All(features, f => Assert.IsType<Polygon>(f.Geometry));
            Assert.All(features, f => Assert.True(Orientation.IsCCW(((Polygon)f.Geometry).ExteriorRing.CoordinateSequence)));

            var stitched = features.Single(f => (string)f.Attributes.GetOptionalValue("height") == "12.5");
            var env = stitched.Geometry.EnvelopeInternal;
            double seamLon = TileLon(TX + 1);
            Assert.True(env.MinX < seamLon && env.MaxX > seamLon, "Stitched building should span the tile seam");
            Assert.Equal(5, stitched.Geometry.NumPoints); // a clean rectangle: seam vertices removed by the union
            Assert.Equal(160d / 4096 / (1 << Z) * 360, env.Width, 9);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DataService_MergesRoadsAndMapsTags()
    {
        string path = WriteTestArchive(useLeafDirectory: false);
        try
        {
            var service = CreateService(path);
            var roads = service.GetOsmDataAsGeoJson(TestBbox(), new TestFilter("highways")).ToList();

            var road = Assert.Single(roads);
            Assert.IsType<LineString>(road.Geometry);
            Assert.Equal("primary", road.Attributes["highway"]);
            Assert.Equal("Rue Test", road.Attributes["name"]);
            Assert.Equal("yes", road.Attributes["bridge"]);
            Assert.False(road.Attributes.Exists("tunnel"));
            Assert.NotNull(road.Attributes["osmid"]);

            // world px 3000 in tile A -> px 500 in tile B
            double expectedLength = (4096 - 3000 + 500) / 4096d / (1 << Z) * 360;
            Assert.Equal(expectedLength, road.Geometry.EnvelopeInternal.Width, 9);

            // rail features in the roads layer must not leak into highways
            var rails = service.GetOsmDataAsGeoJson(TestBbox(), new TestFilter("railway")).ToList();
            Assert.Equal("rail", Assert.Single(rails).Attributes["railway"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DataService_UnmappedLayerReturnsEmpty()
    {
        string path = WriteTestArchive(useLeafDirectory: false);
        try
        {
            var service = CreateService(path);
            Assert.Empty(service.GetOsmDataAsGeoJson(TestBbox(), new TestFilter("unknown-layer")));
            Assert.Equal(0, service.GetOsmDataCount(TestBbox(), new TestFilter("unknown-layer")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DataStats_CountsVerticesAndLengthWithoutBuildingGeometries()
    {
        string path = WriteTestArchive(useLeafDirectory: false);
        try
        {
            var service = CreateService(path);

            // buildings: 2 rectangles in tile A + the seam building's part in tile B (4 vertices each, ClosePath not counted)
            Assert.Equal(12, service.GetOsmDataStats(TestBbox(), new TestFilter("buildings")).Vertices);
            // the road: 2 vertices per tile; the rail stays out of highways
            var roads = service.GetOsmDataStats(TestBbox(), new TestFilter("highways"));
            Assert.Equal(4, roads.Vertices);
            // both road parts with their tile buffer: (4200 - 3000) + (500 + 1096) world px at z15
            var bbox = TestBbox();
            double metersPerPx = 40075016.686 * Math.Cos((bbox.yMin + bbox.yMax) / 2 * Math.PI / 180) / (4096d * (1 << Z));
            Assert.Equal(2796 * metersPerPx, roads.LengthMeters.Value, 6);
            Assert.Equal(2, service.GetOsmDataStats(TestBbox(), new TestFilter("railway")).Vertices);
            Assert.Equal(new OsmDataStats(0, 0), service.GetOsmDataStats(TestBbox(), new TestFilter("unknown-layer")));
            // historical scale kept for GetOsmDataCount
            Assert.Equal(24, service.GetOsmDataCount(TestBbox(), new TestFilter("buildings")));

            // close to the full query (which adds closing points and seam vertices, then merges the seam building)
            int queried = service.GetOsmDataAsGeoJson(TestBbox(), new TestFilter("buildings")).Sum(f => f.Geometry.NumPoints);
            Assert.InRange(queried, 8, 12);

            // partial bbox: only the small building of tile A (px 1000-1100)
            var small = new BoundingBox(TileLon(TX + 0.2), TileLon(TX + 0.3), TileLat(TY + 0.3), TileLat(TY + 0.2));
            Assert.Equal(4, service.GetOsmDataStats(small, new TestFilter("buildings")).Vertices);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DataService_TooManyTilesThrowsTooLarge()
    {
        string path = WriteTestArchive(useLeafDirectory: false);
        try
        {
            var service = CreateService(path, maxTiles: 1);
            var ex = Assert.Throws<OsmQueryTooLargeException>(() => service.GetOsmDataStats(TestBbox(), new TestFilter("buildings")));
            Assert.Equal(2, ex.TileCount);
            Assert.Throws<OsmQueryTooLargeException>(() => service.GetOsmDataAsGeoJson(TestBbox(), new TestFilter("buildings")));
        }
        finally { File.Delete(path); }
    }

    #region Helpers

    private static PMTilesDataService CreateService(string path, int maxTiles = 2500)
    {
        var options = Options.Create(new OsmElevationOptions
        {
            DataServiceType = OsmDataServiceType.PMTiles,
            PMTilesFilePath = path,
            PMTilesMaxTilesPerQuery = maxTiles,
        });
        var provider = new PMTilesReaderProvider(options, NullLogger<PMTilesReaderProvider>.Instance);
        return new PMTilesDataService(NullLogger<PMTilesDataService>.Instance, provider, options);
    }

    private static double TileLon(double x) => x / (1 << Z) * 360d - 180d;
    private static double TileLat(double y) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / (1 << Z)))) * 180d / Math.PI;

    // Inside both tiles, a bit away from the outer borders
    private static BoundingBox TestBbox() => new BoundingBox(TileLon(TX + 0.05), TileLon(TX + 1.95), TileLat(TY + 0.95), TileLat(TY + 0.05));

    private sealed class TestFilter : IOsmDataSettings
    {
        public TestFilter(string id) => FilterIdentifier = id;
        public string FilterIdentifier { get; }
        public bool ComputeElevations { get; set; }
    }

    private static string WriteTestArchive(bool useLeafDirectory)
    {
        // Tile A: a complete building, the left part of a building and of a road (both with a 64px buffer beyond the seam)
        byte[] tileA = Mvt(
            Layer("buildings",
                Feature(3, Rect(1000, 1000, 1100, 1100), ("kind", "building")),
                Feature(3, Rect(4000, 1000, 4160, 1100), ("kind", "building"), ("height", 12.5))),
            Layer("roads",
                Feature(2, Line((3000, 2000), (4200, 2000)), ("kind", "major_road"), ("kind_detail", "primary"), ("name", "Rue Test"), ("is_bridge", true)),
                Feature(2, Line((3000, 3000), (3500, 3000)), ("kind", "rail"), ("kind_detail", "rail"))));

        // Tile B: right part of the same building and road, in tile B coordinates (buffer extends to negative x)
        byte[] tileB = Mvt(
            Layer("buildings",
                Feature(3, Rect(-96, 1000, 64, 1100), ("kind", "building"), ("height", 12.5))),
            Layer("roads",
                Feature(2, Line((-1096, 2000), (500, 2000)), ("kind", "major_road"), ("kind_detail", "primary"), ("name", "Rue Test"), ("is_bridge", true))));

        var tiles = new[] { (Id: PMTilesTileId.FromZxy(Z, TX, TY), Data: Gzip(tileA)), (Id: PMTilesTileId.FromZxy(Z, TX + 1, TY), Data: Gzip(tileB)) }
            .OrderBy(t => t.Id).ToArray();

        var tileEntries = new List<(ulong TileId, ulong Offset, ulong Length, ulong RunLength)>();
        var tileData = new MemoryStream();
        foreach (var t in tiles)
        {
            tileEntries.Add((t.Id, (ulong)tileData.Length, (ulong)t.Data.Length, 1));
            tileData.Write(t.Data);
        }

        byte[] leaf = Array.Empty<byte>();
        byte[] root;
        if (useLeafDirectory)
        {
            leaf = Gzip(EncodeDirectory(tileEntries));
            root = Gzip(EncodeDirectory(new List<(ulong, ulong, ulong, ulong)> { (tiles[0].Id, 0, (ulong)leaf.Length, 0) }));
        }
        else root = Gzip(EncodeDirectory(tileEntries));

        ulong rootOffset = 127, leafOffset = rootOffset + (ulong)root.Length, tileOffset = leafOffset + (ulong)leaf.Length;
        var header = new byte[127];
        Encoding.ASCII.GetBytes("PMTiles").CopyTo(header, 0);
        header[7] = 3;
        void U64(int at, ulong v) => BitConverter.GetBytes(v).CopyTo(header, at);
        void I32(int at, double deg) => BitConverter.GetBytes((int)Math.Round(deg * 1e7)).CopyTo(header, at);
        U64(8, rootOffset); U64(16, (ulong)root.Length);
        U64(24, tileOffset); U64(32, 0);                      // no metadata
        U64(40, leafOffset); U64(48, (ulong)leaf.Length);
        U64(56, tileOffset); U64(64, (ulong)tileData.Length);
        U64(72, 2); U64(80, 2); U64(88, 2);
        header[96] = 1; header[97] = 2; header[98] = 2; header[99] = 1; header[100] = 0; header[101] = 15;
        I32(102, TileLon(TX)); I32(106, TileLat(TY + 1)); I32(110, TileLon(TX + 2)); I32(114, TileLat(TY));

        string path = Path.Combine(Path.GetTempPath(), $"pmtiles-test-{Guid.NewGuid():N}.pmtiles");
        using (var fs = File.Create(path))
        {
            fs.Write(header);
            fs.Write(root);
            fs.Write(leaf);
            tileData.Position = 0;
            tileData.CopyTo(fs);
        }
        return path;
    }

    private static byte[] EncodeDirectory(List<(ulong TileId, ulong Offset, ulong Length, ulong RunLength)> entries)
    {
        var ms = new MemoryStream();
        WriteVarint(ms, (ulong)entries.Count);
        ulong last = 0;
        foreach (var e in entries) { WriteVarint(ms, e.TileId - last); last = e.TileId; }
        foreach (var e in entries) WriteVarint(ms, e.RunLength);
        foreach (var e in entries) WriteVarint(ms, e.Length);
        foreach (var e in entries) WriteVarint(ms, e.Offset + 1);
        return ms.ToArray();
    }

    private static byte[] Gzip(byte[] data)
    {
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(data);
        return ms.ToArray();
    }

    // --- Minimal MVT encoder ---

    private sealed class TestFeature
    {
        public int Type;
        public List<uint> Geometry;
        public (string Key, object Value)[] Props;
    }

    private static TestFeature Feature(int type, List<uint> geometry, params (string, object)[] props)
        => new TestFeature { Type = type, Geometry = geometry, Props = props };

    private static (string Name, TestFeature[] Features) Layer(string name, params TestFeature[] features) => (name, features);

    private static byte[] Mvt(params (string Name, TestFeature[] Features)[] layers)
    {
        var tile = new MemoryStream();
        foreach (var (name, features) in layers)
        {
            var keys = new List<string>();
            var values = new List<object>();
            var layer = new MemoryStream();
            WriteKey(layer, 15, 0); WriteVarint(layer, 2);
            WriteString(layer, 1, name);
            foreach (var f in features)
            {
                var tags = new List<uint>();
                foreach (var (k, v) in f.Props)
                {
                    if (!keys.Contains(k)) keys.Add(k);
                    int vi = values.FindIndex(x => x.Equals(v));
                    if (vi < 0) { values.Add(v); vi = values.Count - 1; }
                    tags.Add((uint)keys.IndexOf(k));
                    tags.Add((uint)vi);
                }
                var feature = new MemoryStream();
                WritePacked(feature, 2, tags);
                WriteKey(feature, 3, 0); WriteVarint(feature, (ulong)f.Type);
                WritePacked(feature, 4, f.Geometry);
                WriteBytes(layer, 2, feature.ToArray());
            }
            foreach (var k in keys) WriteString(layer, 3, k);
            foreach (var v in values)
            {
                var value = new MemoryStream();
                switch (v)
                {
                    case string s: WriteString(value, 1, s); break;
                    case double d: WriteKey(value, 3, 1); value.Write(BitConverter.GetBytes(d)); break;
                    case bool b: WriteKey(value, 7, 0); WriteVarint(value, b ? 1UL : 0UL); break;
                    default: throw new NotSupportedException(v.GetType().Name);
                }
                WriteBytes(layer, 4, value.ToArray());
            }
            WriteKey(layer, 5, 0); WriteVarint(layer, 4096);
            WriteBytes(tile, 3, layer.ToArray());
        }
        return tile.ToArray();
    }

    // Clockwise on screen (y down) => positive surveyor area => exterior ring per MVT spec
    private static List<uint> Rect(int x0, int y0, int x1, int y1)
        => Commands(true, (x0, y0), (x1, y0), (x1, y1), (x0, y1));

    private static List<uint> Line(params (int X, int Y)[] pts) => Commands(false, pts);

    private static List<uint> Commands(bool close, params (int X, int Y)[] pts)
    {
        var g = new List<uint> { (1 & 7) | (1 << 3) };
        int cx = 0, cy = 0;
        void Add((int X, int Y) p) { g.Add(ZigZag(p.X - cx)); g.Add(ZigZag(p.Y - cy)); cx = p.X; cy = p.Y; }
        Add(pts[0]);
        g.Add((uint)((2 & 7) | ((pts.Length - 1) << 3)));
        foreach (var p in pts.Skip(1)) Add(p);
        if (close) g.Add((7 & 7) | (1 << 3));
        return g;
    }

    private static uint ZigZag(int n) => (uint)((n << 1) ^ (n >> 31));

    private static void WriteVarint(Stream s, ulong v)
    {
        while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
        s.WriteByte((byte)v);
    }

    private static void WriteKey(Stream s, int field, int wire) => WriteVarint(s, (ulong)((field << 3) | wire));

    private static void WriteBytes(Stream s, int field, byte[] data)
    {
        WriteKey(s, field, 2);
        WriteVarint(s, (ulong)data.Length);
        s.Write(data);
    }

    private static void WriteString(Stream s, int field, string str) => WriteBytes(s, field, Encoding.UTF8.GetBytes(str));

    private static void WritePacked(Stream s, int field, List<uint> values)
    {
        var ms = new MemoryStream();
        foreach (var v in values) WriteVarint(ms, v);
        WriteBytes(s, field, ms.ToArray());
    }

    #endregion
}
