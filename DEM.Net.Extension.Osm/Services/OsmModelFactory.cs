//
// IOsmTagsParser.cs
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
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DEM.Net.Extension.Osm
{
    public abstract class OsmModelFactory<TModel> where TModel : CommonModel
    {
        public OsmModelFactory(ILogger logger)
        {
            _logger = logger;
        }
        public TagRegistry TagRegistry { get; private set; } = new TagRegistry();
        internal int _totalPoints;
        private readonly ILogger _logger;

        /// <summary>
        /// Parse model tags and return bool indicating if the model is valid.
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public abstract bool ParseTags(TModel model);
        public abstract IEnumerable<TModel> CreateModel(IFeature feature);

        public virtual void RegisterTags(IFeature feature)
        {
            TagRegistry.RegisterTags(feature);
        }

        internal string GetTagsReport()
        {
            return TagRegistry.GetReport();
        }

        protected void ParseTag<T>(TModel model, string tagName, Action<T> updateAction)
        {
            ParseTag<T, T>(model, tagName, t => t, updateAction);
        }
        protected void ParseTag<Tin, Tout>(TModel model, string tagName, Func<Tin, Tout> transformFunc, Action<Tout> updateAction)
        {
            if (model.Tags.TryGetValue(tagName, out object val))
            {
                try
                {
                    Tin typedVal = (Tin)Convert.ChangeType(val, typeof(Tin), CultureInfo.InvariantCulture);
                    Tout outVal = transformFunc(typedVal);
                    updateAction(outVal);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Cannot convert tag value {tagName}, got value {val}. {ex.Message}");
                }
            }
        }

    }
    public class TagRegistry
    {
        const string Separator = "\t";
        Dictionary<string, int> _tagsOccurences = new Dictionary<string, int>();
        Dictionary<OgcGeometryType, int> _geomTypes = new Dictionary<OgcGeometryType, int>();
        Dictionary<string, Dictionary<object, int>> _tagsValuesOccurences = new Dictionary<string, Dictionary<object, int>>();

        internal string GetReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Join(Separator, "GeometryType", "Tag", "Value", "Occurences"));
            foreach (var occur in _geomTypes.OrderBy(k => k.Key))
            {
                sb.AppendLine(string.Join(Separator, occur.Key, "", "", occur.Value));
            }
            foreach (var occur in _tagsOccurences.OrderBy(k => k.Key))
            {
                sb.AppendLine(string.Join(Separator, "", occur.Key, "", occur.Value));
            }
            foreach (var occur in _tagsValuesOccurences.OrderBy(k => k.Key))
            {
                if (occur.Key == "osmid") continue;

                foreach (var valOccur in occur.Value.OrderBy(k => k.Key))
                {
                    sb.AppendLine(string.Join(Separator, "", occur.Key, valOccur.Key, valOccur.Value));
                }
            }
            return sb.ToString();
        }

        internal void RegisterTags(IFeature feature)
        {
            if (!_geomTypes.ContainsKey(feature.Geometry.OgcGeometryType))
            {
                _geomTypes.Add(feature.Geometry.OgcGeometryType, 0);
            }
            _geomTypes[feature.Geometry.OgcGeometryType]++;

            foreach (var prop in feature.Attributes as AttributesTable)
            {
                if (!_tagsOccurences.ContainsKey(prop.Key))
                {
                    _tagsOccurences.Add(prop.Key, 0);
                    _tagsValuesOccurences.Add(prop.Key, new Dictionary<object, int>());
                }
                if (!_tagsValuesOccurences[prop.Key].ContainsKey(prop.Value))
                {
                    _tagsValuesOccurences[prop.Key].Add(prop.Value, 0);
                }


                _tagsOccurences[prop.Key]++;
                _tagsValuesOccurences[prop.Key][prop.Value]++;

            }
        }
    }
}