using DEM.Net.Core;
using DEM.Net.Extension.Osm.OverpassAPI;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System;
using System.Text;
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

        ModelRoot Run(ModelRoot gltfModel, BoundingBox bbox, DEMDataSet dataSet, bool downloadMissingFiles);


    }
}
