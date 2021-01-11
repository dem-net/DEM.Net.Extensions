using DEM.Net.Core;
using GeoJSON.Net.Feature;

namespace DEM.Net.Extension.Osm
{
    public interface IOsmDataService
    {
        FeatureCollection GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataFilter filter);
        int GetOsmDataCount(BoundingBox bbox, IOsmDataFilter filter);
    }
}