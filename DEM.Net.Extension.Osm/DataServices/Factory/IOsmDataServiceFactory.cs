using System;

namespace DEM.Net.Extension.Osm
{
    public interface IOsmDataServiceFactory
    {
        IOsmDataService Create(OsmDataService dataService);
    }

}
