using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    [Flags]
    public enum OsmLayer
    {
        None = 0,
        Buildings = 1,
        Highways = 2,
        PisteSki = 4,
        Railway = 8,
        Water = 16,

        All = Buildings | Highways | PisteSki,        
    }
}
