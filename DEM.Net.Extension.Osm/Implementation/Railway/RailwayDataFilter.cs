namespace DEM.Net.Extension.Osm.Railway;

internal class RailwayDataFilter : IOsmDataSettings
{
    public string FilterIdentifier => "railway";

    public bool ComputeElevations { get; set; }
}
