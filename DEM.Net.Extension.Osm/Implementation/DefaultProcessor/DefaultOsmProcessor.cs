﻿using DEM.Net.Core;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System.Collections.Generic;
using DEM.Net.Extension.Osm.Highways;
using DEM.Net.Extension.Osm.Model;
using System;
using System.Linq;
using DEM.Net.Core.Configuration;
using Microsoft.Extensions.Options;
using DEM.Net.Extension.Osm.Railway;
using DEM.Net.Extension.Osm.Water;

namespace DEM.Net.Extension.Osm
{
    public class DefaultOsmProcessor
    {
        private readonly IOsmDataServiceFactory _dataServiceFactory;
        private readonly DEMNetOptions _options;
        private readonly ElevationService _elevationService;
        private readonly SharpGltfService _gltfService;
        private readonly MeshService _meshService;
        private readonly ILogger<DefaultOsmProcessor> _logger;

        public OsmElevationOptions Settings { get; set; } 

        public DefaultOsmProcessor(ElevationService elevationService
            , SharpGltfService gltfService
            , MeshService meshService
            , IOsmDataServiceFactory dataServiceFactory
            , IOptions<DEMNetOptions> options
            , IOptions<OsmElevationOptions> osmOptions
            , ILogger<DefaultOsmProcessor> logger)
        {
            this._dataServiceFactory = dataServiceFactory;
            this._options = options.Value;
            this.Settings = osmOptions.Value;
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._logger = logger;
        }

        private List<IOsmProcessor> Build(OsmLayer layers, bool computeElevations = false, GeoTransformPipeline transform = null, bool withBuildingsColors = false, string defaultBuildingsColor = null)
        {
            List<IOsmProcessor> processors = new List<IOsmProcessor>();

            if (layers.HasFlag(OsmLayer.Buildings)) processors.Add(new OsmBuildingProcessor(transform, withBuildingsColors, defaultBuildingsColor));
            if (layers.HasFlag(OsmLayer.Railway)) processors.Add(new OsmRailwayProcessor(transform));
            if (layers.HasFlag(OsmLayer.Water)) processors.Add(new OsmWaterProcessor(transform));
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

            model = model ?? _gltfService.CreateNewModel();

            if (layers == OsmLayer.None)
                return model;

            IOsmDataService osmDataService = _dataServiceFactory.Create(Settings.DataServiceType);
            List<IOsmProcessor> processors = Build(layers, computeElevations, transform, withBuildingsColors, defaultBuildingsColor);

            foreach (var p in processors)
            {
                p.DataSettings.FlatGeobufTilesDirectory = Settings.FlatGeobufTilesDirectory;
                p.Init(_elevationService, _gltfService, _meshService, osmDataService, _logger);

                model = p.Run(model, bbox, computeElevations, dataSet, downloadMissingFiles);
            }

            return model;


        }

        public int GetCount(BoundingBox bbox, OsmLayer layers, DEMDataSet dataSet)
        {
            List<IOsmProcessor> processors = Build(layers);
            IOsmDataService osmDataService = _dataServiceFactory.Create(Settings.DataServiceType);
            int count = 0;
            foreach (var p in processors)
            {

                count += osmDataService.GetOsmDataCount(bbox, p.DataSettings);
            }

            return count;

            
        }

    }
}
