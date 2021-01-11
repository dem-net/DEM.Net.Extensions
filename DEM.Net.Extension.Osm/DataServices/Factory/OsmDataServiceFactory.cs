using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public class OsmDataServiceFactory : IOsmDataServiceFactory
    {
        private readonly Func<OsmServiceOverpassAPI> _overpassApiService;
        private readonly Func<OsmDataServiceVectorTiles> _vectorTilesService;

        public OsmDataServiceFactory(Func<OsmServiceOverpassAPI> overpassApiService,
            Func<OsmDataServiceVectorTiles> vectorTilesService)
        {
            this._vectorTilesService = vectorTilesService;
            this._overpassApiService = overpassApiService;
        }
        public IOsmDataService Create(OsmDataServiceType dataServiceType)
        {
            switch (dataServiceType)
            {
                case OsmDataServiceType.OverpassAPI:
                    return _overpassApiService();
                case OsmDataServiceType.VectorTiles:
                    return _vectorTilesService();
                default:
                    throw new InvalidOperationException();
            }
        }
    }

}
