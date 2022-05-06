namespace DEM.Net.Extension.Osm
{
    internal class PisteSkiDataFilter : IOsmDataSettings
    {
        public string FilterIdentifier => "skipistes";
        public string[] WaysFilter { get; set; } = new string[] { "piste:type" };
        public string[] RelationsFilter { get; set; } = null;
        public string[] NodesFilter { get; set; } = null;

        public string FlatGeobufTilesDirectory { get; set; }
        public bool ComputeElevations { get; set; }
    }
}
