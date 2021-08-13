using DEM.Net.Core;
using DEM.Net.Extension.Osm.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DEM.Net.Extension.Osm.Highways
{
    internal class HighwayValidator : OsmModelFactory<HighwayModel>
    {
        private readonly Vector4 _defaultColor = Vector4.One;
        public HighwayValidator(ILogger logger, string highwaysColor = null) : base(logger ?? NullLogger<HighwayValidator>.Instance)
        {
            this._logger = logger ?? NullLogger<HighwayValidator>.Instance; 
            if (!string.IsNullOrWhiteSpace(highwaysColor))
            {
                this._defaultColor = HtmlColorTranslator.ToVector4(highwaysColor);
            }
        }

        private readonly ILogger _logger;

        public override bool ParseTags(HighwayModel model)
        {
            base.ParseTag<string>(model, "highway", v => model.Type = v);
            base.ParseTag<string>(model, "name", v => model.Name = v);
            base.ParseTag<int>(model, "lanes", v => model.Lanes = v);
            base.ParseTag<int>(model, "layer", v => model.Layer = v);
            base.ParseTag<string, bool>(model, "area", s => s == "yes", v => model.Area = v);
            base.ParseTag<string, bool>(model, "tunnel", s => s == "yes", v => model.Tunnel = v);
            base.ParseTag<string, bool>(model, "bridge", s => s == "yes", v => model.Bridge = v);

            model.Color = _defaultColor;

            if (model.Area)
            {
                _logger.LogWarning($"Area polygons not supported yet. Will process only exterior ring. {nameof(HighwayModel)} {model.Type} {model.Id}.");
                switch (model.Type)
                {
                    case "pedestrian":
                    case "platform":
                    case "footway":
                        return false;
                    default:
                        return true;
                }
            }
            return true;
        }

        public override IEnumerable<HighwayModel> CreateModel(IFeature feature)
        {
            if (feature == null) yield break;

            HighwayModel model = null;
            switch (feature.Geometry.OgcGeometryType)
            {
                case OgcGeometryType.LineString:
                    model = BuildModelFromGeometry((LineString)feature.Geometry, ref _totalPoints);
                    break;
                case OgcGeometryType.Polygon:
                    var poly = (Polygon)feature.Geometry;

                    model = BuildModelFromGeometry(poly.ExteriorRing, ref _totalPoints);

                    break;
                default:
                    _logger.LogWarning($"{feature.Geometry.GeometryType} not supported for {nameof(HighwayModel)} {feature.Attributes["osmid"]}.");
                    break;
            }

            if (model != null)
            {
                model.Id = feature.Attributes["osmid"].ToString();
                model.Tags =(feature.Attributes as AttributesTable).ToDictionary(k=> k.Key, k=>k.Value);
            }


            yield return model;
        }

        private HighwayModel BuildModelFromGeometry(LineString geom, ref int geoPointIdCounter)
        {
            // Can't do it with a linq + lambda because of ref int param
            List<GeoPoint> outerRingGeoPoints = ConvertLineString(geom, ref geoPointIdCounter);

            var model = new HighwayModel(outerRingGeoPoints);

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
