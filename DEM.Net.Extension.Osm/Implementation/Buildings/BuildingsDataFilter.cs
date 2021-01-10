using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    class BuildingsDataFilter : IOsmDataFilter
    {
        public  string[] WaysFilter { get; set; } = new string[] { "building", "building:part" };
        public string[] RelationsFilter { get; set; } =
        new string[] { "building" };
        public string[] NodesFilter { get; set; } = null;
    }
}
