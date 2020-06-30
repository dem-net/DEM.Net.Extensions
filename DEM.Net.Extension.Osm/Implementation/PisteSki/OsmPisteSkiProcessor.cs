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
using System.Threading;
using System.Threading.Tasks;

namespace DEM.Net.Extension.Osm.Buildings
{
    public class OsmPisteSkiProcessor : OsmProcessorStage<PisteModel>
    {

        private const float PisteWidthMeters = 30F;
        public override string[] WaysFilter { get; set; } = new string[] { "piste:type" };
        public override string[] RelationsFilter { get; set; } = null;
        public override string[] NodesFilter { get; set; } = null;
        public override bool ComputeElevations { get; set; } = true;

        public OsmPisteSkiProcessor(GeoTransformPipeline transformPipeline) : base(transformPipeline)
        {

        }
        public override OsmModelFactory<PisteModel> ModelFactory => new SkiPisteValidator(base._logger);

        public override string glTFNodeName => "SkiPiste";

        protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, OsmModelList<PisteModel> models)
        {
            if (models.Any())
            {
                foreach (var difficultyGroup in models.GroupBy(m => m.Difficulty))
                {
                    gltfModel = _gltfService.AddLines(gltfModel, $"{glTFNodeName}_{difficultyGroup.Key}", difficultyGroup.Select(m => ((IEnumerable<GeoPoint>)m.LineString, PisteWidthMeters)), difficultyGroup.First().ColorVec4);
                }
            }
            return gltfModel;
        }

        protected override List<PisteModel> ComputeModelElevationsAndTransform(OsmModelList<PisteModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
        {
            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug))
            {
                if (computeElevations)
                {
                    Parallel.ForEach(models, model =>
                    {
                        model.LineString = Transform.TransformPoints(_elevationService.GetLineGeometryElevation(model.LineString, dataSet))
                                             .ToList();
                    });
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
