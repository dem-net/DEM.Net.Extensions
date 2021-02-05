using System.Collections.Generic;
using DEM.Net.Core;
using NetTopologySuite.Features;

namespace DEM.Net.Extension.Osm
{
    public interface IOsmDataService
    {
        IEnumerable<IFeature> GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataSettings filter);
        int GetOsmDataCount(BoundingBox bbox, IOsmDataSettings filter);
    }
}