//
// VisualTopoSample.cs
//
// Author:
//       Xavier Fischer 2020-6
//
// Copyright (c) 2020 Xavier Fischer
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using ClosedXML.Excel;
using DEM.Net.Core;
using DEM.Net.Core.Graph;
using DEM.Net.Core.Services.Lab;
using DEM.Net.glTF.SharpglTF;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;


namespace DEM.Net.Extension.VisualTopo
{
    public class VisualTopoService
    {
        private readonly MeshService _meshService;
        private readonly ElevationService _elevationService;
        private readonly ILogger<VisualTopoService> _logger;
        private const int ModelDefaultSRID = Reprojection.SRID_PROJECTED_LAMBERT_93;
        private readonly Convers3Reprojection convers3Reprojection;

        public VisualTopoService(MeshService meshService, ElevationService elevationService, ILogger<VisualTopoService> logger)
        {
            _meshService = meshService;
            _elevationService = elevationService;
            convers3Reprojection = new Convers3Reprojection();
            _logger = logger;            
        }
        public VisualTopoModel LoadFile(string vtopoFile, Encoding encoding, bool decimalDegrees, bool ignoreRadialBeams, float zFactor = 1f)
        {
            var model = ParseFile(vtopoFile, encoding, decimalDegrees, ignoreRadialBeams);

            model = PreAnalyzeFile(model, zFactor);

            return model;
        }
        public VisualTopoModel LoadFile(Stream vtopoStream, Encoding encoding, bool decimalDegrees, bool ignoreRadialBeams, float zFactor = 1f)
        {
            var model = ParseFile(vtopoStream, encoding, decimalDegrees, ignoreRadialBeams);

            model = PreAnalyzeFile(model, zFactor);

            return model;
        }

        private VisualTopoModel PreAnalyzeFile(VisualTopoModel model, float zFactor = 1f)
        {
            // ========================
            // Graph
            CreateGraph(model);

            // ========================
            // 3D model - do not remove
            Build3DTopology(model, zFactor);

            return model;
        }

        private VisualTopoModel ParseFile(string vtopoFile, Encoding encoding, bool decimalDegrees, bool ignoreRadialBeams)
        {
            VisualTopoModel model = null;

            // ========================
            // Parsing
            using (StreamReader sr = new StreamReader(vtopoFile, encoding))
            {
                model = ParseFile(sr, decimalDegrees, ignoreRadialBeams);
            }

            return model;
        }
        private VisualTopoModel ParseFile(Stream vtopoStream, Encoding encoding, bool decimalDegrees, bool ignoreRadialBeams)
        {
            VisualTopoModel model = null;

            // ========================
            // Parsing
            using (StreamReader sr = new StreamReader(vtopoStream, encoding))
            {
                model = ParseFile(sr, decimalDegrees, ignoreRadialBeams);
            }

            return model;
        }

        private VisualTopoModel ParseFile(StreamReader vtopoFileReader, bool decimalDegrees, bool ignoreRadialBeams)
        {
            VisualTopoModel model = new VisualTopoModel();

            // ========================
            // Parsing

            model = this.ParseHeader(model, vtopoFileReader);

            while (!vtopoFileReader.EndOfStream)
            {
                model = this.ParseSet(model, vtopoFileReader, decimalDegrees, ignoreRadialBeams);
            }


            return model;
        }
        public void Create3DTriangulation(VisualTopoModel model)
        {
            // ========================
            // 3D model
            Build3DTopology_Triangulation(model, ColorStrategy.CreateFromModel);
            //Build3DTopology_Triangulation(model, ColorStrategy.CreateDepthGradient(model));
        }
        private void CreateGraph(VisualTopoModel model)
        {
            Dictionary<string, Node<VisualTopoData>> nodesByName = new Dictionary<string, Node<VisualTopoData>>();

            foreach (var data in model.Sets.SelectMany(s => s.Data))
            {
                if (data.Entree == model.Entree && model.Graph.Root == null) // Warning! Entrance may not be the start node
                {
                    data.IsRoot = true;
                    var node = model.Graph.CreateRoot(data, data.Entree);
                    nodesByName[node.Key] = node;
                }

                if (data.Entree != data.Sortie)
                {
                    var node = model.Graph.CreateNode(data, data.Sortie);
                    if (!nodesByName.ContainsKey(data.Entree))
                    {
                        // Début graphe disjoint
                        _logger.LogWarning($"Disconnected node {data.Entree} found");
                        node.Model.IsDisconnected = true;
                        nodesByName[data.Entree] = node;
                    }
                    nodesByName[data.Entree].AddArc(node, data.Longueur);
                    nodesByName[node.Key] = node;
                }
            }
        }

