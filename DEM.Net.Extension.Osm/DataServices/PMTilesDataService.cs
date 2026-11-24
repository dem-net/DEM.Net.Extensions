using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DEM.Net.Core;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Extension.Osm.PMTiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Simplify;

namespace DEM.Net.Extension.Osm;

/// <summary>
/// Opens the configured PMTiles archives once and shares them (file handle + directory cache) across requests:
/// the Protomaps basemap, and the optional Overture buildings archive.
/// </summary>
public sealed class PMTilesReaderProvider : IDisposable
{
    private readonly Lazy<PMTilesReader> _reader;
    private readonly Lazy<PMTilesReader>? _buildingsReader;

    public PMTilesReaderProvider(IOptions<OsmElevationOptions> options, ILogger<PMTilesReaderProvider> logger)
    {
        Lazy<PMTilesReader> Open(Func<string?> path, string optionName) => new(() =>
        {
            string? p = path();
            if (string.IsNullOrWhiteSpace(p))
                throw new InvalidOperationException($"{nameof(OsmElevationOptions)}.{optionName} must be set when {nameof(OsmElevationOptions.DataServiceType)} is {OsmDataServiceType.PMTiles}.");

            var reader = new PMTilesReader(p);
            if (reader.Header.TileType != PMTilesTileType.Mvt)
            {
                reader.Dispose();
                throw new InvalidDataException($"{p} does not contain vector tiles (tile type {reader.Header.TileType}).");
            }
            logger.LogInformation("Opened {Path}: {Header}", p, reader.Header);
            return reader;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        _reader = Open(() => options.Value.PMTilesFilePath, nameof(OsmElevationOptions.PMTilesFilePath));
        if (!string.IsNullOrWhiteSpace(options.Value.BuildingsPMTilesFilePath))
            _buildingsReader = Open(() => options.Value.BuildingsPMTilesFilePath, nameof(OsmElevationOptions.BuildingsPMTilesFilePath));
    }

    /// <summary>The Protomaps basemap.</summary>
    public PMTilesReader Reader => _reader.Value;

    /// <summary>The Overture buildings archive.</summary>
    /// <exception cref="InvalidOperationException">No buildings archive is configured.</exception>
    public PMTilesReader BuildingsReader => _buildingsReader?.Value
        ?? throw new InvalidOperationException($"{nameof(OsmElevationOptions)}.{nameof(OsmElevationOptions.BuildingsPMTilesFilePath)} is not set.");

    public void Dispose()
    {
        if (_reader.IsValueCreated) _reader.Value.Dispose();
        if (_buildingsReader?.IsValueCreated == true) _buildingsReader.Value.Dispose();
    }
}

/// <summary>
/// <see cref="IOsmDataService"/> backed by a local Protomaps basemap PMTiles archive (https://maps.protomaps.com/builds/).
/// Features are read at the archive max zoom, clipped to exact tile bounds, stitched back across tile seams and
/// returned with OSM-like tags so the existing validators can process them unchanged.
/// Buildings come from the Overture archive instead when <see cref="OsmElevationOptions.BuildingsPMTilesFilePath"/> is set.
/// </summary>
public class PMTilesDataService(ILogger<PMTilesDataService> logger, PMTilesReaderProvider readerProvider,
    IOptions<OsmElevationOptions> options, RoofPriorProvider? roofPriorProvider = null) : IOsmDataService
{
    private const double W = MvtDecoder.WorldUnitsPerTile;
    private const double BorderEpsilon = 1e-6;
    private const double EarthCircumferenceMeters = 40075016.686;

    // World coordinates (4096 units per tile at the query zoom, y down). Snapping to 1 unit ≈ 0.3 m at z15.
    private static readonly GeometryFactory WorldFactory = new();
    private static readonly PrecisionModel SnapPrecision = new(1.0);
    private static readonly GeometryFactory LonLatFactory = new(new PrecisionModel(), Reprojection.SRID_GEODETIC);

    private readonly ILogger<PMTilesDataService> _logger = logger;
    private readonly PMTilesReaderProvider _readerProvider = readerProvider;
    private readonly OsmElevationOptions _options = options.Value;
    private readonly RoofPriorProvider? _roofPriorProvider = roofPriorProvider;

    public IEnumerable<IFeature> GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataSettings filter)
    {
        var mapping = FindMapping(filter?.FilterIdentifier);
        if (mapping == null)
        {
            _logger.LogWarning("Layer {Layer} is not in the Protomaps schema, no data returned.", filter?.FilterIdentifier);
            return [];
        }

        using (new TimeSpanBlock(nameof(GetOsmDataAsGeoJson), _logger, LogLevel.Debug))
        {
            var features = Query(bbox, mapping);
            _logger.LogInformation("{Count} {Layer} features read from PMTiles", features.Count, mapping.FilterIdentifier);
            return features;
        }
    }

    public int GetOsmDataCount(BoundingBox bbox, IOsmDataSettings filter)
    {
        var mapping = FindMapping(filter?.FilterIdentifier);
        if (mapping == null)
            return 0;

        // Node count * 2, the scale of the former Overpass API service, which the size estimates were calibrated with
        return CountVertices(bbox, mapping).Vertices * 2;
    }

    public OsmDataStats GetOsmDataStats(BoundingBox bbox, IOsmDataSettings filter)
    {
        var mapping = FindMapping(filter?.FilterIdentifier);
        if (mapping == null)
            return new OsmDataStats(0, 0);

        using (new TimeSpanBlock(nameof(GetOsmDataStats), _logger, LogLevel.Debug))
            return CountVertices(bbox, mapping);
    }

    /// <summary>The Overture mapping for buildings when its archive is configured, the Protomaps one otherwise.</summary>
    internal ProtomapsLayerMapping? FindMapping(string? filterIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(_options.BuildingsPMTilesFilePath) && OvertureSchema.Find(filterIdentifier) is { } overture)
            return overture;
        return ProtomapsSchema.Find(filterIdentifier);
    }

