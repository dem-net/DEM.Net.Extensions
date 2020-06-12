using DEM.Net.Core;
using GeoJSON.Net;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using DEM.Net.Extension.Osm.OverpassAPI;
using DEM.Net.glTF.SharpglTF;
using SharpGLTF.Schema2;
using System.Numerics;
using DEM.Net.Extension.Osm.Model;

namespace DEM.Net.Extension.Osm.Buildings
{

    public class OsmBuildingProcessor : OsmProcessorStage<BuildingModel>
    {

        const double LevelHeightMeters = 3;


        public override string[] WaysFilter { get; set; } = new string[] { "building", "building:part" };
        public override string[] RelationsFilter { get; set; } =
        new string[] { "building" };
        public override string[] NodesFilter { get; set; } = null;
        public override bool ComputeElevations { get; set; } = true;
        public override OsmModelFactory<BuildingModel> ModelFactory => new BuildingValidator(base._logger, true, "white");
        public override string glTFNodeName => "Buildings";

        protected override ModelRoot AddToModel(ModelRoot gltfModel, string nodeName, OsmModelList<BuildingModel> models)
        {
            TriangulationNormals triangulation = this.Triangulate(models.Models);


            gltfModel = _gltfService.AddMesh(gltfModel, glTFNodeName, new IndexedTriangulation(triangulation), null, null, doubleSided: true);

            return gltfModel;

        }

        protected override List<BuildingModel> ComputeModelElevationsAndTransform(OsmModelList<BuildingModel> models, bool computeElevations, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline transform)
        {
            if (models.Count == 0) return models.Models;

            Dictionary<int, GeoPoint> reprojectedPointsById = null;

            // georeference
            var bbox = new BoundingBox();
            foreach (var p in models.Models)
            {
                foreach (var pt in p.ExteriorRing)
                    bbox.UnionWith(pt.Longitude, pt.Latitude, 0);
            }
            var bbox3857 = bbox.ReprojectTo(4326, 3857);


            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug))
            {
                // Select all points (outer ring) + (inner rings)
                // They all have an Id, so we can lookup in which building they should be mapped after
                var allBuildingPoints = models.Models
                    .SelectMany(b => b.Points);


                // Compute elevations if requested
                IEnumerable<GeoPoint> geoPoints = computeElevations ? _elevationService.GetPointsElevation(allBuildingPoints
                                                                        , dataSet
                                                                        , behavior: NoDataBehavior.SetToZero
                                                                        , downloadMissingFiles: downloadMissingFiles)
                                                                    : allBuildingPoints;

                geoPoints = transform?.TransformPoints(geoPoints);

                reprojectedPointsById = geoPoints.ToDictionary(p => p.Id.Value, p => p);
                //.ZScale(zScale)
                //.ReprojectGeodeticToCartesian(pointCount)
                //.CenterOnOrigin(bbox3857)

            }

            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Remap points", _logger, LogLevel.Debug))
            {
                int checksum = 0;
                foreach (var buiding in models.Models)
                {
                    foreach (var point in buiding.Points)
                    {
                        var newPoint = reprojectedPointsById[point.Id.Value];
                        point.Latitude = newPoint.Latitude;
                        point.Longitude = newPoint.Longitude;
                        point.Elevation = newPoint.Elevation;
                        checksum++;
                    }
                }
                Debug.Assert(checksum == reprojectedPointsById.Count);
                reprojectedPointsById.Clear();
                reprojectedPointsById = null;
            }

