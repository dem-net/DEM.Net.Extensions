using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    class BuildingsDataFilter : IOsmDataSettings
    {
        public string FilterIdentifier => "buildings";
        public string[] WaysFilter { get; set; } = new string[] { "building", "building:part" };
        public string[] RelationsFilter { get; set; } =
        new string[] { "building" };
        public string[] NodesFilter { get; set; } = null;
        public string FlatGeobufTilesDirectory { get; set; }
    }
}
