using System.Collections.Generic;
using System.Linq;
using DEM.Net.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;

namespace DEM.Net.Extension.Osm.Water
{
    internal class WaterValidator : OsmModelFactory<WaterModel>
    {
        public WaterValidator(ILogger logger) : base(logger ?? NullLogger<WaterValidator>.Instance)
        {
            this._logger = logger ?? NullLogger<WaterValidator>.Instance;
        }

        private readonly ILogger _logger;

        public override bool ParseTags(WaterModel model)
        {
            base.ParseTag<string>(model, "natural", v => model.NaturalType = v);
            base.ParseTag<string>(model, "waterway", v => model.WaterwayType = v);
            return true;
        }


        public override IEnumerable<WaterModel> CreateModel(IFeature feature)
        {
            if (feature == null) yield break;

            WaterModel model = null;
            switch (feature.Geometry.OgcGeometryType)
            {
                case OgcGeometryType.LineString:
                    model = BuildModelFromGeometry((LineString)feature.Geometry, ref _totalPoints);
                    break;
                case OgcGeometryType.MultiPolygon:
                    _logger.LogWarning($"{feature.Geometry.GeometryType} not supported for {nameof(WaterModel)} {feature.Attributes["osmid"]}. Doing exterior ring only now");
                       
                    model = BuildModelFromGeometry((((MultiPolygon)feature.Geometry).Geometries[0] as Polygon).ExteriorRing, ref _totalPoints);
                    break;
                default:
                    _logger.LogWarning($"{feature.Geometry.GeometryType} not supported for {nameof(WaterModel)} {feature.Attributes["osmid"]}");
                    break;
            }

            if (model != null)
            {
                model.Id = feature.Attributes["osmid"].ToString();
                model.Tags = (feature.Attributes as AttributesTable).ToDictionary(k => k.Key, k => k.Value);
            }


            yield return model;
        }

        private WaterModel BuildModelFromGeometry(LineString geom, ref int geoPointIdCounter)
        {
            // Can't do it with a linq + lambda because of ref int param
            List<GeoPoint> outerRingGeoPoints = ConvertLineString(geom, ref geoPointIdCounter);

            var model = new WaterModel(outerRingGeoPoints);

            return model;
        }

        private List<GeoPoint> ConvertLineString(LineString lineString, ref int geoPointIdCounter)
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
