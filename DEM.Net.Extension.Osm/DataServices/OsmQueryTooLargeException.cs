using System;

namespace DEM.Net.Extension.Osm;

/// <summary>
/// The bounding box needs more tiles than <see cref="OsmElevationOptions.PMTilesMaxTilesPerQuery"/> allows.
/// Callers can report it as "extent too big" rather than as a failure.
/// </summary>
public class OsmQueryTooLargeException(long tileCount, int maxTiles, int zoom) : InvalidOperationException($"Bounding box needs {tileCount:N0} z{zoom} tiles, more than {nameof(OsmElevationOptions.PMTilesMaxTilesPerQuery)}={maxTiles:N0}.")
{
    public long TileCount { get; } = tileCount;
    public int MaxTiles { get; } = maxTiles;
    public int Zoom { get; } = zoom;
}
