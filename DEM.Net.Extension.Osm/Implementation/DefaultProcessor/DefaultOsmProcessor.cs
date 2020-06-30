using DEM.Net.Core;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System.Collections.Generic;
using DEM.Net.Extension.Osm.Highways;
using DEM.Net.Extension.Osm.Model;
using DEM.Net.Extension.Osm.OverpassAPI;
using System;
using System.Linq;
using DEM.Net.Core.Configuration;
using Microsoft.Extensions.Options;

namespace DEM.Net.Extension.Osm
{
    public class DefaultOsmProcessor
    {
        private readonly DEMNetOptions _options;
        private readonly IElevationService _elevationService;
        private readonly SharpGltfService _gltfService;
        private readonly IMeshService _meshService;
        private readonly OsmService _osmService;
        private readonly ILogger<DefaultOsmProcessor> _logger;

        public DefaultOsmProcessor(IElevationService elevationService
            , SharpGltfService gltfService
            , IMeshService meshService
            , OsmService osmService
            , IOptions<DEMNetOptions> options
            , ILogger<DefaultOsmProcessor> logger)
        {
            this._options = options.Value;
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._osmService = osmService;
            this._logger = logger;
        }

        private List<IOsmProcessor> Build(OsmLayer layers, bool computeElevations = false, GeoTransformPipeline transform = null, bool withBuildingsColors = false, string defaultBuildingsColor = null)
        {
            List<IOsmProcessor> processors = new List<IOsmProcessor>();

            if (layers.HasFlag(OsmLayer.Buildings)) processors.Add(new OsmBuildingProcessor(transform, withBuildingsColors, defaultBuildingsColor));
            if (layers.HasFlag(OsmLayer.Highways))
            {
                var processor = new OsmHighwayProcessor(transform);
                if (computeElevations)
                {
                    processor.AddPostTransform(p => p.ZTranslate(_options.RenderGpxZTranslateTrackMeters));
                }
                processors.Add(processor);
            }
            if (layers.HasFlag(OsmLayer.PisteSki))
            {
                var processor = new OsmPisteSkiProcessor(transform);
                if (computeElevations)
                {
                    processor.AddPostTransform(p => p.ZTranslate(_options.RenderGpxZTranslateTrackMeters));
                }
                processors.Add(processor);
            }

            return processors;
        }
        public ModelRoot Run(ModelRoot model, OsmLayer layers, BoundingBox bbox, GeoTransformPipeline transform, bool computeElevations, DEMDataSet dataSet = null, bool downloadMissingFiles = true, bool withBuildingsColors = false, string defaultBuildingsColor = null)
        {
            List<IOsmProcessor> processors = Build(layers, computeElevations, transform, withBuildingsColors, defaultBuildingsColor);

            model = model ?? _gltfService.CreateNewModel();

            foreach (var p in processors)
            {
                p.Init(_elevationService, _gltfService, _meshService, _osmService, _logger);

                model = p.Run(model, bbox, computeElevations, dataSet, downloadMissingFiles);
            }

            return model;


        }

        public int GetCount(BoundingBox bbox, OsmLayer layers, DEMDataSet dataSet)
        {
            List<IOsmProcessor> processors = Build(layers);

            OverpassQuery q = new OverpassQuery(bbox, _logger);
            foreach (var p in processors)
            {
                // Download buildings and convert them to GeoJson
                if (p.WaysFilter != null) foreach (var filter in p.WaysFilter) q.WithWays(filter);
                if (p.RelationsFilter != null) foreach (var filter in p.RelationsFilter) q.WithRelations(filter);
                if (p.NodesFilter != null) foreach (var filter in p.NodesFilter) q.WithNodes(filter);
            }

            OverpassCountResult countResult = _osmService.GetOsmDataCount(bbox, q);

            return countResult.Tags.Nodes * 2;
        }

    }
}
