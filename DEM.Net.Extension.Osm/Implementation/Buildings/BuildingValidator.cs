//
// BuildingValidator.cs
//
// Author:
//       Xavier Fischer 2020-2
//
// Copyright (c) 2020 Xavier Fischer
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using DEM.Net.Core;
using DEM.Net.Extension.Osm.Extensions;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;

namespace DEM.Net.Extension.Osm.Buildings;

public class BuildingValidator : OsmModelFactory<BuildingModel>
{
    // Will capture 4 items : inputval / sign / value / unit
    const string ValueAndUnitRegex = @"([+\-])?((?:\d+\/|(?:\d+|^|\s)\.)?\d+)\s*([^\s\d+\-.,:;^\/]+(?:\^\d+(?:$|(?=[\s:;\/])))?(?:\/[^\s\d+\-.,:;^\/]+(?:\^\d+(?:$|(?=[\s:;\/])))?)*)?";

    private readonly ILogger _logger;
    private readonly bool _useOsmColors;
    private readonly Vector4 _defaultColor = Vector4.One; // white

    public BuildingValidator(ILogger logger, bool useOsmColors, string? defaultHtmlColor = null) : base(logger)
    {
        this._logger = logger;
        this._useOsmColors = useOsmColors;
        if (!string.IsNullOrWhiteSpace(defaultHtmlColor))
        {
            this._defaultColor = HtmlColorTranslator.ToVector4(defaultHtmlColor);
        }

    }

    public override bool ParseTags(BuildingModel model)
    {
        ParseTag<string>(model, "building", v => model.Type = v);
        ParseTag<int>(model, "building:levels", v => model.Levels = v);
        ParseTag<int>(model, "buildings:levels", v => model.Levels = v);
        ParseTag<int>(model, "building:min_level", v => model.MinLevel = v);
        ParseTag<string>(model, "buildings:part", v => model.IsPart = v.ToLower() == "yes");
        ParseTag<string>(model, "building:part", v => model.IsPart = v.ToLower() == "yes");
        ParseLengthTag(model, "min_height", v => model.MinHeight = v);
        ParseLengthTag(model, "height", v => model.Height = v);
        ParseTag<string>(model, PMTiles.OvertureSchema.EstimatedHeightTag, v => model.HeightIsEstimated = v == "yes");
        ParseTag<string>(model, "roof:shape", v => model.RoofShape = v);
        ParseLengthTag(model, "roof:height", v => model.RoofHeight = v);
        ParseTag<string>(model, "roof:direction", v => model.RoofDirection = ParseDirection(v));
        ParseTag<string>(model, "roof:orientation", v => model.RoofAcross = string.Equals(v, "across", StringComparison.OrdinalIgnoreCase));
        if (_useOsmColors)
        {
            ParseTag<string, Vector4>(model, "building:colour", htmlColor => HtmlColorTranslator.ToVector4(htmlColor), v => model.Color = v);
            ParseTag<string, Vector4>(model, "roof:colour", htmlColor => HtmlColorTranslator.ToVector4(htmlColor), v => model.RoofColor = v);
        }
        else
        {
            model.Color = _defaultColor;
            // no roof color, it would duplicate roof vertices
        }

        ParseLengthTag(model, "building:height", v =>
        {
            if (model.Height != null)
            {
                _logger.LogWarning($"Height is passed as height and building:height, got value {v}");
            }
            model.Height = v;
        });

        return true;
    }


