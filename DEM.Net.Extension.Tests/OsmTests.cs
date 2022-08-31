using DEM.Net.Core;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Test;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text;
using Xunit;
using DEM.Net.Extension.Osm;
using DEM.Net.Core.Imagery;
using SharpGLTF.Schema2;
using DEM.Net.glTF.SharpglTF;

namespace DEM.Net.Extension.Tests
{
    public class OsmTests : IClassFixture<DemNetFixture>
    {

        const string WKT_SF_BIG = "POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))";
        const string WKT_SF_SMALL = "POLYGON((-122.42722692299768 37.81034598808797, -122.38886060524865 37.81034598808797, -122.38886060524865 37.784573673820816, -122.42722692299768 37.784573673820816, -122.42722692299768 37.81034598808797))";
        const string WKT_SF_SUPERSMALL = "POLYGON((-122.41063177228989 37.80707295150412,-122.40904390455307 37.80707295150412,-122.40904390455307 37.806064225434206,-122.41063177228989 37.806064225434206,-122.41063177228989 37.80707295150412))";

        const string WKT_CHAMONIX = "POLYGON((6.7569761565750674 45.94389499881037,6.981509237629755 45.94389499881037,6.981509237629755 45.793767472321136,6.7569761565750674 45.793767472321136,6.7569761565750674 45.94389499881037))";
        const string WKT_AIX = "POLYGON((5.34415720579406 43.604782382279176,5.512557016585076 43.604782382279176,5.512557016585076 43.48396362090737,5.34415720579406 43.48396362090737,5.34415720579406 43.604782382279176))";
        const string WKT_AIX_DEBUG = "POLYGON((5.431936525888528 43.5582229526827,5.439317965097512 43.5582229526827,5.439317965097512 43.550945193773394,5.431936525888528 43.550945193773394,5.431936525888528 43.5582229526827))";
        // FULL
        const string WKT_PRIPYAT_FULL = "POLYGON((29.993379474855704 51.438414833369904,30.183580280519767 51.438414833369904,30.183580280519767 51.333857487728544,29.993379474855704 51.333857487728544,29.993379474855704 51.438414833369904))";
        // Cas 1
        const string WKT_PRIPYAT_1 = "POLYGON((30.05430313958436 51.404795567637294,30.055118531124887 51.404795567637294,30.055118531124887 51.40432372279637,30.05430313958436 51.40432372279637,30.05430313958436 51.404795567637294))";
        // cas 2
        const string WKT_PRIPYAT_2 = "POLYGON((30.062877280833636 51.40748141189236,30.063456637980853 51.40748141189236,30.063456637980853 51.40716017522757,30.062877280833636 51.40716017522757,30.062877280833636 51.40748141189236))";
        // cas 3
        const string WKT_PRIPYAT_RELATION = "POLYGON((30.05205144421368 51.40627424301629,30.056739945571714 51.40627424301629,30.056739945571714 51.401629354055544,30.05205144421368 51.401629354055544,30.05205144421368 51.40627424301629))";
        const string WKT_PRIPYAT_POLICE =
        "POLYGON((30.05531887410026 51.40760207487213,30.05977134106498 51.40760207487213,30.05977134106498 51.406430891737706,30.05531887410026 51.406430891737706,30.05531887410026 51.40760207487213))";

        // Napoli, multi polygon (https://www.openstreetmap.org/relation/8955771)
        const string WKT_RELATION_NAPOLI = "POLYGON((14.364430059744153 40.78433307340424, 14.365218629194532 40.78433307340424, 14.365218629194532 40.785023575175295, 14.364430059744153 40.785023575175295, 14.364430059744153 40.78433307340424))";

        const string WKT_LIECHTENSTEIN = "POLYGON((9.40770363486505 47.275366751293845,9.7015879122088 47.275366751293845,9.7015879122088 47.021325245910816,9.40770363486505 47.021325245910816,9.40770363486505 47.275366751293845))";
        const string WKT_LIECHTENSTEIN_RELATION = "POLYGON((9.476624699624079 47.07032043432385,9.484263630898493 47.07032043432385,9.484263630898493 47.06564348494006,9.476624699624079 47.06564348494006,9.476624699624079 47.07032043432385))";
        const string WKT_LIECHTENSTEIN_TILE_LIMIT = "POLYGON((9.489639854980533 47.0729467846166,9.494489288879459 47.0729467846166,9.494489288879459 47.07009695786518,9.489639854980533 47.07009695786518,9.489639854980533 47.0729467846166))";

