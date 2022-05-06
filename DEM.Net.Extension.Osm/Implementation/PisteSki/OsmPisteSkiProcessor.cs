using DEM.Net.Core;
using DEM.Net.Extension.Osm.Ski;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DEM.Net.Extension.Osm.Buildings
{
    public class OsmPisteSkiProcessor : OsmProcessorStage<PisteModel>
    {

        private const float PisteWidthMeters = 30F;

        public OsmPisteSkiProcessor(GeoTransformPipeline transformPipeline) : base(transformPipeline)
        {
            this.DataSettings = new PisteSkiDataFilter();
        }
        public override OsmModelFactory<PisteModel> ModelFactory => new SkiPisteValidator(base._logger);


        public override IOsmDataSettings DataSettings { get; set; }
        public override string glTFNodeName => "SkiPiste";

        protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IEnumerable<PisteModel> models)
        {

            foreach (var difficultyGroup in models.GroupBy(m => m.Difficulty))
            {
                gltfModel = _gltfService.AddLines(gltfModel, $"{glTFNodeName}_{difficultyGroup.Key}", difficultyGroup.Select(m => ((IEnumerable<GeoPoint>)m.LineString, PisteWidthMeters)), difficultyGroup.First().ColorVec4);
            }

            return gltfModel;
        }

        protected override IEnumerable<PisteModel> ComputeModelElevationsAndTransform(IEnumerable<PisteModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
        {
            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug))
            {
                if (computeElevations)
                {
                    foreach (var model in models)
                    {
                        model.LineString = Transform.TransformPoints(_elevationService.GetLineGeometryElevation(model.LineString, dataSet))
                                             .ToList();

                        yield return model;
                    }
                    //Parallel.ForEach(models, model =>
                    //{
                    //    model.LineString = Transform.TransformPoints(_elevationService.GetLineGeometryElevation(model.LineString, dataSet))
                    //                         .ToList();
                    //});
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
