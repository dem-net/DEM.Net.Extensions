using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DEM.Net.Core;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;

namespace DEM.Net.Extension.Osm.Highways;


public class OsmHighwayProcessor(GeoTransformPipeline transform, string highwaysColor) : OsmProcessorStage<HighwayModel>(transform)
{

    private const float LaneWidthMeters = 3.5F;
    /// <summary>
    /// Roads are lifted above the terrain (meters, scaled by the vertical exaggeration like the terrain): at the
    /// exact DEM elevation they flicker through it (z-fighting), and the reduced terrain mesh departs from the DEM.
    /// </summary>
    public const double HeightAboveTerrainMeters = 0.5;
    private readonly string highwaysColor = highwaysColor;

    public override IOsmDataSettings DataSettings { get; set; } = new HighwaysDataFilter();
    public override OsmModelFactory<HighwayModel> ModelFactory => new HighwayValidator(base._logger, highwaysColor);
    public override string glTFNodeName => "Streets";

    protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IReadOnlyList<HighwayModel> models)
    {
        if (models.Count > 0)
        {
            gltfModel = _gltfService.AddLines(gltfModel, glTFNodeName, models.Select(m => ((IEnumerable<GeoPoint>)m.LineString, this.GetRoadWidth(m))), models[0].Color);
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
            return road.Type switch
            {
                "unclassified" => 3,
                _ => LaneWidthMeters,
            };
        }
    }

    protected override IEnumerable<HighwayModel> ComputeModelElevationsAndTransform(IEnumerable<HighwayModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
    {

        using TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug);
        if (computeElevations)
        {
            int i = 0;
            long pointCount = 0;
            Stopwatch sw = Stopwatch.StartNew();
            foreach (var model in models)
            {
                var points = (_elevationService.GetLineGeometryElevation(model.LineString, dataSet) ?? Enumerable.Empty<GeoPoint>())
                    .Select(p => p with { Elevation = (p.Elevation ?? 0) + HeightAboveTerrainMeters })
                    .ToList();
                model.LineString = [.. Transform.TransformPoints(ToDataSetSrid(points, dataSet))];
                pointCount += model.LineString.Count;

                if (++i % 100 == 0) _logger.LogInformation($"Computed {i:N0} highways elevation ({pointCount:N0} points {pointCount / sw.Elapsed.TotalSeconds:N0} pts/s)...");
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
