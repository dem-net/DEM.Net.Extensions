using DEM.Net.Core;
using DEM.Net.Core.Imagery;
using DEM.Net.Core.Services.Lab;
using DEM.Net.Extension.Osm;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Extension.Osm.OverpassAPI;
using DEM.Net.Extension.SketchFab;
using DEM.Net.glTF.SharpglTF;
using GeoJSON.Net.Feature;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SharpGLTF.Schema2;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DEM.Net.glTF.SharpglTF.SharpGltfService;

namespace SampleApp
{
    public class HelladicSample
    {
        private readonly BuildingService _buildingService;
        private readonly ImageryService _imageryService;
        private readonly IElevationService _elevationService;
        private readonly SharpGltfService _gltfService;
        private readonly IMeshService _meshService;
        private readonly SketchFabApi _sketchFabApi;
        private readonly ILogger _logger;


        public HelladicSample(BuildingService buildingService
                , ImageryService imageryService
                , IElevationService elevationService
                , SharpGltfService gltfService
                , IMeshService meshService
                , SketchFabApi sketchFabApi
                , ILogger<OsmExtensionSample> logger)
        {
            this._buildingService = buildingService;
            this._imageryService = imageryService;
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._sketchFabApi = sketchFabApi;
            this._logger = logger;
        }

        public void Run()
        {
            //BatchGeneration(@"Helladic\3D_Initial.txt", "3D_Initial_nonormal");
            BatchUpload(@"Helladic\3D_Initial.txt", "3D_Initial_nonormal");
        }

        public void BatchGeneration(string fileName, string outputDirName)
        {
            List<Location3DModelSettings> allSettings = new List<Location3DModelSettings>();
            //Location3DModelSettings settingsSpeed = new Location3DModelSettings()
            //{
            //    Dataset = DEMDataSet.ASTER_GDEMV3,
            //    ImageryProvider = null,
            //    ZScale = 2f,
            //    SideSizeKm = 1.5f,
            //    OsmBuildings = false,
            //    DownloadMissingFiles = false,
            //    GenerateTIN = false,
            //    MinTilesPerImage = 8,
            //    MaxDegreeOfParallelism = 4,
            //    OutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "All_Speed")
            //};
            //Location3DModelSettings settingsNormal = new Location3DModelSettings()
            //{
            //    Dataset = DEMDataSet.ASTER_GDEMV3,
            //    ImageryProvider = ImageryProvider.OpenTopoMap,
            //    ZScale = 2f,
            //    SideSizeKm = 1.5f,
            //    OsmBuildings = true,
            //    DownloadMissingFiles = false,
            //    GenerateTIN = false,
            //    MinTilesPerImage = 8,
            //    MaxDegreeOfParallelism = 1,
            //    OutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "All_Normal")
            //};
            Location3DModelSettings settings = new Location3DModelSettings()
            {
                Dataset = DEMDataSet.NASADEM,
                ImageryProvider = ImageryProvider.ThunderForestLandscape,
                ZScale = 2f,
                SideSizeKm = 1.5f,
                OsmBuildings = true,
                DownloadMissingFiles = false,
                GenerateTIN = false,
                MaxDegreeOfParallelism = 1,
                ClearOutputDir = false,
                OutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), outputDirName)
            };


            List<Location3DModelRequest> requests = new List<Location3DModelRequest>();
            using (StreamReader sr = new StreamReader(fileName, Encoding.UTF8))
            {
                sr.ReadLine(); // skip header
                do
                {
                    //pk,pn,lat,lon,link
                    Location3DModelRequest request = ParseCsvLine(sr.ReadLine(), '\t');
                    requests.Add(request);
                } while (!sr.EndOfStream);
            }


