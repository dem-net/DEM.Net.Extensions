﻿using DEM.Net.Core;
using DEM.Net.Core.Imagery;
using DEM.Net.Extension.Osm;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Extension.Osm.Highways;
using DEM.Net.Extension.Osm.OverpassAPI;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static DEM.Net.glTF.SharpglTF.SharpGltfService;

namespace SampleApp
{
    public class OsmExtensionSample
    {
        private readonly DefaultOsmProcessor _osmProcessor;
        private readonly ImageryService _imageryService;
        private readonly ElevationService _elevationService;
        private readonly SharpGltfService _gltfService;
        private readonly ILogger _logger;
        private readonly OverpassAPIDataService _osmService;
        private float ZScale = 2f;

        // FULL
        const string WKT_PRIPYAT_FULL = "POLYGON((29.993379474855704 51.438414833369904,30.183580280519767 51.438414833369904,30.183580280519767 51.333857487728544,29.993379474855704 51.333857487728544,29.993379474855704 51.438414833369904))";

        const string WKT_LIECHTENSTEIN = "POLYGON((9.40770363486505 47.275366751293845,9.7015879122088 47.275366751293845,9.7015879122088 47.021325245910816,9.40770363486505 47.021325245910816,9.40770363486505 47.275366751293845))";
        const string WKT_LIECHTENSTEIN_RELATION = "POLYGON((9.476624699624079 47.07032043432385,9.484263630898493 47.07032043432385,9.484263630898493 47.06564348494006,9.476624699624079 47.06564348494006,9.476624699624079 47.07032043432385))";
        const string WKT_LIECHTENSTEIN_TILE_LIMIT = "POLYGON((9.489639854980533 47.0729467846166,9.494489288879459 47.0729467846166,9.494489288879459 47.07009695786518,9.489639854980533 47.07009695786518,9.489639854980533 47.0729467846166))";

        const string WKT_KIEV = "POLYGON((30.3095979141151 50.599341687974714,30.7600373672401 50.599341687974714,30.7600373672401 50.295018130747046,30.3095979141151 50.295018130747046,30.3095979141151 50.599341687974714))";
        const string WKT_LVIV = "POLYGON((23.843580176976825 50.05162731023709,24.239087989476825 50.05162731023709,24.458814551976825 49.903258512973096,24.458814551976825 49.6834012352496,24.239087989476825 49.55884730392324,23.865552833226825 49.55528394127019,23.607374122289325 49.70116866730184,23.601880958226825 49.89618191840424,23.843580176976825 50.05162731023709))";
        const string WKT_WEST_UKRAINE = "POLYGON((22.182915548219313 51.7771750191374,30.928032735719313 51.7771750191374,30.928032735719313 47.66074941542309,22.182915548219313 47.66074941542309,22.182915548219313 51.7771750191374))";

        public OsmExtensionSample(DefaultOsmProcessor osmProcessor
                , OverpassAPIDataService osmService
                , ImageryService imageryService
                , ElevationService elevationService
                , SharpGltfService gltfService
                , ILogger<OsmExtensionSample> logger)
        {
            this._osmProcessor = osmProcessor;
            this._imageryService = imageryService;
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._logger = logger;
            this._osmService = osmService;
        }
        public void Run()
        {
            //using (FileStream fs = new FileStream(@"D:\Data\NLD\pays-de-la-loire.poly", FileMode.Open))
            //{
            //    string wkt = _osmService.ConvertOsmosisPolyToWkt(fs);
            //}
            //RunOsmPbfSample(@"C:\Temp\provence-alpes-cote-d-azur-latest.osm.pbf");

            FlatGeoBufTilesTest();

            Buildings3DOnly();

            //Run3DModelSamples_BuildingsGeoReferencing();
            //Run3DModelSamples_Buildings();
            //Run3DModelSamples_SkiResortsAndBuildings();

            //RunTesselationSample();

        }

