using DEM.Net.Core;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DEM.Net.Extension.Osm.Highways
{
    internal class HighwayValidator : OsmModelFactory<HighwayModel>
    {
        public HighwayValidator(ILogger logger)
        {
            this._logger = logger;
        }

        private readonly ILogger _logger;

        public override void ParseTags(HighwayModel model)
        {
            base.ParseTag<string>(model, "name", v => model.Name = v);
            base.ParseTag<int>(model, "lanes", v => model.Lanes = v);
            base.ParseTag<string>(model, "highway", v => model.Type = v);
            base.ParseTag<int>(model, "layer", v => model.Layer = v);
            base.ParseBoolTag(model, "area", v => v == "yes", v => model.Area = v);
            base.ParseBoolTag(model, "tunnel", v => v == "yes", v => model.Tunnel = v);
            base.ParseBoolTag(model, "bridge", v => v == "yes", v => model.Bridge = v);

            if (model.Area)
            {
                _logger.LogWarning($"Area polygons not supported yet. Will process only exterior ring. {nameof(HighwayModel)} {model.Id}.");
            }
        }


        public override HighwayModel CreateModel(Feature feature)
        {
            if (feature == null) return null;

            HighwayModel model = null;
            switch (feature.Geometry.Type)
            {
                case GeoJSON.Net.GeoJSONObjectType.LineString:
                    model = BuildModelFromGeometry((LineString)feature.Geometry, ref _totalPoints);
                    break;
                case GeoJSON.Net.GeoJSONObjectType.Polygon:
                    var poly = (Polygon)feature.Geometry;
                    
                    model = BuildModelFromGeometry(poly.Coordinates.First(), ref _totalPoints);
                    
                    if (poly.Coordinates.Count > 1) _logger.LogWarning($"Polygon has {poly.Coordinates.Count} rings. Single ring processing supported so far. {nameof(HighwayModel)} {feature.Id}.");

                    break;
                default:
                    _logger.LogWarning($"{feature.Geometry.Type} not supported for {nameof(HighwayModel)} {feature.Id}.");
                    break;
            }

            if (model != null)
            {
                model.Id = feature.Id;
                model.Tags = feature.Properties;
            }


            return model;
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
            List<GeoPoint> geoPoints = new List<GeoPoint>(lineString.Coordinates.Count);
            foreach (var pt in lineString.Coordinates)
            {
                geoPoints.Add(new GeoPoint(++geoPointIdCounter, pt.Latitude, pt.Longitude));
            }
            return geoPoints;
        }

    }


}
