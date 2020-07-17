using DEM.Net.Core;
using DEM.Net.Core.Imagery;
using DEM.Net.Extension.VisualTopo;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;

namespace SampleApp
{
    /// <summary>
    /// VisualTopo integration
    /// Goal: generate 3D model from visual topo file
    /// </summary>
    class VisualTopoSample
    {
        private readonly ILogger<VisualTopoSample> _logger;
        private readonly SharpGltfService _gltfService;
        private readonly MeshService _meshService;
        private readonly ImageryService _imageryService;
        private readonly VisualTopoService _visualTopoService;
        private readonly ElevationService _elevationService;

        public VisualTopoSample(ILogger<VisualTopoSample> logger
                , SharpGltfService gltfService
                , MeshService meshService
                , ElevationService elevationService
                , ImageryService imageryService
                , VisualTopoService visualTopoService)
        {
            _logger = logger;
            _meshService = meshService;
            _gltfService = gltfService;
            _elevationService = elevationService;
            _imageryService = imageryService;
            _visualTopoService = visualTopoService;
        }

        public void Run()
        {

            // Single file
            Run(Path.Combine("SampleData", "VisualTopo", "topo asperge avec ruisseau.TRO"), imageryProvider: ImageryProvider.MapBoxSatelliteStreet, bboxMarginMeters: 500, generateTopoOnlyModel: true);

            Run(Path.Combine("SampleData", "VisualTopo", "small", "0 bifurc", "Test 3 arcs.tro"), imageryProvider: null, bboxMarginMeters: 50, generateTopoOnlyModel: true);

            Run(Path.Combine("SampleData", "VisualTopo", "small", "0 bifurc", "Test 4 arcs.tro"), imageryProvider: null, bboxMarginMeters: 50, generateTopoOnlyModel: true);

            Run(Path.Combine("SampleData", "VisualTopo", "LA SALLE.TRO"), imageryProvider: ImageryProvider.OpenTopoMap, bboxMarginMeters: 50);



            // All files in given directory
            foreach (var file in Directory.EnumerateFileSystemEntries(Path.Combine("SampleData", "VisualTopo"), "*.tro", SearchOption.AllDirectories))
            {
                _logger.LogInformation("Generating model for file " + file);
                Run(file, ImageryProvider.MapBoxSatelliteStreet, bboxMarginMeters: 50, generateTopoOnlyModel: true);
            }

        }

