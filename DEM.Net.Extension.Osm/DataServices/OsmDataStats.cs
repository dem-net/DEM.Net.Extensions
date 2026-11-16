namespace DEM.Net.Extension.Osm;

/// <summary>Size of the OSM data of one layer in a bounding box, for model size estimates.</summary>
/// <param name="Vertices">Source vertices (OSM nodes), ring closing points excluded.</param>
/// <param name="LengthMeters">Total length of the line segments (lines and polygon rings), or null when the data service can't compute it.</param>
public readonly record struct OsmDataStats(int Vertices, double? LengthMeters);
