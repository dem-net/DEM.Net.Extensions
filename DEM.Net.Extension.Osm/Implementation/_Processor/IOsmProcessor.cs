using DEM.Net.Core;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DEM.Net.Extension.Osm
{

    /// <summary>
    /// Base interface for all OSM processors
    /// </summary>
    public interface IOsmProcessor
    {
        IOsmDataSettings DataSettings { get; }
    
        void Init(ElevationService elevationService
            , SharpGltfService gltfService
            , MeshService meshService
            , IOsmDataService osmService
            , ILogger logger);

        GeoTransformPipeline Transform { get; set; }

        /// <summary>Adds the layer to the model. Elevation files are downloaded first when <paramref name="downloadMissingFiles"/> is true.</summary>
        Task<ModelRoot> RunAsync(ModelRoot gltfModel, BoundingBox bbox, DEMDataSet dataSet, bool downloadMissingFiles, CancellationToken cancellationToken = default);


    }
}