        const string WKT_KIEV = "POLYGON((30.3095979141151 50.599341687974714,30.7600373672401 50.599341687974714,30.7600373672401 50.295018130747046,30.3095979141151 50.295018130747046,30.3095979141151 50.599341687974714))";
        const string WKT_LVIV = "POLYGON((23.843580176976825 50.05162731023709,24.239087989476825 50.05162731023709,24.458814551976825 49.903258512973096,24.458814551976825 49.6834012352496,24.239087989476825 49.55884730392324,23.865552833226825 49.55528394127019,23.607374122289325 49.70116866730184,23.601880958226825 49.89618191840424,23.843580176976825 50.05162731023709))";
        const string WKT_WEST_UKRAINE = "POLYGON((22.182915548219313 51.7771750191374,30.928032735719313 51.7771750191374,30.928032735719313 47.66074941542309,22.182915548219313 47.66074941542309,22.182915548219313 51.7771750191374))";

        //const string WKT_LUXEMBOURG = "POLYGON((5.915900337119999 49.72970938054274,6.334754096885624 49.72970938054274,6.334754096885624 49.51263020240245,5.915900337119999 49.51263020240245,5.915900337119999 49.72970938054274))";
        const string WKT_LUXEMBOURG = "POLYGON((5.692000675392981 50.19333895867552,6.543441105080481 50.19333895867552,6.543441105080481 49.420530054093895,5.692000675392981 49.420530054093895,5.692000675392981 50.19333895867552))";

        const string WKT_MONACO = "POLYGON((7.392898394013687 43.75862393086027,7.457443071748062 43.75862393086027,7.457443071748062 43.71571004610483,7.392898394013687 43.71571004610483,7.392898394013687 43.75862393086027))";

        const string WKT_KOSOVO = "POLYGON((19.79116580319729 43.395593514077966,21.99941775632229 43.395593514077966,21.99941775632229 41.76160664082583,19.79116580319729 41.76160664082583,19.79116580319729 43.395593514077966))";

        const string WKT_LATVIA = "POLYGON((20.139797573199644 58.258844643629544,28.906887416949644 58.258844643629544,28.906887416949644 55.386795442762434,20.139797573199644 55.386795442762434,20.139797573199644 58.258844643629544))";

        const string WKT_CORSICA = "POLYGON((8.480069186401286 43.04187678076409,9.625393893432536 43.04187678076409,9.625393893432536 41.31606594811492,8.480069186401286 41.31606594811492,8.480069186401286 43.04187678076409))";

        const string WKT_UKRAINE_MOUNTAINS = "POLYGON((23.621024638843874 48.915008011278594,23.98014023942981 48.915008011278594,23.98014023942981 48.67119290393242,23.621024638843874 48.67119290393242,23.621024638843874 48.915008011278594))";

        private readonly DefaultOsmProcessor _osmProcessor;
        private readonly ElevationService _elevationService;
        private readonly ImageryService _imageryService;
        private readonly SharpGltfService _gltfService;

        public OsmTests(DemNetFixture fixture)
        {
            _osmProcessor = fixture.ServiceProvider.GetService<DefaultOsmProcessor>();
            _elevationService = fixture.ServiceProvider.GetService<ElevationService>();

            _imageryService = fixture.ServiceProvider.GetService<ImageryService>();
            _gltfService = fixture.ServiceProvider.GetService<SharpGltfService>();
        }


