using DEM.Net.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public class OsmElevationOptions
    {
        public bool ComputeElevations { get; set; } = false;
        public DEMDataSet Dataset { get; set; }
        public bool DownloadMissingDEMFiles { get; set; } = true;

        public OsmDataService DataServiceType { get; set; } = OsmDataService.OverpassAPI;
    }
}
