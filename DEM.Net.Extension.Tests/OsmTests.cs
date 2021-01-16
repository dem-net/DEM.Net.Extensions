﻿using DEM.Net.Core;
using DEM.Net.Extension.Osm.Buildings;
using DEM.Net.Test;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text;
using Xunit;
using DEM.Net.Extension.Osm;

namespace DEM.Net.Extension.Tests
{
    public class OsmTests : IClassFixture<DemNetFixture>
    {

        const string WKT_SF_BIG = "POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))";
        const string WKT_SF_SMALL = "POLYGON((-122.42722692299768 37.81034598808797, -122.38886060524865 37.81034598808797, -122.38886060524865 37.784573673820816, -122.42722692299768 37.784573673820816, -122.42722692299768 37.81034598808797))";
        const string WKT_SF_SUPERSMALL = "POLYGON((-122.41063177228989 37.80707295150412,-122.40904390455307 37.80707295150412,-122.40904390455307 37.806064225434206,-122.41063177228989 37.806064225434206,-122.41063177228989 37.80707295150412))";

        // FULL
        const string WKT_PRIPYAT_FULL = "POLYGON((30.021919548702254 51.41813804241615,30.083030998897566 51.41813804241615,30.083030998897566 51.389438684773985,30.021919548702254 51.389438684773985,30.021919548702254 51.41813804241615))";
        // Cas 1
        const string WKT_PRIPYAT_1 = "POLYGON((30.05430313958436 51.404795567637294,30.055118531124887 51.404795567637294,30.055118531124887 51.40432372279637,30.05430313958436 51.40432372279637,30.05430313958436 51.404795567637294))";
        // cas 2
        const string WKT_PRIPYAT_2 = "POLYGON((30.062877280833636 51.40748141189236,30.063456637980853 51.40748141189236,30.063456637980853 51.40716017522757,30.062877280833636 51.40716017522757,30.062877280833636 51.40748141189236))";
        // cas 3
        const string WKT_PRIPYAT_3 = "POLYGON((30.065251398582948 51.407283441091266,30.066243815918458 51.407283441091266,30.066243815918458 51.40558353075506,30.065251398582948 51.40558353075506,30.065251398582948 51.407283441091266))";
        const string WKT_PRIPYAT_POLICE =
        "POLYGON((30.05531887410026 51.40760207487213,30.05977134106498 51.40760207487213,30.05977134106498 51.406430891737706,30.05531887410026 51.406430891737706,30.05531887410026 51.40760207487213))";

        // Napoli, multi polygon (https://www.openstreetmap.org/relation/8955771)
        const string WKT_RELATION_NAPOLI = "POLYGON((14.364430059744153 40.78433307340424, 14.365218629194532 40.78433307340424, 14.365218629194532 40.785023575175295, 14.364430059744153 40.785023575175295, 14.364430059744153 40.78433307340424))";

        const string WKT_VADUZ = "POLYGON((9.508242015596382 47.146491518431674,9.530643825288765 47.146491518431674,9.530643825288765 47.13250869886131,9.508242015596382 47.13250869886131,9.508242015596382 47.146491518431674))";

        const string WKT_KIEV = "POLYGON((30.3095979141151 50.599341687974714,30.7600373672401 50.599341687974714,30.7600373672401 50.295018130747046,30.3095979141151 50.295018130747046,30.3095979141151 50.599341687974714))";
        const string WKT_LVIV = "POLYGON((23.843580176976825 50.05162731023709,24.239087989476825 50.05162731023709,24.458814551976825 49.903258512973096,24.458814551976825 49.6834012352496,24.239087989476825 49.55884730392324,23.865552833226825 49.55528394127019,23.607374122289325 49.70116866730184,23.601880958226825 49.89618191840424,23.843580176976825 50.05162731023709))";

        private readonly DefaultOsmProcessor _osmProcessor;

        public OsmTests(DemNetFixture fixture)
        {
            _osmProcessor = fixture.ServiceProvider.GetService<DefaultOsmProcessor>();
        }


