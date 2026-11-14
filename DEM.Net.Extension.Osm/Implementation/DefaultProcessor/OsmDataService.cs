using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public enum OsmDataServiceType
    {
        OverpassAPI,
        FlatGeobuf,
        /// <summary>Local Protomaps basemap PMTiles archive (see <see cref="OsmElevationOptions.PMTilesFilePath"/>).</summary>
        PMTiles,
    }
}
