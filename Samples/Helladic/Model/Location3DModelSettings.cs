using DEM.Net.Core;
using DEM.Net.Core.Imagery;
using System;

namespace SampleApp
{
    #region Model

    public class Location3DModelSettings
    {
        public DEMDataSet Dataset { get; set; }
        public ImageryProvider ImageryProvider { get; set; }
        public float ZScale { get; set; } = 2f;
        public float SideSizeKm { get; internal set; } = 1.5f;
        public bool OsmBuildings { get; internal set; } = true;
        public int MinTilesPerImage { get; internal set; } = 8;
        public bool DownloadMissingFiles { get; internal set; } = true;
        public bool GenerateTIN { get; internal set; } = false;
        public int MaxDegreeOfParallelism { get; internal set; } = -1;
        public Func<Location3DModelSettings, Location3DModelRequest, string> ModelFileNameGenerator { get; set; } = (s,r) => $"{r.Id}_{DateTime.Now:yyyyMMdd_hhmmss}_{s.Dataset.Name}_{(s.OsmBuildings ? "Osm" : "")}{(s.GenerateTIN ? "TIN" : "")}{s.ImageryProvider.Name}.glb";
    }

    #endregion
}
