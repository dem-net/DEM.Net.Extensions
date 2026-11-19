using System;
using System.Collections.Generic;
using System.Globalization;

namespace DEM.Net.Extension.Osm.PMTiles;

/// <summary>
/// Maps one DEM.Net OSM layer (identified by <see cref="IOsmDataSettings.FilterIdentifier"/>) to Protomaps basemap
/// layers, and translates Protomaps properties back to the OSM tags expected by the existing validators.
/// Schema reference: https://docs.protomaps.com/basemaps/layers (v4 "kind"/"kind_detail", v3 "pmap:kind").
/// </summary>
internal sealed class ProtomapsLayerMapping
{
    public string FilterIdentifier { get; set; } = null!;
    public string[] SourceLayers { get; set; } = null!;
    public Func<string, IReadOnlyDictionary<string, object>, MvtGeometryType, bool> Accept { get; set; } = null!;
    public Func<IReadOnlyDictionary<string, object>, MvtGeometryType, IDictionary<string, object>> ToOsmTags { get; set; } = null!;

    /// <summary>Some validators (water) only accept MultiPolygon, not Polygon.</summary>
    public bool PolygonsAsMultiPolygon { get; set; }
}

internal static class ProtomapsSchema
{
    private static readonly HashSet<string> NonHighwayRoadKinds = new(StringComparer.Ordinal)
    {
        "rail", "ferry", "aerialway", "transit", "aeroway", "pier"
    };

    private static readonly Dictionary<string, ProtomapsLayerMapping> Mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["buildings"] = new ProtomapsLayerMapping
        {
            FilterIdentifier = "buildings",
            SourceLayers = ["buildings"],
            Accept = (layer, p, type) => type == MvtGeometryType.Polygon,
            ToOsmTags = (p, type) =>
            {
                var tags = new Dictionary<string, object>
                {
                    ["building"] = Str(p, "kind_detail") ?? "yes",
                };
                if (Kind(p) == "building_part") tags["building:part"] = "yes";
                Copy(p, tags, "height", "height");
                Copy(p, tags, "min_height", "min_height");
                Copy(p, tags, "name", "name");
                return tags;
            },
        },

        ["highways"] = new ProtomapsLayerMapping
        {
            FilterIdentifier = "highways",
            SourceLayers = ["roads"],
            Accept = (layer, p, type) => type == MvtGeometryType.LineString && Kind(p) is string k && !NonHighwayRoadKinds.Contains(k),
            ToOsmTags = (p, type) =>
            {
                var tags = new Dictionary<string, object>
                {
                    ["highway"] = Str(p, "kind_detail") ?? HighwayFromKind(Kind(p)),
                };
                Copy(p, tags, "name", "name");
                Copy(p, tags, "ref", "ref");
                CopyFirst(p, tags, "layer", "layer", "level");
                Flag(p, tags, "tunnel", "is_tunnel", "pmap:is_tunnel");
                Flag(p, tags, "bridge", "is_bridge", "pmap:is_bridge");
                return tags;
            },
        },

        // v4 puts railways in "roads" (kind=rail); v3 had them in "transit"
        ["railway"] = new ProtomapsLayerMapping
        {
            FilterIdentifier = "railway",
            SourceLayers = ["roads", "transit"],
            Accept = (layer, p, type) => type == MvtGeometryType.LineString && Kind(p) == "rail",
            ToOsmTags = (p, type) =>
            {
                var tags = new Dictionary<string, object>
                {
                    ["railway"] = Str(p, "kind_detail") ?? "rail",
                };
                Copy(p, tags, "name", "name");
                CopyFirst(p, tags, "layer", "layer", "level");
                Flag(p, tags, "tunnel", "is_tunnel", "pmap:is_tunnel");
                Flag(p, tags, "bridge", "is_bridge", "pmap:is_bridge");
                return tags;
            },
        },

        ["water"] = new ProtomapsLayerMapping
        {
            FilterIdentifier = "water",
            SourceLayers = ["water"],
            Accept = (layer, p, type) => type == MvtGeometryType.Polygon || type == MvtGeometryType.LineString,
            PolygonsAsMultiPolygon = true,
            ToOsmTags = (p, type) =>
            {
                var tags = new Dictionary<string, object>();
                string? detail = Str(p, "kind_detail") ?? Kind(p);
                if (type == MvtGeometryType.Polygon)
                {
                    tags["natural"] = "water";
                    if (detail != null) tags["water"] = detail;
                }
                else tags["waterway"] = detail ?? "stream";
                Copy(p, tags, "name", "name");
                return tags;
            },
        },

    };

    public static ProtomapsLayerMapping? Find(string? filterIdentifier)
        => filterIdentifier != null && Mappings.TryGetValue(filterIdentifier, out var m) ? m : null;

    internal static string? Kind(IReadOnlyDictionary<string, object> p) => Str(p, "kind") ?? Str(p, "pmap:kind");

    private static string? Str(IReadOnlyDictionary<string, object> p, string key)
    {
        if (!p.TryGetValue(key, out var v) || v == null) return null;
        if (v is string s) return s.Length == 0 ? null : s;
        return Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    // OSM tags are strings; keep that contract so ParseTag/ParseLengthTag behave as with OSM data
    private static void Copy(IReadOnlyDictionary<string, object> p, IDictionary<string, object> tags, string from, string to)
    {
        var s = Str(p, from) ?? (from.StartsWith("pmap:") ? null : Str(p, "pmap:" + from));
        if (s != null) tags[to] = s;
    }

    private static void CopyFirst(IReadOnlyDictionary<string, object> p, IDictionary<string, object> tags, string to, params string[] from)
    {
        foreach (var key in from)
        {
            var s = Str(p, key);
            if (s != null) { tags[to] = s; return; }
        }
    }

    private static void Flag(IReadOnlyDictionary<string, object> p, IDictionary<string, object> tags, string to, params string[] from)
    {
        foreach (var key in from)
        {
            if (p.TryGetValue(key, out var v) && (v is bool b ? b : string.Equals(Convert.ToString(v, CultureInfo.InvariantCulture), "true", StringComparison.OrdinalIgnoreCase)))
            {
                tags[to] = "yes";
                return;
            }
        }
    }

    private static string HighwayFromKind(string? kind)
    {
        return kind switch
        {
            "highway" => "motorway",
            "major_road" => "primary",
            "medium_road" => "secondary",
            "minor_road" => "residential",
            "path" => "path",
            _ => "road",
        };
    }
}
