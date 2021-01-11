using DEM.Net.Core;
using DEM.Net.glTF.SharpglTF;
using GeoJSON.Net.Feature;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System;
using System.Linq;
using System.Collections.Generic;

namespace DEM.Net.Extension.Osm
{
    public abstract class OsmProcessorStage<T> : IOsmProcessor
        where T : CommonModel
    {
        protected ElevationService _elevationService;
        protected SharpGltfService _gltfService;
        protected MeshService _meshService;
        protected IOsmDataService _osmDataService;
        protected ILogger _logger;

        public void Init(ElevationService elevationService
            , SharpGltfService gltfService
            , MeshService meshService
            , IOsmDataService osmDataService
            , ILogger logger)
        {
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._osmDataService = osmDataService;
            this._logger = logger;
        }

        public OsmProcessorStage(GeoTransformPipeline transform)
        {
            this.Transform = transform?.Clone();
        }

        public void AddPostTransform(Func<IEnumerable<GeoPoint>, IEnumerable<GeoPoint>> postTransform)
        {
            Transform.TransformPoints = Transform.TransformPoints.PostTransform(p => postTransform(p));
        }

        public abstract IOsmDataFilter DataFilter { get; }

        public abstract bool ComputeElevations { get; set; }

        public abstract OsmModelFactory<T> ModelFactory { get; }

        public abstract string glTFNodeName { get; }

        public virtual GeoTransformPipeline Transform { get; set; }


        public ModelRoot Run(ModelRoot gltfModel, BoundingBox bbox, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
        {
            try
            {
                // Download buildings and convert them to GeoJson
                FeatureCollection features = _osmDataService.GetOsmDataAsGeoJson(bbox, DataFilter);
                // Create internal building model
                OsmModelList<T> parsed = this.CreateModelsFromGeoJson<T>(features, ModelFactory);

                _logger.LogInformation($"Computing elevations ({parsed.Models.Count} lines, {parsed.TotalPoints} total points)...");
                // Compute elevations (faster elevation when point count is known in advance)
                // Download elevation data if missing
                if (downloadMissingFiles) _elevationService.DownloadMissingFiles(dataSet, bbox);
                parsed.Models = this.ComputeModelElevationsAndTransform(parsed, computeElevations, dataSet, downloadMissingFiles);

                if (parsed.Models.Any())
                {
                    gltfModel = this.AddToModel(gltfModel, glTFNodeName, parsed);
                }

                return gltfModel;

            }
            catch (Exception ex)
            {
                _logger.LogError($"{typeof(T).Name} generator error: {ex.Message}");
                throw;
            }
        }

        protected abstract List<T> ComputeModelElevationsAndTransform(OsmModelList<T> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles);

        protected abstract ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, OsmModelList<T> models);

        public OsmModelList<T> CreateModelsFromGeoJson<T>(FeatureCollection features, OsmModelFactory<T> validator) where T : CommonModel
        {

            OsmModelList<T> models = new OsmModelList<T>(features.Features.Count);
            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(CreateModelsFromGeoJson), _logger, LogLevel.Debug))
            {
                int count = 0;
                foreach (var feature in features.Features)
                {
                    count++;
                    validator.RegisterTags(feature);
                    T model = validator.CreateModel(feature);

                    if (model == null)
                    {
                        _logger.LogWarning($"{nameof(CreateModelsFromGeoJson)}: {feature.Id}, type {feature.Geometry.Type} not supported.");
                    }
                    else if (validator.ParseTags(model)) // Model not processed further if tag parsing fails
                    {
                        models.Add(model);
                    }
                }
            }

            //#if DEBUG
            //            File.WriteAllText($"{typeof(T).Name}_osm_tag_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt", validator.GetTagsReport(), Encoding.UTF8);
            //#endif

            _logger.LogInformation($"{nameof(CreateModelsFromGeoJson)} done for {validator._totalPoints} points.");

            models.TotalPoints = validator._totalPoints;

            return models;

        }
    }
}
