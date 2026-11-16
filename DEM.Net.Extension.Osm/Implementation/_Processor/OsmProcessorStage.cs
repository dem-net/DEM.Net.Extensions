using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DEM.Net.Core;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using SharpGLTF.Schema2;

namespace DEM.Net.Extension.Osm;

public abstract class OsmProcessorStage<T>(GeoTransformPipeline transform) : IOsmProcessor
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

    public void AddPostTransform(Func<IEnumerable<GeoPoint>, IEnumerable<GeoPoint>> postTransform)
    {
        Transform.TransformPoints = Transform.TransformPoints.PostTransform(p => postTransform(p));
    }

    public abstract IOsmDataSettings DataSettings { get; set; }

    public abstract OsmModelFactory<T> ModelFactory { get; }

    public abstract string glTFNodeName { get; }

    public virtual GeoTransformPipeline Transform { get; set; } = transform?.Clone();


    public async Task<ModelRoot> RunAsync(ModelRoot gltfModel, BoundingBox bbox, DEMDataSet dataSet, bool downloadMissingFiles, CancellationToken cancellationToken = default)
    {
        try
        {
            // Download buildings and convert them to GeoJson
            IEnumerable<IFeature> features = _osmDataService.GetOsmDataAsGeoJson(bbox, DataSettings);
            // End features at the terrain edge
            features = OsmFeatureClipper.ClipToBoundingBox(features, bbox);
            // Create internal building model
            IEnumerable<T> parsed = this.CreateModelsFromGeoJson<T>(features, ModelFactory);

            // Download elevation data if missing: elevations are then read from local files
            if (DataSettings.ComputeElevations && downloadMissingFiles)
                await _elevationService.DownloadMissingFilesAsync(dataSet, bbox, cancellationToken).ConfigureAwait(false);

            // Materialized once: parsing, clipping and elevations must not run again for each enumeration
            List<T> models = [.. this.ComputeModelElevationsAndTransform(parsed, DataSettings.ComputeElevations, dataSet, downloadMissingFiles)];
            cancellationToken.ThrowIfCancellationRequested();

            return this.AddToModel(gltfModel, glTFNodeName, models);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Model} generator error", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// OSM lines are in WGS84, as are the points returned by GetLineGeometryElevation, while <see cref="Transform"/>
    /// starts from the dataset SRID (Web Mercator for Mapterhorn).
    /// </summary>
    protected static IEnumerable<GeoPoint> ToDataSetSrid(IEnumerable<GeoPoint> pointsWgs84, DEMDataSet dataSet)
    {
        if (pointsWgs84 == null)
            return [];
        if (dataSet == null || dataSet.SRID == Reprojection.SRID_GEODETIC)
            return pointsWgs84;
        return pointsWgs84.ReprojectTo(Reprojection.SRID_GEODETIC, dataSet.SRID);
    }

    /// <summary>Elevations are read from local files: <see cref="RunAsync"/> downloads the missing ones first.</summary>
    protected abstract IEnumerable<T> ComputeModelElevationsAndTransform(IEnumerable<T> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles);

    protected abstract ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, IReadOnlyList<T> models);

    public IEnumerable<Tmodel> CreateModelsFromGeoJson<Tmodel>(IEnumerable<IFeature> features, OsmModelFactory<Tmodel> validator) where Tmodel : CommonModel
    {

        int numValid = 0;
        int numInvalid = 0;

        using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(CreateModelsFromGeoJson), _logger, LogLevel.Debug))
        {

            foreach (var feature in features)
            {
                validator.RegisterTags(feature as Feature);
                foreach (Tmodel model in validator.CreateModel(feature as Feature))
                {

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
        }

        _logger.LogInformation($"{nameof(CreateModelsFromGeoJson)} done for {validator._totalPoints} points. {numValid:N0} valid models, {numInvalid:N0} invalid");


    }
}