    /// <summary>
    /// Fast estimate: decodes the tiles but builds no geometry (no clipping or seam merging). Counts the vertices and
    /// line length of accepted features whose envelope intersects the bbox, within each tile's own extent (not its
    /// buffer), so features crossing tile edges add up to their stitched size. Each feature is counted once: by its
    /// MVT id when the archive has ids, otherwise in the tile holding the centre of its envelope. Buildings also get
    /// their expected pitched roofs (<see cref="OsmBuildingProcessor.PitchedRoofChance"/>).
    /// </summary>
    internal OsmDataStats CountVertices(BoundingBox bbox, ProtomapsLayerMapping mapping)
    {
        if (bbox.SRID != Reprojection.SRID_GEODETIC)
            bbox = bbox.ReprojectTo(bbox.SRID, Reprojection.SRID_GEODETIC);

        var range = GetTileRange(bbox, mapping);
        var wantedLayers = new HashSet<string>(mapping.SourceLayers, StringComparer.Ordinal);
        var bboxEnv = range.BboxWorldEnvelope;
        var total = new Tally();
        var sync = new Lock();
        var counted = new ConcurrentDictionary<(string Layer, ulong Id), byte>();

        // Web Mercator scale at the bbox center latitude
        double worldSize = W * (1L << range.Zoom);
        double centerLatRad = (bbox.yMin + bbox.yMax) / 2d * Math.PI / 180d;
        double metersPerWorldUnit = EarthCircumferenceMeters * Math.Cos(centerLatRad) / worldSize;
        bool buildings = string.Equals(mapping.FilterIdentifier, "buildings", StringComparison.OrdinalIgnoreCase);
        RoofPrior? roofPrior = buildings ? _roofPriorProvider?.Prior : null;

        Parallel.For(0, (int)range.TileCount, () => new Tally(), (idx, _, local) =>
        {
            int tx = range.MinTx + idx % range.Cols, ty = range.MinTy + idx / range.Cols;
            byte[]? data = range.Reader.GetTile(range.Zoom, tx, ty);
            if (data == null || data.Length == 0) return local;

            var tileEnv = new Envelope(tx * W, (tx + 1) * W, ty * W, (ty + 1) * W);
            foreach (var layer in MvtDecoder.Decode(data, wantedLayers))
                foreach (var mvtFeature in layer.Features)
                {
                    var props = layer.GetProperties(mvtFeature);
                    if (!mapping.Accept(layer.Name, props, mvtFeature.Type)) continue;
                    var size = MvtDecoder.Measure(mvtFeature, tx, ty, layer.Extent);
                    if (size.Envelope.IsNull || !bboxEnv.Intersects(size.Envelope)) continue;
                    local.Vertices += size.Vertices;
                    local.Length += size.Length;

                    bool first = mvtFeature.Id is ulong id && id != 0
                        ? counted.TryAdd((layer.Name, id), 0)
                        : ContainsHalfOpen(tileEnv, size.Envelope.Centre);
                    if (!first) continue;
                    local.Features++;
                    if (mapping.HasColor?.Invoke(props) == true) local.Colored++;
                    if (buildings)
                    {
                        var (lon, lat) = TileMath.WorldToLonLat(size.Envelope.Centre.X, size.Envelope.Centre.Y, worldSize);
                        double areaM2 = size.Area * metersPerWorldUnit * metersPerWorldUnit;
                        local.PitchedRoofs += OsmBuildingProcessor.PitchedRoofChance(roofPrior, mapping.ToOsmTags(props, mvtFeature.Type), lon, lat, areaM2, size.Holes > 0);
                    }
                }
            return local;
        }, local =>
        {
            lock (sync) total.Add(local);
        });

        return new OsmDataStats((int)Math.Min(total.Vertices, int.MaxValue), total.Length * metersPerWorldUnit,
            (int)Math.Min(total.Features, int.MaxValue), (int)Math.Min(total.Colored, int.MaxValue), total.PitchedRoofs);
    }

