using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public interface IOsmDataSettings
    {
        string FilterIdentifier { get; }
        string[] WaysFilter { get; set; }
        string[] RelationsFilter { get; set; }
        string[] NodesFilter { get; set; }

        string FlatGeobufTilesDirectory { get; set; }
        bool ComputeElevations { get; set; }

        void Apply(OsmElevationOptions settings)
        {
            FlatGeobufTilesDirectory = settings.FlatGeobufTilesDirectory;
            ComputeElevations = settings.ComputeElevations;
        }
    }

}
