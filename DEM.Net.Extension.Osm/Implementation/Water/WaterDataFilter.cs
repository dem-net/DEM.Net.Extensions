namespace DEM.Net.Extension.Osm.Water
{
    internal class WaterDataFilter : IOsmDataSettings
    {
        public string FilterIdentifier => "water";
        public string[] WaysFilter { get; set; } = null;
        public string[] RelationsFilter { get; set; } = null;
        public string[] NodesFilter { get; set; } = null;

        public string FlatGeobufTilesDirectory { get; set; }
    }
}
