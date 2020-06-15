﻿using DEM.Net.Core;
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
        protected IElevationService _elevationService;
        protected SharpGltfService _gltfService;
        protected IMeshService _meshService;
        protected OsmService _osmService;
        protected ILogger _logger;

        public void Init(IElevationService elevationService
            , SharpGltfService gltfService
            , IMeshService meshService
            , OsmService osmService
            , ILogger logger)
        {
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._osmService = osmService;
            this._logger = logger;
        }

        public abstract string[] WaysFilter { get; set; }
        public abstract string[] RelationsFilter { get; set; }
        public abstract string[] NodesFilter { get; set; }
        public abstract bool ComputeElevations { get; set; }

        public abstract OsmModelFactory<T> ModelFactory { get; }

        public abstract string glTFNodeName { get; }

        public ModelRoot Run(ModelRoot gltfModel, BoundingBox bbox, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline transform)
        {
            try
            {
                // Download buildings and convert them to GeoJson
                FeatureCollection features = _osmService.GetOsmDataAsGeoJson(bbox, q =>
                {
                    if (WaysFilter != null) foreach (var filter in WaysFilter) q.WithWays(filter);
                    if (RelationsFilter != null) foreach (var filter in RelationsFilter) q.WithRelations(filter);
                    if (NodesFilter != null) foreach (var filter in NodesFilter) q.WithNodes(filter);

                    return q;
                });

                // Create internal building model
                OsmModelList<T> parsed = _osmService.CreateModelsFromGeoJson<T>(features, ModelFactory);

                _logger.LogInformation($"Computing elevations ({parsed.Models.Count} lines, {parsed.TotalPoints} total points)...");
                // Compute elevations (faster elevation when point count is known in advance)
                // Download elevation data if missing
                if (downloadMissingFiles) _elevationService.DownloadMissingFiles(dataSet, bbox);
                parsed.Models = this.ComputeModelElevationsAndTransform(parsed, computeElevations, dataSet, downloadMissingFiles, transform);

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

        protected abstract List<T> ComputeModelElevationsAndTransform(OsmModelList<T> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline transform);

        protected abstract ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, OsmModelList<T> models);

    }
}