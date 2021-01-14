namespace DEM.Net.Extension.Osm
{
    internal class HighwaysDataFilter : IOsmDataSettings
    {
        public string[] WaysFilter { get; set; } = new string[] { "highway" };
        public string[] RelationsFilter { get; set; } = null;
        public string[] NodesFilter { get; set; } = null;

        public string FlatGeobufTilesDirectory => @"D:\Perso\Repos\OpenStreetMapDotNet\OsmCitySample\bin\Debug\netcoreapp3.1\ukraine-latest.osm_FlatGeobuf_StreetsRules";
    }
}