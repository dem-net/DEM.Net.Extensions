using DEM.Net.Core;
using DEM.Net.Extension.Osm.OverpassAPI;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SharpGLTF.Schema2;
using System.Text;
using System.Threading.Tasks;

namespace DEM.Net.Extension.Osm
{

    /// <summary>
    /// Base interface for all OSM processors
    /// </summary>
    public interface IOsmProcessor
    {
        void Init(IElevationService elevationService
            , SharpGltfService gltfService
            , IMeshService meshService
            , OsmService osmService
            , ILogger logger);

        ModelRoot Run(ModelRoot gltfModel, BoundingBox bbox,bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline transform);
    }
}