    private sealed class Tally
    {
        public long Vertices, Features, Colored;
        public double Length, PitchedRoofs;

        public void Add(Tally t)
        {
            Vertices += t.Vertices;
            Features += t.Features;
            Colored += t.Colored;
            Length += t.Length;
            PitchedRoofs += t.PitchedRoofs;
        }
    }

    private static bool ContainsHalfOpen(Envelope tile, Coordinate c)
        => c.X >= tile.MinX && c.X < tile.MaxX && c.Y >= tile.MinY && c.Y < tile.MaxY;

    private sealed class TileRange
    {
        public PMTilesReader Reader = null!;
        public int Zoom, MinTx, MinTy, Cols, Rows;
        public long TileCount => (long)Cols * Rows;
        public Envelope BboxWorldEnvelope = null!;
    }

    /// <exception cref="OsmQueryTooLargeException">More tiles than <see cref="OsmElevationOptions.PMTilesMaxTilesPerQuery"/>.</exception>
    private TileRange GetTileRange(BoundingBox bbox, ProtomapsLayerMapping mapping)
    {
        if (bbox.SRID != Reprojection.SRID_GEODETIC)
            bbox = bbox.ReprojectTo(bbox.SRID, Reprojection.SRID_GEODETIC);

        // PMTilesZoom is for the Protomaps archive; the buildings archive is read at its max zoom
        PMTilesReader reader = mapping.BuildingsArchive ? _readerProvider.BuildingsReader : _readerProvider.Reader;
        int? zoom = mapping.BuildingsArchive ? null : _options.PMTilesZoom;
        int z = Math.Clamp(zoom ?? reader.Header.MaxZoom, reader.Header.MinZoom, reader.Header.MaxZoom);

        var (minTx, minTy) = TileMath.LonLatToTile(bbox.xMin, bbox.yMax, z);
        var (maxTx, maxTy) = TileMath.LonLatToTile(bbox.xMax, bbox.yMin, z);
        var range = new TileRange { Reader = reader, Zoom = z, MinTx = minTx, MinTy = minTy, Cols = maxTx - minTx + 1, Rows = maxTy - minTy + 1 };
        if (range.TileCount > _options.PMTilesMaxTilesPerQuery)
            throw new OsmQueryTooLargeException(range.TileCount, _options.PMTilesMaxTilesPerQuery, z);

        double worldSize = W * (1L << z);
        var (bx0, by0) = TileMath.LonLatToWorld(bbox.xMin, bbox.yMax, worldSize);
        var (bx1, by1) = TileMath.LonLatToWorld(bbox.xMax, bbox.yMin, worldSize);
        range.BboxWorldEnvelope = new Envelope(bx0, bx1, by0, by1);
        return range;
    }

