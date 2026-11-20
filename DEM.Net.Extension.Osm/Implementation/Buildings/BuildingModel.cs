using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DEM.Net.Core;

namespace DEM.Net.Extension.Osm.Buildings;

public class BuildingModel(List<GeoPoint> exteriorRingPoints, List<List<GeoPoint>>? interiorRings) : CommonModel, IPolygon<GeoPoint>
{
    public List<GeoPoint> ExteriorRing { get; set; } = exteriorRingPoints;

    public List<List<GeoPoint>> InteriorRings { get; set; } = interiorRings ?? [];

    public IEnumerable<GeoPoint> Points
    {
        get
        {
            return ExteriorRing.Concat(this.InteriorRings == null ? [] : this.InteriorRings.SelectMany(r => r));
        }
    }

    // building:levels
    // height
    // min_height
    public int? Levels { get; set; }
    /// <summary>building:min_level: floors skipped below a part (an overhang), used when min_height is missing.</summary>
    public int? MinLevel { get; set; }
    public double? MinHeight { get; set; }
    public double? Height { get; set; }
    /// <summary>The height is a machine-learning estimate (Overture: Microsoft ML Buildings), taken as the wall height.</summary>
    public bool HeightIsEstimated { get; set; }

    // roof:shape, roof:height (metres), roof:direction (compass degrees the slope faces), roof:orientation=across
    public string? RoofShape { get; set; }
    public double? RoofHeight { get; set; }
    public double? RoofDirection { get; set; }
    public bool RoofAcross { get; set; }

    /// <summary>Centre of the footprint, longitude and latitude, recorded before the points are transformed.</summary>
    public (double Lon, double Lat)? Centre { get; set; }

    public double? ComputedFloorAltitude { get; set; }
    public double? ComputedRoofAltitude { get; set; }
    public bool HasHeightInformation { get; set; }
    public bool IsPart { get; set; }
    public Vector4? Color { get; set; }
    public Vector4? RoofColor { get; set; }

    public override string ToString()
    {
        string outStr = string.Concat(IsPart ? "part" : "", " "
            , Id, " "
            , "Height: ", Height, " "
            , "Levels: ", Levels, " "
            , "MinHeight: ", MinHeight, " ");
        return outStr;
    }
}
