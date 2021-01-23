﻿//
// SkiPisteValidator.cs
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
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;

namespace DEM.Net.Extension.Osm.Ski
{
    /// <summary>
    /// https://wiki.openstreetmap.org/wiki/Piste_Maps
    /// </summary>
    public class SkiPisteValidator : OsmModelFactory<PisteModel>
    {
        public SkiPisteValidator(ILogger logger) : base(logger)
        {
            this._logger = logger;
        }

        private readonly ILogger _logger;

        public override bool ParseTags(PisteModel model)
        {
            ParseTag<string>(model, "piste:name", v => model.Name = v);
            ParseTag<string>(model, "piste:difficulty", v =>
            {
                model.Difficulty = v;
                model.ColorVec4 = GetDifficultyColor(v);
            });
            ParseTag<string>(model, "man_made", v => model.ManMade = v);
            ParseTag<string>(model, "piste:type", v => model.Type = v);

            return true;
        }


        private Vector4 GetDifficultyColor(string difficulty)
        {
            float alpha = 0.75f;
            switch (difficulty.ToLower().Trim())
            {
                case "novice": return new Vector4(0, 1, 0, alpha);
                case "easy": return new Vector4(0, 0, 1, alpha);
                case "intermediate": return new Vector4(1, 0, 0, alpha);
                case "advanced": return new Vector4(0, 0, 0, alpha);
                case "expert": return new Vector4(1, 165 / 255, 0, alpha);
                case "freeride": return new Vector4(0, 1, 1, alpha);
                default:
                    _logger.LogWarning($"Difficulty {difficulty} not supported. Returning red.");
                    return new Vector4(1, 0, 0, alpha);
            }
        }

        private void ParseTag<T>(PisteModel model, string tagName, Action<T> updateAction)
        {
            if (model.Tags.TryGetValue(tagName, out object val))
            {
                try
                {
                    T typedVal = (T)Convert.ChangeType(val, typeof(T), CultureInfo.InvariantCulture);
                    updateAction(typedVal);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Cannot convert tag value {tagName}, got value {val}. {ex.Message}");
                }
            }
        }


        public override IEnumerable<PisteModel> CreateModel(IFeature feature)
        {            
            if (feature == null) yield break;

            PisteModel model = null;
            switch (feature.Geometry.OgcGeometryType)
            {
                case OgcGeometryType.LineString:
                    model = BuildModelFromGeometry((LineString)feature.Geometry, ref _totalPoints);
                    break;
                default:
                    _logger.LogDebug($"{feature.Geometry.OgcGeometryType} not supported for {nameof(PisteModel)} {feature.Attributes["osmid"]}.");
                    break;
            }

            if (model != null)
            {
                model.Id = feature.Attributes["osmid"].ToString();
                model.Tags = (feature.Attributes as AttributesTable).ToDictionary(k => k.Key, k => k.Value);
            }

            yield return model;
        }

        private PisteModel BuildModelFromGeometry(LineString geom, ref int geoPointIdCounter)
        {
            // Can't do it with a linq + lambda because of ref int param
            List<GeoPoint> outerRingGeoPoints = ConvertLineString(geom, ref geoPointIdCounter);

            var model = new PisteModel(outerRingGeoPoints);

            return model;
        }

        private List<GeoPoint> ConvertLineString(LineString lineString, ref int geoPointIdCounter)
        {
            // Can't do it with a linq + lambda because of ref int param
            List<GeoPoint> geoPoints = new List<GeoPoint>(lineString.NumPoints);
            foreach (var pt in lineString.Coordinates)
            {
                geoPoints.Add(new GeoPoint(++geoPointIdCounter, pt.Y, pt.X));
            }
            return geoPoints;
        }

    }


}