    internal List<IFeature> Query(BoundingBox bbox, ProtomapsLayerMapping mapping)
    {
        if (bbox.SRID != Reprojection.SRID_GEODETIC)
            bbox = bbox.ReprojectTo(bbox.SRID, Reprojection.SRID_GEODETIC);

        var range = GetTileRange(bbox, mapping);
        PMTilesReader reader = range.Reader;
        int z = range.Zoom, minTx = range.MinTx, minTy = range.MinTy, cols = range.Cols;
        long tileCount = range.TileCount;

        var wantedLayers = new HashSet<string>(mapping.SourceLayers, StringComparer.Ordinal);
        var complete = new ConcurrentBag<Fragment>();
        var seam = new ConcurrentBag<Fragment>();

        Parallel.For(0, (int)tileCount, idx =>
        {
            int tx = minTx + idx % cols, ty = minTy + idx / cols;
            byte[]? data = reader.GetTile(z, tx, ty);
            if (data == null || data.Length == 0) return;

            var tileEnv = new Envelope(tx * W, (tx + 1) * W, ty * W, (ty + 1) * W);
            int featureIndex = 0;
            foreach (var layer in MvtDecoder.Decode(data, wantedLayers))
            {
                foreach (var mvtFeature in layer.Features)
                {
                    featureIndex++;
                    var props = layer.GetProperties(mvtFeature);
                    if (!mapping.Accept(layer.Name, props, mvtFeature.Type)) continue;

                    Geometry? geom;
                    try { geom = MvtDecoder.ToGeometry(mvtFeature, tx, ty, layer.Extent, WorldFactory); }
                    catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException)
                    {
                        _logger.LogDebug("Skipping invalid MVT geometry in {Z}/{X}/{Y} {Layer}: {Message}", z, tx, ty, layer.Name, ex.Message);
                        continue;
                    }
                    if (geom == null || geom.IsEmpty) continue;

                    string syntheticId = $"pmtiles/{z}/{tx}/{ty}/{layer.Name}/{featureIndex}";
                    foreach (var part in ClipToTile(geom, mvtFeature.Type, tileEnv))
                    {
                        var fragment = new Fragment(layer.Name, props, mvtFeature.Id, mvtFeature.Type, part, syntheticId);
                        if (mvtFeature.Type != MvtGeometryType.Point && TouchesBorder(part.EnvelopeInternal, tileEnv))
                            seam.Add(fragment);
                        else
                            complete.Add(fragment);
                    }
                }
            }
        });

        var fragments = new List<Fragment>(complete);
        fragments.AddRange(MergeSeamFragments(seam));

