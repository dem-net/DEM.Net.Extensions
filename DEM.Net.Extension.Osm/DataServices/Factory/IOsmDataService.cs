using System.Collections.Generic;
using DEM.Net.Core;
using NetTopologySuite.Features;

namespace DEM.Net.Extension.Osm;

public interface IOsmDataService
{
    IEnumerable<IFeature> GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataSettings filter);
    int GetOsmDataCount(BoundingBox bbox, IOsmDataSettings filter);

    /// <summary>
    /// Source vertex count (OSM nodes) and, when the service can compute it, line length of the data matching the
    /// filter in the bounding box, for size estimates. The default derives the vertex count from
    /// <see cref="GetOsmDataCount"/> (which returns twice the node count) and leaves the length unknown.
    /// </summary>
    OsmDataStats GetOsmDataStats(BoundingBox bbox, IOsmDataSettings filter) => new(GetOsmDataCount(bbox, filter) / 2, null);
}