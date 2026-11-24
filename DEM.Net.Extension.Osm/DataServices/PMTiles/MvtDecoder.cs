using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NetTopologySuite.Geometries;

namespace DEM.Net.Extension.Osm.PMTiles;

internal enum MvtGeometryType { Unknown = 0, Point = 1, LineString = 2, Polygon = 3 }

internal sealed class MvtLayer
{
    public string Name = null!;
    public uint Extent = 4096;
    public readonly List<string> Keys = [];
    public readonly List<object> Values = [];
    public readonly List<MvtFeature> Features = [];

    public Dictionary<string, object> GetProperties(MvtFeature feature)
    {
        var props = new Dictionary<string, object>(feature.Tags.Count / 2, StringComparer.Ordinal);
        for (int i = 0; i + 1 < feature.Tags.Count; i += 2)
        {
            int k = (int)feature.Tags[i], v = (int)feature.Tags[i + 1];
            if (k < Keys.Count && v < Values.Count && Values[v] != null)
                props[Keys[k]] = Values[v];
        }
        return props;
    }
}

internal sealed class MvtFeature
{
    public ulong? Id;
    public MvtGeometryType Type;
    public readonly List<uint> Tags = [];
    public readonly List<uint> Geometry = [];
}

/// <summary>
/// Minimal Mapbox Vector Tile (v2) decoder. https://github.com/mapbox/vector-tile-spec/tree/master/2.1
/// </summary>
internal static class MvtDecoder
{
    /// <summary>World units per tile used for decoded coordinates (independent of each layer's extent).</summary>
    public const double WorldUnitsPerTile = 4096d;

    public static List<MvtLayer> Decode(byte[] tile, ISet<string>? wantedLayers = null)
    {
        var layers = new List<MvtLayer>();
        var reader = new ProtoReader(tile, 0, tile.Length);
        while (reader.HasMore)
        {
            var (field, wire) = reader.ReadKey();
            if (field == 3 && wire == ProtoReader.WireLengthDelimited)
            {
                var layerReader = reader.ReadMessage();
                if (wantedLayers == null || wantedLayers.Contains(PeekLayerName(layerReader.Clone())!))
                    layers.Add(ReadLayer(layerReader));
            }
            else reader.Skip(wire);
        }
        return layers;
    }

    private static string? PeekLayerName(ProtoReader r)
    {
        while (r.HasMore)
        {
            var (field, wire) = r.ReadKey();
            if (field == 1 && wire == ProtoReader.WireLengthDelimited) return r.ReadString();
            r.Skip(wire);
        }
        return null;
    }

    private static MvtLayer ReadLayer(ProtoReader r)
    {
        var layer = new MvtLayer();
        while (r.HasMore)
        {
            var (field, wire) = r.ReadKey();
            switch (field)
            {
                case 1: layer.Name = r.ReadString(); break;
                case 2: layer.Features.Add(ReadFeature(r.ReadMessage())); break;
                case 3: layer.Keys.Add(r.ReadString()); break;
                case 4: layer.Values.Add(ReadValue(r.ReadMessage())!); break;
                case 5: layer.Extent = (uint)r.ReadVarint(); break;
                default: r.Skip(wire); break; // 15 = version
            }
        }
        if (layer.Extent == 0) layer.Extent = 4096;
        return layer;
    }

    private static MvtFeature ReadFeature(ProtoReader r)
    {
        var f = new MvtFeature();
        while (r.HasMore)
        {
            var (field, wire) = r.ReadKey();
            switch (field)
            {
                case 1: f.Id = r.ReadVarint(); break;
                case 2: r.ReadUInt32s(wire, f.Tags); break;
                case 3: f.Type = (MvtGeometryType)r.ReadVarint(); break;
                case 4: r.ReadUInt32s(wire, f.Geometry); break;
                default: r.Skip(wire); break;
            }
        }
        return f;
    }