        /// <summary>
        /// Generates a VisualTopo file 3D model
        /// </summary>
        /// <remarks>LT* (Lambert Carto) projections are not supported and could produce imprecise results (shifted by +10meters)</remarks>
        /// <param name="vtopoFile">VisualTopo .TRO file</param>
        /// <param name="imageryProvider">Imagery provider for terrain texture. Set to null for untextured model</param>
        /// <param name="bboxMarginMeters">Terrain margin (meters) around VisualTopo model</param>
        public void Run(string vtopoFile, ImageryProvider imageryProvider, float bboxMarginMeters = 1000, bool generateTopoOnlyModel = false)
        {
            try
            {

                //=======================
                // Generation params
                //
                int outputSRID = 3857;                                  // Output SRID
                float zFactor = 1F;                                     // Z exaggeration
                float lineWidth = 1.0F;                                 // Topo lines width (meters)
                var dataset = DEMDataSet.AW3D30;                        // DEM dataset for terrain and elevation
                int TEXTURE_TILES = 12;                                 // Texture quality (number of tiles for bigger side) 4: med, 8: high, 12: ultra
                string outputDir = Directory.GetCurrentDirectory();
                bool GENERATE_LINE3D = false;

                //=======================
                // Open and parse file
                //
                //string vtopoFile = Path.Combine("SampleData", "VisualTopo", "topo asperge avec ruisseau.TRO");
                //string vtopoFile = Path.Combine("SampleData", "VisualTopo", "topo asperge avec ruisseau.TRO");
                //string vtopoFile = Path.Combine("SampleData", "VisualTopo", "Olivier4326.TRO");
                //string vtopoFile = Path.Combine("SampleData", "VisualTopo", "LA SALLE.TRO");

                // Open and parse file
                // model will have available properties
                // => BoundingBox
                // => Topology3D -> list of point-to-point lines
                // => SRID of model file
                StopwatchLog timeLog = new StopwatchLog(_logger);
                VisualTopoModel model = _visualTopoService.LoadFile(vtopoFile, Encoding.GetEncoding("ISO-8859-1")
                                                                    , decimalDegrees: true
                                                                    , ignoreRadialBeams: true
                                                                    , 1f);
                timeLog.LogTime($"Loading {vtopoFile} model file", reset: true);

                // Warn if badly supported projection
                if (model.EntryPointProjectionCode.StartsWith("LT"))
                    _logger.LogWarning($"Model entry projection is Lambert Carto and is not fully supported. Will result in 20m shifts. Consider changing projection to UTM");

                // for debug, 
                //var b = GetBranches(model); // graph list of all nodes
                //var lowestPoint = model.Sets.Min(s => s.Data.Min(d => d.GlobalGeoPoint?.Elevation ?? 0));

                BoundingBox bbox = model.BoundingBox // relative coords
                                        .Translate(model.EntryPoint.Longitude, model.EntryPoint.Latitude, model.EntryPoint.Elevation ?? 0) // absolute coords
                                        .Pad(bboxMarginMeters) // margin around model
                                        .ReprojectTo(model.SRID, dataset.SRID); // DEM coords
                                                                                // Get height map
                                                                                // Note that ref Bbox means that the bbox will be adjusted to match DEM data
                var heightMap = _elevationService.GetHeightMap(ref bbox, dataset, true);
                var bboxTerrainSpace = bbox.ReprojectTo(dataset.SRID, outputSRID); // terrain coords
                timeLog.LogTime("Terrain height map", reset: true);

                //=======================
                // Get entry elevation (need to reproject to DEM coordinate system first)
                // and sections entry elevations
                // 
                ComputeCavityElevations(model, dataset);
                _visualTopoService.Create3DTriangulation(model, zFactor);
                timeLog.LogTime("Cavity points elevation", reset: true);

                //=======================
                // Local transform function from model coordinates (relative to entry, in meters)
                // and global coordinates absolute in final 3D model space
                //
                IEnumerable<GeoPoint> TransformLine(IEnumerable<GeoPoint> line)
                {
                    var newLine = line.Translate(model.EntryPoint)              // Translate to entry (=> global topo coord space)
                                        .ReprojectTo(model.SRID, outputSRID)    // Reproject to terrain coord space
                                        .ZScale(zFactor)                        // Z exaggeration if necessary
                                        .CenterOnOrigin(bboxTerrainSpace);      // Center on terrain space origin
                    return newLine;
                };
                GeoPoint axisOriginPt = model.EntryPoint.ReprojectTo(model.SRID, outputSRID).CenterOnOrigin(bboxTerrainSpace);
                Vector3 axisOrigin = axisOriginPt.AsVector3();


                //=======================
                // 3D model
                //
                var gltfModel = _gltfService.CreateNewModel();

                // Add X/Y/Z axis on entry point
                var axis = _meshService.CreateAxis();
                _gltfService.AddMesh(gltfModel, "Axis", axis.Translate(axisOrigin), doubleSided: false);

                int i = 0;
                var triangulation = model.TriangulationFull3D.Translate(model.EntryPoint.AsVector3())
                                                .ReprojectTo(model.SRID, outputSRID)
                                                .ZScale(zFactor)
                                                .CenterOnOrigin(bboxTerrainSpace);
                gltfModel = _gltfService.AddMesh(gltfModel, "Cavite3D", model.TriangulationFull3D, VectorsExtensions.CreateColor(0, 255, 0), doubleSided: false);

                if (GENERATE_LINE3D)
                {
                    foreach (var line in model.Topology3D) // model.Topology3D is the graph of topo paths
                    {
                        // Add line to model
                        gltfModel = _gltfService.AddLine(gltfModel
                                                        , string.Concat("GPX", i++)     // name of 3D node
                                                        , TransformLine(line)               // call transform function
                                                        , color: VectorsExtensions.CreateColor(255, 0, 0, 128)
                                                        , lineWidth);
                    }
                }
                timeLog.LogTime("Topo 3D model", reset: true);

                if (generateTopoOnlyModel)
                {
                    // Uncomment this to save 3D model for topo only (without terrain)
                    gltfModel.SaveGLB(string.Concat(Path.GetFileNameWithoutExtension(vtopoFile) + "_TopoOnly.glb"));
                }

                // Reproject and center height map coordinates
                heightMap = heightMap.ReprojectTo(dataset.SRID, outputSRID)
                                    .CenterOnOrigin(bboxTerrainSpace)
                                    .ZScale(zFactor);
                //.BakeCoordinates();
                timeLog.LogTime("Height map transform", reset: true);

                //=======================
                // Textures
                //
                PBRTexture pbrTexture = null;
                if (imageryProvider != null)
                {
                    TileRange tiles = _imageryService.DownloadTiles(bbox, imageryProvider, TEXTURE_TILES);
                    string fileName = Path.Combine(outputDir, "Texture.jpg");
                    timeLog.LogTime("Imagery download", reset: true);

                    Console.WriteLine("Construct texture...");
                    TextureInfo texInfo = _imageryService.ConstructTexture(tiles, bbox, fileName, TextureImageFormat.image_jpeg);
                    //var topoTexture = topo3DLine.First().Translate(model.EntryPoint).ReprojectTo(model.SRID, 4326);
                    //TextureInfo texInfo = _imageryService.ConstructTextureWithGpxTrack(tiles, bbox, fileName, TextureImageFormat.image_jpeg
                    //    , topoTexture, false);

                    pbrTexture = PBRTexture.Create(texInfo, null);
                    timeLog.LogTime("Texture creation", reset: true);
                }
                //
                //=======================

                // Triangulate height map
                _logger.LogInformation($"Triangulating height map and generating 3D mesh...");

                gltfModel = _gltfService.AddTerrainMesh(gltfModel, heightMap, pbrTexture);
                gltfModel.SaveGLB(string.Concat(Path.GetFileNameWithoutExtension(vtopoFile) + ".glb"));
                timeLog.LogTime("3D model", reset: true);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error :" + ex.Message);
            }

        }

