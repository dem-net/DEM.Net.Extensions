namespace DEM.Net.Extension.Osm;

internal class HighwaysDataFilter : IOsmDataSettings
{
    public string FilterIdentifier => "highways";
    public bool ComputeElevations { get; set; }
}