using System;

namespace DEM.Net.Extension.Osm
{
    /// <summary>
    /// The bounding box needs more tiles than <see cref="OsmElevationOptions.PMTilesMaxTilesPerQuery"/> allows.
    /// Callers can report it as "extent too big" rather than as a failure.
    /// </summary>
    public class OsmQueryTooLargeException : InvalidOperationException
    {
        public OsmQueryTooLargeException(long tileCount, int maxTiles, int zoom)
            : base($"Bounding box needs {tileCount:N0} z{zoom} tiles, more than {nameof(OsmElevationOptions.PMTilesMaxTilesPerQuery)}={maxTiles:N0}.")
        {
            TileCount = tileCount;
            MaxTiles = maxTiles;
            Zoom = zoom;
        }

        public long TileCount { get; }
        public int MaxTiles { get; }
        public int Zoom { get; }
    }
}
