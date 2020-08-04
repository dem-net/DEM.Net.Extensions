using DEM.Net.Core;
using System.Collections.Generic;
using System.Numerics;

namespace DEM.Net.Extension.Osm.Highways
{
    public class HighwayModel : CommonModel
    {

        // layer
        // aera = yes

        public string Name { get; internal set; }
        public int? Lanes { get; internal set; }
        public string Type { get; internal set; }
        public bool Area { get; internal set; } = false;
        public bool Tunnel { get; internal set; } = false;
        public bool Bridge { get; internal set; } = false;
        public int Layer { get; internal set; } = 0;

        public List<GeoPoint> LineString { get; set; }

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