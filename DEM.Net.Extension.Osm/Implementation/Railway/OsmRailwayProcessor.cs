using System.Collections.Generic;
using System.Linq;
using DEM.Net.Core;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;

namespace DEM.Net.Extension.Osm.Railway;

internal class OsmRailwayProcessor(GeoTransformPipeline transformPipeline) : OsmProcessorStage<RailwayModel>(transformPipeline)
{

    private const float WidthMeters = 2.5F;

    public override OsmModelFactory<RailwayModel> ModelFactory => new RailwayValidator(base._logger);


    public override IOsmDataSettings DataSettings { get; set; } = new RailwayDataFilter();
    public override string glTFNodeName => nameof(RailwayModel);

    protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IEnumerable<RailwayModel> models)
    {
        gltfModel = _gltfService.AddLines(gltfModel, glTFNodeName, models.Select(m => (m.LineString.AsEnumerable(), WidthMeters)), VectorsExtensions.CreateColor(165, 42, 42));

        return gltfModel;
    }

    protected override IEnumerable<RailwayModel> ComputeModelElevationsAndTransform(IEnumerable<RailwayModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
    {
        using TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug);
        if (computeElevations)
        {
            foreach (var model in models)
            {
                model.LineString = [.. Transform.TransformPoints(ToDataSetSrid(_elevationService.GetLineGeometryElevation(model.LineString, dataSet), dataSet))];

                yield return model;
            }
        }
        else
        {
            foreach (var model in models)
            {
                model.LineString = [.. Transform.TransformPoints(ToDataSetSrid(model.LineString, dataSet))];
                yield return model;
            }
        }

    }
}
