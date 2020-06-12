using DEM.Net.Core;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;

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
            base.ParseTag<string>(model, "highway", v => model.Type= v);
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
                default:
                    _logger.LogDebug($"{feature.Geometry.Type} not supported for {nameof(HighwayModel)} {feature.Id}.");
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
