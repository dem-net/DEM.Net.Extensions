using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DEM.Net.Core;
using DEM.Net.Core.Configuration;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Extension.Osm.Highways;
using DEM.Net.Extension.Osm.Railway;
using DEM.Net.Extension.Osm.Water;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpGLTF.Schema2;

namespace DEM.Net.Extension.Osm;

public class DefaultOsmProcessor(ElevationService elevationService
        , SharpGltfService gltfService
        , MeshService meshService
        , IOsmDataServiceFactory dataServiceFactory
        , IOptions<DEMNetOptions> options
        , IOptions<OsmElevationOptions> osmOptions
        , ILogger<DefaultOsmProcessor> logger)
{
    private readonly IOsmDataServiceFactory _dataServiceFactory = dataServiceFactory;
    private readonly DEMNetOptions _options = options.Value;
    private readonly ElevationService _elevationService = elevationService;
    private readonly SharpGltfService _gltfService = gltfService;
    private readonly MeshService _meshService = meshService;
    private readonly ILogger<DefaultOsmProcessor> _logger = logger;

    public OsmElevationOptions Settings { get; set; } = osmOptions.Value;

    private List<IOsmProcessor> Build(OsmLayer layers, GeoTransformPipeline? transform = null, bool withBuildingsColors = false, string? defaultBuildingsColor = null, string? highwaysColor = null)
    {
        List<IOsmProcessor> processors = [];

        if (layers.HasFlag(OsmLayer.Buildings)) processors.Add(new OsmBuildingProcessor(transform, withBuildingsColors, defaultBuildingsColor));
        if (layers.HasFlag(OsmLayer.Railway)) processors.Add(new OsmRailwayProcessor(transform));
        if (layers.HasFlag(OsmLayer.Water)) processors.Add(new OsmWaterProcessor(transform));
        // Roads are lifted by OsmLineElevation.HeightAboveTerrainMeters
        if (layers.HasFlag(OsmLayer.Highways)) processors.Add(new OsmHighwayProcessor(transform, highwaysColor));

        processors.ForEach(p => p.DataSettings.Apply(Settings));

        return processors;
    }
    public async Task<ModelRoot> RunAsync(ModelRoot? model, OsmLayer layers, BoundingBox bbox, GeoTransformPipeline transform, DEMDataSet? dataSet = null, bool downloadMissingFiles = true, bool withBuildingsColors = false, string? defaultBuildingsColor = null, string? highwaysColor = null, CancellationToken cancellationToken = default)
    {

        model = model ?? _gltfService.CreateNewModel();

        if (layers == OsmLayer.None)
            return model;

        IOsmDataService osmDataService = _dataServiceFactory.Create(Settings.DataServiceType);
        List<IOsmProcessor> processors = Build(layers, transform, withBuildingsColors, defaultBuildingsColor, highwaysColor);

        foreach (var p in processors)
        {
            p.Init(_elevationService, _gltfService, _meshService, osmDataService, _logger);

            model = await p.RunAsync(model, bbox, dataSet, downloadMissingFiles, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Source vertex count (OSM nodes) and line length per requested layer, for size estimates. With the PMTiles
    /// data service this only decodes tiles (no geometry is built).
    /// </summary>
    /// <exception cref="OsmQueryTooLargeException">The bbox needs more PMTiles tiles than allowed.</exception>
    public IReadOnlyDictionary<OsmLayer, OsmDataStats> GetDataStats(BoundingBox bbox, OsmLayer layers)
    {
        var counts = new Dictionary<OsmLayer, OsmDataStats>();
        if (layers == OsmLayer.None)
            return counts;

        IOsmDataService osmDataService = _dataServiceFactory.Create(Settings.DataServiceType);
        foreach (OsmLayer layer in new[] { OsmLayer.Buildings, OsmLayer.Highways, OsmLayer.Railway, OsmLayer.Water })
        {
            if (!layers.HasFlag(layer)) continue;
            var processor = Build(layer).Single();
            counts[layer] = osmDataService.GetOsmDataStats(bbox, processor.DataSettings);
        }
        return counts;
    }

}
