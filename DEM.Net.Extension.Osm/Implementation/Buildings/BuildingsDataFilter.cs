using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    class BuildingsDataFilter : IOsmDataSettings
    {
        public string FilterIdentifier => "buildings";
        public bool ComputeElevations { get; set; }
    }
}
