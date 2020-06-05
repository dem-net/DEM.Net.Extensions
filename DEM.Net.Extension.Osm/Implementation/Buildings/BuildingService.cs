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
    public class BuildingService
    {

        private readonly IElevationService _elevationService;
        private readonly SharpGltfService _gltfService;
        private readonly IMeshService _meshService;
        private readonly OsmService _osmService;
        private readonly ILogger<BuildingService> _logger;

        const double LevelHeightMeters = 3;

        private bool computeElevations = false;

        //const string OverpassQueryBody = @"(way[""building""] ({{bbox}});
        //                way[""building:part""] ({{bbox}});
        //                //relation[type=building] ({{bbox}});
        //                //relation[""building""] ({{bbox}});
        //               );";
        const string OverpassQueryBody = @"(way[""building""] ({{bbox}});
                        way[""building:part""] ({{bbox}});
                        //relation[type=building] ({{bbox}});
                        relation[""building""] ({{bbox}});
                       );";

        public BuildingService(IElevationService elevationService
            , SharpGltfService gltfService
            , IMeshService meshService
            , OsmService osmService
            , ILogger<BuildingService> logger)
        {
            this._elevationService = elevationService;
            this._gltfService = gltfService;
            this._meshService = meshService;
            this._osmService = osmService;
            this._logger = logger;
        }

        public ModelRoot GetBuildings3DModel(List<BuildingModel> buildings, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline geoTransform)
        {
            try
            {
                TriangulationNormals triangulation = this.GetBuildings3DTriangulation(buildings, null, dataSet, downloadMissingFiles, geoTransform);

                // georeference
                var bbox = new BoundingBox();
                foreach (var p in triangulation.Positions)
                {
                    bbox.UnionWith(p.X, p.Y, p.Z);
                }


                var model = _gltfService.AddMesh(null, new IndexedTriangulation(triangulation), null, null, doubleSided: true);

                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetBuildings3DModel)} error: {ex.Message}");
                throw;
            }
        }

        public ModelRoot GetBuildings3DModel(BoundingBox bbox, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline geoTransform, bool useOsmColors, string defaultHtmlColor = null)
        {
            try
            {
                TriangulationNormals triangulation = this.GetBuildings3DTriangulation(bbox, dataSet, downloadMissingFiles, geoTransform, useOsmColors, defaultHtmlColor);

                var model = _gltfService.AddMesh(null, new IndexedTriangulation(triangulation), null, null, doubleSided: true);

                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetBuildings3DModel)} error: {ex.Message}");
                throw;
            }
        }
        public (List<BuildingModel> Buildings, int TotalPoints) GetBuildingsModel(BoundingBox bbox, bool useOsmColors, string defaultHtmlColor = null, Action<string, int> progressReport = null)
        {
            try
            {
                // Download buildings and convert them to GeoJson
                //FeatureCollection buildings = _osmService.GetOsmDataAsGeoJson(bbox, q => q
                //.WithWays("building")
                //.WithWays("building:part")
                //.WithRelations("type=building")
                //.WithRelations("building"));

                progressReport?.Invoke("OSM buildings: downloading", 0);
                FeatureCollection buildings = _osmService.GetOsmDataAsGeoJson(bbox,
                    BuildingService.OverpassQueryBody);


                progressReport?.Invoke("OSM buildings: converting", 20);
                // Create internal building model
                var buildingValidator = new BuildingValidator(_logger, useOsmColors, defaultHtmlColor);
                (List<BuildingModel> Buildings, int TotalPoints) parsedBuildings = _osmService.CreateModelsFromGeoJson(buildings, buildingValidator);


                // Attempt to fix mesh for internal building parts.
                // ref: https://wiki.openstreetmap.org/wiki/Simple_3D_buildings
                // Ex: a part can have building:levels=11 and main building:levels=10
                //
                // TODO: on big model this could by divided to conquer by using quad tree
                // every parent should be indexed on a quad an then child/parent check will
                // be made for only potential parents of that quad
                StopwatchLog timer = new StopwatchLog(_logger);
                {
                    var parts = parsedBuildings.Buildings.Where(b => b.IsPart);
                    var mainBuildings = parsedBuildings.Buildings.Where(b => !b.IsPart);
                    foreach (var part in parts)
                    {
                        var main = FindMainBuilding(part, mainBuildings);
                        if (main != null)
                        {
                            if (part.Parent != null)
                            {
                                _logger.LogWarning($"Part {part.Id} has more than one parent");
                            }
                            Debug.Assert(part.Parent == null);

                            part.Parent = main;

                        }
                    }
                    HashSet<string> idsToRemove = new HashSet<string>();
                    foreach (var part in mainBuildings)
                    {
                        var main = FindMainBuilding(part, mainBuildings.Where(p => p.Id != part.Id));
                        if (main != null)
                        {
                            idsToRemove.Add(main.Id);
                            if (part.Parent != null)
                            {
                                _logger.LogWarning($"Part {part.Id} has more than one parent");
                            }
                            Debug.Assert(part.Parent == null);

                            part.Parent = main;

                        }
                    }
                    timer.LogTime("Building parent point in polygon");
                    parsedBuildings.Buildings.RemoveAll(b => idsToRemove.Contains(b.Id));
                }
                
                return parsedBuildings;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetBuildingsModel)} error: {ex.Message}");
                throw;
            }
        }

        // Source: https://stackoverflow.com/a/49434625/1818237
        private bool IsInPolygon(GeoPoint point, IEnumerable<GeoPoint> polygon)
        {
            bool result = false;
            var a = polygon.Last();
            foreach (var b in polygon)
            {
                if ((b.Longitude == point.Longitude) && (b.Latitude == point.Latitude))
                    return true;

                if ((b.Latitude == a.Latitude) && (point.Latitude == a.Latitude) && (a.Longitude <= point.Longitude) && (point.Longitude <= b.Longitude))
                    return true;

                if ((b.Latitude < point.Latitude) && (a.Latitude >= point.Latitude) || (a.Latitude < point.Latitude) && (b.Latitude >= point.Latitude))
                {
                    if (b.Longitude + (point.Latitude - b.Latitude) / (a.Latitude - b.Latitude) * (a.Longitude - b.Longitude) <= point.Longitude)
                        result = !result;
                }
                a = b;
            }
            return result;
        }
        private BuildingModel FindMainBuilding(BuildingModel building, IEnumerable<BuildingModel> candidates)
        {
            foreach(var candidate in candidates)
            {
                if (building.ExteriorRing.All(p => IsInPolygon(p, candidate.ExteriorRing)))
                    return candidate;                
            }
            return null;
        }


        public OverpassCountResult GetCount(BoundingBox bbox)
        {
            try
            {
                // Download buildings and convert them to GeoJson
                //FeatureCollection buildings = _osmService.GetOsmDataAsGeoJson(bbox, q => q
                //.WithWays("building")
                //.WithWays("building:part")
                //.WithRelations("type=building")
                //.WithRelations("building"));

                OverpassCountResult buildingsCount = _osmService.GetOsmDataCount(bbox,
                   BuildingService.OverpassQueryBody);


                return buildingsCount;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetCount)} error: {ex.Message}");
                throw;
            }
        }
        public TriangulationNormals GetBuildings3DTriangulation(BoundingBox bbox, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline geoTransform, bool useOsmColors, string defaultHtmlColor = null, Action<string, int> progressReport = null)
        {
            try
            {

                // Download elevation data if missing
                if (downloadMissingFiles) _elevationService.DownloadMissingFiles(dataSet, bbox);

                (List<BuildingModel> Buildings, int TotalPoints) parsedBuildings = GetBuildingsModel(bbox, useOsmColors, defaultHtmlColor, progressReport);

                return GetBuildings3DTriangulation(parsedBuildings.Buildings, parsedBuildings.TotalPoints, dataSet, downloadMissingFiles, geoTransform, progressReport);
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetBuildings3DModel)} error: {ex.Message}");
                throw;
            }
        }
        public TriangulationNormals GetBuildings3DTriangulation(List<BuildingModel> buildings, int? count, DEMDataSet dataSet, bool downloadMissingFiles, IGeoTransformPipeline geoTransform, Action<string, int> progressReport = null)
        {

            progressReport?.Invoke("OSM buildings: getting elevation...", 50);
            // Compute elevations (faster elevation when point count is known in advance)
            buildings = this.ComputeElevations(buildings, count ?? buildings.Sum(b => b.Points.Count()), dataSet, downloadMissingFiles, geoTransform);

            progressReport?.Invoke("OSM buildings: triangulating...", 75);
            TriangulationNormals triangulation = this.Triangulate(buildings);
            return triangulation;

        }


        public FeatureCollection GetBuildingsGeoJson(int wayId)
        {
            try
            {
                using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(GetBuildingsGeoJson), _logger, LogLevel.Debug))
                {
                    var task = new OverpassQuery()
                    .WithWays("id", wayId.ToString())
                    .ToGeoJSON();

                    FeatureCollection buildings = task.GetAwaiter().GetResult();

                    return buildings;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetBuildingsGeoJson)} error: {ex.Message}");
                throw;
            }

        }

        public List<BuildingModel> ComputeElevations(List<BuildingModel> buildingModels, int pointCount, DEMDataSet dataset, bool downloadMissingFiles = true, IGeoTransformPipeline geoTransform = null)
        {
            if (buildingModels.Count == 0) return buildingModels;

            Dictionary<int, GeoPoint> reprojectedPointsById = null;

            // georeference
            var bbox = new BoundingBox();
            foreach (var p in buildingModels)
            {
                foreach (var pt in p.ExteriorRing)
                    bbox.UnionWith(pt.Longitude, pt.Latitude, 0);
            }
            var bbox3857 = bbox.ReprojectTo(4326, 3857);


            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Elevations+Reprojection", _logger, LogLevel.Debug))
            {
                // Select all points (outer ring) + (inner rings)
                // They all have an Id, so we can lookup in which building they should be mapped after
                var allBuildingPoints = buildingModels
                    .SelectMany(b => b.Points);

                // Compute elevations
                var geoPoints = _elevationService.GetPointsElevation(allBuildingPoints
                                                                    , dataset
                                                                    , downloadMissingFiles: downloadMissingFiles);
                geoPoints = geoTransform?.TransformPoints(geoPoints);

                reprojectedPointsById = geoPoints.ToDictionary(p => p.Id.Value, p => p);
                //.ZScale(zScale)
                //.ReprojectGeodeticToCartesian(pointCount)
                //.CenterOnOrigin(bbox3857)

            }

            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock("Remap points", _logger, LogLevel.Debug))
            {
                int checksum = 0;
                foreach (var buiding in buildingModels)
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

            return buildingModels;

        }

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
            if ( building.ComputedRoofAltitude.HasValue)
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
                        building.Levels = (building.Parent.Levels ?? 3) +1 ;
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
    }
}
