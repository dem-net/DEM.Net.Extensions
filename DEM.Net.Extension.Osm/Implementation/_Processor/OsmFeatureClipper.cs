using System;
using System.Collections.Generic;
using System.Linq;
using DEM.Net.Core;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;

namespace DEM.Net.Extension.Osm
{
    /// <summary>
    /// Cuts OSM features to the model's bounding box, so that buildings and roads end at the terrain edge. Data services
    /// return whole ways (Overpass) or whole tiles (PMTiles) that can reach far beyond it.
    /// </summary>
    public static class OsmFeatureClipper
    {
        /// <summary>
        /// Features fully inside are kept as is, features outside are dropped. Clipped features keep their attributes
        /// and geometry type (the model factories support a fixed set): a clipped LineString or Polygon gives one
        /// feature per part, a MultiPolygon stays one MultiPolygon. Other geometry types are not clipped.
        /// </summary>
        public static IEnumerable<IFeature> ClipToBoundingBox(IEnumerable<IFeature> features, BoundingBox bboxWgs84)
        {
            var clipEnvelope = new Envelope(bboxWgs84.xMin, bboxWgs84.xMax, bboxWgs84.yMin, bboxWgs84.yMax);
            Geometry clipPolygon = null;

            foreach (var feature in features)
            {
                var geom = feature?.Geometry;
                if (geom == null || geom.IsEmpty)
                    continue;
                if (!clipEnvelope.Intersects(geom.EnvelopeInternal))
                    continue;
                if (clipEnvelope.Contains(geom.EnvelopeInternal) || !IsClippable(geom))
                {
                    yield return feature;
                    continue;
                }

                clipPolygon ??= geom.Factory.ToGeometry(clipEnvelope);
                Geometry clipped = Intersection(geom, clipPolygon);
                if (clipped == null)
                {
                    yield return feature; // could not clip: better whole than missing
                    continue;
                }

                foreach (var part in Parts(geom, clipped))
                {
                    part.SRID = geom.SRID;
                    yield return new Feature(part, feature.Attributes);
                }
            }
        }

        private static bool IsClippable(Geometry geom) => geom.OgcGeometryType is OgcGeometryType.LineString
            or OgcGeometryType.Polygon or OgcGeometryType.MultiPolygon;

        private static IEnumerable<Geometry> Parts(Geometry source, Geometry clipped)
        {
            if (source.OgcGeometryType == OgcGeometryType.LineString)
                return LineStringExtracter.GetLines(clipped).Where(g => !g.IsEmpty && g.Length > 0);

            var polygons = PolygonExtracter.GetPolygons(clipped).Cast<Polygon>().Where(g => !g.IsEmpty && g.Area > 0).ToArray();
            if (source.OgcGeometryType == OgcGeometryType.Polygon)
                return polygons;
            return polygons.Length == 0 ? Array.Empty<Geometry>() : new Geometry[] { source.Factory.CreateMultiPolygon(polygons) };
        }

        private static Geometry Intersection(Geometry geom, Geometry clip)
        {
            try
            {
                return OverlayNGRobust.Overlay(geom, clip, SpatialFunction.Intersection);
            }
            catch (Exception) when (geom is IPolygonal)
            {
                try { return OverlayNGRobust.Overlay(geom.Buffer(0), clip, SpatialFunction.Intersection); }
                catch (Exception) { return null; }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
