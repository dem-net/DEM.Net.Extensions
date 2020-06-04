using DEM.Net.Core;
using DEM.Net.Core.Configuration;
using DEM.Net.Core.Imagery;
using DEM.Net.Core.Services.Lab;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpGLTF.Schema2;
using SketchFab;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace SampleApp
{
    public class HelladicSample
    {
        private readonly BuildingService _buildingService;
        private readonly ImageryService _imageryService;
        private readonly IElevationService _elevationService;
        private readonly SharpGltfService _gltfService;
        private readonly IMeshService _meshService;
        private readonly SketchFab.SketchFabApi _sketchFabApi;
        private readonly string _sketchFabToken;
        private readonly ILogger _logger;


        public HelladicSample(BuildingService buildingService
                , ImageryService imageryService
                , IElevationService elevationService
                , SharpGltfService gltfService
                , IMeshService meshService
                , SketchFab.SketchFabApi sketchFabApi
                , IOptions<AppSecrets> secrets
                , ILogger<HelladicSample> logger)
        {
            this._buildingService = buildingService;
            this._imageryService = imageryService;
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._sketchFabApi = sketchFabApi;
            this._sketchFabToken = secrets.Value.SketchFabToken;
            this._logger = logger;

            if (string.IsNullOrEmpty(_sketchFabToken))
            {
                _logger.LogWarning($"SketchFabToken is not set. Ensure you have a secrets.json file with a SketchFabToken entry with your api token (see https://sketchfab.com/settings/password)");
            }
        }

        public void Run()
        {
            BatchGenerationAndUpload(@"Helladic\3D_all.txt", "3D_Initial_2ndbatch");
        }


        public void BatchGenerationAndUpload(string fileName, string outputDirName)
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
                OutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), outputDirName)
            };

            string currentOutFilePath = string.Concat(Path.ChangeExtension(fileName, null), $"_out.txt");
            Dictionary<string, Location3DModelRequest> requests = ParseInputFile(fileName);
            Dictionary<string, Location3DModelResponse> responses = ParseOutputFile(currentOutFilePath);

            // Backup file by creating a copy
            var outFilePath = string.Concat(Path.ChangeExtension(fileName, null), $"_out_{DateTime.Now:ddMMyyyy-hhmmss}.txt");

            bool append = responses.Count > 0;
            _logger.LogInformation($"Append mode: {append}");

            // Restart from previous run
            Directory.CreateDirectory(settings.OutputDirectory);
            // Filter already generated files
            int countBefore = requests.Count;
            //requests = requests.Where(r => !File.Exists(Path.Combine(settings.OutputDirectory, settings.ModelFileNameGenerator(settings, r.Value)))).ToList();
            if (requests.Count < countBefore)
            {
                _logger.LogInformation($"Skipping {countBefore - requests.Count} files already generated.");
            }


            // Generate and upload
            int sumTilesDownloaded = 0;

            using (StreamWriter sw = new StreamWriter(outFilePath, append: append, Encoding.UTF8))
            {
                sw.WriteLine(string.Join("\t", "pk", "pn", "lat", "lon", "link", "tilecount_running_total", "sketchfab_status", "sketchfab_id"));

                foreach (var request in requests.Values)
                {
                    UploadModelRequest uploadRequest;
                    try
                    {
                        bool modelExists = File.Exists(Path.Combine(settings.OutputDirectory, settings.ModelFileNameGenerator(settings, request)));
                        Location3DModelResponse response = null;
                        if (!(modelExists && responses.TryGetValue(request.Id, out response))) // check model file exist
                        {
                            try
                            {
                                //===========================
                                // Generation
                                response = Generate3DLocationModel(request, settings);
                                sumTilesDownloaded += response.NumTiles ?? 0;
                                response.Id = request.Id;
                                responses[response.Id] = response;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex.Message);
                            }
                            finally
                            {
                                _logger.LogInformation("Model generated. Waiting 10s...");
                                Thread.Sleep(10000); // wait 2 sec to dive overpassAPI some breath
                            }
                        }

                        if (response != null && string.IsNullOrWhiteSpace(response.UploadedFileId))
                        {
                            try
                            {
                                //===========================
                                // Upload
                                uploadRequest = GetUploadRequest(settings, request);
                                var sfResponse = _sketchFabApi.UploadModelAsync(uploadRequest, _sketchFabToken).GetAwaiter().GetResult();
                                response.UploadedFileId = sfResponse.ModelId;
                                response.UploadStatus = sfResponse.StatusCode == HttpStatusCode.Created ? UploadStatus.OK : UploadStatus.Error;
                                _logger.LogInformation($"SketchFab upload ok : {response.UploadedFileId}");
                            }
                            catch (Exception ex)
                            {
                                response.UploadStatus = UploadStatus.Error;
                                response.UploadedFileId = null;
                                _logger.LogError(ex.Message);
                            }
                            finally
                            {
                                _logger.LogInformation($"Waiting 10s...");
                                Thread.Sleep(10000); // wait 2 sec to give SkecthFab some breath
                            }
                        }

                        sw.WriteLine(string.Join("\t", request.Id, request.Title, request.Latitude, request.Longitude
                           , request.Description // link
                           , sumTilesDownloaded // tilecount_running_total
                           , response.UploadStatus.ToString() // sketchfab_status
                           , response.UploadedFileId // sketchfab_id
                            ));

                        sw.Flush();
                        if (responses.Count > 0)
                            _logger.LogInformation($"Reponse: {responses.Last().Value.Elapsed.TotalSeconds:N3} s, Average: {responses.Average(r => r.Value.Elapsed.TotalSeconds):N3} s ({responses.Count}/{requests.Count} model(s) so far, {sumTilesDownloaded} tiles)");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex.Message);
                    }

                }
            }



        }

        #region Input/Output file parsing
        private Dictionary<string, Location3DModelRequest> ParseInputFile(string fileName)
        {
            Dictionary<string, Location3DModelRequest> requests = new Dictionary<string, Location3DModelRequest>();
            using (StreamReader sr = new StreamReader(fileName, Encoding.UTF8))
            {
                sr.ReadLine(); // skip header
                do
                {
                    //pk,pn,lat,lon,link
                    Location3DModelRequest request = ParseRequestCsvLine(sr.ReadLine(), '\t');
                    requests.Add(request.Id, request);
                } while (!sr.EndOfStream);
            }

            return requests;
        }
        private Dictionary<string, Location3DModelResponse> ParseOutputFile(string fileName)
        {
            Dictionary<string, Location3DModelResponse> results = new Dictionary<string, Location3DModelResponse>();
            if (File.Exists(fileName))
            {
                using (StreamReader sr = new StreamReader(fileName, Encoding.UTF8))
                {
                    sr.ReadLine(); // skip header
                    do
                    {
                        //pk,pn,lat,lon,link
                        Location3DModelResponse result = ParseResultCsvLine(sr.ReadLine(), '\t');
                        results.Add(result.Id, result);
                    } while (!sr.EndOfStream);
                }
            }

            return results;
        }

        private Location3DModelRequest ParseRequestCsvLine(string cols, char separator)
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
        private Location3DModelResponse ParseResultCsvLine(string cols, char separator)
        {
            string[] values = cols.Split(separator);
            //pk,	pn,	lat,	lon,	link,	tilecount_running_total,	sketchfab_status	, sketchfab_id

            int index = 0;
            Location3DModelRequest request = new Location3DModelRequest();
            request.Id = values[index++];
            request.Title = values[index++].Trim('\"');
            request.Latitude = double.Parse(values[index++], CultureInfo.InvariantCulture);
            request.Longitude = double.Parse(values[index++], CultureInfo.InvariantCulture);
            request.Description = values[index++];
            Location3DModelResponse result = new Location3DModelResponse();
            result.Request = request;
            result.Id = request.Id;


            result.NumTiles = int.Parse(values[index++]);
            if (Enum.TryParse<UploadStatus>(values[index++], true, out UploadStatus uploadStatus))
            {
                result.UploadStatus = uploadStatus;
            }
            result.UploadedFileId = values[index++];

            return result;
        }

        #endregion

        private UploadModelRequest GetUploadRequest(Location3DModelSettings settings, Location3DModelRequest request)
        {
            UploadModelRequest upload = new UploadModelRequest()
            {
                Description = GenerateDescription(settings, request),// "TEST",// * Generated by [DEM Net Elevation API](https://elevationapi.com)\n* Helladic test upload",
                FilePath = Path.Combine(settings.OutputDirectory, settings.ModelFileNameGenerator(settings, request)),
                IsInspectable = true,
                IsPrivate = false,
                IsPublished = true,
                Name = string.Concat(request.Id, " ", request.Title),
                Options = new ModelOptions() { Background = SkecthFabEnvironment.Tokyo_Big_Sight, Shading = ShadingType.lit },
                Source = "mycenaean-atlas-project_elevationapi",
                TokenType = TokenType.Token
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

            desc.Add($"Imagery: [{settings.ImageryProvider.Attribution.Text}]({settings.ImageryProvider.Attribution.Url})");

            return string.Join(Environment.NewLine, desc.Select(d => string.Concat("* ", d)));
        }

        private Location3DModelResponse Generate3DLocationModel(Location3DModelRequest request, Location3DModelSettings settings)
        {
            Location3DModelResponse response = new Location3DModelResponse();
            var transform = new ModelGenerationTransform(Reprojection.SRID_PROJECTED_MERCATOR, true, settings.ZScale);
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
                        Debug.Assert(tiles.Count < 400);

                        tiles = _imageryService.DownloadTiles(tiles, settings.ImageryProvider);

                        string fileName = Path.Combine(settings.OutputDirectory, $"{request.Id}_Texture.jpg");
                        TextureInfo texInfo = _imageryService.ConstructTexture(tiles, bbox, fileName, TextureImageFormat.image_jpeg);

                        transform.BoundingBox = bbox;
                        hMap = transform.TransformHeightMap(hMap);


                        //var normalMap = _imageryService.GenerateNormalMap(hMap, settings.OutputDirectory, $"{request.Id}_normalmap.png");
                        pbrTexture = PBRTexture.Create(texInfo);
                    }

                    // Center on origin
                    //hMap = hMap.CenterOnOrigin(out Matrix4x4 transform).BakeCoordinates();
                    //response.Origin = new GeoPoint(request.Latitude, request.Longitude).ReprojectTo(Reprojection.SRID_GEODETIC, Reprojection.SRID_PROJECTED_MERCATOR);

                    ModelRoot model = _gltfService.CreateNewModel();
                    
                    //=======================
                    // Buildings
                    if (settings.OsmBuildings)
                    {
                        var triangulationNormals = _buildingService.GetBuildings3DTriangulation(bbox, settings.Dataset, settings.DownloadMissingFiles, transform, useOsmColors: true);
                        var indexedTriangulation = new IndexedTriangulation(triangulationNormals);

                        if (indexedTriangulation.Positions.Count > 0)
                        {
                            //if (!transform.IsIdentity)
                            //{
                            //    indexedTriangulation.Positions = indexedTriangulation.Positions.Select(v => Vector3.Transform(v, transform)).ToList();
                            //}

                            model = _gltfService.AddMesh(model, indexedTriangulation, null, null, doubleSided: true);
                        }
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
                    //if (pbrTexture != null)
                    //{
                    //    if (pbrTexture.NormalTexture != null) File.Delete(pbrTexture.NormalTexture.FilePath);
                    //    File.Delete(pbrTexture.BaseColorTexture.FilePath);
                    //}

                    response.Elapsed = timer.Elapsed;
                    response.NumTiles = pbrTexture.BaseColorTexture.TileCount;
                }
            }
            catch (Exception)
            {
                throw;
            }
            return response;
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
