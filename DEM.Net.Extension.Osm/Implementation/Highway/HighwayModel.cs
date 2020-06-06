using DEM.Net.Core;
using System.Collections.Generic;
using System.Numerics;

namespace DEM.Net.Extension.Osm.Highways
{
    public class HighwayModel : CommonModel
    {
        public string Name { get; internal set; }
        public int Lanes { get; internal set; } = 2;
        public string Type { get; internal set; }

        public List<GeoPoint> LineString { get; internal set; }

        public IEnumerable<GeoPoint> Points
        {
            get
            {
                return LineString;
            }
        }


        public Vector4 ColorVec4 { get; internal set; } = new Vector4(1, 1, 1, 1);

        public HighwayModel(List<GeoPoint> lineString)
        {
            this.LineString = lineString;
        }

    }
}