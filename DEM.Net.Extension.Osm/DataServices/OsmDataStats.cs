namespace DEM.Net.Extension.Osm;

/// <summary>Size of the OSM data of one layer in a bounding box, for model size estimates.</summary>
/// <param name="Vertices">Source vertices (OSM nodes), ring closing points excluded.</param>
/// <param name="LengthMeters">Total length of the line segments (lines and polygon rings), or null when the data service can't compute it.</param>
/// <param name="Features">Features (buildings…), counted once per tile they cross; 0 when the data service doesn't count them.</param>
/// <param name="ColoredFeatures">Features with a mapped colour (buildings: facade or roof): with OSM colours on, one is enough for every
/// vertex of the layer to get a colour attribute.</param>
public readonly record struct OsmDataStats(int Vertices, double? LengthMeters, int Features = 0, int ColoredFeatures = 0);