        return ToFeatures(fragments, mapping, bbox, z);
    }


    private IEnumerable<Geometry> ClipToTile(Geometry geom, MvtGeometryType type, Envelope tileEnv)
    {
        if (type == MvtGeometryType.Point)
        {
            // Half-open tile bounds: a point in the buffer belongs to exactly one tile
            return PointExtracter.GetPoints(geom).Cast<Point>()
                .Where(p => p.X >= tileEnv.MinX && p.X < tileEnv.MaxX && p.Y >= tileEnv.MinY && p.Y < tileEnv.MaxY)
                .ToList();
        }

        Geometry? clipped = tileEnv.Contains(geom.EnvelopeInternal) ? geom : SafeIntersection(geom, WorldFactory.ToGeometry(tileEnv), type);
        if (clipped == null || clipped.IsEmpty) return [];

        return type == MvtGeometryType.Polygon
            ? PolygonExtracter.GetPolygons(clipped).Where(g => !g.IsEmpty && g.Area > 0).ToList()
            : [.. LineStringExtracter.GetLines(clipped).Where(g => !g.IsEmpty && g.Length > 0)];
    }

    private Geometry? SafeIntersection(Geometry geom, Geometry clip, MvtGeometryType type)
    {
        try
        {
            return OverlayNGRobust.Overlay(geom, clip, SpatialFunction.Intersection);
        }
        catch (Exception ex) when (type == MvtGeometryType.Polygon)
        {
            try { return OverlayNGRobust.Overlay(geom.Buffer(0), clip, SpatialFunction.Intersection); }
            catch
            {
                _logger.LogDebug("Could not clip polygon to tile: {Message}", ex.Message);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not clip geometry to tile: {Message}", ex.Message);
            return null;
        }
    }

    private static bool TouchesBorder(Envelope e, Envelope tile)
        => e.MinX <= tile.MinX + BorderEpsilon || e.MaxX >= tile.MaxX - BorderEpsilon
        || e.MinY <= tile.MinY + BorderEpsilon || e.MaxY >= tile.MaxY - BorderEpsilon;

    /// <summary>
    /// Fragments cut by a tile border are regrouped (by feature id when the archive has ids, otherwise by layer +
    /// attributes) then unioned (polygons) or line-merged (lines).
    /// Caveat: without ids, adjacent features with identical attributes along a seam (e.g. terraced houses) merge.
    /// </summary>
    private IEnumerable<Fragment> MergeSeamFragments(IEnumerable<Fragment> seamFragments)
    {
        foreach (var group in seamFragments.GroupBy(f => f.MergeKey))
        {
            var parts = group.OrderBy(f => f.SyntheticId, StringComparer.Ordinal).ToList();
            if (parts.Count == 1)
            {
                yield return parts[0];
                continue;
            }

            List<Geometry> merged;
            try
            {
                if (parts[0].Type == MvtGeometryType.Polygon)
                {
                    var union = UnaryUnionNG.Union(WorldFactory.BuildGeometry(parts.Select(p => p.Geometry)), SnapPrecision);
                    // Drop the now collinear vertices left on the former seam (~3 mm tolerance at z15)
                    union = DouglasPeuckerSimplifier.Simplify(union, 0.01);
                    merged = [.. PolygonExtracter.GetPolygons(union).Where(g => !g.IsEmpty)];
                }
                else
                {
                    var merger = new LineMerger();
                    foreach (var p in parts) merger.Add(SnapEndpoints((LineString)p.Geometry));
                    merged = [.. merger.GetMergedLineStrings().Cast<Geometry>()];
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Seam merge failed for {Key}, keeping {Count} fragments: {Message}", group.Key, parts.Count, ex.Message);
                merged = [.. parts.Select(p => p.Geometry)];
            }

            foreach (var g in merged)
                yield return parts[0].WithGeometry(g);
        }
    }

    // Both sides of a seam clip at the same exact border coordinate; rounding the free axis makes endpoints match.
    private static LineString SnapEndpoints(LineString line)
    {
        var coords = line.Coordinates.Select(c => c.Copy()).ToArray();
        foreach (var c in new[] { coords[0], coords[coords.Length - 1] })
        {
            c.X = Math.Round(c.X);
            c.Y = Math.Round(c.Y);
        }
        return WorldFactory.CreateLineString(coords);
    }


    private List<IFeature> ToFeatures(List<Fragment> fragments, ProtomapsLayerMapping mapping, BoundingBox bbox, int z)
    {
        double worldSize = W * (1L << z);
        var (bx0, by0) = TileMath.LonLatToWorld(bbox.xMin, bbox.yMax, worldSize);
        var (bx1, by1) = TileMath.LonLatToWorld(bbox.xMax, bbox.yMin, worldSize);
        var bboxEnv = new Envelope(bx0, bx1, by0, by1);
        var bboxGeom = WorldFactory.ToGeometry(bboxEnv);
        var toLonLat = new WorldToLonLatFilter(worldSize);

        var features = new List<IFeature>(fragments.Count);
        foreach (var fragment in fragments.OrderBy(f => f.SyntheticId, StringComparer.Ordinal))
        {
            var g = fragment.Geometry;
            if (!bboxEnv.Intersects(g.EnvelopeInternal) || !g.Intersects(bboxGeom)) continue;

            g = g.Copy();
            g.Apply(toLonLat);
            g = LonLatFactory.CreateGeometry(g);
            if (g is Polygon polygon)
            {
                polygon = ToRfc7946Orientation(polygon);
                g = mapping.PolygonsAsMultiPolygon ? (Geometry)LonLatFactory.CreateMultiPolygon([polygon]) : polygon;
            }

            var attributes = new AttributesTable
            {
                { "osmid", fragment.Id.HasValue && fragment.Id.Value != 0 ? fragment.Id.Value.ToString() : fragment.SyntheticId }
            };
            foreach (var tag in mapping.ToOsmTags(fragment.Properties, fragment.Type))
                attributes.Add(tag.Key, tag.Value);

            features.Add(new Feature(g, attributes));
        }
        return features;
    }

    // Exterior CCW, holes CW (the y-axis flip from tile to lat/lon inverts MVT winding)
    private static Polygon ToRfc7946Orientation(Polygon p)
    {
        LinearRing Orient(LinearRing ring, bool ccw)
            => Orientation.IsCCW(ring.CoordinateSequence) == ccw ? ring : (LinearRing)ring.Reverse();

        var shell = Orient((LinearRing)p.ExteriorRing, true);
        var holes = p.InteriorRings.Select(r => Orient((LinearRing)r, false)).ToArray();
        return LonLatFactory.CreatePolygon(shell, holes);
    }

    private sealed class Fragment(string layer, IReadOnlyDictionary<string, object> properties, ulong? id, MvtGeometryType type, Geometry geometry, string syntheticId)
    {
        public string Layer { get; } = layer;
        public IReadOnlyDictionary<string, object> Properties { get; } = properties;
        public ulong? Id { get; } = id;
        public MvtGeometryType Type { get; } = type;
        public Geometry Geometry { get; } = geometry;
        public string SyntheticId { get; } = syntheticId;

        public string MergeKey
        {
            get
            {
                var sb = new StringBuilder(Layer).Append('|').Append((int)Type).Append('|');
                if (Id.HasValue && Id.Value != 0) return sb.Append("id:").Append(Id.Value).ToString();
                foreach (var kv in Properties.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
                return sb.ToString();
            }
        }

        public Fragment WithGeometry(Geometry g) => new(Layer, Properties, Id, Type, g, SyntheticId);
    }

    private sealed class WorldToLonLatFilter(double worldSize) : ICoordinateSequenceFilter
    {
        private readonly double _worldSize = worldSize;

        public bool Done => false;
        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            var (lon, lat) = TileMath.WorldToLonLat(seq.GetX(i), seq.GetY(i), _worldSize);
            seq.SetX(i, lon);
            seq.SetY(i, lat);
        }
    }
}

internal static class TileMath
{
    private const double MaxLat = 85.0511287798066;

    public static (int X, int Y) LonLatToTile(double lon, double lat, int z)
    {
        int n = 1 << z;
        var (wx, wy) = LonLatToWorld(lon, lat, n);
        return (Math.Clamp((int)Math.Floor(wx), 0, n - 1), Math.Clamp((int)Math.Floor(wy), 0, n - 1));
    }

    /// <summary>Web mercator, origin top-left, y down, world spans [0, worldSize].</summary>
    public static (double X, double Y) LonLatToWorld(double lon, double lat, double worldSize)
    {
        double latRad = Math.Clamp(lat, -MaxLat, MaxLat) * Math.PI / 180d;
        double x = (lon + 180d) / 360d * worldSize;
        double y = (1d - Math.Log(Math.Tan(latRad) + 1d / Math.Cos(latRad)) / Math.PI) / 2d * worldSize;
        return (x, y);
    }

    public static (double Lon, double Lat) WorldToLonLat(double x, double y, double worldSize)
    {
        double lon = x / worldSize * 360d - 180d;
        double lat = Math.Atan(Math.Sinh(Math.PI * (1d - 2d * y / worldSize))) * 180d / Math.PI;
        return (lon, lat);
    }
}
