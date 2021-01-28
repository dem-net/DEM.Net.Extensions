using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using DEM.Net.Core;

namespace DEM.Net.Extension.Osm.Railway
{
    class RailwayModel : CommonModel
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
        public bool Tunnel { get; internal set; }
        public bool Bridge { get; internal set; }

        public RailwayModel(List<GeoPoint> lineString)
        {
            this.LineString = lineString;
        }

    }
}
