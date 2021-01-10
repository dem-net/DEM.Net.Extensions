using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public interface IOsmDataFilter
    {
        string[] WaysFilter { get; set; }
        string[] RelationsFilter { get; set; }
        string[] NodesFilter { get; set; }
    }
}
