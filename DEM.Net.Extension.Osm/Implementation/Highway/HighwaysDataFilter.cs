namespace DEM.Net.Extension.Osm
{
    internal class HighwaysDataFilter : IOsmDataFilter
    {
        public string[] WaysFilter { get; set; } = new string[] { "highway" };
        public string[] RelationsFilter { get; set; } = null;
        public string[] NodesFilter { get; set; } = null;
    }
}