            if (settings.ClearOutputDir)
            {
                if (Directory.Exists(settings.OutputDirectory)) Directory.Delete(settings.OutputDirectory, true);
                Directory.CreateDirectory(settings.OutputDirectory);
            }
            else
            {
                Directory.CreateDirectory(settings.OutputDirectory);
                // Filter already generated files
                int countBefore = requests.Count;
                requests = requests.Where(r => !File.Exists(Path.Combine(settings.OutputDirectory, settings.ModelFileNameGenerator(settings, r)))).ToList();
                if (requests.Count < countBefore)
                {
                    _logger.LogInformation($"Skipping {countBefore - requests.Count} files already generated.");
                }
            }

            ConcurrentBag<Location3DModelResponse> responses = new ConcurrentBag<Location3DModelResponse>();
            //foreach (var request in requests)
            Parallel.ForEach(requests, new ParallelOptions { MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism }, request =>
             {
                 try
                 {
                     var response = Generate3DLocationModel(request, settings);
                     responses.Add(response);

                     //Location3DModelResponse response = Generate3DLocationModel(request, settings);
                 }
                 catch (Exception ex)
                 {
                     _logger.LogError(ex.Message);
                 }
                 finally
                 {
                     if (responses.Count > 0)
                         _logger.LogInformation($"Reponse: {responses.Last().Elapsed.TotalSeconds:N3} s, Average: {responses.Average(r => r.Elapsed.TotalSeconds):N3} s ({responses.Count}/{requests.Count} model(s) so far)");
                 }
             }
            );


