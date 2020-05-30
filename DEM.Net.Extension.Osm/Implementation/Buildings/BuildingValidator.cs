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
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using GeoJSON.Net;
using System.Numerics;
using System.Drawing;
using DEM.Net.Extension.Osm.Extensions;

namespace DEM.Net.Extension.Osm.Buildings
{
    public class BuildingValidator : OsmModelFactory<BuildingModel>
    {
        // Will capture 4 items : inputval / sign / value / unit
        const string ValueAndUnitRegex = @"([+\-])?((?:\d+\/|(?:\d+|^|\s)\.)?\d+)\s*([^\s\d+\-.,:;^\/]+(?:\^\d+(?:$|(?=[\s:;\/])))?(?:\/[^\s\d+\-.,:;^\/]+(?:\^\d+(?:$|(?=[\s:;\/])))?)*)?";

        private readonly ILogger _logger;
        private readonly bool _useOsmColors;
        private readonly Vector4 _defaultColor = new Vector4(.9f, .9f, .9f, 1f); // Color.FromArgb(230, 230, 230);

        public BuildingValidator(ILogger logger, bool useOsmColors, string defaultHtmlColor = null)
        {
            this._logger = logger;
            this._useOsmColors = useOsmColors;
            if (!string.IsNullOrWhiteSpace(defaultHtmlColor))
            {
                this._defaultColor = HtmlColorToVec4(defaultHtmlColor);
            }

        }

        public override void ParseTags(BuildingModel model)
        {
            ParseTag<int>(model, v => model.Levels = v, "building:levels", "buildings:levels");
            ParseTag<string>(model, v => model.IsPart = v.ToLower() == "yes", "building:part", "buildings:part");
            ParseLengthTag(model, v => model.MinHeight = v, "min_height");
            ParseLengthTag(model, v => model.Height = v, "height", "building:height", "buildings:height");
            if (_useOsmColors)
            {
                ParseTag<string, Vector4>(model, htmlColor => HtmlColorToVec4(htmlColor), v => model.Color = v, "building:colour", "building:color");
                ParseTag<string, Vector4>(model, htmlColor => HtmlColorToVec4(htmlColor), v => model.RoofColor = v, "roof:colour", "roof:color");
            }
            else
            {
                model.Color = _defaultColor;
                // no roof color, it would duplicate roof vertices
            }
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

        private void ParseTag<T>(BuildingModel model, Action<T> updateAction, params string[] tagNames)
        {
            ParseTag<T, T>(model, t => t, updateAction, tagNames);
        }
        private void ParseTag<Tin, Tout>(BuildingModel model, Func<Tin, Tout> transformFunc, Action<Tout> updateAction, params string[] tagNames)
        {
            foreach (var tagName in tagNames)
            {
                if (model.Tags.TryGetValue(tagName, out object val))
                {
                    try
                    {
                        Tin typedVal = (Tin)Convert.ChangeType(val, typeof(Tin), CultureInfo.InvariantCulture);
                        Tout outVal = transformFunc(typedVal);
                        updateAction(outVal);

                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Cannot convert tag value {tagName}, got value {val}. {ex.Message}");
                    }
                }
            }
        }
        // Parse with unit conversion to meters
        private void ParseLengthTag(BuildingModel model, Action<double> updateAction, params string[] tagNames)
        {
            foreach (var tagName in tagNames)
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

                            break;
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

        public override BuildingModel CreateModel(Feature feature)
        {
            if (feature == null) return null;

            BuildingModel model = null;
            switch (feature.Geometry.Type)
            {
                case GeoJSON.Net.GeoJSONObjectType.Polygon:
                    model = ConvertBuildingGeometry((Polygon)feature.Geometry, ref base._totalPoints);
                    break;
                default:
                    _logger.LogDebug($"{feature.Geometry.Type} not supported for {nameof(BuildingModel)} {feature.Id}.");
                    break;
            }

            if (model != null)
            {
                model.Id = feature.Id;
                model.Tags = feature.Properties;
            }


            return model;
        }

        private BuildingModel ConvertBuildingGeometry(Polygon poly, ref int geoPointIdCounter)
        {
            // Can't do it with a linq + lambda because of ref int param
            List<GeoPoint> outerRingGeoPoints = ConvertBuildingLineString(poly.Coordinates.First(), ref geoPointIdCounter);

            List<List<GeoPoint>> interiorRings = null;
            if (poly.Coordinates.Count > 1)
            {
                interiorRings = new List<List<GeoPoint>>();
                foreach (LineString innerRing in poly.Coordinates.Skip(1))
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
            List<GeoPoint> geoPoints = new List<GeoPoint>(lineString.Coordinates.Count);
            foreach (var pt in lineString.Coordinates)
            {
                geoPoints.Add(new GeoPoint(++geoPointIdCounter, pt.Latitude, pt.Longitude));
            }
            return geoPoints;
        }

    }
}

