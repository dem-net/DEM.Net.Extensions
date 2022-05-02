using DEM.Net.Core;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DEM.Net.Extension.Osm.Highways
{

    public class OsmHighwayProcessor : OsmProcessorStage<HighwayModel>
    {

        private const float LaneWidthMeters = 3.5F;
        private readonly string highwaysColor;

        public OsmHighwayProcessor(GeoTransformPipeline transform, string highwaysColor) : base(transform)
        {
            this.DataSettings = new HighwaysDataFilter();
            this.highwaysColor = highwaysColor;
        }


        public override IOsmDataSettings DataSettings { get; set; }
        public override bool ComputeElevations { get; set; } = true;
        public override OsmModelFactory<HighwayModel> ModelFactory => new HighwayValidator(base._logger, highwaysColor);
        public override string glTFNodeName => "Streets";

        protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IEnumerable<HighwayModel> models)
        {
            if (models.Any())
            {
                gltfModel = _gltfService.AddLines(gltfModel, glTFNodeName, models.Select(m => ((IEnumerable<GeoPoint>)m.LineString, this.GetRoadWidth(m))), models.First().Color);
            }

            return gltfModel;

        }

        private float GetRoadWidth(HighwayModel road)
        {
            if ((road.Lanes ?? 0) > 0)
            {
                return road.Lanes.Value * LaneWidthMeters;
            }
            else
            {
                switch (road.Type)
                {
                    case "unclassified": return 3;
                    default:
                        return LaneWidthMeters;
                }
            }
        }

        protected override IEnumerable<HighwayModel> ComputeModelElevationsAndTransform(IEnumerable<HighwayModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
        {

            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug))
            {
                if (computeElevations)
                {
                    int i = 0;
                    long pointCount = 0;
                    Stopwatch sw = Stopwatch.StartNew();
                    foreach (var model in models)
                    {
                        IEnumerable<GeoPoint> lineString = model.LineString;
                        if (dataSet.SRID != 4326)
                        {
                            lineString  = lineString.ReprojectTo(4326, dataSet.SRID);
                        }

                        model.LineString = Transform.TransformPoints(_elevationService.GetLineGeometryElevation(lineString, dataSet))
                                             .ToList();
                        pointCount += model.LineString.Count;

                        if (++i % 100 == 0) _logger.LogInformation($"Computed {i:N0} highways elevation ({pointCount:N0} points {pointCount/sw.Elapsed.TotalSeconds:N0} pts/s)...");
                        yield return model;
                    }
                    //int parallelCount = -1;
                    //Parallel.ForEach(models, new ParallelOptions { MaxDegreeOfParallelism = parallelCount }, model =>
                    //   {

                    //       model.LineString = Transform.TransformPoints(_elevationService.GetLineGeometryElevation(model.LineString, dataSet))
                    //                            .ToList();

                    //       yield return model;
                    //   }
                    //);
                }
                else
                {
                    foreach (var model in models)
                    {
                        model.LineString = new List<GeoPoint>(Transform.TransformPoints(model.LineString));
                        yield return model;
                    }
                }

            }

        }


    }
}
