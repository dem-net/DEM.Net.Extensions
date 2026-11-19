using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DEM.Net.Core;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;

namespace DEM.Net.Extension.Osm.Highways;


public class OsmHighwayProcessor(GeoTransformPipeline? transform, string? highwaysColor) : OsmProcessorStage<HighwayModel>(transform)
{

    /// <inheritdoc cref="OsmLineElevation.HeightAboveTerrainMeters"/>
    public const double HeightAboveTerrainMeters = OsmLineElevation.HeightAboveTerrainMeters;
    private readonly string? highwaysColor = highwaysColor;

    public override IOsmDataSettings DataSettings { get; set; } = new HighwaysDataFilter();
    public override OsmModelFactory<HighwayModel> ModelFactory => new HighwayValidator(base._logger, highwaysColor);
    public override string glTFNodeName => "Streets";

    protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IReadOnlyList<HighwayModel> models)
    {
        if (models.Count > 0)
        {
            float unitsPerMeter = (float)(Transform?.ModelUnitsPerMeter ?? 1d);
            gltfModel = _gltfService.AddLines(gltfModel, glTFNodeName, models.Select(m => ((IEnumerable<GeoPoint>)m.LineString, OsmLineElevation.RoadWidthMeters(m.Type, m.Lanes) * unitsPerMeter)), models[0].Color);
        }

        return gltfModel;

    }

    protected override IEnumerable<HighwayModel> ComputeModelElevationsAndTransform(IEnumerable<HighwayModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
    {

        using TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug);
        if (computeElevations)
        {
            var roads = models as IReadOnlyList<HighwayModel> ?? [.. models];
            Stopwatch sw = Stopwatch.StartNew();
            var lines = GetLinesElevation(roads, m => m.LineString, dataSet);
            long pointCount = 0;
            for (int i = 0; i < roads.Count; i++)
            {
                var points = OsmLineElevation.Elevate(lines[i], roads[i].Bridge);
                roads[i].LineString = [.. Transform.TransformPoints(ToDataSetSrid(points, dataSet))];
                pointCount += roads[i].LineString.Count;
                yield return roads[i];
            }
            _logger.LogInformation("Computed {Count:N0} highways elevation ({Points:N0} points, {Rate:N0} pts/s)", roads.Count, pointCount, pointCount / Math.Max(sw.Elapsed.TotalSeconds, 1e-3));
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
