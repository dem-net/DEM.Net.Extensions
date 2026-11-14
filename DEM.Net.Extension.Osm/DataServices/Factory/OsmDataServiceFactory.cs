using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public class OsmDataServiceFactory : IOsmDataServiceFactory
    {
        private readonly Func<OverpassAPIDataService> _overpassApiService;
        private readonly Func<PMTilesDataService> _pmTilesService;

        public OsmDataServiceFactory(Func<OverpassAPIDataService> overpassApiService, Func<PMTilesDataService> pmTilesService)
        {
            this._overpassApiService = overpassApiService;
            this._pmTilesService = pmTilesService;
        }
        public IOsmDataService Create(OsmDataServiceType dataServiceType)
        {
            switch (dataServiceType)
            {
                case OsmDataServiceType.OverpassAPI:
                    return _overpassApiService();
                case OsmDataServiceType.PMTiles:
                    return _pmTilesService();
                default:
                    throw new NotImplementedException(dataServiceType.ToString());
            }
        }
    }

}