        private void ComputeCavityElevations(VisualTopoModel model, DEMDataSet dataset)
        {
            var entryPoint4326 = model.EntryPoint.ReprojectTo(model.SRID, dataset.SRID);
            _elevationService.DownloadMissingFiles(dataset, entryPoint4326); // download required DEM files
            model.EntryPoint.Elevation = _elevationService.GetPointElevation(entryPoint4326, dataset).Elevation ?? 0;

            foreach (var set in model.Sets.Where(s => s.Data.First().GlobalGeoPoint != null))
            {
                VisualTopoData setStartData = set.Data.First(d => d.GlobalGeoPoint != null);
                var setStartPoint = setStartData.GlobalGeoPoint.Clone();
                setStartPoint.Longitude += model.EntryPoint.Longitude;
                setStartPoint.Latitude += model.EntryPoint.Latitude;
                var setStartPointDem = setStartPoint.ReprojectTo(model.SRID, dataset.SRID);
                _elevationService.DownloadMissingFiles(dataset, setStartPointDem); // download required DEM files
                setStartData.TerrainElevationAbove = _elevationService.GetPointElevation(setStartPointDem, dataset).Elevation ?? 0;
            }
        }

        private IEnumerable<GeoPoint> BuildAxis(GeoPoint axisEnd)
        {
            yield return GeoPoint.Zero;
            yield return axisEnd * 50F;
        }
    }
}
