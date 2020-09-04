using DEM.Net.Core;
using DEM.Net.Extension.Osm;
using DEM.Net.Extension.Osm.Highways;
using GeoJSON.Net.Feature;
using Microsoft.Extensions.Logging;
using Sketchfab;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleApp
{
    public class HighestPointFinder
    {
        private readonly ILogger<HighestPointFinder> _logger;
        private readonly ElevationService _elevationService;
        private readonly OsmService _osmService;
        private readonly DefaultOsmProcessor _osmProcessor;

        public HighestPointFinder(ILogger<HighestPointFinder> logger
                , ElevationService elevationService
                , OsmService osmService
                , DefaultOsmProcessor osmProcessor)
        {
            _logger = logger;
            _elevationService = elevationService;
            _osmProcessor = osmProcessor;
            _osmService = osmService;
        }
        public void Run()
        {
            try
            {
                // SF
                GeoPoint location4326 = new GeoPoint(37.766974, -122.431062);
                //GeoPoint location4326 = new GeoPoint( 43.542544, 5.445379);
                DEMDataSet dataset = DEMDataSet.NASADEM;
                double radius = 5000;

                GeoPoint location3857 = location4326.ReprojectTo(4326, 3857);
                BoundingBox bbox3857 = BoundingBox.AroundPoint(location3857, radius); // 5km around point
                bbox3857.SRID = 3857;
                BoundingBox bbox4326 = bbox3857.ReprojectTo(Reprojection.SRID_PROJECTED_MERCATOR, Reprojection.SRID_GEODETIC);

                HeightMap heightMap = _elevationService.GetHeightMap(ref bbox4326, dataset);

                // Highest point
                var highest = heightMap.Coordinates.First(pt => pt.Elevation.Value == heightMap.Maximum);
                _logger.LogInformation($"Highest point: {highest} at {highest.DistanceTo(location4326)} meters");

                OsmHighwayProcessor roadsProcessor = new OsmHighwayProcessor(GeoTransformPipeline.Default);

                // Download buildings and convert them to GeoJson
                FeatureCollection features = _osmService.GetOsmDataAsGeoJson(bbox4326, q => q.WithWays("highway"));
                // Create internal building model
                OsmModelList<HighwayModel> parsed = _osmService.CreateModelsFromGeoJson<HighwayModel>(features, roadsProcessor.ModelFactory);

                int parallelCount = -1;
                Parallel.ForEach(parsed.Models, new ParallelOptions { MaxDegreeOfParallelism = parallelCount }, model =>
                //foreach(var model in parsed.Models)
                {

                    model.LineString = _elevationService.GetLineGeometryElevation(model.LineString, dataset);
                }
                );
                var osmRoads = parsed.Models.ToDictionary(p => p.Id, p => p);
                var osmRoadLines = parsed.Models.ToDictionary(p => p.Id, p => p.LineString);

                (Slope Slope, HighwayModel Road) maxSlope = (Slope.Zero, null);
                (Slope Slope, HighwayModel Road) maxAvgSlope = (Slope.Zero, null);
                foreach (var model in osmRoadLines)
                {
                    var metrics = model.Value.ComputeMetrics();

                    var slope = GetMaxSlope(model.Value);
                    if (slope > maxSlope.Slope)
                    {
                        maxSlope.Slope = slope;
                        maxSlope.Road = osmRoads[model.Key];
                    }


                    var slopeAvg = ComputeSlope(model.Value.First(), model.Value.Last());
                    if (slopeAvg > maxAvgSlope.Slope)
                    {
                        maxAvgSlope.Slope = slopeAvg;
                        maxSlope.Road = osmRoads[model.Key];
                    }
                }

                //int parallelCount = -1;
                //Parallel.ForEach(parsed.Models, new ParallelOptions { MaxDegreeOfParallelism = parallelCount }, model =>
                ////foreach (var model in parsed.Models)
                //{

                //    model.LineString =_elevationService.GetLineGeometryElevation(model.LineString, dataset);
                //}
                //);
                //osmRoadLines = parsed.Models.ToDictionary(p => p.Id, p => p.LineString);

                //maxSlope = (Slope.Zero, null);
                //maxAvgSlope = (Slope.Zero, null);
                //foreach (var model in osmRoadLines)
                //{
                //    var metrics = model.Value.ComputeMetrics();

                //    var slope = GetMaxSlope(model.Value);
                //    if (slope > maxSlope.Slope)
                //    {
                //        maxSlope.Slope = slope;
                //        maxSlope.Road = osmRoads[model.Key];
                //    }


                //    var slopeAvg = ComputeSlope(model.Value.First(), model.Value.Last());
                //    if (slopeAvg > maxAvgSlope.Slope)
                //    {
                //        maxAvgSlope.Slope = slopeAvg;
                //        maxSlope.Road = osmRoads[model.Key];
                //    }
                //}


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in {nameof(HighestPointFinder)}");
            }
        }

        public Slope GetMaxSlope(List<GeoPoint> line)
        {
            Slope max = Slope.Zero;
            for (int i = 1; i < line.Count; i++)
            {
                Slope segmentSlope = ComputeSlope(line[i - 1], line[i]);
                if (segmentSlope > max)
                {
                    max = segmentSlope;
                }
            }
            return max;
        }
        public Slope ComputeSlope(GeoPoint a, GeoPoint b)
        {
            if (a == null || b == null)
                return Slope.Zero;

            double run = b.DistanceFromOriginMeters.Value - a.DistanceFromOriginMeters.Value;
            if (run <= double.Epsilon || !a.Elevation.HasValue || !b.Elevation.HasValue)
                return Slope.Zero;

            double rise = b.Elevation.Value - a.Elevation.Value;

            return Slope.FromRiseRun(rise, run);

        }
    }

    public struct Slope : IComparable<Slope>
    {
        public double Degrees { get; set; }

        public double Percent { get; set; }

        public static Slope Zero => new Slope() { Degrees = 0, Percent = 0 };

        public static Slope FromRiseRun(double rise, double run)
        {
            if (run == 0) return Slope.Zero;

            return new Slope()
            {
                Degrees = MathHelper.ToDegrees(Math.Atan(rise / run))
                ,
                Percent = 100.0 * (rise / run)
            };
        }

        public int CompareTo([AllowNull] Slope other)
        {
            return Math.Abs(this.Degrees).CompareTo(Math.Abs(other.Degrees));
        }

        public static bool operator >(Slope a, Slope b)
        {
            return a.CompareTo(b) > 0;
        }
        public static bool operator <(Slope a, Slope b)
        {
            return a.CompareTo(b) < 0;
        }
    }
}
