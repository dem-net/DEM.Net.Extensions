namespace DEM.Net.Extension.Osm
{
    internal class HighwaysDataFilter : IOsmDataSettings
    {
        public string FilterIdentifier => "highways";
        public string[] WaysFilter { get; set; } = new string[] { "highway" };
        public string[] RelationsFilter { get; set; } = null;
        public string[] NodesFilter { get; set; } = null;
        public string FlatGeobufTilesDirectory { get; set; } = null;
    }
}