        public MemoryStream ExportToCsv(VisualTopoModel model)
        {
            MemoryStream ms = new MemoryStream();
            using (StreamWriter sw = new StreamWriter(ms))
            {
                sw.WriteLine(string.Join("\t", "Entree", "Sortie", "Longueur", "Cap", "Pente", "Gauche", "Droite", "Haut", "Bas", "X", "Y", "Z", "Distance", "Profondeur", "Commentaire"));

                foreach (var set in model.Sets)
                {
                    sw.WriteLine(string.Join("\t", "Entree", "Sortie", "Longueur", "Cap", "Pente", set.Color.ToRgbString(), "", "", "", "", "", "", "", "", set.Name));

                    foreach (var data in set.Data)
                    {
                        sw.WriteLine(string.Join("\t", data.Entree, data.Sortie, data.Longueur, data.Cap, data.Pente
                            , data.CutSection.left, data.CutSection.right, data.CutSection.up, data.CutSection.down
                            , data.VectorLocal.X, data.VectorLocal.Y, data.VectorLocal.Z
                            , data.DistanceFromEntry, data.Depth, data.Comment));
                    }
                }

            }
            return ms;
        }
        public MemoryStream ExportToExcel(VisualTopoModel model, bool autoFitColumns = true)
        {
            using (TimeSpanBlock timer = new TimeSpanBlock("VisualTopo Excel export", _logger))
            using (var wb = new XLWorkbook(XLEventTracking.Disabled))
            {
                var ws = wb.Worksheets.Add($"Cavité entrée Z={model.EntryPoint.Elevation:N1} m");

                int ro = 1;
                int co = 0;
                ws.Cell(ro, ++co).Value = "Pt Départ";
                ws.Cell(ro, ++co).Value = "Pt Arrivée";
                ws.Cell(ro, ++co).Value = "Longueur";
                ws.Cell(ro, ++co).Value = "Cap";
                ws.Cell(ro, ++co).Value = "Pente";
                ws.Cell(ro, ++co).Value = "Gauche";
                ws.Cell(ro, ++co).Value = "Droite";
                ws.Cell(ro, ++co).Value = "Haut";
                ws.Cell(ro, ++co).Value = "Bas";
                ws.Cell(ro, ++co).Value = "X";
                ws.Cell(ro, ++co).Value = "Y";
                ws.Cell(ro, ++co).Value = "Z";
                ws.Cell(ro, ++co).Value = "Latitude";
                ws.Cell(ro, ++co).Value = "Longitude";
                ws.Cell(ro, ++co).Value = "X (Lambert 93)";
                ws.Cell(ro, ++co).Value = "Y (Lambert 93)";
                ws.Cell(ro, ++co).Value = "Distance";
                ws.Cell(ro, ++co).Value = "Profondeur relative entrée";
                ws.Cell(ro, ++co).Value = "Altitude terrain";
                ws.Cell(ro, ++co).Value = "Profondeur réelle";
                ws.Cell(ro, ++co).Value = "Commentaire";

                var row = ws.Row(ro);
                row.Style.Fill.BackgroundColor = XLColor.GreenYellow;
                row.Style.Font.Bold = true;

                foreach (var set in model.Sets)
                {
                    ro++;
                    co = 0;
                    ws.Cell(ro, ++co).Value = "Section";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "Pente";
                    ws.Cell(ro, ++co).Value = set.Color.ToRgbString();
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = "";
                    ws.Cell(ro, ++co).Value = set.Name;

                    var setRow = ws.Row(ro);
                    setRow.Style.Fill.BackgroundColor = XLColor.GreenYellow;
                    setRow.Style.Font.Bold = true;

                    foreach (var data in set.Data)
                    {
                        ro++;
                        co = 0;
                        ws.Cell(ro, ++co).Value = string.Concat("'", data.Entree);
                        ws.Cell(ro, ++co).Value = string.Concat("'", data.Sortie);
                        ws.Cell(ro, ++co).Value = data.Longueur; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.Cap; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.Pente; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.CutSection.left; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.CutSection.right; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.CutSection.up; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.CutSection.down; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.VectorLocal.X; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.VectorLocal.Y; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.VectorLocal.Z; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        // Latitude, Long, X,Y
                        ws.Cell(ro, ++co).Value = data.GeoPointGlobal_DEMProjection?.Latitude;//  ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.GeoPointGlobal_DEMProjection?.Longitude;  //ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.GeoPointGlobal_ProjectedCoords?.Longitude; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.GeoPointGlobal_ProjectedCoords?.Latitude; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;

                        ws.Cell(ro, ++co).Value = data.DistanceFromEntry; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.VectorLocal.Z; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        ws.Cell(ro, ++co).Value = data.TerrainElevationAbove; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;
                        var formula = string.Format(CultureInfo.InvariantCulture, "=RC[-1]-{0:N2}-RC[-2]", model.EntryPoint.Elevation ?? 0);
                        ws.Cell(ro, ++co).FormulaR1C1 = formula; ws.Cell(ro, co).Style.NumberFormat.NumberFormatId = 2;

                        // comment row (red for disconnected node)
                        if (data.IsDisconnected)
                        {
                            var disconnectedRow = ws.Row(ro);
                            setRow.Style.Fill.BackgroundColor = XLColor.Red;
                            disconnectedRow.Style.Fill.BackgroundColor = XLColor.Red;
                            ws.Cell(ro, ++co).Value = string.Concat("[DISCONNECTED] ", data.Comment);
                        }
                        else
                        {
                            ws.Cell(ro, ++co).Value = data.Comment;
                        }

                    }
                }

                if (autoFitColumns) ws.Columns().AdjustToContents();

                wb.CalculateMode = XLCalculateMode.Auto;

                MemoryStream ms = new MemoryStream();
                wb.SaveAs(ms, validate: true, evaluateFormulae: true);
                ms.Seek(0, SeekOrigin.Begin);

                return ms;
            }
        }

