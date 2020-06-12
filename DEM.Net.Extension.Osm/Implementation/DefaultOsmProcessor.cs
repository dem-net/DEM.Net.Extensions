using DEM.Net.Core;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System.Collections.Generic;
using DEM.Net.Extension.Osm.Highways;

namespace DEM.Net.Extension.Osm
{
    public class DefaultOsmProcessor
    {
        private readonly IElevationService _elevationService;
        private readonly SharpGltfService _gltfService;
        private readonly IMeshService _meshService;
        private readonly OsmService _osmService;
        private readonly ILogger<DefaultOsmProcessor> _logger;

        public DefaultOsmProcessor(IElevationService elevationService
            , SharpGltfService gltfService
            , IMeshService meshService
            , OsmService osmService
            , ILogger<DefaultOsmProcessor> logger)
        {
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._osmService = osmService;
            this._logger = logger;
        }
        public ModelRoot Run(BoundingBox bbox, IGeoTransformPipeline transform, bool computeElevations, DEMDataSet dataSet = null, bool downloadMissingFiles = true)
        {
            List<IOsmProcessor> processors = new List<IOsmProcessor>();

            processors.Add(new OsmBuildingProcessor());
            processors.Add(new OsmHighwayProcessor());
            processors.Add(new OsmPisteSkiProcessor());

            ModelRoot model = _gltfService.CreateNewModel();

            foreach (var p in processors)
            {
                p.Init(_elevationService, _gltfService, _meshService, _osmService, _logger);

                model = p.Run(model, bbox, computeElevations, dataSet, downloadMissingFiles, transform);
            }

            return model;


        }
    }
}
