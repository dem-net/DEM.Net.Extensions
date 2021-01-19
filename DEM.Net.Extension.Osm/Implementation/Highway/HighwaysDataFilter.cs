﻿namespace DEM.Net.Extension.Osm
{
    internal class HighwaysDataFilter : IOsmDataSettings
    {
        public string[] WaysFilter { get; set; } = new string[] { "highway" };
        public string[] RelationsFilter { get; set; } = null;
        public string[] NodesFilter { get; set; } = null;

        //public string FlatGeobufTilesDirectory => @"D:\Perso\Repos\OpenStreetMapDotNet\OsmCitySample\Data\streets";
        public string FlatGeobufTilesDirectory => @"D:\Perso\Repos\OpenStreetMapDotNet\OsmCitySample\bin\Debug\netcoreapp3.1\corse-latest.osm_FlatGeobuf_StreetsRules\_3D";
    }
}