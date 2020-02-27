using DEM.Net.Core;
using DEM.Net.Core.Imagery;
using DEM.Net.Core.Services.Lab;
using DEM.Net.Extension.Osm;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Extension.Osm.OverpassAPI;
using DEM.Net.glTF.SharpglTF;
using GeoJSON.Net.Feature;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
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
        private readonly ILogger _logger;


        public HelladicSample(BuildingService buildingService
                , ImageryService imageryService
                , IElevationService elevationService
                , SharpGltfService gltfService
                , IMeshService meshService
                , ILogger<OsmExtensionSample> logger)
        {
            this._buildingService = buildingService;
            this._imageryService = imageryService;
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._logger = logger;
        }

        public void Run()
        {
            Location3DModelSettings settings = new Location3DModelSettings()
            {
                Dataset = DEMDataSet.AW3D30,
                ImageryProvider = ImageryProvider.MapTilerSatellite,
                ZScale = 2f,
                SideSizeKm = 1.5f,
                OsmBuildings = true,
                DownloadMissingFiles = false,
                GenerateTIN = false,
                MinTilesPerImage = 4,
                MaxDegreeOfParallelism = 2,
        };


            List<Location3DModelRequest> requests = new List<Location3DModelRequest>();
            using (StreamReader sr = new StreamReader(@"Helladic\3D_Initial.csv", Encoding.UTF8))
            {
                sr.ReadLine(); // skip header
                do
                {
                    //pk,pn,lat,lon,link
                    Location3DModelRequest request = ParseCsvLine(sr.ReadLine());
                    requests.Add(request);
                } while (!sr.EndOfStream);
            }

            
            Parallel.ForEach(requests, new ParallelOptions() { MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism }, request =>
                            {
                                Location3DModelResponse response = Generate3DLocationModel(request, settings);
                            });


        }

        private Location3DModelResponse Generate3DLocationModel(Location3DModelRequest request, Location3DModelSettings settings)
        {
            Location3DModelResponse response = null;
            try
            {
                using (TimeSpanBlock timer = new TimeSpanBlock($"3D model {request.Id}", _logger))
                {
                    BoundingBox bbox = GetBoundingBoxAroundLocation(request.Latitude, request.Longitude, settings.SideSizeKm);

                    HeightMap hMap = _elevationService.GetHeightMap(ref bbox, settings.Dataset);


                    PBRTexture pbrTexture = null;
                    if (settings.ImageryProvider != null)
                    {
                        // Imagery
                        TileRange tiles = _imageryService.ComputeBoundingBoxTileRange(bbox, settings.ImageryProvider, settings.MinTilesPerImage);
                        tiles = _imageryService.DownloadTiles(tiles, settings.ImageryProvider);
                        string fileName = Path.Combine(Directory.GetCurrentDirectory(), $"{request.Id}_Texture.jpg");
                        TextureInfo texInfo = _imageryService.ConstructTexture(tiles, bbox, fileName, TextureImageFormat.image_jpeg);



                        hMap = hMap.ReprojectTo(Reprojection.SRID_GEODETIC, Reprojection.SRID_PROJECTED_MERCATOR)
                                    .ZScale(settings.ZScale)
                                    .BakeCoordinates();
                        var normalMap = _imageryService.GenerateNormalMap(hMap, Directory.GetCurrentDirectory(), $"{request.Id}_normalmap.png");
                        pbrTexture = PBRTexture.Create(texInfo, normalMap);
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
                    model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), settings.ModelFileNameGenerator(settings, request)));

                }
            }
            catch (Exception)
            {
                throw;
            }
            return response;
        }

        private Location3DModelRequest ParseCsvLine(string cols)
        {
            string[] values = cols.Split(',');
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
