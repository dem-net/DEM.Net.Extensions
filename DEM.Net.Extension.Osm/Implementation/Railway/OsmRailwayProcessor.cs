using System.Collections.Generic;
using System.Linq;
using DEM.Net.Core;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;

namespace DEM.Net.Extension.Osm.Railway
{
    internal class OsmRailwayProcessor : OsmProcessorStage<RailwayModel>
    {

        private const float WidthMeters = 2.5F;

        public OsmRailwayProcessor(GeoTransformPipeline transformPipeline) : base(transformPipeline)
        {
            this.DataSettings = new RailwayDataFilter();
        }
        public override OsmModelFactory<RailwayModel> ModelFactory => new RailwayValidator(base._logger);


        public override IOsmDataSettings DataSettings { get; set; }
        public override string glTFNodeName => nameof(RailwayModel);

        protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IEnumerable<RailwayModel> models)
        {
            gltfModel = _gltfService.AddLines(gltfModel, glTFNodeName, models.Select(m => (m.LineString.AsEnumerable(), WidthMeters)), VectorsExtensions.CreateColor(165, 42, 42));

            return gltfModel;
        }

        protected override IEnumerable<RailwayModel> ComputeModelElevationsAndTransform(IEnumerable<RailwayModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
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
