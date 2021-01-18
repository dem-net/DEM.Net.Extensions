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
        const string WKT_PRIPYAT_FULL = "POLYGON((29.993379474855704 51.438414833369904,30.183580280519767 51.438414833369904,30.183580280519767 51.333857487728544,29.993379474855704 51.333857487728544,29.993379474855704 51.438414833369904))";
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

        const string WKT_LIECHTENSTEIN = "POLYGON((9.40770363486505 47.275366751293845,9.7015879122088 47.275366751293845,9.7015879122088 47.021325245910816,9.40770363486505 47.021325245910816,9.40770363486505 47.275366751293845))";

        const string WKT_KIEV = "POLYGON((30.3095979141151 50.599341687974714,30.7600373672401 50.599341687974714,30.7600373672401 50.295018130747046,30.3095979141151 50.295018130747046,30.3095979141151 50.599341687974714))";
        const string WKT_LVIV = "POLYGON((23.843580176976825 50.05162731023709,24.239087989476825 50.05162731023709,24.458814551976825 49.903258512973096,24.458814551976825 49.6834012352496,24.239087989476825 49.55884730392324,23.865552833226825 49.55528394127019,23.607374122289325 49.70116866730184,23.601880958226825 49.89618191840424,23.843580176976825 50.05162731023709))";
        const string WKT_WEST_UKRAINE = "POLYGON((22.182915548219313 51.7771750191374,30.928032735719313 51.7771750191374,30.928032735719313 47.66074941542309,22.182915548219313 47.66074941542309,22.182915548219313 51.7771750191374))";

        //const string WKT_LUXEMBOURG = "POLYGON((5.915900337119999 49.72970938054274,6.334754096885624 49.72970938054274,6.334754096885624 49.51263020240245,5.915900337119999 49.51263020240245,5.915900337119999 49.72970938054274))";
        const string WKT_LUXEMBOURG = "POLYGON((5.692000675392981 50.19333895867552,6.543441105080481 50.19333895867552,6.543441105080481 49.420530054093895,5.692000675392981 49.420530054093895,5.692000675392981 50.19333895867552))";

        const string WKT_MONACO = "POLYGON((7.392898394013687 43.75862393086027,7.457443071748062 43.75862393086027,7.457443071748062 43.71571004610483,7.392898394013687 43.71571004610483,7.392898394013687 43.75862393086027))";

        const string WKT_KOSOVO = "POLYGON((19.79116580319729 43.395593514077966,21.99941775632229 43.395593514077966,21.99941775632229 41.76160664082583,19.79116580319729 41.76160664082583,19.79116580319729 43.395593514077966))";

        const string WKT_LATVIA = "POLYGON((20.139797573199644 58.258844643629544,28.906887416949644 58.258844643629544,28.906887416949644 55.386795442762434,20.139797573199644 55.386795442762434,20.139797573199644 58.258844643629544))";

        private readonly DefaultOsmProcessor _osmProcessor;

        public OsmTests(DemNetFixture fixture)
        {
            _osmProcessor = fixture.ServiceProvider.GetService<DefaultOsmProcessor>();
        }


        [Theory(DisplayName = "OSM Buildings")]
        [InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_3), WKT_PRIPYAT_3, true, false, 2)]
        [InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, false, 2)]
        [InlineData(nameof(WKT_LIECHTENSTEIN), WKT_LIECHTENSTEIN, true, false, 2)]
        [InlineData(nameof(WKT_KIEV), WKT_KIEV, true, false, 2)]
        [InlineData(nameof(WKT_LUXEMBOURG), WKT_LUXEMBOURG, true, false, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_3), WKT_PRIPYAT_3, true, true, 2)]
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
            var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin, ZScale, centerOnZOrigin: true);


            var model = _osmProcessor.Run(null, OsmLayer.Buildings, bbox, transform, computeElevations, dataSet: DEMDataSet.NASADEM, downloadMissingFiles: true, withBuildingsColors: true);

            
            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMBuildings_{name}.glb"));

        }

        [Theory(DisplayName = "OSM Streets")]
        [InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_3), WKT_PRIPYAT_3, true, false, 2)]
        [InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, false, 2)]
        [InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, false, 2)]
        [InlineData(nameof(WKT_LIECHTENSTEIN), WKT_LIECHTENSTEIN, true, false, 2)]
        [InlineData(nameof(WKT_KIEV), WKT_KIEV, true, false, 2)]
        [InlineData(nameof(WKT_LUXEMBOURG), WKT_LUXEMBOURG, true, false, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, true, 2)]
        //[InlineData(nameof(WKT_PRIPYAT_3), WKT_PRIPYAT_3, true, true, 2)]
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
            var transform = new ModelGenerationTransform(bbox, Reprojection.SRID_PROJECTED_MERCATOR, centerOnOrigin, ZScale, centerOnZOrigin: true);


            var model = _osmProcessor.Run(null, OsmLayer.Highways, bbox, transform, computeElevations, dataSet: DEMDataSet.NASADEM, downloadMissingFiles: true, withBuildingsColors: true);

            model.SaveGLB(Path.Combine(Directory.GetCurrentDirectory(), $"OSMStreets_{name}.glb"));

        }

        [Theory(DisplayName = "OSM Streets and buildings")]
        [InlineData(nameof(WKT_PRIPYAT_FULL), WKT_PRIPYAT_FULL, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_1), WKT_PRIPYAT_1, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_2), WKT_PRIPYAT_2, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_3), WKT_PRIPYAT_3, true, 2)]
        [InlineData(nameof(WKT_RELATION_NAPOLI), WKT_RELATION_NAPOLI, true, 2)]
        [InlineData(nameof(WKT_PRIPYAT_POLICE), WKT_PRIPYAT_POLICE, true, 2)]
        [InlineData(nameof(WKT_LIECHTENSTEIN), WKT_LIECHTENSTEIN, true, 2)]
        [InlineData(nameof(WKT_KIEV), WKT_KIEV, true, 2)]
        [InlineData(nameof(WKT_LVIV), WKT_LVIV, true, 2)]
        [InlineData(nameof(WKT_LUXEMBOURG), WKT_LUXEMBOURG, true, 2)]
        [InlineData(nameof(WKT_MONACO), WKT_MONACO, true, 2)]
        [InlineData(nameof(WKT_LATVIA), WKT_LATVIA, true, 2)]
        [InlineData(nameof(WKT_KOSOVO), WKT_KOSOVO, true, 2)]
        //[InlineData(nameof(WKT_WEST_UKRAINE), WKT_WEST_UKRAINE, true, 2)]
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
