using DEM.Net.Core;
using DEM.Net.Extension.Osm.OverpassAPI;
using DEM.Net.Extension.Osm.Ski;
using DEM.Net.glTF.SharpglTF;
using GeoJSON.Net;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace DEM.Net.Extension.Osm.Highways
{

    public class OsmHighwayProcessor : OsmProcessorStage<HighwayModel>
    {

        private const float LaneWidthMeters = 3.5F;

        public OsmHighwayProcessor(GeoTransformPipeline transform) : base(transform)
        {
        }

        public override string[] WaysFilter { get; set; } = new string[] { "highway" };
        public override string[] RelationsFilter { get; set; } = null;
        public override string[] NodesFilter { get; set; } = null;
        public override bool ComputeElevations { get; set; } = true;
        public override OsmModelFactory<HighwayModel> ModelFactory => new HighwayValidator(base._logger);
        public override string glTFNodeName => "Roads";

        protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, OsmModelList<HighwayModel> models)
        {
            if (models.Any())
            {
                gltfModel = _gltfService.AddLines(gltfModel, glTFNodeName, models.Select(m => ((IEnumerable<GeoPoint>)m.LineString, this.GetRoadWidth(m))), models.First().ColorVec4);
            }
            return gltfModel;

        }

        private float GetRoadWidth(HighwayModel road)
        {
            if (road.Lanes.HasValue)
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

        protected override List<HighwayModel> ComputeModelElevationsAndTransform(OsmModelList<HighwayModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
        {

            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug))
            {
                if (computeElevations)
                {
                    int parallelCount = -1;
                    Parallel.ForEach(models, new ParallelOptions { MaxDegreeOfParallelism = parallelCount }, model =>
                       {

                           model.LineString = Transform.TransformPoints(_elevationService.GetLineGeometryElevation(model.LineString, dataSet))
                                                .ToList();
                       }
                    );
                }
                else
                {
                    foreach (var model in models)
                    {
                        model.LineString = new List<GeoPoint>(Transform.TransformPoints(model.LineString));
                    }
                }

            }

            return models.Models;
        }


    }
}
