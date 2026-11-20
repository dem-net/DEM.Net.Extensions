using System;
using System.Collections.Generic;
using System.Globalization;

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
