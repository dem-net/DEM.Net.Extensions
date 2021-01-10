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
        protected ElevationService _elevationService;
        protected SharpGltfService _gltfService;
        protected MeshService _meshService;
        protected OsmService _osmService;
        protected ILogger _logger;

        public void Init(ElevationService elevationService
            , SharpGltfService gltfService
            , MeshService meshService
            , OsmService osmService
            , ILogger logger)
        {
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._osmService = osmService;
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
                FeatureCollection features = _osmService.GetOsmDataAsGeoJson(bbox, q =>
                {
                    if (DataFilter?.WaysFilter != null) foreach (var filter in DataFilter.WaysFilter) q.WithWays(filter);
                    if (DataFilter?.RelationsFilter != null) foreach (var filter in DataFilter.RelationsFilter) q.WithRelations(filter);
                    if (DataFilter?.NodesFilter != null) foreach (var filter in DataFilter.NodesFilter) q.WithNodes(filter);

                    return q;
                });

                // Create internal building model
                OsmModelList<T> parsed = _osmService.CreateModelsFromGeoJson<T>(features, ModelFactory);

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

    }
}
