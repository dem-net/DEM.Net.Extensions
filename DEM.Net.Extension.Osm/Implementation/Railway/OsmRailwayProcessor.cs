using System.Collections.Generic;
using System.Linq;
using DEM.Net.Core;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;

namespace DEM.Net.Extension.Osm.Railway;

internal class OsmRailwayProcessor(GeoTransformPipeline? transformPipeline) : OsmProcessorStage<RailwayModel>(transformPipeline)
{

    public override OsmModelFactory<RailwayModel> ModelFactory => new RailwayValidator(base._logger);


    public override IOsmDataSettings DataSettings { get; set; } = new RailwayDataFilter();
    public override string glTFNodeName => nameof(RailwayModel);

    protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IReadOnlyList<RailwayModel> models)
    {
        float width = OsmLineElevation.RailwayWidthMeters * (float)(Transform?.ModelUnitsPerMeter ?? 1d);
        gltfModel = _gltfService.AddLines(gltfModel, glTFNodeName, models.Select(m => (m.LineString.AsEnumerable(), width)), VectorsExtensions.CreateColor(165, 42, 42));

        return gltfModel;
    }

    protected override IEnumerable<RailwayModel> ComputeModelElevationsAndTransform(IEnumerable<RailwayModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
    {
        using TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug);
        if (computeElevations)
        {
            var list = models as IReadOnlyList<RailwayModel> ?? [.. models];
            var lines = GetLinesElevation(list, m => m.LineString, dataSet);
            for (int i = 0; i < list.Count; i++)
            {
                list[i].LineString = [.. Transform.TransformPoints(ToDataSetSrid(OsmLineElevation.Elevate(lines[i], list[i].Bridge), dataSet))];
                yield return list[i];
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