            // Now, scan again 

        }


        public void BatchUpload(string fileName, string outputDirName)
        {
            Location3DModelSettings settings = new Location3DModelSettings()
            {
                Dataset = DEMDataSet.NASADEM,
                ImageryProvider = ImageryProvider.ThunderForestLandscape,
                ZScale = 2f,
                SideSizeKm = 1.5f,
                OsmBuildings = true,
                DownloadMissingFiles = false,
                GenerateTIN = false,
                MaxDegreeOfParallelism = 1,
                ClearOutputDir = false,
                OutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), outputDirName)
            };


            List<Location3DModelRequest> requests = new List<Location3DModelRequest>();
            using (StreamReader sr = new StreamReader(fileName, Encoding.UTF8))
            {
                sr.ReadLine(); // skip header
                do
                {
                    //pk,pn,lat,lon,link
                    Location3DModelRequest request = ParseCsvLine(sr.ReadLine(), '\t');
                    requests.Add(request);
                } while (!sr.EndOfStream);
            }


            // Take generated files
            int countBefore = requests.Count;
            requests = requests.Where(r => File.Exists(Path.Combine(settings.OutputDirectory, settings.ModelFileNameGenerator(settings, r)))).ToList();
            _logger.LogInformation($"{requests.Count}/{countBefore} files generated.");

            string outFilePath = string.Concat(Path.ChangeExtension(fileName, null), "_out.txt");

            using (StreamWriter sw = new StreamWriter(outFilePath))
            {
                sw.WriteLine(string.Join("\t", "pk", "pn", "lat", "lon", "link", "sketchfabstatus", "sketchfabid"));
                foreach(var request in requests)
                {
                    UploadModelRequest uploadRequest = GetUploadRequest(settings, request);
                    string uuid = null;
                    bool ok = false;
                    try
                    {
                        uuid = _sketchFabApi.UploadModelAsync(uploadRequest).GetAwaiter().GetResult();

                        // TODO add to collection / delete API

                        ok = true;
                        _logger.LogInformation($"SketchFab upload ok : {uuid}");
                    }
                    catch (Exception exSketchFab)
                    {
                        _logger.LogError($"Error in SketchFab upload: {exSketchFab.Message}");
                        ok = false;
                        uuid = exSketchFab.Message;
                    }
                    finally
                    {
                        sw.WriteLine(string.Join("\t", request.Id, request.Title, request.Latitude, request.Longitude, request.Description,
                             ok ? "OK" : uuid,
                             ok ? uuid : ""));
                    }                    
                }
            }

          

        }

        private UploadModelRequest GetUploadRequest(Location3DModelSettings settings, Location3DModelRequest request)
        {
            
             UploadModelRequest upload = new UploadModelRequest()
            {
                Description =  GenerateDescription(settings,request),// "TEST",// * Generated by [DEM Net Elevation API](https://elevationapi.com)\n* Helladic test upload",
                FilePath = Path.Combine(settings.OutputDirectory, settings.ModelFileNameGenerator(settings, request)),
                IsInspectable = true,
                IsPrivate = false,
                IsPublished = false,
                Name = string.Concat(request.Id, " ", request.Title),
                Options = new ModelOptions() { Background = SkecthFabEnvironment.Footprint_Court, Shading = ShadingType.lit }
            };
            return upload;
        }

        private string GenerateDescription(Location3DModelSettings settings, Location3DModelRequest request)
        {
            List<string> desc = new List<string>();
            // Location
            //desc.Add($"[See on Helladic.info](https://www.google.com/maps/@{request.Latitude:N7},{request.Longitude:N7},13z)");

            // Helladic.info
            desc.Add($"[Helladic.info Link]({request.Description})");

            desc.AddRange(settings.Attributions.Select(a => $"{a.Subject}: [{a.Text}]({a.Url})"));
            desc.Add($"Elevation: [{settings.Dataset.Attribution.Text}]({settings.Dataset.Attribution.Url})");
            if (settings.OsmBuildings)
            {
                desc.Add("Data: OpenStreetMap and Contributors [www.openstreetmap.org](https://www.openstreetmap.org)");
            }

            return string.Join(Environment.NewLine, desc.Select(d => string.Concat("* ", d)));
        }

        private Location3DModelResponse Generate3DLocationModel(Location3DModelRequest request, Location3DModelSettings settings)
        {
            Location3DModelResponse response = new Location3DModelResponse();
            try
            {
                bool imageryFailed = false;
                using (TimeSpanBlock timer = new TimeSpanBlock($"3D model {request.Id}", _logger))
                {
                    BoundingBox bbox = GetBoundingBoxAroundLocation(request.Latitude, request.Longitude, settings.SideSizeKm);

                    HeightMap hMap = _elevationService.GetHeightMap(ref bbox, settings.Dataset);


                    response.Attributions.AddRange(settings.Attributions); // will be added to the model
                    response.Attributions.Add(settings.Dataset.Attribution); // will be added to the model


                    PBRTexture pbrTexture = null;
                    if (settings.ImageryProvider != null)
                    {
                        response.Attributions.Add(settings.ImageryProvider.Attribution); // will be added to the model

                        // Imagery
                        TileRange tiles = _imageryService.ComputeBoundingBoxTileRange(bbox, settings.ImageryProvider, settings.MinTilesPerImage);
                        Debug.Assert(tiles.EnumerateRange().Count() < 400);
                        try
                        {
                            tiles = _imageryService.DownloadTiles(tiles, settings.ImageryProvider);
                        }
                        catch (Exception)
                        {
                            imageryFailed = true;
                            try
                            {
                                tiles = _imageryService.ComputeBoundingBoxTileRange(bbox, ImageryProvider.MapBoxSatelliteStreet, settings.MinTilesPerImage);
                                tiles = _imageryService.DownloadTiles(tiles, ImageryProvider.MapBoxSatelliteStreet);
                            }
                            catch (Exception)
                            {
                                throw;
                            }
                        }
                        string fileName = Path.Combine(settings.OutputDirectory, $"{request.Id}_Texture.jpg");
                        TextureInfo texInfo = _imageryService.ConstructTexture(tiles, bbox, fileName, TextureImageFormat.image_jpeg);

                        hMap = hMap.ReprojectTo(Reprojection.SRID_GEODETIC, Reprojection.SRID_PROJECTED_MERCATOR)
                                    .ZScale(settings.ZScale)
                                    .BakeCoordinates();
                        //var normalMap = _imageryService.GenerateNormalMap(hMap, settings.OutputDirectory, $"{request.Id}_normalmap.png");
                        pbrTexture = PBRTexture.Create(texInfo);
                    }

                    ModelRoot model = _gltfService.CreateNewModel();
                    //=======================
                    // Buildings
                    if (settings.OsmBuildings)
                    {
                        var triangulationNormals = _buildingService.GetBuildings3DTriangulation(bbox, settings.Dataset, settings.DownloadMissingFiles, settings.ZScale, useOsmColors: true);
                        var indexedTriangulation = new IndexedTriangulation(triangulationNormals);
                        if (indexedTriangulation.Positions.Count > 0)
                            model = _gltfService.AddMesh(model, indexedTriangulation, null, null, doubleSided: true);
                    }


                    if (settings.GenerateTIN)
                    {
                        model = AddTINMesh(model, hMap, 2d, _gltfService, pbrTexture, Reprojection.SRID_PROJECTED_MERCATOR);
                    }
                    else
                    {
                        model = _gltfService.AddTerrainMesh(model, hMap, pbrTexture);
                    }
                    model.Asset.Generator = "DEM Net Elevation API with SharpGLTF";
                    model.TryUseExtrasAsList(true).AddRange(response.Attributions);
                    model.SaveGLB(Path.Combine(settings.OutputDirectory, string.Concat(imageryFailed ? "imageryFailed_" : "", settings.ModelFileNameGenerator(settings, request))));

                    // cleanup
                    if (pbrTexture != null)
                    {
                        if (pbrTexture.NormalTexture != null) File.Delete(pbrTexture.NormalTexture.FilePath);
                        File.Delete(pbrTexture.BaseColorTexture.FilePath);
                    }

                    response.Elapsed = timer.Elapsed;
                }
            }
            catch (Exception)
            {
                throw;
            }
            return response;
        }

        private Location3DModelRequest ParseCsvLine(string cols, char separator)
        {
            string[] values = cols.Split(separator);
            //pk,pn,lat,lon,link

            int index = 0;
            Location3DModelRequest request = new Location3DModelRequest();
            request.Id = values[index++];
            request.Title = values[index++].Trim('\"');
            request.Latitude = double.Parse(values[index++], CultureInfo.InvariantCulture);
            request.Longitude = double.Parse(values[index++], CultureInfo.InvariantCulture);
            request.Description = values[index++];

            return request;
        }

        private BoundingBox GetBoundingBoxAroundLocation(double lat, double lng, double sideSizeKm)
        {
            GeoPoint pt = new GeoPoint(lat, lng);
            pt = pt.ReprojectTo(Reprojection.SRID_GEODETIC, Reprojection.SRID_PROJECTED_MERCATOR);
            double halfSideMeters = sideSizeKm * 1000d / 2d;
            var bbox = new BoundingBox(pt.Longitude - halfSideMeters, pt.Longitude + halfSideMeters, pt.Latitude - halfSideMeters, pt.Latitude + halfSideMeters);
            return bbox.ReprojectTo(Reprojection.SRID_PROJECTED_MERCATOR, Reprojection.SRID_GEODETIC);
        }

        #region TIN (todo move to lib)

        public static Triangulation GenerateTIN(HeightMap hMap, double precision, int srid)
        {
            var v_pointsToTest = GetGeoPointsByHMap(hMap, srid);


            var _paramTin = FLabServices.createCalculMedium().GetParametresDuTinParDefaut();
            _paramTin.p11_initialisation_determinationFrontieres = enumModeDelimitationFrontiere.pointsProchesDuMbo;
            _paramTin.p12_extensionSupplementaireMboEnM = 0;
            _paramTin.p13_modeCalculZParDefaut = enumModeCalculZ.alti_0;
            _paramTin.p14_altitudeParDefaut = -200;
            _paramTin.p15_nbrePointsSupplMultiples4 = 0;
            _paramTin.p16_initialisation_modeChoixDuPointCentral.p01_excentrationMinimum = 0;
            _paramTin.p21_enrichissement_modeChoixDuPointCentral.p01_excentrationMinimum = precision;

            //
            var _topolFacettes = FLabServices.createCalculMedium().GetInitialisationTin(v_pointsToTest, _paramTin);
            FLabServices.createCalculMedium().AugmenteDetailsTinByRef(ref _topolFacettes, _paramTin);


            Dictionary<int, int> v_indiceParIdPoint = new Dictionary<int, int>();
            int v_indice = 0;
            GeoPoint v_geoPoint;
            List<GeoPoint> p00_geoPoint = new List<GeoPoint>(_topolFacettes.p11_pointsFacettesByIdPoint.Count);
            List<List<int>> p01_listeIndexPointsfacettes = new List<List<int>>(_topolFacettes.p13_facettesById.Count);

            foreach (BeanPoint_internal v_point in _topolFacettes.p11_pointsFacettesByIdPoint.Values)
            {
                v_geoPoint = new GeoPoint(v_point.p10_coord[1], v_point.p10_coord[0], (float)v_point.p10_coord[2]);
                p00_geoPoint.Add(v_geoPoint);
                v_indiceParIdPoint.Add(v_point.p00_id, v_indice);
                v_indice++;
            }
            //p00_geoPoint = p00_geoPoint.CenterOnOrigin().ToList();
            p00_geoPoint = p00_geoPoint.ToList();


            //Création des listes d'indices et normalisation du sens des points favettes
            List<int> v_listeIndices;
            bool v_renvoyerNullSiPointsColineaires_vf = true;
            bool v_normalisationSensHoraireSinonAntihoraire = false;


            foreach (BeanFacette_internal v_facette in _topolFacettes.p13_facettesById.Values)
            {
                List<BeanPoint_internal> v_normalisationDuSens = FLabServices.createCalculMedium().GetOrdonnancementPointsFacette(v_facette.p01_pointsDeFacette, v_renvoyerNullSiPointsColineaires_vf, v_normalisationSensHoraireSinonAntihoraire);
                if (v_normalisationDuSens != null)
                {
                    v_listeIndices = new List<int>();
                    foreach (BeanPoint_internal v_ptFacette in v_normalisationDuSens)
                    {
                        v_listeIndices.Add(v_indiceParIdPoint[v_ptFacette.p00_id]);
                    }
                    p01_listeIndexPointsfacettes.Add(v_listeIndices);
                }
            }

            return new Triangulation(p00_geoPoint, p01_listeIndexPointsfacettes.SelectMany(c => c).ToList());

        }

        public static ModelRoot GenerateTIN(HeightMap hMap, double precision, SharpGltfService gltf, PBRTexture textures, int srid)
        {
            return AddTINMesh(gltf.CreateNewModel(), hMap, precision, gltf, textures, srid);
        }

        public static ModelRoot AddTINMesh(ModelRoot model, HeightMap hMap, double precision, SharpGltfService gltf, PBRTexture textures, int srid)
        {
            Triangulation triangulation = GenerateTIN(hMap, precision, srid);

            return gltf.AddTerrainMesh(model, triangulation, textures, doubleSided: true);

        }

        private static List<BeanPoint_internal> GetGeoPointsByHMap(HeightMap p_hMap, int p_srid)
        {
            return p_hMap.Coordinates.Select(c => GetPointInternalFromGeoPoint(c, p_srid)).ToList();
        }
        private static BeanPoint_internal GetPointInternalFromGeoPoint(GeoPoint p_geoPoint, int p_srid)
        {
            BeanPoint_internal v_ptInternal = new BeanPoint_internal(p_geoPoint.Longitude, p_geoPoint.Latitude, p_geoPoint.Elevation.GetValueOrDefault(0), p_srid);
            return v_ptInternal;
        }

        #endregion
    }


}
