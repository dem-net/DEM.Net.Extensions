using System.Collections.Generic;
using System.Linq;
using DEM.Net.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;

namespace DEM.Net.Extension.Osm.Railway
{
    internal class RailwayValidator : OsmModelFactory<RailwayModel>
    {
        public RailwayValidator(ILogger logger) : base(logger ?? NullLogger<RailwayValidator>.Instance)
        {
            this._logger = logger ?? NullLogger<RailwayValidator>.Instance;
        }

        private readonly ILogger _logger;

        public override bool ParseTags(RailwayModel model)
        {
            base.ParseTag<string>(model, "railway", v => model.Type = v);
            base.ParseTag<string, bool>(model, "tunnel", s => s == "yes", v => model.Tunnel = v);
            base.ParseTag<string, bool>(model, "bridge", s => s == "yes", v => model.Bridge = v);
            return true;
        }


        public override IEnumerable<RailwayModel> CreateModel(IFeature feature)
        {
            if (feature == null) yield break;

            RailwayModel model = null;
            switch (feature.Geometry.OgcGeometryType)
            {
                case OgcGeometryType.LineString:
                    model = BuildModelFromGeometry((LineString)feature.Geometry, ref _totalPoints);
                    break;
                default:
                    _logger.LogWarning($"{feature.Geometry.GeometryType} not supported for {nameof(RailwayModel)} {feature.Attributes["osmid"]}.");
                    break;
            }

            if (model != null)
            {
                model.Id = feature.Attributes["osmid"].ToString();
                model.Tags = (feature.Attributes as AttributesTable).ToDictionary(k => k.Key, k => k.Value);
            }


            yield return model;
        }

        private RailwayModel BuildModelFromGeometry(LineString geom, ref int geoPointIdCounter)
        {
            // Can't do it with a linq + lambda because of ref int param
            List<GeoPoint> outerRingGeoPoints = ConvertLineString(geom, ref geoPointIdCounter);

            var model = new RailwayModel(outerRingGeoPoints);

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
