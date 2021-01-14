using DEM.Net.Core;
using NetTopologySuite.Features;

namespace DEM.Net.Extension.Osm
{
    public interface IOsmDataService
    {
        FeatureCollection GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataFilter filter);
        int GetOsmDataCount(BoundingBox bbox, IOsmDataFilter filter);
    }
}