using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public interface IOsmDataSettings
    {
        string FilterIdentifier { get; }

        bool ComputeElevations { get; set; }

        void Apply(OsmElevationOptions settings)
        {
            ComputeElevations = settings.ComputeElevations;
        }
    }

}
