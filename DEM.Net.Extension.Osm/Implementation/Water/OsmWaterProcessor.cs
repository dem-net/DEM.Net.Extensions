using DEM.Net.Core;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DEM.Net.Extension.Osm.Water
{
    internal class OsmWaterProcessor : OsmProcessorStage<WaterModel>
    {

        private const float WaterWidthMeters = 30F;
        public override bool ComputeElevations { get; set; } = true;

        public OsmWaterProcessor(GeoTransformPipeline transformPipeline) : base(transformPipeline)
        {
            this.DataSettings = new WaterDataFilter();
        }
        public override OsmModelFactory<WaterModel> ModelFactory => new WaterValidator(base._logger);


        public override IOsmDataSettings DataSettings { get; set; }
        public override string glTFNodeName => nameof(WaterModel);

        protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IEnumerable<WaterModel> models)
        {
            if (models.Any())
            {
                gltfModel = _gltfService.AddLines(gltfModel, glTFNodeName, models.Select(m => (m.LineString.AsEnumerable(), WaterWidthMeters)), VectorsExtensions.CreateColor(102, 178, 255));
            }
            return gltfModel;
        }

        protected override IEnumerable<WaterModel> ComputeModelElevationsAndTransform(IEnumerable<WaterModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
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
