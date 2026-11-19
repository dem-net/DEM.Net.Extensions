using System;
using System.Collections.Generic;
using DEM.Net.Core;

namespace DEM.Net.Extension.Osm;

/// <summary>
/// Elevations of OSM lines (roads, railways) above the terrain.
/// </summary>
internal static class OsmLineElevation
{
    /// <summary>
    /// Lines are lifted above the terrain (metres, before the transform, so scaled by the vertical exaggeration like
    /// the terrain): at the exact DEM elevation they flicker through it (z-fighting), and the reduced terrain mesh
    /// departs from the DEM.
    /// </summary>
    public const double HeightAboveTerrainMeters = 0.5;

    /// <summary>
    /// Elevations of a line, from its DEM elevations (WGS84 points, in line order).
    /// </summary>
    /// <param name="demPoints">Line points with the DEM elevation</param>
    /// <param name="bridge">A bridge deck goes straight from one end to the other: its elevation is interpolated
    /// linearly between its two ends along the line, and stays above the terrain. The ends keep the DEM
    /// elevation, so the deck joins the neighbouring ways.</param>
    public static List<GeoPoint> Elevate(IReadOnlyList<GeoPoint> demPoints, bool bridge)
    {
        var result = new List<GeoPoint>(demPoints.Count);
        if (!bridge || demPoints.Count < 3)
        {
            foreach (var p in demPoints)
                result.Add(p with { Elevation = (p.Elevation ?? 0) + HeightAboveTerrainMeters });
            return result;
        }

        // distance along the line (degrees scaled to metres: the ratio is all that matters)
        var along = new double[demPoints.Count];
        double cosLat = Math.Cos(demPoints[0].Latitude * Math.PI / 180);
        for (int i = 1; i < demPoints.Count; i++)
        {
            double dx = (demPoints[i].Longitude - demPoints[i - 1].Longitude) * cosLat;
            double dy = demPoints[i].Latitude - demPoints[i - 1].Latitude;
            along[i] = along[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }

        double length = along[^1];
        double start = demPoints[0].Elevation ?? 0;
        double end = demPoints[^1].Elevation ?? 0;
        for (int i = 0; i < demPoints.Count; i++)
        {
            double t = length > 0 ? along[i] / length : 0;
            double deck = start + (end - start) * t;
            double terrain = demPoints[i].Elevation ?? 0;
            result.Add(demPoints[i] with { Elevation = Math.Max(deck, terrain) + HeightAboveTerrainMeters });
        }
        return result;
    }

    /// <summary>
    /// Full road width in metres, by OSM highway type (Protomaps has no lanes count).
    /// </summary>
    public static float RoadWidthMeters(string? highwayType, int? lanes)
    {
        const float LaneWidthMeters = 3.5f;
        if (lanes is > 0)
            return lanes.Value * LaneWidthMeters;

        return highwayType switch
        {
            "motorway" or "trunk" => 14,
            "motorway_link" or "trunk_link" => 7,
            "primary" => 10,
            "secondary" => 8,
            "primary_link" or "secondary_link" => 6,
            "tertiary" or "tertiary_link" or "residential" or "unclassified" or "living_street" or "road" => 6,
            "service" or "pedestrian" => 4,
            "track" => 3,
            "footway" or "path" or "cycleway" or "bridleway" or "steps" or "corridor" => 2,
            _ => 4,
        };
    }

    /// <summary>
    /// Full railway width in metres (track and ballast)
    /// </summary>
    public const float RailwayWidthMeters = 4f;
}
