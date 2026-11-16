using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using DEM.Net.Core;

namespace DEM.Net.Extension.Osm.Water;

internal class WaterModel(List<GeoPoint> lineString) : CommonModel
{
    public List<GeoPoint> LineString { get; internal set; } = lineString;

    public IEnumerable<GeoPoint> Points
    {
        get
        {
            return LineString;
        }
    }


    public Vector4 ColorVec4 { get; internal set; }
    public string NaturalType { get; internal set; }
    public string WaterwayType { get; internal set; }
}
