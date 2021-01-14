using DEM.Net.Core;
using NetTopologySuite.Features;

namespace DEM.Net.Extension.Osm
{
    public interface IOsmDataService
    {
        FeatureCollection GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataSettings filter);
        int GetOsmDataCount(BoundingBox bbox, IOsmDataSettings filter);
    }
}