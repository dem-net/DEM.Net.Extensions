using DEM.Net.Core;
using DEM.Net.Core.Imagery;
using System;
using System.Collections.Generic;

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
        public Func<Location3DModelSettings, Location3DModelRequest, string> ModelFileNameGenerator { get; set; } = (s,r) => $"{r.Id}_{DateTime.Now:yyyyMMdd_hhmmss}_{s.Dataset.Name}_{(s.OsmBuildings ? "Osm" : "")}{(s.GenerateTIN ? "TIN" : "")}{s.ImageryProvider?.Name}.glb";
        public List<Attribution> Attributions { get; internal set; } = new List<Attribution>
        {
            new Attribution("Data","Myceanean Atlas Project","https://helladic.info","Robert H. Consoli is the creator of this website and the database that underlies it. The site and the database feature contributions by Dr. Sarah Murray of the University of Nebraska. License: Attribution-ShareAlike 4.0 International (CC BY-SA 4.0) (https://creativecommons.org/licenses/by-sa/4.0/)"),
            new Attribution("Generator","DEM Net Elevation API","https://elevationapi.com","Xavier Fischer, Frédéric Aubin, and contributors. License: MIT, free for individuals/private use or companies generating less than 100K$ income."),
        };
        public string OutputDirectory { get; internal set; }
    }

    #endregion
}