        // Elevations
        public void ComputeCavityElevations(VisualTopoModel model, DEMDataSet dataset, float zFactor = 1)
        {
            var entryPoint4326 = model.EntryPoint.ReprojectTo(model.SRID, dataset.SRID);
            model.EntryPoint.Elevation = zFactor * _elevationService.GetPointElevation(entryPoint4326, dataset).Elevation ?? 0;

            foreach (var set in model.Sets.Where(s => s.Data.First().GeoPointLocal != null))
            {
                VisualTopoData setStartData = set.Data.First(d => d.GeoPointLocal != null);
                GeoPoint dataPoint = setStartData.GeoPointLocal.Clone();
                dataPoint.Longitude += model.EntryPoint.Longitude;
                dataPoint.Latitude += model.EntryPoint.Latitude;
                var setStartPointDem = dataPoint.ReprojectTo(model.SRID, dataset.SRID);
                setStartData.TerrainElevationAbove = zFactor * _elevationService.GetPointElevation(setStartPointDem, dataset).Elevation ?? 0;
            }
        }
        public void ComputeFullCavityElevations(VisualTopoModel model, DEMDataSet dataset, float zFactor = 1)
        {
            var entryPoint4326 = model.EntryPoint.ReprojectTo(model.SRID, dataset.SRID);
            model.EntryPoint.Elevation = zFactor * _elevationService.GetPointElevation(entryPoint4326, dataset).Elevation ?? 0;

            foreach (var data in model.Graph.AllNodes.Where(n => n.Model.GeoPointLocal != null).Select(n => n.Model))
            {
                GeoPoint dataPoint = data.GeoPointLocal.Clone();
                dataPoint.Longitude += model.EntryPoint.Longitude;
                dataPoint.Latitude += model.EntryPoint.Latitude;
                var dataPointDem = dataPoint.ReprojectTo(model.SRID, dataset.SRID);

                data.GeoPointGlobal_ProjectedCoords = dataPoint;
                data.GeoPointGlobal_DEMProjection = dataPointDem;

                data.TerrainElevationAbove = zFactor * _elevationService.GetPointElevation(dataPointDem, dataset).Elevation ?? 0;
                data.Depth = data.TerrainElevationAbove - model.EntryPoint.Elevation.Value - data.VectorLocal.Z;
            }
        }

