using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public class OsmDataServiceFactory : IOsmDataServiceFactory
    {
        private readonly Func<OsmServiceOverpassAPI> _overpassApiService;
        private readonly Func<OsmDataServiceFlatGeobuf> _vectorTilesService;

        public OsmDataServiceFactory(Func<OsmServiceOverpassAPI> overpassApiService,
            Func<OsmDataServiceFlatGeobuf> vectorTilesService)
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
                case OsmDataServiceType.FlatGeobuf:
                    return _vectorTilesService();
                default:
                    throw new InvalidOperationException();
            }
        }
    }

}
