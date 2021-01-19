﻿//
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
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Diagnostics;
using DEM.Net.Core;
using System.Numerics;
using System.Drawing;
using DEM.Net.Extension.Osm.Extensions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Features;

namespace DEM.Net.Extension.Osm.Buildings
{
    public class BuildingValidator : OsmModelFactory<BuildingModel>
    {
        // Will capture 4 items : inputval / sign / value / unit
        const string ValueAndUnitRegex = @"([+\-])?((?:\d+\/|(?:\d+|^|\s)\.)?\d+)\s*([^\s\d+\-.,:;^\/]+(?:\^\d+(?:$|(?=[\s:;\/])))?(?:\/[^\s\d+\-.,:;^\/]+(?:\^\d+(?:$|(?=[\s:;\/])))?)*)?";

        private readonly ILogger _logger;
        private readonly bool _useOsmColors;
        private readonly Vector4 _defaultColor = new Vector4(.9f, .9f, .9f, 1f); // Color.FromArgb(230, 230, 230);

        public BuildingValidator(ILogger logger, bool useOsmColors, string defaultHtmlColor = null) : base(logger)
        {
            this._logger = logger;
            this._useOsmColors = useOsmColors;
            if (!string.IsNullOrWhiteSpace(defaultHtmlColor))
            {
                this._defaultColor = HtmlColorToVec4(defaultHtmlColor);
            }

        }

        public override bool ParseTags(BuildingModel model)
        {
            ParseTag<string>(model, "building", v => model.Type = v);
            ParseTag<int>(model, "building:levels", v => model.Levels = v);
            ParseTag<int>(model, "buildings:levels", v => model.Levels = v);
            ParseTag<string>(model, "buildings:part", v => model.IsPart = v.ToLower() == "yes");
            ParseTag<string>(model, "building:part", v => model.IsPart = v.ToLower() == "yes");
            ParseLengthTag(model, "min_height", v => model.MinHeight = v);
            ParseLengthTag(model, "height", v => model.Height = v);
            if (_useOsmColors)
            {
                ParseTag<string, Vector4>(model, "building:colour", htmlColor => HtmlColorToVec4(htmlColor), v => model.Color = v);
                ParseTag<string, Vector4>(model, "roof:colour", htmlColor => HtmlColorToVec4(htmlColor), v => model.RoofColor = v);
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


        public override BuildingModel CreateModel(IFeature feature)
        {
            if (feature == null) return null;

            BuildingModel model = null;
            switch (feature.Geometry.OgcGeometryType)
            {
                case OgcGeometryType.Polygon:
                    model = ConvertBuildingGeometry((Polygon)feature.Geometry, ref base._totalPoints);
                    break;
                case OgcGeometryType.MultiPolygon:
                    model = ConvertBuildingGeometry(((MultiPolygon)feature.Geometry).Geometries.First() as Polygon, ref base._totalPoints);
                    break;
                default:
                    _logger.LogDebug($"{feature.Geometry.OgcGeometryType} not supported for {nameof(BuildingModel)} {feature.Attributes.GetOptionalValue("osmid")}.");
                    break;
            }

            if (model != null)
            {
                model.Id = feature.Attributes["osmid"].ToString();
                model.Tags = (feature.Attributes as AttributesTable).ToDictionary(k => k.Key, k => k.Value);
            }

            return model;
        }

        private Vector4 HtmlColorToVec4(string htmlColor)
        {
            float componentRemap(byte c) => c / 255f;

            var color = Color.FromArgb(230, 230, 230);
            try
            {
                color = HtmlColorTranslator.FromHtml(htmlColor);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Cannot parse color {htmlColor}");
            }
            return new Vector4(componentRemap(color.R), componentRemap(color.G), componentRemap(color.B), componentRemap(color.A));
        }



        // Parse with unit conversion to meters
        private void ParseLengthTag(BuildingModel model, string tagName, Action<double> updateAction)
        {
            if (model.Tags.TryGetValue(tagName, out object val))
            {
                try
                {
                    // Will capture 4 items : inputval / sign / value / unit
                    var match = Regex.Match(val.ToString(), ValueAndUnitRegex, RegexOptions.Singleline | RegexOptions.Compiled);
                    if (match.Success)
                    {
                        var groups = match.Groups[0];
                        int sign = match.Groups[1].Value == "-" ? -1 : 1;
                        string valueStr = match.Groups[2].Value;
                        double typedVal = sign * double.Parse(valueStr, CultureInfo.InvariantCulture);

                        string unit = match.Groups[3].Value;
                        double factor = 1d;
                        switch (unit.ToLower())
                        {
                            case "ft":
                            case "feet":
                            case "foot":
                            case "'":
                                factor = 0.3048009193;
                                break;

                            case "":
                                factor = 1d;
                                break;
                            case "m":
                            case "meters":
                                factor = 1d;
                                break;
                            default:
                                throw new NotSupportedException($"Length unit {unit} conversion is not supported.");
                        }

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


        private BuildingModel ConvertBuildingGeometry(Polygon poly, ref int geoPointIdCounter)
        {
            // Can't do it with a linq + lambda because of ref int param
            List<GeoPoint> outerRingGeoPoints = ConvertBuildingLineString(poly.ExteriorRing, ref geoPointIdCounter);

            List<List<GeoPoint>> interiorRings = null;
            if (poly.NumPoints > 1)
            {
                interiorRings = new List<List<GeoPoint>>();
                foreach (LineString innerRing in poly.InteriorRings)
                {
                    interiorRings.Add(ConvertBuildingLineString(innerRing, ref geoPointIdCounter));
                }
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
                geoPoints.Add(new GeoPoint(++geoPointIdCounter, pt.Y, pt.X, pt.Z));
            }
            return geoPoints;
        }

    }
}

