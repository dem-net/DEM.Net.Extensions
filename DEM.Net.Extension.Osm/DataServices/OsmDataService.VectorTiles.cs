using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DEM.Net.Core;
using DEM.Net.Extension.Osm.Model;
using GeoJSON.Net.Feature;
using Microsoft.Extensions.Logging;

namespace DEM.Net.Extension.Osm
{
    public class OsmDataServiceVectorTiles: IOsmDataService
    {
        private readonly ILogger<OsmDataServiceVectorTiles> _logger;

        public OsmDataServiceVectorTiles(ILogger<OsmDataServiceVectorTiles> logger)
        {
            this._logger = logger;
        }

        public FeatureCollection GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataFilter filter)
        {
            try
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetOsmDataAsGeoJson)} error: {ex.Message}");
                throw;
            }

        }
       
        public int GetOsmDataCount(BoundingBox bbox, IOsmDataFilter filter)
        {
            return 0;
        }

        

    }
}
