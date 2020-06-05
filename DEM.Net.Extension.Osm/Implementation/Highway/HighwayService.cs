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
    public class HighwayService
    {
        private readonly IElevationService _elevationService;
        private readonly SharpGltfService _gltfService;
        private readonly IMeshService _meshService;
        private readonly OsmService _osmService;
        private readonly ILogger<HighwayService> _logger;

        public HighwayService(IElevationService elevationService
            , SharpGltfService gltfService
            , IMeshService meshService
            , OsmService osmService
            , ILogger<HighwayService> logger)
        {
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._osmService = osmService;
            this._logger = logger;
        }

        public ModelRoot GetHighway3DModel(BoundingBox bbox, string wayTag, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline transform)
        {
            try
            {
                ModelRoot gltfModel = _gltfService.CreateNewModel();
                gltfModel = AddHighway3DModel(gltfModel, bbox, wayTag, dataSet, downloadMissingFiles, transform);

                return gltfModel;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetHighway3DModel)} error: {ex.Message}");
                throw;
            }
        }
        public ModelRoot AddHighway3DModel(ModelRoot model, BoundingBox bbox, string wayTag, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline transform)
        {
            try
            {

                List<HighwayModel> models = GetHighwayModels(bbox, wayTag, dataSet, downloadMissingFiles, transform);

                foreach (var m in models)
                {
                    model = _gltfService.AddLine(model, m.LineString, m.ColorVec4, 30);
                }

                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetHighway3DModel)} error: {ex.Message}");
                throw;
            }
        }

        public List<HighwayModel> GetHighwayModels(BoundingBox bbox, string wayTag, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline transform)
        {
            try
            {
                // Download buildings and convert them to GeoJson
                FeatureCollection features = _osmService.GetOsmDataAsGeoJson(bbox, q => q
                                                                                       .WithWays(wayTag)
                                                                              );

                // Download elevation data if missing
                if (downloadMissingFiles) _elevationService.DownloadMissingFiles(dataSet, bbox);

                // Create internal building model
                var validator = new HighwayValidator(_logger);
                (List<HighwayModel> Models, int TotalPoints) parsed = _osmService.CreateModelsFromGeoJson<HighwayModel>(features, validator);

                _logger.LogInformation($"Computing elevations ({parsed.Models.Count} lines, {parsed.TotalPoints} total points)...");
                // Compute elevations (faster elevation when point count is known in advance)
                parsed.Models = this.ComputeElevations(parsed.Models, parsed.TotalPoints, dataSet, transform);

                return parsed.Models;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetHighwayModels)} error: {ex.Message}");
                throw;
            }
        }
        public ModelRoot GetHighway3DModel(List<HighwayModel> models)
        {
            try
            {

                ModelRoot gltfModel = _gltfService.CreateNewModel();
                foreach (var m in models)
                {
                    gltfModel = _gltfService.AddLine(gltfModel, m.LineString, m.ColorVec4, 30);
                }

                return gltfModel;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetHighway3DModel)} error: {ex.Message}");
                throw;
            }
        }

        public List<HighwayModel> ComputeElevations(List<HighwayModel> models, int pointCount, DEMDataSet dataset, IGeoTransformPipeline transform)
        {
            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug))
            {
                //foreach(var model in models)
                Parallel.ForEach(models, model =>
                {
                    model.LineString = transform.TransformPoints(_elevationService.GetLineGeometryElevation(model.LineString, dataset))
                                         .ZTranslate(10)
                                         .ToList();
                }
                );

            }

            return models;

        }

        
    }
}
