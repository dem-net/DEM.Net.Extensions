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
        Railway = 8,
        Water = 16,

        // 4 was PisteSki (removed with the Overpass API data service): kept free so the other values do not change
        All = Buildings | Highways,
    }
}
