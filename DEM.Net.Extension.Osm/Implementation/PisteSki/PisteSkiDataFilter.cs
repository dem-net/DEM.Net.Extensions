namespace DEM.Net.Extension.Osm
{
    internal class PisteSkiDataFilter : IOsmDataFilter
    {
        public string[] WaysFilter { get; set; } = new string[] { "piste:type" };
        public string[] RelationsFilter { get; set; } = null;
        public string[] NodesFilter { get; set; } = null;
    }
}