    private static object? ReadValue(ProtoReader r)
    {
        object? value = null;
        while (r.HasMore)
        {
            var (field, wire) = r.ReadKey();
            switch (field)
            {
                case 1: value = r.ReadString(); break;
                case 2: value = (double)r.ReadFloat(); break;
                case 3: value = r.ReadDouble(); break;
                case 4: value = (long)r.ReadVarint(); break;
                case 5: value = r.ReadVarint(); break;
                case 6: value = ZigZag64(r.ReadVarint()); break;
                case 7: value = r.ReadVarint() != 0; break;
                default: r.Skip(wire); break;
            }
        }
        return value;
    }

    /// <summary>
    /// Decodes the feature geometry into world coordinates: (tileX + px / extent) * <see cref="WorldUnitsPerTile"/>.
    /// Y axis points down (tile convention). Returns null for empty/degenerate geometries.
    /// </summary>
    public static Geometry? ToGeometry(MvtFeature feature, int tileX, int tileY, uint extent, GeometryFactory factory)
    {
        double scale = WorldUnitsPerTile / extent;
        double ox = tileX * WorldUnitsPerTile, oy = tileY * WorldUnitsPerTile;

        var parts = new List<List<Coordinate>>();
        List<Coordinate>? current = null;
        long cx = 0, cy = 0;
        var g = feature.Geometry;
        int i = 0;
        while (i < g.Count)
        {
            uint cmd = g[i++];
            int id = (int)(cmd & 0x7);
            int count = (int)(cmd >> 3);
            switch (id)
            {
                case 1: // MoveTo
                    for (int k = 0; k < count && i + 1 < g.Count; k++)
                    {
                        cx += ZigZag32(g[i++]);
                        cy += ZigZag32(g[i++]);
                        current = [new(ox + cx * scale, oy + cy * scale)];
                        parts.Add(current);
                    }
                    break;
                case 2: // LineTo
                    if (current == null) throw new InvalidDataException("MVT LineTo without MoveTo.");
                    for (int k = 0; k < count && i + 1 < g.Count; k++)
                    {
                        cx += ZigZag32(g[i++]);
                        cy += ZigZag32(g[i++]);
                        current.Add(new Coordinate(ox + cx * scale, oy + cy * scale));
                    }
                    break;
                case 7: // ClosePath
                    if (current != null && current.Count > 0) current.Add(current[0].Copy());
                    break;
                default:
                    throw new InvalidDataException($"Unknown MVT geometry command {id}.");
            }
        }

        switch (feature.Type)
        {
            case MvtGeometryType.Point:
                if (parts.Count == 0) return null;
                if (parts.Count == 1) return factory.CreatePoint(parts[0][0]);
                var points = new Point[parts.Count];
                for (int p = 0; p < parts.Count; p++) points[p] = factory.CreatePoint(parts[p][0]);
                return factory.CreateMultiPoint(points);

            case MvtGeometryType.LineString:
                var lines = new List<LineString>(parts.Count);
                foreach (var part in parts)
                    if (part.Count >= 2) lines.Add(factory.CreateLineString([.. part]));
                if (lines.Count == 0) return null;
                return lines.Count == 1 ? (Geometry)lines[0] : factory.CreateMultiLineString([.. lines]);

            case MvtGeometryType.Polygon:
                return BuildPolygons(parts, factory);

            default:
                return null;
        }
    }

