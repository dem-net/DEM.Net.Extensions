namespace DEM.Net.Extension.Osm.Railway
{
    internal class RailwayDataFilter : IOsmDataSettings
    {
        public string FilterIdentifier => "railway";
        public string[] WaysFilter { get; set; } = null;
        public string[] RelationsFilter { get; set; } = null;
        public string[] NodesFilter { get; set; } = null;

        public string FlatGeobufTilesDirectory { get; set; }
        public bool ComputeElevations { get; set; }
    }
}
