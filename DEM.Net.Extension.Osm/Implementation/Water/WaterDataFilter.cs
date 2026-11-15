namespace DEM.Net.Extension.Osm.Water
{
    internal class WaterDataFilter : IOsmDataSettings
    {
        public string FilterIdentifier => "water";

        public bool ComputeElevations { get; set; }
    }
}