        #region Graph Traversal (full 3D)


        private void Build3DTopology_Triangulation(VisualTopoModel model, IColorCalculator colorFunc)
        {
            // Build color function
            float minElevation = model.Graph.AllNodes.Min(n => n.Model.VectorLocal.Z);


            // Generate triangulation
            //
            TriangulationList<Vector3> markersTriangulation = new TriangulationList<Vector3>();
            TriangulationList<Vector3> triangulation = GraphTraversal_Triangulation(model, null, ref markersTriangulation, model.Graph.Root, colorFunc);

            model.TriangulationFull3D = triangulation + markersTriangulation;
        }


        private TriangulationList<Vector3> GraphTraversal_Triangulation(VisualTopoModel visualTopoModel, TriangulationList<Vector3> triangulation, ref TriangulationList<Vector3> markersTriangulation, Node<VisualTopoData> node, IColorCalculator colorFunc)
        {
            triangulation = triangulation ?? new TriangulationList<Vector3>();

            var model = node.Model;
            if (model.IsSectionStart && triangulation.NumPositions > 0)
            {
                // Cylinder height = point depth + (terrain height above - entry Z)
                float cylinderHeight = -model.VectorLocal.Z + (float)(model.TerrainElevationAbove - visualTopoModel.EntryPoint.Elevation.Value);
                markersTriangulation += _meshService.CreateCylinder(model.VectorLocal, 0.2f, cylinderHeight, model.Set.Color);

                //var surfacePos = new Vector3(model.GlobalVector.X, model.GlobalVector.Y, (float)model.TerrainElevationAbove);
                float coneHeight = 10;
                markersTriangulation += _meshService.CreateCone(model.VectorLocal, 5, coneHeight, model.Set.Color)
                                            //.Translate(Vector3.UnitZ * -coneHeight / 2F)
                                            .Transform(Matrix4x4.CreateRotationY((float)Math.PI, new Vector3(model.VectorLocal.X, model.VectorLocal.Y, model.VectorLocal.Z + coneHeight / 2f)))
                                            //.Translate(Vector3.UnitZ * coneHeight / 2F)
                                            .Translate(Vector3.UnitZ * cylinderHeight);
            }

            if (node.Arcs.Count == 0) // leaf
            {
                Debug.Assert(triangulation.NumPositions > 0, "Triangulation should not be empty");

                // Make a rectangle perpendicual to direction centered on point(should be centered at human eye(y = 2m)
                triangulation = AddCorridorRectangleSection(triangulation, model, null, triangulation.NumPositions - 4, colorFunc);
            }
            else
            {
                int posIndex = triangulation.NumPositions - 4;
                foreach (var arc in node.Arcs)
                {
                    // Make a rectangle perpendicual to direction centered on point(should be centered at human eye(y = 2m)
                    triangulation = AddCorridorRectangleSection(triangulation, model, arc.Child.Model, posIndex, colorFunc);
                    posIndex = triangulation.NumPositions - 4;

                    triangulation = GraphTraversal_Triangulation(visualTopoModel, triangulation, ref markersTriangulation, arc.Child, colorFunc);

                }
            }

            return triangulation;
        }
        /// <summary>
        /// // Make a rectangle perpendicual to direction centered on point (should be centered at human eye (y = 2m)
        /// </summary>
        /// <param name="triangulation"></param>
        /// <param name="current"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        private TriangulationList<Vector3> AddCorridorRectangleSection(TriangulationList<Vector3> triangulation, VisualTopoData current, VisualTopoData nextData, int startIndex, IColorCalculator colorFunc)
        {
            Vector3 next = (nextData == null) ? current.VectorLocal : nextData.VectorLocal;
            GeoPointRays rays = current.GeoPointLocal;
            Vector3 direction = (nextData == null) ? Vector3.UnitZ * -1 : next - current.VectorLocal;
            direction = (direction == Vector3.Zero) ? Vector3.UnitZ * -1 : direction;
            var position = current.VectorLocal;

            Vector3 side = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
            if (IsInvalid(side)) // Vector3 is UnitY
            {
                side = Vector3.UnitX; // set it to UnitX
            }
            Vector3 up = Vector3.Normalize(Vector3.Cross(direction, side));

            if (IsInvalid(side) || IsInvalid(up))
            {
                return triangulation;
            }
            //var m = Matrix4x4.CreateWorld(next, direction, Vector3.UnitZ);

            triangulation.Positions.Add(position - side * rays.Left - up * rays.Down);
            triangulation.Positions.Add(position - side * rays.Left + up * rays.Up);
            triangulation.Positions.Add(position + side * rays.Right + up * rays.Up);
            triangulation.Positions.Add(position + side * rays.Right - up * rays.Down);

            //Vector4 color = (colorIndex++) % 2 == 0 ? VectorsExtensions.CreateColor(0, 255, 0) : VectorsExtensions.CreateColor(0, 0, 255);

            triangulation.Colors.AddRange(Enumerable.Repeat(colorFunc.GetColor(current, position), 4));

            // corridor sides
            if (triangulation.NumPositions > 4)
            {
                int i = startIndex; // triangulation.NumPositions - 8;
                int lastIndex = triangulation.NumPositions - 4;
                for (int n = 0; n < 4; n++)
                {
                    AddFace(ref triangulation, i + n, i + (n + 1) % 4
                                             , lastIndex + n, lastIndex + (n + 1) % 4);
                }
            }
            return triangulation;
        }

