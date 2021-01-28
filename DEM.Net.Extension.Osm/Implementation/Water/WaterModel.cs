using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using DEM.Net.Core;

namespace DEM.Net.Extension.Osm.Water
{
    internal class WaterModel : CommonModel
    {
        public List<GeoPoint> LineString { get; internal set; }

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

        public WaterModel(List<GeoPoint> lineString)
        {
            this.LineString = lineString;
        }

    }
}
