using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm;

public class OsmDataServiceFactory : IOsmDataServiceFactory
{
    private readonly Func<PMTilesDataService> _pmTilesService;

    public OsmDataServiceFactory(Func<PMTilesDataService> pmTilesService)
    {
        this._pmTilesService = pmTilesService;
    }
    public IOsmDataService Create(OsmDataServiceType dataServiceType)
    {
        switch (dataServiceType)
        {
            case OsmDataServiceType.PMTiles:
                return _pmTilesService();
            default:
                throw new NotImplementedException(dataServiceType.ToString());
        }
    }
}