            return models.Models;
        }

        #region Triangulation

        public TriangulationNormals Triangulate(List<BuildingModel> buildingModels)
        {

            List<Vector3> positions = new List<Vector3>();
            List<Vector4> colors = new List<Vector4>();
            List<int> indices = new List<int>();
            List<Vector3> normals = new List<Vector3>();

            using (TimeSpanBlock timer = new TimeSpanBlock(nameof(Triangulate), _logger, LogLevel.Information))
            {
                // Get highest base point
                // Retrieve building size
                int numWithHeight = 0;
                int numWithColor = 0;

                foreach (var building in buildingModels)
                {
                    numWithHeight += building.HasHeightInformation ? 1 : 0;
                    numWithColor += (building.Color.HasValue || building.RoofColor.HasValue) ? 1 : 0;

                    var triangulation = this.Triangulate(building);

                    var positionsVec3 = triangulation.Positions.ToVector3().ToList();
                    var buildingNormals = _meshService.ComputeMeshNormals(positionsVec3, triangulation.Indices);

                    int initialPositionsCount = positions.Count;

                    positions.AddRange(positionsVec3);
                    indices.AddRange(triangulation.Indices.Select(i => i + initialPositionsCount).ToList());
                    colors.AddRange(triangulation.Colors);
                    normals.AddRange(buildingNormals);
                }

                _logger.LogInformation($"Building heights: {numWithHeight}/{buildingModels.Count} ({numWithHeight / (float)buildingModels.Count:P}) with height information.");
                _logger.LogInformation($"Building colors: {numWithColor}/{buildingModels.Count} ({numWithColor / (float)buildingModels.Count:P}) with color information.");
            }


            return new TriangulationNormals(positions, indices, normals, colors);
        }
        public TriangulationList<GeoPoint> Triangulate(BuildingModel building)
        {
            //==========================
            // Footprint triangulation
            //
            var footPrintOutline = building.ExteriorRing.Skip(1); // In GeoJson, ring last point == first point, we must filter the first point out
            var footPrintInnerRingsFlattened = building.InteriorRings == null ? null : building.InteriorRings.Select(r => r.Skip(1));

            TriangulationList<GeoPoint> triangulation = _meshService.Tesselate(footPrintOutline, footPrintInnerRingsFlattened);
            int numFootPrintIndices = triangulation.Indices.Count;
            /////

            // Now extrude it (build the sides)
            // Algo
            // First triangulate the foot print (with inner rings if existing)
            // This triangulation is the roof top if building is flat
            building = this.ComputeBuildingHeightMeters(building);

            int totalPoints = building.ExteriorRing.Count - 1 + building.InteriorRings.Sum(r => r.Count - 1);

            // Triangulate wall for each ring
            // (We add floor indices before copying the vertices, they will be duplicated and z shifted later on)
            List<int> numVerticesPerRing = new List<int>();
            numVerticesPerRing.Add(building.ExteriorRing.Count - 1);
            numVerticesPerRing.AddRange(building.InteriorRings.Select(r => r.Count - 1));
            triangulation = this.TriangulateRingsWalls(triangulation, numVerticesPerRing, totalPoints);

            // Roof
            // Building has real elevations

            // Create floor vertices by copying roof vertices and setting their z min elevation (floor or min height)
            var floorVertices = triangulation.Positions.Select(pt => pt.Clone(building.ComputedFloorAltitude)).ToList();
            triangulation.Positions.AddRange(floorVertices);

            // Take the first vertices and z shift them
            foreach (var pt in triangulation.Positions.Take(totalPoints))
            {
                pt.Elevation = building.ComputedRoofAltitude;
            }

            //==========================
            // Colors: if walls and roof color is the same, all vertices can have the same color
            // otherwise we must duplicate vertices to ensure consistent triangles color (avoid unrealistic shades)
            // AND shift the roof triangulation indices
            // Before:
            //      Vertices: <roof_wallcolor_0..i> / <floor_wallcolor_i..j>
            //      Indices: <roof_triangulation_0..i> / <roof_wall_triangulation_0..j>
            // After:
            //      Vertices: <roof_wallcolor_0..i> / <floor_wallcolor_i..j> // <roof_roofcolor_j..k>
            //      Indices: <roof_triangulation_j..k> / <roof_wall_triangulation_0..j>
            Vector4 DefaultColor = Vector4.One;
            bool mustCopyVerticesForRoof = (building.Color ?? DefaultColor) != (building.RoofColor ?? building.Color);
            // assign wall or default color to all vertices
            triangulation.Colors = triangulation.Positions.Select(p => building.Color ?? DefaultColor).ToList();

            if (mustCopyVerticesForRoof)
            {
                triangulation.Positions.AddRange(triangulation.Positions.Take(totalPoints));
                triangulation.Colors.AddRange(Enumerable.Range(1, totalPoints).Select(_ => building.RoofColor ?? DefaultColor));

                // shift roof triangulation indices
                for (int i = 0; i < numFootPrintIndices; i++)
                {
                    triangulation.Indices[i] += (triangulation.Positions.Count - totalPoints);
                }

            }

            Debug.Assert(triangulation.Colors.Count == 0 || triangulation.Colors.Count == triangulation.Positions.Count);

            return triangulation;

        }

        private TriangulationList<GeoPoint> TriangulateRingsWalls(TriangulationList<GeoPoint> triangulation, List<int> numVerticesPerRing, int totalPoints)
        {
            int offset = numVerticesPerRing.Sum();

            Debug.Assert(totalPoints == offset);

            int ringOffset = 0;
            foreach (var numRingVertices in numVerticesPerRing)
            {
                int i = 0;
                do
                {
                    triangulation.Indices.Add(ringOffset + i);
                    triangulation.Indices.Add(ringOffset + i + offset);
                    triangulation.Indices.Add(ringOffset + i + 1);

                    triangulation.Indices.Add(ringOffset + i + offset);
                    triangulation.Indices.Add(ringOffset + i + offset + 1);
                    triangulation.Indices.Add(ringOffset + i + 1);

                    i++;
                }
                while (i < numRingVertices - 1);

                // Connect last vertices to start vertices
                triangulation.Indices.Add(ringOffset + i);
                triangulation.Indices.Add(ringOffset + i + offset);
                triangulation.Indices.Add(ringOffset + 0);

                triangulation.Indices.Add(ringOffset + i + offset);
                triangulation.Indices.Add(ringOffset + 0 + offset);
                triangulation.Indices.Add(ringOffset + 0);

                ringOffset += numRingVertices;

            }
            return triangulation;
        }


        private BuildingModel ComputeBuildingHeightMeters(BuildingModel building)
        {
            if (building.ComputedRoofAltitude.HasValue)
                return building;

            building.HasHeightInformation = building.Levels.HasValue || building.Height.HasValue || building.MinHeight.HasValue;

            if (building.Levels.HasValue && building.Height.HasValue)
            {
                if (building.Levels.Value * LevelHeightMeters > building.Height.Value)
                {
                    _logger.LogWarning($"Conflicting height info (Levels:{building.Levels.Value} and Height:{building.Height}), height will be used.");
                }
            }
            if (building.IsPart && building.Parent != null)
            {
                if (building.Levels == null && building.Height == null)
                {
                    _logger.LogWarning("Undertermined height");
                    building.Levels = 1;
                }

                if (building.Levels.Value <= (building.Parent.Levels ?? 3))
                {
                    _logger.LogWarning($"Conflicting height info between building and part (parent:{building.Parent.Levels ?? 3} >= part:{building.Levels.Value}), choosing parent level.");
                    if (building.Levels.Value == (building.Parent.Levels ?? 3))
                    {
                        building.Levels = (building.Parent.Levels ?? 3) + 1;
                    }
                    else
                    {
                        building.Levels = (building.Parent.Levels ?? 3) + 1;
                    }

                }

                if (building.Parent.ComputedRoofAltitude == null)
                {
                    // Compute parent roof first
                    ComputeBuildingHeightMeters(building.Parent);
                }
                double highestFloorElevation = building.Parent.ComputedRoofAltitude.Value;

                double computedHeight = (building.Levels.Value - building.Parent.Levels ?? 3) * LevelHeightMeters;
                double roofElevation = computedHeight + highestFloorElevation;

                building.ComputedRoofAltitude = roofElevation;
                building.ComputedFloorAltitude = highestFloorElevation;// - 0.5; // 50 cm inclusion inside parent mesh
            }
            else
            {
                double highestFloorElevation = building.Points.OrderByDescending(p => p.Elevation ?? 0).First().Elevation ?? 0;

                double computedHeight = building.Height ?? (building.Levels ?? 3) * LevelHeightMeters;
                double roofElevation = computedHeight + highestFloorElevation;

                double? computedMinHeight = null;
                if (building.MinHeight.HasValue)
                    computedMinHeight = roofElevation - building.MinHeight.Value;

                building.ComputedRoofAltitude = roofElevation;
                building.ComputedFloorAltitude = computedMinHeight;
            }


            return building;
        }

        #endregion
    }
}
