using DEM.Net.Core;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System;
using System.Linq;
using System.Collections.Generic;
using NetTopologySuite.Features;

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

        public abstract IOsmDataSettings DataSettings { get; set; }

        public abstract bool ComputeElevations { get; set; }

        public abstract OsmModelFactory<T> ModelFactory { get; }

        public abstract string glTFNodeName { get; }

        public virtual GeoTransformPipeline Transform { get; set; }


        public ModelRoot Run(ModelRoot gltfModel, BoundingBox bbox, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles)
        {
            try
            {
                // Download buildings and convert them to GeoJson
                IEnumerable<IFeature> features = _osmDataService.GetOsmDataAsGeoJson(bbox, DataSettings);
                // Create internal building model
                IEnumerable<T> parsed = this.CreateModelsFromGeoJson<T>(features, ModelFactory);

                //_logger.LogInformation($"Computing elevations ({parsed.Models.Count} lines, {parsed.TotalPoints} total points)...");
                // Compute elevations (faster elevation when point count is known in advance)
                // Download elevation data if missing
                if (computeElevations && downloadMissingFiles) _elevationService.DownloadMissingFiles(dataSet, bbox);
                parsed = this.ComputeModelElevationsAndTransform(parsed, computeElevations, dataSet, downloadMissingFiles);

                gltfModel = this.AddToModel(gltfModel, glTFNodeName, parsed);


                return gltfModel;

            }
            catch (Exception ex)
            {
                _logger.LogError($"{typeof(T).Name} generator error: {ex.Message}");
                throw;
            }
        }

        protected abstract IEnumerable<T> ComputeModelElevationsAndTransform(IEnumerable<T> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles);

        protected abstract ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IEnumerable<T> models);

        public IEnumerable<T> CreateModelsFromGeoJson<T>(IEnumerable<IFeature> features, OsmModelFactory<T> validator) where T : CommonModel
        {

            int numValid = 0;
            int numInvalid = 0;

            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(CreateModelsFromGeoJson), _logger, LogLevel.Debug))
            {

                foreach (var feature in features)
                {
                    validator.RegisterTags(feature as Feature);
                    T model = validator.CreateModel(feature as Feature);

                    if (model == null)
                    {
                        numInvalid++;
                        _logger.LogWarning($"{nameof(CreateModelsFromGeoJson)}: {feature.Attributes["osmid"]}, type {feature.Geometry.OgcGeometryType} not supported.");
                    }
                    else if (validator.ParseTags(model)) // Model not processed further if tag parsing fails
                    {
                        numValid++;
                        yield return model;
                    }
                }
            }

            _logger.LogInformation($"{nameof(CreateModelsFromGeoJson)} done for {validator._totalPoints} points. {numValid:N0} valid models, {numInvalid:N0} invalid");


        }
    }
}