    /// <summary>
    /// Measures a feature without building a geometry, for fast size estimates, in world coordinates:
    /// <list type="bullet">
    /// <item>vertices (MoveTo + LineTo; the ClosePath repeat is not counted) and length of the LineTo segments, both
    /// within the tile's own extent only: vertices in the half-open extent, segments clipped to it, so the fragments of a
    /// feature cut by tile seams add up to the feature itself (the buffer copies and the clip points are not counted);</item>
    /// <item>the envelope, the area of the exterior rings and the number of holes of the whole decoded geometry (buffer
    /// included).</item>
    /// </list>
    /// </summary>
    public static MvtFeatureSize Measure(MvtFeature feature, int tileX, int tileY, uint extent)
    {
        double scale = WorldUnitsPerTile / extent;
        double ox = tileX * WorldUnitsPerTile, oy = tileY * WorldUnitsPerTile;
        long cx = 0, cy = 0, minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
        long sx = 0, sy = 0; // ring start
        int decoded = 0, vertices = 0, holes = 0;
        double length = 0, ring2 = 0, area2 = 0; // twice the signed areas, in tile units
        var g = feature.Geometry;
        int i = 0;
        while (i < g.Count)
        {
            uint cmd = g[i++];
            int id = (int)(cmd & 0x7);
            int count = (int)(cmd >> 3);
            if (id == 7) // ClosePath has no parameters
            {
                // MVT: exterior rings have a positive area (y down), holes a negative one
                ring2 += (double)cx * sy - (double)sx * cy;
                if (ring2 > 0) area2 += ring2;
                else if (ring2 < 0) holes++;
                ring2 = 0;
                continue;
            }
            if (id != 1 && id != 2) break; // malformed: count what was read
            for (int k = 0; k < count && i + 1 < g.Count; k++)
            {
                int dx = ZigZag32(g[i++]), dy = ZigZag32(g[i++]);
                if (id == 2)
                {
                    length += ClippedLength(cx, cy, cx + dx, cy + dy, extent);
                    ring2 += (double)cx * (cy + dy) - (double)(cx + dx) * cy;
                }
                cx += dx;
                cy += dy;
                if (id == 1) { sx = cx; sy = cy; ring2 = 0; }
                decoded++;
                if (cx >= 0 && cx < extent && cy >= 0 && cy < extent) vertices++;
                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cy < minY) minY = cy;
                if (cy > maxY) maxY = cy;
            }
        }
        var envelope = decoded == 0
            ? new Envelope()
            : new Envelope(ox + minX * scale, ox + maxX * scale, oy + minY * scale, oy + maxY * scale);
        return new MvtFeatureSize(vertices, length * scale, envelope, area2 / 2 * scale * scale, holes);
    }

    /// <summary>Length of the segment inside the square [0, extent]² (Liang–Barsky clipping).</summary>
    internal static double ClippedLength(long x0, long y0, long x1, long y1, uint extent)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double t0 = 0, t1 = 1;
        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0; // parallel to this border: inside or outside
            double t = q / p;
            if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
        if (!Clip(-dx, x0) || !Clip(dx, extent - x0) || !Clip(-dy, y0) || !Clip(dy, extent - y0))
            return 0;
        return (t1 - t0) * Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// MVT spec: exterior rings have positive surveyor area in tile coordinates (y down), interior rings negative.
    /// </summary>
    private static Geometry? BuildPolygons(List<List<Coordinate>> rings, GeometryFactory factory)
    {
        var polygons = new List<Polygon>();
        LinearRing? shell = null;
        var holes = new List<LinearRing>();

        void Flush()
        {
            if (shell != null) polygons.Add(factory.CreatePolygon(shell, [.. holes]));
            shell = null;
            holes.Clear();
        }

        foreach (var ring in rings)
        {
            if (ring.Count < 4) continue;
            if (!ring[0].Equals2D(ring[ring.Count - 1])) ring.Add(ring[0].Copy());

            double area = SignedArea(ring);
            if (area == 0) continue;

            var linearRing = factory.CreateLinearRing([.. ring]);
            if (area > 0 || shell == null)
            {
                Flush();
                shell = linearRing;
            }
            else holes.Add(linearRing);
        }
        Flush();

        if (polygons.Count == 0) return null;
        return polygons.Count == 1 ? (Geometry)polygons[0] : factory.CreateMultiPolygon([.. polygons]);
    }

    // Relative to the first vertex to keep precision with large world coordinates
    private static double SignedArea(List<Coordinate> ring)
    {
        double x0 = ring[0].X, y0 = ring[0].Y, sum = 0;
        for (int i = 0; i < ring.Count - 1; i++)
        {
            double x1 = ring[i].X - x0, y1 = ring[i].Y - y0;
            double x2 = ring[i + 1].X - x0, y2 = ring[i + 1].Y - y0;
            sum += x1 * y2 - x2 * y1;
        }
        return sum / 2;
    }

    private static int ZigZag32(uint n) => (int)(n >> 1) ^ -(int)(n & 1);
    private static long ZigZag64(ulong n) => (long)(n >> 1) ^ -(long)(n & 1);
}

