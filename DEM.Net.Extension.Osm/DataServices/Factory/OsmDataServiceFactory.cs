using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public class OsmDataServiceFactory : IOsmDataServiceFactory
    {
        private readonly Func<OverpassAPIDataService> _overpassApiService;

        public OsmDataServiceFactory(Func<OverpassAPIDataService> overpassApiService)
        {
            this._overpassApiService = overpassApiService;
        }
        public IOsmDataService Create(OsmDataServiceType dataServiceType)
        {
            switch (dataServiceType)
            {
                case OsmDataServiceType.OverpassAPI:
                    return _overpassApiService();
                default:
                    throw new NotImplementedException(dataServiceType.ToString());
            }
        }
    }

}
