using DEM.Net.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public class OsmElevationOptions
    {
        public bool ComputeElevations { get; set; } = true;
        public DEMDataSet Dataset { get; set; }
        public bool DownloadMissingDEMFiles { get; set; } = true;

        public OsmDataServiceType DataServiceType { get; set; } = OsmDataServiceType.OverpassAPI;

        public string FlatGeobufTilesDirectory { get; set; }

        /// <summary>Path to a Protomaps basemap .pmtiles archive (https://maps.protomaps.com/builds/).</summary>
        public string PMTilesFilePath { get; set; }

        /// <summary>Zoom level to read features at. Defaults to the archive max zoom (15 for Protomaps builds), which has full detail.</summary>
        public int? PMTilesZoom { get; set; }

        /// <summary>Safety limit on the number of tiles decoded per layer and request (2500 z15 tiles ≈ 40x40 km at mid latitudes).</summary>
        public int PMTilesMaxTilesPerQuery { get; set; } = 2500;

        /// <summary>Use Overpass API for layers absent from the Protomaps schema (ski pistes).</summary>
        public bool PMTilesFallbackToOverpass { get; set; } = true;
    }
}