        private bool IsInvalid(Vector3 vector)
        {
            return float.IsNaN(vector.X) || float.IsNaN(vector.Y) || float.IsNaN(vector.Z)
                || float.IsInfinity(vector.X) || float.IsInfinity(vector.Y) || float.IsInfinity(vector.Z);
        }

        private void AddFace(ref TriangulationList<Vector3> triangulation, int i0, int i1, int i4, int i5)
        {
            // left side tri low
            triangulation.Indices.Add(i0);
            triangulation.Indices.Add(i4);
            triangulation.Indices.Add(i5);

            // left side tri high
            triangulation.Indices.Add(i0);
            triangulation.Indices.Add(i5);
            triangulation.Indices.Add(i1);
        }

        #endregion

        #region Graph Traversal and vectors computation

        private void Build3DTopology(VisualTopoModel model, float zFactor)
        {
            List<List<GeoPointRays>> branches = new List<List<GeoPointRays>>();
            GraphTraversal_Lines(model.Graph.Root, branches, null, Vector3.Zero, 0, zFactor);
            model.Topology3D = branches;
        }

        private void GraphTraversal_Lines(Node<VisualTopoData> node, List<List<GeoPointRays>> branches, List<GeoPointRays> current, Vector3 local, double runningTotalLength, float zFactor)
        {

            var p = node.Model;
            var direction = Vector3.UnitX * p.Longueur;
            var matrix = Matrix4x4.CreateRotationY((float)MathHelper.ToRadians(-p.Pente))
                * Matrix4x4.CreateRotationZ((float)(Math.PI / 2f - MathHelper.ToRadians(p.Cap)))
                * Matrix4x4.CreateScale(1, 1, zFactor);

            direction = Vector3.Transform(direction, matrix);
            runningTotalLength += p.Longueur; // on pourrait aussi utiliser p.GlobalVector.Length()
            p.VectorLocal = direction + local;
            p.GeoPointLocal = new GeoPointRays(p.VectorLocal.Y, p.VectorLocal.X, p.VectorLocal.Z
                                                , Vector3.Normalize(direction)
                                                , p.CutSection.left, p.CutSection.right, p.CutSection.up, p.CutSection.down);
            p.DistanceFromEntry = runningTotalLength;


            if (current == null) current = new List<GeoPointRays>();
            if (node.Arcs.Count == 0) // leaf
            {
                current.Add(node.Model.GeoPointLocal);
                branches.Add(current);
                return;
            }
            else
            {
                bool firstArc = true;
                foreach (var arc in node.Arcs)
                {
                    if (firstArc)
                    {
                        firstArc = false;

                        current.Add(node.Model.GeoPointLocal);
                        GraphTraversal_Lines(arc.Child, branches, current, node.Model.VectorLocal, runningTotalLength, zFactor);
                    }
                    else
                    {
                        var newBranch = new List<GeoPointRays>
                        {
                            node.Model.GeoPointLocal
                        };
                        GraphTraversal_Lines(arc.Child, branches, newBranch, node.Model.VectorLocal, runningTotalLength, zFactor);
                    }
                }
            }
        }