        [Theory(DisplayName = "OSM Buildings")]
        [InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_3), WKT_PRIPYAT_3, true, 2)]
        [InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, 2)]
        [InlineData(nameof(WKT_VADUZ), WKT_VADUZ, true, 2)]
        [InlineData(nameof(WKT_KIEV), WKT_KIEV, true, 2)]
        public void OSMBuildingsOverlapMeshes(string name, string bboxWKT, bool centerOnOrigin, float ZScale)
        {
            string outputDir = Directory.GetCurrentDirectory();

            // SF Big: POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))
            // SF Small: POLYGON((-122.41967382241174 37.81034598808797,-122.39761533547326 37.81034598808797,-122.39761533547326 37.79162804294824,-122.41967382241174 37.79162804294824,-122.41967382241174 37.81034598808797))

            var bbox = GeometryService.GetBoundingBox(bboxWKT);
            var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin, ZScale, centerOnZOrigin: true);


            var model = _osmProcessor.Run(null, OsmLayer.Buildings, bbox, transform, computeElevations: true, dataSet: DEMDataSet.NASADEM, downloadMissingFiles: true, withBuildingsColors: true);

            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMBuildings_{name}.glb"));

        }

        [Theory(DisplayName = "OSM Streets")]
        [InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_3), WKT_PRIPYAT_3, true, 2)]
        [InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, 2)]
        [InlineData(nameof(WKT_VADUZ), WKT_VADUZ, true, 2)]
        [InlineData(nameof(WKT_KIEV), WKT_KIEV, true, 2)]
        [InlineData(nameof(WKT_LVIV), WKT_LVIV, true, 2)]
        public void OSMStreets(string name, string bboxWKT, bool centerOnOrigin, float ZScale)
        {
            string outputDir = Directory.GetCurrentDirectory();

            // SF Big: POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))
            // SF Small: POLYGON((-122.41967382241174 37.81034598808797,-122.39761533547326 37.81034598808797,-122.39761533547326 37.79162804294824,-122.41967382241174 37.79162804294824,-122.41967382241174 37.81034598808797))

            var bbox = GeometryService.GetBoundingBox(bboxWKT);
            var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin, ZScale, centerOnZOrigin: true);


            var model = _osmProcessor.Run(null, OsmLayer.Highways, bbox, transform, computeElevations: true, dataSet: DEMDataSet.NASADEM, downloadMissingFiles: true, withBuildingsColors: true);

            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMStreets_{name}.glb"));

        }

        [Theory(DisplayName = "OSM Streets and buildings")]
        [InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_3), WKT_PRIPYAT_3, true, 2)]
        [InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, 2)]
        [InlineData(nameof(WKT_VADUZ), WKT_VADUZ, true, 2)]
        [InlineData(nameof(WKT_KIEV), WKT_KIEV, true, 2)]
        [InlineData(nameof(WKT_LVIV), WKT_LVIV, true, 2)]
        public void OSMStreetsBuildings(string name, string bboxWKT, bool centerOnOrigin, float ZScale)
        {
            string outputDir = Directory.GetCurrentDirectory();

            // SF Big: POLYGON((-122.53517427420718 37.81548554152065,-122.35149660086734 37.81548554152065,-122.35149660086734 37.70311455416941,-122.53517427420718 37.70311455416941,-122.53517427420718 37.81548554152065))
            // SF Small: POLYGON((-122.41967382241174 37.81034598808797,-122.39761533547326 37.81034598808797,-122.39761533547326 37.79162804294824,-122.41967382241174 37.79162804294824,-122.41967382241174 37.81034598808797))

            var bbox = GeometryService.GetBoundingBox(bboxWKT);
            var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin, ZScale, centerOnZOrigin: true);


            var model = _osmProcessor.Run(null, OsmLayer.Highways | OsmLayer.Buildings, bbox, transform, computeElevations: false, dataSet: DEMDataSet.NASADEM, downloadMissingFiles: true, withBuildingsColors: true);

            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMStreetsBuildings_{name}.glb"));

        }

    }
}
