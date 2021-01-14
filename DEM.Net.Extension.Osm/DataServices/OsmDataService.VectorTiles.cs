using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DEM.Net.Core;
using DEM.Net.Extension.Osm.Model;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;

namespace DEM.Net.Extension.Osm
{
    public class OsmDataServiceFlatGeobuf : IOsmDataService
    {
        private readonly ILogger<OsmDataServiceFlatGeobuf> _logger;

        public OsmDataServiceFlatGeobuf(ILogger<OsmDataServiceFlatGeobuf> logger)
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
