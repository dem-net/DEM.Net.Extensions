using System;
using System.Collections.Generic;
using System.Text;
using DEM.Net.Core;

namespace DEM.Net.Extension.Osm;

public class OsmElevationOptions
{
    public bool ComputeElevations { get; set; } = true;
    public DEMDataSet? Dataset { get; set; }
    public bool DownloadMissingDEMFiles { get; set; } = true;

    public OsmDataServiceType DataServiceType { get; set; } = OsmDataServiceType.PMTiles;

    /// <summary>Path to a Protomaps basemap .pmtiles archive (https://maps.protomaps.com/builds/).</summary>
    public string? PMTilesFilePath { get; set; }

    /// <summary>Zoom level to read features at. Defaults to the archive max zoom (15 for Protomaps builds), which has full detail.</summary>
    public int? PMTilesZoom { get; set; }

    /// <summary>
    /// Optional Overture Maps buildings archive (<c>tiles/&lt;release&gt;/buildings.pmtiles</c> from
    /// https://docs.overturemaps.org/examples/overture-tiles/). When set, buildings are read from it, with their
    /// floors, roof shape and colours, instead of from the Protomaps archive (which only has heights). Read at its
    /// max zoom (14).
    /// </summary>
    public string? BuildingsPMTilesFilePath { get; set; }

    /// <summary>
    /// Optional roof prior (<c>roof-prior.bin</c>, made by <c>tools/roof-prior/build_roof_prior.py</c>): the odds of
    /// each roof shape by place and kind of building, from the roof shapes mapped in OpenStreetMap. Buildings without
    /// a mapped roof shape get a roof drawn from it. Without it, only small, nearly rectangular, low houses get a
    /// default gabled roof.
    /// </summary>
    public string? RoofPriorFilePath { get; set; }

    /// <summary>Safety limit on the number of tiles decoded per layer and request (2500 z15 tiles ≈ 40x40 km at mid latitudes).</summary>
    public int PMTilesMaxTilesPerQuery { get; set; } = 2500;
}