        #endregion

        #region DebugBranches

        // Useful to debug : output graph as node names
        public List<List<string>> GetBranchesNodeNames(VisualTopoModel model)
        {
            List<List<string>> branches = new List<List<string>>();
            GetBranches(model.Graph.Root, branches, null, n => n.Sortie);
            return branches;
        }
        private void GetBranches<T>(Node<VisualTopoData> node, List<List<T>> branches, List<T> current, Func<VisualTopoData, T> extractInfo)
        {
            if (current == null) current = new List<T>();

            T info = extractInfo(node.Model);
            if (node.Arcs.Count == 0)
            {
                current.Add(info);
                branches.Add(current);
                return;
            }
            else
            {
                bool firstArc = true;
                foreach (var arc in node.Arcs)
                {
                    if (firstArc)
                    {
                        firstArc = false;
                        current.Add(info);
                        GetBranches(arc.Child, branches, current, extractInfo);
                    }
                    else
                    {
                        var newBranch = new List<T>
                        {
                            info
                        };
                        GetBranches(arc.Child, branches, newBranch, extractInfo);
                    }
                }
            }
        }

        #endregion

        #region Parsing

        private void ParseEntryHeader(VisualTopoModel model, string entry)
        {
            var data = entry.Split(',');
            model.Name = data[0];
            model.EntryPointProjectionCode = data[4];
            model.EntryPoint = convers3Reprojection.CreateGeoPoint(inputProjectionCode: model.EntryPointProjectionCode, outputSrid: ModelDefaultSRID
                , x: double.Parse(data[1], CultureInfo.InvariantCulture)
                , y: double.Parse(data[2], CultureInfo.InvariantCulture)
                , z: double.Parse(data[3], CultureInfo.InvariantCulture));
            model.SRID = ModelDefaultSRID;
        }

        private VisualTopoModel ParseHeader(VisualTopoModel model, StreamReader sr)
        {

            sr.ReadUntil(string.IsNullOrWhiteSpace);
            var headerLines = sr.ReadUntil(string.IsNullOrWhiteSpace)
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .Select(s => s.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries))
                                .ToDictionary(s => s[0], s => s[1]);
            if (headerLines.TryGetValue("Trou", out string trou))
            {
                ParseEntryHeader(model, trou);
            }
            if (headerLines.TryGetValue("Club", out string club))
            {
                model.Author = club;
            }
            if (headerLines.TryGetValue("Entree", out string entree))
            {
                model.Entree = entree;
            }
            if (headerLines.TryGetValue("Toporobot", out string toporobot))
            {
                model.TopoRobot = toporobot == "1";
            }
            if (headerLines.TryGetValue("Couleur", out string couleur))
            {
                model.DefaultColor = ParseColor(couleur);
            }
            return model;
        }

