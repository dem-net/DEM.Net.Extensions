using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    class BuildingsDataFilter : IOsmDataSettings
    {
        public string[] WaysFilter { get; set; } = new string[] { "building", "building:part" };
        public string[] RelationsFilter { get; set; } =
        new string[] { "building" };
        public string[] NodesFilter { get; set; } = null;
        public string FlatGeobufTilesDirectory => @"D:\Perso\Repos\OpenStreetMapDotNet\OsmCitySample\bin\Debug\netcoreapp3.1\ukraine-latest.osm_FlatGeobuf_BuildingsRules";
    }
}