        private void FlatGeoBufTilesTest()
        {
            string outputDir = Directory.GetCurrentDirectory();

            // SF Big: POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))
            // SF Small: POLYGON((-122.41967382241174 37.81034598808797,-122.39761533547326 37.81034598808797,-122.39761533547326 37.79162804294824,-122.41967382241174 37.79162804294824,-122.41967382241174 37.81034598808797))

            var bbox = GeometryService.GetBoundingBox(WKT_LIECHTENSTEIN);
            var transform = new ModelGenerationTransform(bbox, DEMDataSet.NASADEM.SRID, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin: true, ZScale, centerOnZOrigin: true);


            _osmProcessor.Settings.ComputeElevations=false;
            var model = _osmProcessor.Run(null,
                OsmLayer.Water | OsmLayer.Railway | OsmLayer.Highways | OsmLayer.Buildings,
                bbox, transform, dataSet: DEMDataSet.NASADEM, downloadMissingFiles: false, withBuildingsColors: true);

            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMStreetsBuildings_WKT.glb"));
        }

        private void Buildings3DOnly()
        {
            string outputDir = Directory.GetCurrentDirectory();

            string WKT_SF_BIG = "POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))";
            string WKT_SF_SMALL = "POLYGON((-122.42722692299768 37.81034598808797, -122.38886060524865 37.81034598808797, -122.38886060524865 37.784573673820816, -122.42722692299768 37.784573673820816, -122.42722692299768 37.81034598808797))";
            string WKT_SF_SUPERSMALL = "POLYGON((-122.41063177228989 37.80707295150412,-122.40904390455307 37.80707295150412,-122.40904390455307 37.806064225434206,-122.41063177228989 37.806064225434206,-122.41063177228989 37.80707295150412))";

            // FULL
            string WKT_PRIPYAT_FULL = "POLYGON((30.021919548702254 51.41813804241615,30.083030998897566 51.41813804241615,30.083030998897566 51.389438684773985,30.021919548702254 51.389438684773985,30.021919548702254 51.41813804241615))";
            // Cas 1
            //string WKT_PRIPYAT = "POLYGON((30.05430313958436 51.404795567637294,30.055118531124887 51.404795567637294,30.055118531124887 51.40432372279637,30.05430313958436 51.40432372279637,30.05430313958436 51.404795567637294))";
            // cas 2
            //string WKT_PRIPYAT = "POLYGON((30.062877280833636 51.40748141189236,30.063456637980853 51.40748141189236,30.063456637980853 51.40716017522757,30.062877280833636 51.40716017522757,30.062877280833636 51.40748141189236))";
            // cas 3
            //string WKT_PRIPYAT = "POLYGON((30.065251398582948 51.407283441091266,30.066243815918458 51.407283441091266,30.066243815918458 51.40558353075506,30.065251398582948 51.40558353075506,30.065251398582948 51.407283441091266))";

            string WKT_PRIPRYAT_SOMEROADS = "POLYGON((30.062684362726948 51.40792711901257,30.064690655070088 51.40792711901257,30.064690655070088 51.40693664170241,30.062684362726948 51.40693664170241,30.062684362726948 51.40792711901257))";

            // SF Big: POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))
            // SF Small: POLYGON((-122.41967382241174 37.81034598808797,-122.39761533547326 37.81034598808797,-122.39761533547326 37.79162804294824,-122.41967382241174 37.79162804294824,-122.41967382241174 37.81034598808797))

            // Napoli, multi polygon (https://www.openstreetmap.org/relation/8955771)
            //new BoundingBox(14.364430059744153, 14.365218629194532, 40.78433307340424, 40.785023575175295);

            string WKT_AIX_FULL = "POLYGON((5.402291662243135 43.565714431347274,5.48056925013376 43.565714431347274,5.48056925013376 43.50797300081391,5.402291662243135 43.50797300081391,5.402291662243135 43.565714431347274))";
            string WKT_AIX_WITHTERRAIN = "POLYGON((5.440657648511835 43.55957815383877,5.444434198804804 43.55957815383877,5.444434198804804 43.5579454365131,5.440657648511835 43.5579454365131,5.440657648511835 43.55957815383877))";
            string WKT_AIX_SMALLOSMBUG = "POLYGON((5.441805234256467 43.55910060792738,5.442684998813352 43.55910060792738,5.442684998813352 43.55877017799191,5.441805234256467 43.55877017799191,5.441805234256467 43.55910060792738))";
            string WKT_MONACO = "POLYGON((7.392147587957001 43.75577569838535,7.4410710803886415 43.75577569838535,7.4410710803886415 43.71757458493263,7.392147587957001 43.71757458493263,7.392147587957001 43.75577569838535))";

            string WKT_MONACO_DEBUG = "POLYGON((7.421709439122424 43.73663530909531,7.433961769902453 43.73663530909531,7.433961769902453 43.733007331111345,7.421709439122424 43.733007331111345,7.421709439122424 43.73663530909531))";//"POLYGON((7.426780270757294 43.73870913810349,7.432520198049164 43.73870913810349,7.432520198049164 43.73501926928533,7.426780270757294 43.73501926928533,7.426780270757294 43.73870913810349))";
            string WKT_HK = "POLYGON((114.13119740014092 22.360520982593926,114.21050495629326 22.360520982593926,114.21050495629326 22.28874575980822,114.13119740014092 22.28874575980822,114.13119740014092 22.360520982593926))";
            string WKT_FRISCO = "POLYGON((-122.5235839391063 37.81433638393927,-122.36222224477036 37.81433638393927,-122.36222224477036 37.71228516909579,-122.5235839391063 37.71228516909579,-122.5235839391063 37.81433638393927))";
            string WKT_DEFENSE = "POLYGON((2.222075572216413 48.902615468120246,2.3024130966304757 48.902615468120246,2.3024130966304757 48.86355756505397,2.222075572216413 48.86355756505397,2.222075572216413 48.902615468120246))";

            DEMDataSet dataset = DEMDataSet.NASADEM;
            var name = nameof(WKT_DEFENSE);
            var bbox = GeometryService.GetBoundingBox(WKT_DEFENSE);
            bool computeElevations = true;

            ModelRoot model = _gltfService.CreateNewModel();
            bool withTerrain = false;
            if (withTerrain)
            {
                var heightMap = _elevationService.GetHeightMap(ref bbox, dataset);
                var transform = new ModelGenerationTransform(bbox, dataset.SRID, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin: true, ZScale, centerOnZOrigin: true);
                heightMap = transform.TransformHeightMap(heightMap);
                TileRange tiles = _imageryService.DownloadTiles(bbox, ImageryProvider.MapBoxSatellite, 15);

                string fileName = Path.Combine(outputDir, "Texture.jpg");
                TextureInfo texInfo = _imageryService.ConstructTexture(tiles, bbox, fileName, TextureImageFormat.image_jpeg);
                var pbrTexture = PBRTexture.Create(texInfo, null);

                _osmProcessor.Settings.DownloadMissingDEMFiles = true;
                _osmProcessor.Settings.ComputeElevations = computeElevations;
                model = _osmProcessor.Run(model, OsmLayer.Buildings | OsmLayer.Highways, bbox, transform, dataset);
                model = _gltfService.AddTerrainMesh(model, heightMap, pbrTexture, 0.5f);
            }
            else
            {
                var transform = new ModelGenerationTransform(bbox, dataset.SRID, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin: true, ZScale, centerOnZOrigin: true);
                _osmProcessor.Settings.ComputeElevations = computeElevations;
                _osmProcessor.Settings.DownloadMissingDEMFiles = true;
                model = _osmProcessor.Run(model, OsmLayer.Buildings | OsmLayer.Highways, bbox, transform, dataset);
            }

            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), name + ".glb"));

        }


        //private void Run3DModelSamples_BuildingsGeoReferencing()
        //{
        //    string outputDir = Directory.GetCurrentDirectory();

        //    // Napoli, multi polygon (https://www.openstreetmap.org/relation/8955771)   
        //    var bbox = new BoundingBox(14.364430059744153, 14.365218629194532, 40.78433307340424, 40.785023575175295);
        //    var b = _buildingService.GetBuildingsModel(bbox, useOsmColors: false, defaultHtmlColor: "#ff0000");


        //    var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, true, ZScale);
        //    var model = _buildingService.GetBuildings3DModel(b.Buildings, DEMDataSet.ASTER_GDEMV3, downloadMissingFiles: true, transform);

        //    var heightMap = _elevationService.GetHeightMap(ref bbox, DEMDataSet.ASTER_GDEMV3);
        //    heightMap = heightMap.ReprojectGeodeticToCartesian().ZScale(ZScale);
        //    TileRange tiles = _imageryService.DownloadTiles(bbox, ImageryProvider.MapBoxSatellite, 10);
        //    string fileName = Path.Combine(outputDir, "Texture.jpg");


        //    TextureInfo texInfo = _imageryService.ConstructTextureWithGpxTrack(tiles, bbox, fileName, TextureImageFormat.image_jpeg, b.Buildings.SelectMany(bd => bd.Points).ReprojectTo(Reprojection.SRID_PROJECTED_MERCATOR, Reprojection.SRID_GEODETIC));
        //    //TextureInfo texInfo = _imageryService.ConstructTextureWithGpxTrack(tiles, bbox, fileName, TextureImageFormat.image_jpeg, b.Buildings.SelectMany(bd => bd.Points));
        //    var normalMap = _imageryService.GenerateNormalMap(heightMap, outputDir);
        //    var pbrTexture = PBRTexture.Create(texInfo, normalMap);
        //    model = _gltfService.AddTerrainMesh(model, heightMap, pbrTexture);

        //    model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), "GeoReferencing.glb"));

        //}

        //private void RunTesselationSample()
        //{
        //    List<GeoPoint> geoPoints = new List<GeoPoint>();
        //    geoPoints.Add(new GeoPoint(0, 0));
        //    geoPoints.Add(new GeoPoint(10, 0));
        //    geoPoints.Add(new GeoPoint(10, 10));
        //    geoPoints.Add(new GeoPoint(0, 10));

        //    List<List<GeoPoint>> inners = new List<List<GeoPoint>>();
        //    List<GeoPoint> inner = new List<GeoPoint>();
        //    inner.Add(new GeoPoint(3, 3));
        //    inner.Add(new GeoPoint(7, 3));
        //    inner.Add(new GeoPoint(7, 7));
        //    inner.Add(new GeoPoint(3, 7));
        //    inners.Add(inner);

        //    //_meshService.Tesselate(geoPoints, null);
        //    _meshService.Tesselate(geoPoints, inners);
        //}



        //private void Run3DModelSamples_Buildings()
        //{
        //    BoundingBox bbox;

        //    // Simple 4 vertex poly
        //    bbox = GeometryService.GetBoundingBox("POLYGON((5.418905095715298 43.55466923119226,5.419768767018094 43.55466923119226,5.419768767018094 43.55411328949576,5.418905095715298 43.55411328949576,5.418905095715298 43.55466923119226))");
        //    var b = _buildingService.GetBuildingsModel(bbox, useOsmColors: false);
        //    GetBuildings3D(b.Buildings, bbox, "Test white", terrain: false);
        //    b = _buildingService.GetBuildingsModel(bbox, useOsmColors: false);
        //    b.Buildings.First().RoofColor = new System.Numerics.Vector4(1, 0, 0, 1);
        //    GetBuildings3D(b.Buildings, bbox, "Test roof red", terrain: false);
        //    b = _buildingService.GetBuildingsModel(bbox, useOsmColors: false);
        //    b.Buildings.First().RoofColor = null;
        //    b.Buildings.First().Color = new System.Numerics.Vector4(0, 1, 0, 1);
        //    GetBuildings3D(b.Buildings, bbox, "Test wall green", terrain: false);
        //    b = _buildingService.GetBuildingsModel(bbox, useOsmColors: false);
        //    b.Buildings.First().RoofColor = new System.Numerics.Vector4(0, 0, 1, 1);
        //    b.Buildings.First().Color = new System.Numerics.Vector4(0, 1, 0, 1);
        //    GetBuildings3D(b.Buildings, bbox, "Test roof blue wall green", terrain: false);

        //    // NYC < 50mo
        //    bbox = GeometryService.GetBoundingBox("POLYGON((-74.024179370277 40.73462567898996,-73.97062102066762 40.73462567898996,-73.97062102066762 40.698455082879136,-74.024179370277 40.698455082879136,-74.024179370277 40.73462567898996))");
        //    GetBuildings3D(bbox, "NYC_testcolor");

        //    // NYC < 50mo
        //    bbox = GeometryService.GetBoundingBox("POLYGON((-74.02040281998403 40.79079422191712,-73.93937865006215 40.79079422191712,-73.93937865006215 40.69923595071656,-74.02040281998403 40.69923595071656,-74.02040281998403 40.79079422191712))");
        //    GetBuildings3D(bbox, "NYC", ImageryProvider.MapBoxSatelliteStreet, 12);


        //    /// Building bug NYC
        //    bbox = GeometryService.GetBoundingBox("POLYGON((-74.0044066092415 40.71371931743606,-74.00299576729967 40.71371931743606,-74.00299576729967 40.712487274171245,-74.0044066092415 40.712487274171245,-74.0044066092415 40.71371931743606))");
        //    GetBuildings3D(bbox, "David N NYC");

        //    /// Empire State Building
        //    bbox = GeometryService.GetBoundingBox("POLYGON((-73.98608718293481 40.74874603106414,-73.9851484097796 40.74874603106414,-73.9851484097796 40.748213648614474,-73.98608718293481 40.748213648614474,-73.98608718293481 40.74874603106414))");
        //    GetBuildings3D(bbox, "Empire State Buiding", terrain: false);

        //    ////// NYC
        //    //bbox = GeometryService.GetBoundingBox("POLYGON((-74.02211943375356 40.80112562628496,-73.92427244889028 40.80112562628496,-73.92427244889028 40.69878044956189,-74.02211943375356 40.69878044956189,-74.02211943375356 40.80112562628496))");
        //    //GetBuildings3D(bbox, "NYC", ImageryProvider.MapBoxSatelliteStreet, 12);

        //    // Aix centre
        //    bbox = GeometryService.GetBoundingBox("POLYGON((5.4350868411385145 43.536450929895565,5.461093539746913 43.536450929895565,5.461093539746913 43.51834167296956,5.4350868411385145 43.51834167296956,5.4350868411385145 43.536450929895565))");
        //    GetBuildings3D(bbox, "Aix centre", ImageryProvider.MapBoxSatelliteStreet);

        //    // Paris seine
        //    bbox = GeometryService.GetBoundingBox("POLYGON((2.316319715808053 48.87347870746406,2.3764011977416466 48.87347870746406,2.3764011977416466 48.83586694581099,2.316319715808053 48.83586694581099,2.316319715808053 48.87347870746406))");
        //    GetBuildings3D(bbox, "Paris Seine", ImageryProvider.MapBoxSatelliteStreet);

        //    // Aix en provence / rotonde
        //    bbox = new BoundingBox(5.444927726471018, 5.447502647125315, 43.52600685540608, 43.528138282848076);
        //    GetBuildings3D(bbox, "Aix_Rotonde", ImageryProvider.MapBoxSatelliteStreet);

        //    // Aix / ZA les Milles
        //    bbox = GeometryService.GetBoundingBox("POLYGON((5.337387271772482 43.49858292942485,5.3966104468213105 43.49858292942485,5.3966104468213105 43.46781823961212,5.337387271772482 43.46781823961212,5.337387271772482 43.49858292942485))");
        //    GetBuildings3D(bbox, "Aix_ZA_Milles");

        //    // Aix Mignet / polygon with inner ring
        //    bbox = GeometryService.GetBoundingBox("POLYGON((5.448310034686923 43.52504334503996,5.44888402741611 43.52504334503996,5.44888402741611 43.524666052953144,5.448310034686923 43.524666052953144,5.448310034686923 43.52504334503996))");
        //    GetBuildings3D(bbox, "Aix_Mignet");




        //    //// Chicago
        //    //bbox = GeometryService.GetBoundingBox("POLYGON((-87.93682314060652 42.097186773093576,-87.50560976170027 42.097186773093576,-87.50560976170027 41.64314045894196,-87.93682314060652 41.64314045894196,-87.93682314060652 42.097186773093576))");
        //    //GetBuildings3D(bbox, "Chicago");

        //    //// SF
        //    //bbox = GeometryService.GetBoundingBox("POLYGON((-122.45396156906122 37.838401558170304, -122.37637062667841 37.838401558170304, -122.37637062667841 37.771400298497376, -122.45396156906122 37.771400298497376, -122.45396156906122 37.838401558170304))");
        //    //GetBuildings3D(bbox, "San Francisco");

        //    //bbox = GeometryService.GetBoundingBox("POLYGON((-122.45430489181513 37.819961931258945,-122.35577126144403 37.819961931258945,-122.35577126144403 37.750229379397204,-122.45430489181513 37.750229379397204,-122.45430489181513 37.819961931258945))");
        //    //GetBuildings3D(bbox, "San Francisco (large)");

        //    //// La Paz
        //    //bbox = GeometryService.GetBoundingBox("POLYGON((-68.17064463180934 -16.4766837193842,-68.09339701218043 -16.4766837193842,-68.09339701218043 -16.542681928856904,-68.17064463180934 -16.542681928856904,-68.17064463180934 -16.4766837193842))");
        //    //GetBuildings3D(bbox, "La Paz");

        //    //// Capri
        //    //bbox = GeometryService.GetBoundingBox("POLYGON((10.085373456087536 42.88137857375818, 10.505600506868786 42.88137857375818, 10.505600506868786 42.63737387552473, 10.085373456087536 42.63737387552473, 10.085373456087536 42.88137857375818))");
        //    //GetBuildings3D(bbox, "Capri", numTiles: 20, tinMesh: true);

        //    ////Task.Delay(1000).GetAwaiter().GetResult();
        //    //// Aix en provence / slope
        //    //bbox = new BoundingBox(5.434828019053151, 5.4601480721537365, 43.5386672180082, 43.55272718416761);
        //    //GetBuildings3D(bbox);

        //    ////// BIG one Aix
        //    //bbox = GeometryService.GetBoundingBox("POLYGON((5.396107779203061 43.618902041686354,5.537556753812436 43.618902041686354,5.537556753812436 43.511932043620725,5.396107779203061 43.511932043620725,5.396107779203061 43.618902041686354))");
        //    //GetBuildings3D(bbox);

        //    ////Task.Delay(1000).GetAwaiter().GetResult();
        //    //// POLYGON((5.526716197512567 43.56457608971906,5.6334895739774105 43.56457608971906,5.6334895739774105 43.49662332237486,5.526716197512567 43.49662332237486,5.526716197512567 43.56457608971906))
        //    //// Aix en provence / ste victoire
        //    //bbox = new BoundingBox(5.526716197512567, 5.6334895739774105, 43.49662332237486, 43.56457608971906);
        //    //GetBuildings3D(bbox);


        //}

        //private void Run3DModelSamples_SkiResortsAndBuildings()
        //{
        //    BoundingBox bbox;


        //    bbox = GeometryService.GetBoundingBox("POLYGON((6.168505020891946 45.29983821918378,6.314073868548196 45.29983821918378,6.314073868548196 45.19444990202436,6.168505020891946 45.19444990202436,6.168505020891946 45.29983821918378))");

        //    GetSkiResort3D(bbox, "Saint Sorlin d'Arves", numTiles: 10, tinMesh: false, null, ImageryProvider.ThunderForestOutdoors, ImageryProvider.StamenToner);

        //    // Ski resort - Val d'Isère
        //    bbox = GeometryService.GetBoundingBox("POLYGON((6.801003508029977 45.5157770504273,7.074631742893258 45.5157770504273,7.074631742893258 45.405728861083176,6.801003508029977 45.405728861083176,6.801003508029977 45.5157770504273))");
        //    GetSkiResort3D(bbox, "Val d'Isère_Untextured", null);
        //    //GetSkiResort3D(bbox, "resort_esri", ImageryProvider.EsriWorldImagery, 4);
        //    //GetSkiResort3D(bbox, "resort_mapbox", ImageryProvider.EsriWorldImagery, 12);
        //    //GetSkiResort3D(bbox, "resort_opentopo", ImageryProvider.OpenTopoMap, 12);
        //    //GetSkiResort3D(bbox, "resort_toner", ImageryProvider.StamenToner, 12);

        //    //// Ski resort -  Les Arcs
        //    bbox = GeometryService.GetBoundingBox("POLYGON((6.722226712932899 45.63700796432442,6.898007962932899 45.63700796432442,6.898007962932899 45.51925909655606,6.722226712932899 45.51925909655606,6.722226712932899 45.63700796432442))");
        //    //GetSkiResort3D(bbox, "LesArcs_resort_toner", ImageryProvider.StamenToner, 4);
        //    GetSkiResort3D(bbox, "LesArcs_resort_notexture");


        //}
        //private void GetBuildings3D(List<BuildingModel> buildingModels, BoundingBox bbox, string modelName = "buildings", ImageryProvider provider = null, int numTiles = 4, bool tinMesh = false, bool terrain = true)
        //{
        //    try
        //    {
        //        using (TimeSpanBlock timer = new TimeSpanBlock($"{nameof(GetBuildings3D)} {modelName}", _logger))
        //        {
        //            // debug: write geojson to file
        //            //File.WriteAllText("buildings.json", JsonConvert.SerializeObject(buildingService.GetBuildingsGeoJson(bbox)));
        //            var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, true, ZScale);
        //            var model = _buildingService.GetBuildings3DModel(buildingModels, DEMDataSet.ASTER_GDEMV3, downloadMissingFiles: true, transform);
        //            if (terrain)
        //            {
        //                model = AddTerrainModel(model, bbox, DEMDataSet.ASTER_GDEMV3, withTexture: provider != null, provider, numTiles, tinMesh);
        //            }

        //            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), modelName + ".glb"));
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        throw;
        //    }
        //}
        //private void GetBuildings3D(BoundingBox bbox, string modelName = "buildings", ImageryProvider provider = null, int numTiles = 4, bool tinMesh = false, bool terrain = true)
        //{
        //    try
        //    {
        //        using (TimeSpanBlock timer = new TimeSpanBlock($"{nameof(GetBuildings3D)} {modelName}", _logger))
        //        {
        //            // debug: write geojson to file
        //            //File.WriteAllText("buildings.json", JsonConvert.SerializeObject(buildingService.GetBuildingsGeoJson(bbox)));

        //            var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, true, ZScale);
        //            var model = _buildingService.GetBuildings3DModel(bbox, DEMDataSet.ASTER_GDEMV3, downloadMissingFiles: true, transform, useOsmColors: true);
        //            if (terrain)
        //            {
        //                model = AddTerrainModel(model, bbox, DEMDataSet.ASTER_GDEMV3, withTexture: provider != null, provider, numTiles, tinMesh);
        //            }

        //            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), modelName + ".glb"));
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        throw;
        //    }
        //}
        //private void GetSkiResort3D(BoundingBox bbox, string modelName = "skiresort", ImageryProvider provider = null, int numTiles = 4, bool tinMesh = false)
        //{
        //    try
        //    {
        //        using (TimeSpanBlock timer = new TimeSpanBlock($"{nameof(GetSkiResort3D)} {modelName}", _logger))
        //        {
        //            // debug: write geojson to file
        //            //File.WriteAllText("buildings.json", JsonConvert.SerializeObject(buildingService.GetBuildingsGeoJson(bbox)));

        //            var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, true, ZScale);
        //            var model = _pisteSkiService.GetPiste3DModel(bbox, "piste:type", DEMDataSet.ASTER_GDEMV3, downloadMissingFiles: true, transform);

        //            var triangulationNormals = _buildingService.GetBuildings3DTriangulation(bbox, DEMDataSet.ASTER_GDEMV3, downloadMissingFiles: true, transform, useOsmColors: true); ;
        //            var indexedTriangulation = new IndexedTriangulation(triangulationNormals);
        //            _gltfService.AddMesh(model, indexedTriangulation, null, null, doubleSided: true);


        //            model = AddTerrainModel(model, bbox, DEMDataSet.ASTER_GDEMV3, withTexture: provider != null, provider, numTiles, tinMesh);

        //            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), modelName + ".glb"));
        //        }

        //    }
        //    catch (Exception ex)
        //    {
        //        throw;
        //    }
        //}

        //private void GetSkiResort3D(BoundingBox bbox, string modelName = "skiresort", int numTiles = 4, bool tinMesh = false, params ImageryProvider[] providers)
        //{
        //    try
        //    {
        //        using (TimeSpanBlock timer = new TimeSpanBlock($"{nameof(GetSkiResort3D)} {modelName}", _logger))
        //        {
        //            // debug: write geojson to file
        //            //File.WriteAllText("buildings.json", JsonConvert.SerializeObject(buildingService.GetBuildingsGeoJson(bbox)));

        //            var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, true, ZScale);
        //            var pistes = _pisteSkiService.GetPisteModels(bbox, "piste:type", DEMDataSet.ASTER_GDEMV3, downloadMissingFiles: true, transform);

        //            var triangulationNormals = _buildingService.GetBuildings3DTriangulation(bbox, DEMDataSet.ASTER_GDEMV3, downloadMissingFiles: true, transform, useOsmColors: true);
        //            var indexedTriangulation = new IndexedTriangulation(triangulationNormals);

        //            foreach (var provider in providers)
        //            {

        //                var model = _pisteSkiService.GetPiste3DModel(pistes);
        //                _gltfService.AddMesh(model, indexedTriangulation, null, null, doubleSided: true);

        //                model = AddTerrainModel(model, bbox, DEMDataSet.ASTER_GDEMV3, withTexture: provider != null, provider, numTiles, tinMesh);

        //                model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"{modelName}_{provider?.Name}.glb"));
        //            }


        //        }

        //    }
        //    catch (Exception ex)
        //    {
        //        throw;
        //    }
        //}


        //private ModelRoot AddTerrainModel(ModelRoot model, BoundingBox bbox, DEMDataSet dataset, bool withTexture = true, ImageryProvider provider = null, int numTiles = 4, bool tinMesh = false)
        //{
        //    try
        //    {
        //        string modelName = $"Terrain";
        //        string outputDir = Directory.GetCurrentDirectory();
        //        using (TimeSpanBlock timer = new TimeSpanBlock("Terrain", _logger))
        //        {
        //            provider = provider ?? ImageryProvider.EsriWorldImagery;
        //            _logger.LogInformation($"Getting height map data...");

        //            var heightMap = _elevationService.GetHeightMap(ref bbox, dataset);

        //            _logger.LogInformation($"Processing height map data ({heightMap.Count} coordinates)...");
        //            heightMap = heightMap
        //                .ReprojectGeodeticToCartesian() // Reproject to 3857 (useful to get coordinates in meters)
        //                .ZScale(ZScale);                    // Elevation exageration

        //            //=======================
        //            // Textures
        //            //
        //            PBRTexture pbrTexture = null;
        //            if (withTexture)
        //            {
        //                Console.WriteLine("Download image tiles...");
        //                TileRange tiles = _imageryService.DownloadTiles(bbox, provider, numTiles);
        //                string fileName = Path.Combine(outputDir, "Texture.jpg");

        //                Console.WriteLine("Construct texture...");
        //                TextureInfo texInfo = _imageryService.ConstructTexture(tiles, bbox, fileName, TextureImageFormat.image_jpeg);

        //                //
        //                //=======================

        //                //=======================
        //                // Normal map
        //                Console.WriteLine("Height map...");
        //                var normalMap = _imageryService.GenerateNormalMap(heightMap, outputDir);

        //                pbrTexture = PBRTexture.Create(texInfo, normalMap);

        //                //
        //                //=======================
        //            }
        //            // Triangulate height map
        //            // and add base and sides
        //            _logger.LogInformation($"Triangulating height map and generating 3D mesh...");

        //            model = tinMesh ? TINGeneration.AddTINMesh(model, heightMap, 10d, _gltfService, pbrTexture, Reprojection.SRID_PROJECTED_MERCATOR)
        //                            : _gltfService.AddTerrainMesh(model, heightMap, pbrTexture);
        //            return model;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, ex.Message);
        //        throw;
        //    }
        //}
    }
}