        private VisualTopoModel ParseSet(VisualTopoModel model, StreamReader sr, bool decimalDegrees, bool ignoreRadialBeams)
        {
            VisualTopoSet set = new VisualTopoSet();

            string setHeader = sr.ReadLine();

            if (setHeader.StartsWith("[Configuration "))
            {
                sr.ReadToEnd(); // skip until end of stream
                return model;
            }

            // Set header
            var data = setHeader.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var headerSlots = data[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            set.Color = this.ParseColor(headerSlots);
            set.Name = data.Length > 1 ? data[1].Trim() : string.Empty;

            sr.Skip(1);
            var dataLine = sr.ReadLine();
            do
            {
                VisualTopoData topoData = new VisualTopoData();

                var parts = dataLine.Split(';');
                if (parts.Length > 1) topoData.Comment = parts[1].Trim();
                var slots = parts[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                //Debug.Assert(slots.Length == 13);

                // Parse data line
                topoData = this.ParseData(topoData, slots, decimalDegrees, ignoreRadialBeams);
                if (topoData != null)
                {
                    set.Add(topoData);
                }
                dataLine = sr.ReadLine();
            }
            while (!string.IsNullOrWhiteSpace(dataLine));

            model.Sets.Add(set);

            return model;
        }

        private VisualTopoData ParseData(VisualTopoData topoData, string[] slots, bool decimalDegrees, bool ignoreRadialBeams)
        {
            const string DefaultSize = "0.125";

            topoData.Entree = slots[0];
            topoData.Sortie = slots[1];

            if (topoData.Sortie == "*" && ignoreRadialBeams)
                return null;

            topoData.Longueur = float.Parse(slots[2], CultureInfo.InvariantCulture);
            topoData.Cap = ParseAngle(float.Parse(slots[3], CultureInfo.InvariantCulture), decimalDegrees);
            topoData.Pente = ParseAngle(float.Parse(slots[4], CultureInfo.InvariantCulture), decimalDegrees);
            topoData.CutSection = (left: float.Parse(slots[5] == "*" ? DefaultSize : slots[5], CultureInfo.InvariantCulture),
                                right: float.Parse(slots[6] == "*" ? DefaultSize : slots[6], CultureInfo.InvariantCulture),
                                up: float.Parse(slots[8] == "*" ? DefaultSize : slots[8], CultureInfo.InvariantCulture),
                                down: float.Parse(slots[7] == "*" ? DefaultSize : slots[7], CultureInfo.InvariantCulture));

            return topoData;
        }

        private float ParseAngle(float degMinSec, bool decimalDegrees)
        {
            // 125 deg 30min is not 125,5 BUT 125,3
            if (decimalDegrees)
            {
                return degMinSec;
            }
            else
            {
                // sexagecimal

                float intPart = (float)Math.Truncate(degMinSec);
                float decPart = degMinSec - intPart;

                return intPart + MathHelper.Map(0f, 0.6f, 0f, 1f, Math.Abs(decPart), false) * Math.Sign(degMinSec);

            }
        }

        // Find slot with color info and parse it
        private Vector4 ParseColor(string[] slots)
        {
            var colorStr = slots.Select(s => s.ToLower())
                 .Where(s => s == "std"
                          || (s.Count(ch => ch == ',') >= 2 && !s.Contains("dir")))
                 .FirstOrDefault();
            return ParseColor(colorStr);
        }
        private Vector4 ParseColor(string rgbCommaSeparated)
        {
            if (string.IsNullOrEmpty(rgbCommaSeparated) || rgbCommaSeparated == "std")
                return VectorsExtensions.CreateColor(255, 255, 255);

            var slots = rgbCommaSeparated.Split(',')
                        .Select(s => byte.Parse(s))
                        .ToArray();
            return VectorsExtensions.CreateColor(slots[0], slots[1], slots[2]);
        }

        #endregion

    }



}


