﻿using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public class OsmDataServiceFactory : IOsmDataServiceFactory
    {
#if NET5_0
        private readonly Func<OverpassAPIDataService> _overpassApiService;
        private readonly Func<TiledFlatGeobufDataService> _vectorTilesService;

        public OsmDataServiceFactory(Func<OverpassAPIDataService> overpassApiService,
            Func<TiledFlatGeobufDataService> vectorTilesService)
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
#else
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
#endif
    }

}
