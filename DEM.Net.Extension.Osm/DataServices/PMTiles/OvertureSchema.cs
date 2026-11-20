using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace DEM.Net.Extension.Osm.PMTiles;

/// <summary>
/// Maps the Overture Maps buildings archive (layers <c>building</c> and <c>building_part</c>, schema:
/// https://docs.overturemaps.org/schema/reference/buildings/building/) to the OSM tags read by the building validator.
/// Overture keeps the OSM attributes Protomaps drops (floors, roof shape, colours, materials) and adds heights from
/// other sources. Underground buildings are skipped.
/// </summary>
internal static class OvertureSchema
{
    public static readonly ProtomapsLayerMapping Buildings = new()
    {
        FilterIdentifier = "buildings",
        SourceLayers = ["building", "building_part"],
        BuildingsArchive = true,
        HasColor = p => Str(p, "facade_color") != null || Str(p, "roof_color") != null,
        Accept = (layer, p, type) => type == MvtGeometryType.Polygon && !IsTrue(p, "is_underground"),
        ToOsmTags = (p, type) =>
        {
            var tags = new Dictionary<string, object>
            {
                ["building"] = Str(p, "class") ?? Str(p, "subtype") ?? "yes",
            };
            // Only parts have a building_id (the id of their outline)
            if (Str(p, "building_id") != null) tags["building:part"] = "yes";
            Copy(p, tags, "height", "height");
            if (tags.ContainsKey("height") && HeightIsEstimated(p)) tags[EstimatedHeightTag] = "yes";
            Copy(p, tags, "min_height", "min_height");
            Integer(p, tags, "num_floors", "building:levels");
            Integer(p, tags, "min_floor", "building:min_level");
            Copy(p, tags, "roof_shape", "roof:shape");
            Copy(p, tags, "roof_height", "roof:height");
            Copy(p, tags, "roof_direction", "roof:direction");
            Copy(p, tags, "roof_orientation", "roof:orientation");
            Copy(p, tags, "facade_color", "building:colour");
            Copy(p, tags, "roof_color", "roof:colour");
            Copy(p, tags, "facade_material", "building:material");
            Copy(p, tags, "roof_material", "roof:material");
            Copy(p, tags, "@name", "name");
            return tags;
        },
    };

    /// <summary>Not an OSM tag: set when the height is a machine-learning estimate, not a mapped value.</summary>
    public const string EstimatedHeightTag = "dem:height_estimated";

    /// <summary>
    /// The height comes from Microsoft ML Buildings: either only the height (a source with property
    /// <c>/properties/height</c>), or the whole building. <c>@height_source</c> can't be trusted for this: it names
    /// OpenStreetMap for ML heights put on OSM footprints. ML heights are low (median 4–5 m for Alpine houses):
    /// closer to an eave height than to the ridge.
    /// </summary>
    internal static bool HeightIsEstimated(IReadOnlyDictionary<string, object> p)
    {
        if (Str(p, "sources") is not string json) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;
            string? whole = null, height = null;
            foreach (var s in doc.RootElement.EnumerateArray())
            {
                string? property = s.TryGetProperty("property", out var pr) ? pr.GetString() : null;
                string? provider = s.TryGetProperty("provider", out var pv) ? pv.GetString() : null;
                if (property == "/properties/height") height = provider;
                else if (string.IsNullOrEmpty(property)) whole ??= provider;
            }
            return string.Equals(height ?? whole, "microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    public static ProtomapsLayerMapping? Find(string? filterIdentifier)
        => string.Equals(filterIdentifier, Buildings.FilterIdentifier, StringComparison.OrdinalIgnoreCase) ? Buildings : null;

    private static string? Str(IReadOnlyDictionary<string, object> p, string key) => ProtomapsSchema.Str(p, key);

    private static void Copy(IReadOnlyDictionary<string, object> p, IDictionary<string, object> tags, string from, string to)
    {
        var s = Str(p, from);
        if (s != null) tags[to] = s;
    }

    // Floor counts come as doubles ("5.0" in some archives); the validator parses an int
    private static void Integer(IReadOnlyDictionary<string, object> p, IDictionary<string, object> tags, string from, string to)
    {
        if (Str(p, from) is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
            tags[to] = ((int)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsTrue(IReadOnlyDictionary<string, object> p, string key)
        => p.TryGetValue(key, out var v) && (v is bool b ? b : string.Equals(Convert.ToString(v, CultureInfo.InvariantCulture), "true", StringComparison.OrdinalIgnoreCase));
}