        [Theory(DisplayName = "OSM Buildings")]
        [InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_RELATION), WKT_PRIPYAT_RELATION, true, false, 2)]
        [InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, false, 2)]
        [InlineData(nameof(WKT_LIECHTENSTEIN), WKT_LIECHTENSTEIN, true, false, 2)]
        [InlineData(nameof(WKT_KIEV), WKT_KIEV, true, false, 2)]
        [InlineData(nameof(WKT_LUXEMBOURG), WKT_LUXEMBOURG, true, false, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_RELATION), WKT_PRIPYAT_RELATION, true, true, 2)]
        //[InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, true, 2)]
        //[InlineData(nameof(WKT_VADUZ), WKT_VADUZ, true, true, 2)]
        //[InlineData(nameof(WKT_KIEV), WKT_KIEV, true, true, 2)]
        public void OSMBuildingsOverlapMeshes(string name, string bboxWKT, bool centerOnOrigin, bool computeElevations, float ZScale)
        {
            string outputDir = Directory.GetCurrentDirectory();

            // SF Big: POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))
            // SF Small: POLYGON((-122.41967382241174 37.81034598808797,-122.39761533547326 37.81034598808797,-122.39761533547326 37.79162804294824,-122.41967382241174 37.79162804294824,-122.41967382241174 37.81034598808797))

            var bbox = GeometryService.GetBoundingBox(bboxWKT);
            var transform = new ModelGenerationTransform(bbox, DEMDataSet.NASADEM.SRID, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin, ZScale, centerOnZOrigin: true);

            _osmProcessor.Settings.ComputeElevations = computeElevations;
            var model = _osmProcessor.Run(null, OsmLayer.Buildings, bbox, transform, dataSet: DEMDataSet.NASADEM, downloadMissingFiles: true, withBuildingsColors: true);


            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMBuildings_{name}.glb"));

        }

        [Theory(DisplayName = "OSM Streets")]
        [InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_RELATION), WKT_PRIPYAT_RELATION, true, false, 2)]
        [InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, false, 2)]
        [InlineData(nameof(WKT_LIECHTENSTEIN), WKT_LIECHTENSTEIN, true, false, 2)]
        [InlineData(nameof(WKT_KIEV), WKT_KIEV, true, false, 2)]
        [InlineData(nameof(WKT_LUXEMBOURG), WKT_LUXEMBOURG, true, false, 2)]
        [InlineData(nameof(WKT_LIECHTENSTEIN_TILE_LIMIT), WKT_LIECHTENSTEIN_TILE_LIMIT, true, false, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_RELATION), WKT_PRIPYAT_RELATION, true, true, 2)]
        //[InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, true, 2)]
        //[InlineData(nameof(WKT_VADUZ), WKT_VADUZ, true, true, 2)]
        //[InlineData(nameof(WKT_KIEV), WKT_KIEV, true, true, 2)]
        public void OSMStreets(string name, string bboxWKT, bool centerOnOrigin, bool computeElevations, float ZScale)
        {
            string outputDir = Directory.GetCurrentDirectory();

            // SF Big: POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))
            // SF Small: POLYGON((-122.41967382241174 37.81034598808797,-122.39761533547326 37.81034598808797,-122.39761533547326 37.79162804294824,-122.41967382241174 37.79162804294824,-122.41967382241174 37.81034598808797))

            var bbox = GeometryService.GetBoundingBox(bboxWKT);
            var transform = new ModelGenerationTransform(bbox, DEMDataSet.NASADEM.SRID, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin, ZScale, centerOnZOrigin: true);
            _osmProcessor.Settings.ComputeElevations = computeElevations;

            var model = _osmProcessor.Run(null, OsmLayer.Highways, bbox, transform, dataSet: DEMDataSet.NASADEM, downloadMissingFiles: true, withBuildingsColors: true);

            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMStreets_{name}.glb"));

        }

        [Theory(DisplayName = "OSM Streets and buildings")]
        [InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_RELATION), WKT_PRIPYAT_RELATION, true, 2)]
        //[InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, 2)]
        [InlineData(nameof(WKT_LIECHTENSTEIN), WKT_LIECHTENSTEIN, true, 2)]
        [InlineData(nameof(WKT_LIECHTENSTEIN_RELATION), WKT_LIECHTENSTEIN_RELATION, true, 2)]
        //[InlineData(nameof(WKT_KIEV), WKT_KIEV, true, 2)]
        //[InlineData(nameof(WKT_LVIV), WKT_LVIV, true, 2)]
        //[InlineData(nameof(WKT_LUXEMBOURG), WKT_LUXEMBOURG, true, 2)]
        [InlineData(nameof(WKT_MONACO), WKT_MONACO, true, 2)]
        //[InlineData(nameof(WKT_LATVIA), WKT_LATVIA, true, 2)]
        //[InlineData(nameof(WKT_KOSOVO), WKT_KOSOVO, true, 2)]
        //[InlineData(nameof(WKT_CORSICA), WKT_CORSICA, true, 2)]
        [InlineData(nameof(WKT_UKRAINE_MOUNTAINS), WKT_UKRAINE_MOUNTAINS, true, 2)]
        [InlineData(nameof(WKT_AIX), WKT_AIX, true, 2)]
        [InlineData(nameof(WKT_AIX_DEBUG), WKT_AIX_DEBUG, true, 2)]
        [InlineData(nameof(WKT_CHAMONIX), WKT_CHAMONIX, true, 2)]

        public void OSMStreetsBuildings(string name, string bboxWKT, bool centerOnOrigin, float ZScale, bool computeElevations = false)
        {
            string outputDir = Directory.GetCurrentDirectory();

            // SF Big: POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))
            // SF Small: POLYGON((-122.41967382241174 37.81034598808797,-122.39761533547326 37.81034598808797,-122.39761533547326 37.79162804294824,-122.41967382241174 37.79162804294824,-122.41967382241174 37.81034598808797))

            var bbox = GeometryService.GetBoundingBox(bboxWKT);
            var transform = new ModelGenerationTransform(bbox, DEMDataSet.NASADEM.SRID, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin, ZScale, centerOnZOrigin: true);

            _osmProcessor.Settings.ComputeElevations = computeElevations;
            var model = _osmProcessor.Run(null, OsmLayer.Highways | OsmLayer.Buildings, bbox, transform,  dataSet: DEMDataSet.NASADEM, downloadMissingFiles: false, withBuildingsColors: true);

            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMStreetsBuildings_{name}.glb"));

        }

        [Theory(DisplayName = "OSM StreetsBuildingsRailWater")]
        [InlineData(nameof(WKT_LIECHTENSTEIN), WKT_LIECHTENSTEIN, true, 2)]

        public void OSMStreetsBuildingsRailWater(string name, string bboxWKT, bool centerOnOrigin, float ZScale, bool computeElevations = false)
        {
            string outputDir = Directory.GetCurrentDirectory();

            // SF Big: POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))
            // SF Small: POLYGON((-122.41967382241174 37.81034598808797,-122.39761533547326 37.81034598808797,-122.39761533547326 37.79162804294824,-122.41967382241174 37.79162804294824,-122.41967382241174 37.81034598808797))

            var bbox = GeometryService.GetBoundingBox(bboxWKT);
            var transform = new ModelGenerationTransform(bbox, DEMDataSet.NASADEM.SRID, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin, ZScale, centerOnZOrigin: true);

            _osmProcessor.Settings.ComputeElevations = computeElevations;
            var model = _osmProcessor.Run(null,
                OsmLayer.Water | OsmLayer.Railway | OsmLayer.Highways | OsmLayer.Buildings,
                bbox, transform, dataSet: DEMDataSet.NASADEM, downloadMissingFiles: false, withBuildingsColors: true);

            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMStreetsBuildings_{name}.glb"));

        }

        [Theory(DisplayName = "OSM and Terrain")]
        [InlineData(nameof(WKT_LIECHTENSTEIN), WKT_LIECHTENSTEIN, true, 2)]
        public void OsmAllWithTerrain(string name, string bboxWKT, bool centerOnOrigin, float ZScale)
        {
            DEMDataSet dataset = DEMDataSet.NASADEM;
            var bbox = GeometryService.GetBoundingBox(bboxWKT);
            bool computeElevations = false;
            string outputDir = Directory.GetCurrentDirectory();

            var transform = new ModelGenerationTransform(bbox, dataset.SRID, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin: centerOnOrigin, ZScale, centerOnZOrigin: false);
            var heightMap = _elevationService.GetHeightMap(ref bbox, dataset);
            transform.BoundingBox = bbox; // bbox changed by GetHeigtMap

            heightMap = transform.TransformHeightMap(heightMap);
            TileRange tiles = _imageryService.DownloadTiles(bbox, ImageryProvider.EsriWorldImagery, 15);

            string fileName = Path.Combine(outputDir, "Texture.jpg");
            TextureInfo texInfo = _imageryService.ConstructTexture(tiles, bbox, fileName, TextureImageFormat.image_jpeg);
            var pbrTexture = PBRTexture.Create(texInfo, null);

            _osmProcessor.Settings.ComputeElevations = computeElevations;
            ModelRoot model = _osmProcessor.Run(null, OsmLayer.Buildings | OsmLayer.Highways, bbox, transform, dataset, downloadMissingFiles: true);
            model = _gltfService.AddTerrainMesh(model, heightMap, pbrTexture, 0.5f);


            model.SaveGLB(Path.Combine(outputDir, $"{nameof(OsmAllWithTerrain)}_{name}" + ".glb"));
        }

    }
}