/// <summary>What <see cref="MvtDecoder.Measure"/> reads from a feature, in world units.</summary>
/// <param name="Vertices">Vertices within the tile's own extent.</param>
/// <param name="Length">Length of the line segments clipped to the tile's own extent.</param>
/// <param name="Envelope">Envelope of the whole decoded geometry (tile buffer included).</param>
/// <param name="Area">Area of the exterior rings (holes not subtracted), tile buffer included.</param>
/// <param name="Holes">Interior rings.</param>
internal readonly record struct MvtFeatureSize(int Vertices, double Length, Envelope Envelope, double Area, int Holes);

/// <summary>Just enough protobuf wire-format reading for MVT.</summary>
internal sealed class ProtoReader(byte[] buffer, int start, int length)
{
    public const int WireVarint = 0, Wire64 = 1, WireLengthDelimited = 2, Wire32 = 5;

    private readonly byte[] _buffer = buffer;
    private readonly int _end = start + length;
    private int _pos = start;

    public bool HasMore => _pos < _end;

    public ProtoReader Clone() => new(_buffer, _pos, _end - _pos);

    public ulong ReadVarint()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            if (_pos >= _end) throw new InvalidDataException("Truncated protobuf varint.");
            byte b = _buffer[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift >= 64) throw new InvalidDataException("Protobuf varint too long.");
        }
    }

    public (int Field, int WireType) ReadKey()
    {
        ulong key = ReadVarint();
        return ((int)(key >> 3), (int)(key & 0x7));
    }

    private int ReadLength()
    {
        int length = checked((int)ReadVarint());
        if (length < 0 || _pos + length > _end) throw new InvalidDataException("Protobuf length exceeds message bounds.");
        return length;
    }

    public ProtoReader ReadMessage()
    {
        int length = ReadLength();
        var sub = new ProtoReader(_buffer, _pos, length);
        _pos += length;
        return sub;
    }

    public string ReadString()
    {
        int length = ReadLength();
        string s = Encoding.UTF8.GetString(_buffer, _pos, length);
        _pos += length;
        return s;
    }

    public double ReadDouble()
    {
        EnsureAvailable(8);
        double v = BinaryPrimitives.ReadDoubleLittleEndian(_buffer.AsSpan(_pos, 8));
        _pos += 8;
        return v;
    }

    public float ReadFloat()
    {
        EnsureAvailable(4);
        float v = BinaryPrimitives.ReadSingleLittleEndian(_buffer.AsSpan(_pos, 4));
        _pos += 4;
        return v;
    }

    /// <summary>Reads a repeated uint32 field, packed or not.</summary>
    public void ReadUInt32s(int wireType, List<uint> target)
    {
        if (wireType == WireLengthDelimited)
        {
            var packed = ReadMessage();
            while (packed.HasMore) target.Add((uint)packed.ReadVarint());
        }
        else target.Add((uint)ReadVarint());
    }

    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case WireVarint: ReadVarint(); break;
            case Wire64: EnsureAvailable(8); _pos += 8; break;
            case WireLengthDelimited: _pos += ReadLength(); break;
            case Wire32: EnsureAvailable(4); _pos += 4; break;
            default: throw new InvalidDataException($"Unsupported protobuf wire type {wireType}.");
        }
    }

    private void EnsureAvailable(int count)
    {
        if (_pos + count > _end) throw new InvalidDataException("Truncated protobuf message.");
    }
}