    public override IEnumerable<BuildingModel?> CreateModel(IFeature? feature)
    {
        if (feature == null) yield break;

        switch (feature.Geometry.OgcGeometryType)
        {
            case OgcGeometryType.Polygon:

                BuildingModel? model = ConvertBuildingGeometry((Polygon)feature.Geometry, ref base._totalPoints);
                if (model == null)
                {
                    _logger.LogDebug($"Degenerate building polygon skipped: {feature.Attributes.GetOptionalValue("osmid")}.");
                    yield break;
                }
                model.Id = feature.Attributes["osmid"].ToString()!;
                model.Tags = (feature.Attributes as AttributesTable)!.ToDictionary(k => k.Key, k => k.Value);
                yield return model;
                break;
            case OgcGeometryType.MultiPolygon:

                foreach (Polygon polygon in ((MultiPolygon)feature.Geometry).Geometries)
                {
                    var modelPoly = ConvertBuildingGeometry(polygon, ref base._totalPoints);
                    if (modelPoly == null)
                        continue; // degenerate polygon
                    modelPoly.Id = feature.Attributes["osmid"].ToString()!;
                    modelPoly.Tags = (feature.Attributes as AttributesTable)!.ToDictionary(k => k.Key, k => k.Value);
                    yield return modelPoly;
                }
                break;
            default:
                _logger.LogDebug($"{feature.Geometry.OgcGeometryType} not supported for {nameof(BuildingModel)} {feature.Attributes.GetOptionalValue("osmid")}.");
                break;
        }

    }

    private static readonly string[] Cardinals = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];

    /// <summary>roof:direction: degrees (0 = north, clockwise) or a cardinal direction (N, NNE … NNW).</summary>
    internal static double? ParseDirection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double deg) && double.IsFinite(deg))
            return ((deg % 360) + 360) % 360;
        int i = Array.FindIndex(Cardinals, c => string.Equals(c, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return i < 0 ? null : i * 22.5;
    }

    // Parse with unit conversion to meters
    private void ParseLengthTag(BuildingModel model, string tagName, Action<double> updateAction)
    {
        if (model.Tags.TryGetValue(tagName, out object? val))
        {
            try
            {
                // Will capture 4 items : inputval / sign / value / unit
                var match = Regex.Match(val!.ToString()!, ValueAndUnitRegex, RegexOptions.Singleline | RegexOptions.Compiled);
                if (match.Success)
                {
                    var groups = match.Groups[0];
                    int sign = match.Groups[1].Value == "-" ? -1 : 1;
                    string valueStr = match.Groups[2].Value;
                    double typedVal = sign * double.Parse(valueStr, CultureInfo.InvariantCulture);

                    string unit = match.Groups[3].Value;
                    double factor = 1d;
                    factor = unit.ToLower() switch
                    {
                        "ft" or "feet" or "foot" or "'" => 0.3048009193,
                        "" => 1d,
                        "m" or "meters" => 1d,
                        _ => throw new NotSupportedException($"Length unit {unit} conversion is not supported."),
                    };
                    typedVal *= factor;

                    updateAction(typedVal);
                }
                else
                {
                    _logger.LogWarning($"Cannot extract value and unit for tag {tagName}, got value {val}.");
                }


            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Cannot convert tag value {tagName}, got value {val}. {ex.Message}");
            }
        }
    }


    /// <summary>
    /// A closed ring needs 4 points (a triangle and its closing point)
    /// </summary>
    private const int MinRingPoints = 4;

    /// <returns>null when the outer ring is not a polygon. Degenerate inner rings are skipped.</returns>
    private BuildingModel? ConvertBuildingGeometry(Polygon poly, ref int geoPointIdCounter)
    {
        if (poly.ExteriorRing.NumPoints < MinRingPoints)
            return null;

        // Can't do it with a linq + lambda because of ref int param
        List<GeoPoint> outerRingGeoPoints = ConvertBuildingLineString(poly.ExteriorRing, ref geoPointIdCounter);

        List<List<GeoPoint>> interiorRings = [];
        foreach (LineString innerRing in poly.InteriorRings)
        {
            if (innerRing.NumPoints >= MinRingPoints)
                interiorRings.Add(ConvertBuildingLineString(innerRing, ref geoPointIdCounter));
        }

        var buildingModel = new BuildingModel(outerRingGeoPoints, interiorRings);

        return buildingModel;
    }

    private List<GeoPoint> ConvertBuildingLineString(LineString lineString, ref int geoPointIdCounter)
    {
        // Can't do it with a linq + lambda because of ref int param
        List<GeoPoint> geoPoints = new List<GeoPoint>(lineString.NumPoints);
        foreach (var pt in lineString.Coordinates)
        {
            geoPointIdCounter++;
            geoPoints.Add(new GeoPoint(pt.Y, pt.X, pt.Z));
        }
        return geoPoints;
    }

}

