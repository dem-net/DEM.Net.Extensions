using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm;

public class OsmDataServiceFactory(Func<PMTilesDataService> pmTilesService) : IOsmDataServiceFactory
{
    private readonly Func<PMTilesDataService> _pmTilesService = pmTilesService;

    public IOsmDataService Create(OsmDataServiceType dataServiceType)
    {
        return dataServiceType switch
        {
            OsmDataServiceType.PMTiles => _pmTilesService(),
            _ => throw new NotImplementedException(dataServiceType.ToString()),
        };
    }
}
