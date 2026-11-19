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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DEM.Net.Core;
using DEM.Net.Core.Graph;
using Microsoft.Extensions.Logging;


namespace DEM.Net.Extension.VisualTopo;

public class VisualTopoService(MeshService meshService, ElevationService elevationService, ILogger<VisualTopoService> logger)
{
    private readonly MeshService _meshService = meshService;
    private readonly ElevationService _elevationService = elevationService;
    private readonly ILogger<VisualTopoService> _logger = logger;
    private const int ModelDefaultSRID = Reprojection.SRID_PROJECTED_LAMBERT_93;
    private readonly Convers3Reprojection convers3Reprojection = new Convers3Reprojection();

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
        VisualTopoModel model;

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
        VisualTopoModel model;

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
        Dictionary<string, Node<VisualTopoData>> nodesByName = [];

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

    public MemoryStream ExportToCsv(VisualTopoModel model, string separator)
    {
        return model.ExportToCsv(separator);
    }
    public MemoryStream ExportToExcel(VisualTopoModel model, bool autoFitColumns = true)
    {
        return model.ExportToExcel(autoFitColumns);
    }

    // Elevations
    // All the points of a cavity are looked up in one batch: the DEM files are opened once, not once per point.

    /// <summary>
    /// Entry point and set start elevations, from the DEM files already downloaded
    /// (see <see cref="ComputeCavityElevationsAsync"/> to download the missing files).
    /// </summary>
    public void ComputeCavityElevations(VisualTopoModel model, DEMDataSet dataset, float zFactor = 1)
    {
        var (points, data) = GetSetStartPoints(model, dataset);
        ApplyCavityElevations(model, data, _elevationService.GetPointsElevation(points, dataset), zFactor);
    }

    /// <summary>
    /// Entry point and set start elevations, downloading the missing DEM files first.
    /// </summary>
    public async Task ComputeCavityElevationsAsync(VisualTopoModel model, DEMDataSet dataset, float zFactor = 1, CancellationToken cancellationToken = default)
    {
        var (points, data) = GetSetStartPoints(model, dataset);
        var elevations = await _elevationService.GetPointsElevationAsync(points, dataset, cancellationToken: cancellationToken).ConfigureAwait(false);
        ApplyCavityElevations(model, data, elevations, zFactor);
    }

    /// <summary>
    /// Entry point and every station elevation, depth below the terrain, from the DEM files already downloaded
    /// (see <see cref="ComputeFullCavityElevationsAsync"/> to download the missing files).
    /// </summary>
    public void ComputeFullCavityElevations(VisualTopoModel model, DEMDataSet dataset, float zFactor = 1)
    {
        var (points, data) = GetAllStationPoints(model, dataset);
        ApplyFullCavityElevations(model, data, _elevationService.GetPointsElevation(points, dataset), zFactor);
    }

    /// <summary>
    /// Entry point and every station elevation, depth below the terrain, downloading the missing DEM files first.
    /// </summary>
    public async Task ComputeFullCavityElevationsAsync(VisualTopoModel model, DEMDataSet dataset, float zFactor = 1, CancellationToken cancellationToken = default)
    {
        var (points, data) = GetAllStationPoints(model, dataset);
        var elevations = await _elevationService.GetPointsElevationAsync(points, dataset, cancellationToken: cancellationToken).ConfigureAwait(false);
        ApplyFullCavityElevations(model, data, elevations, zFactor);
    }

    /// <summary>
    /// Points in the DEM projection: the entry point first, then one point per returned station.
    /// </summary>
    private static (List<GeoPoint> Points, List<VisualTopoData> Data) GetSetStartPoints(VisualTopoModel model, DEMDataSet dataset)
    {
        var data = model.Sets.Where(s => s.Data.First().GeoPointLocal != null)
            .Select(s => s.Data.First(d => d.GeoPointLocal != null))
            .ToList();
        List<GeoPoint> points = [model.EntryPoint, .. data.Select(d => ToGlobal(model, d))];
        return ([.. points.ReprojectTo(model.SRID, dataset.SRID)], data);
    }

    private static (List<GeoPoint> Points, List<VisualTopoData> Data) GetAllStationPoints(VisualTopoModel model, DEMDataSet dataset)
    {
        var data = model.Graph.AllNodes.Where(n => n.Model.GeoPointLocal != null).Select(n => n.Model).ToList();
        List<GeoPoint> points = [model.EntryPoint, .. data.Select(d => ToGlobal(model, d))];
        List<GeoPoint> pointsDem = [.. points.ReprojectTo(model.SRID, dataset.SRID)];
        for (int i = 0; i < data.Count; i++)
        {
            data[i].GeoPointGlobal_ProjectedCoords = points[i + 1];
            data[i].GeoPointGlobal_DEMProjection = pointsDem[i + 1];
        }
        return (pointsDem, data);
    }

    private static GeoPoint ToGlobal(VisualTopoModel model, VisualTopoData data)
    {
        GeoPoint local = data.GeoPointLocal.Point;
        return local with { Longitude = local.Longitude + model.EntryPoint.Longitude, Latitude = local.Latitude + model.EntryPoint.Latitude };
    }

    /// <summary>Elevation of the point at <paramref name="index"/>, 0 when the dataset does not cover it.</summary>
    private static double ElevationAt(List<GeoPoint>? elevations, int index, float zFactor)
        => zFactor * elevations?[index].Elevation ?? 0;

    private static void ApplyCavityElevations(VisualTopoModel model, List<VisualTopoData> data, List<GeoPoint>? elevations, float zFactor)
    {
        model.EntryPoint = model.EntryPoint with { Elevation = ElevationAt(elevations, 0, zFactor) };
        for (int i = 0; i < data.Count; i++)
        {
            data[i].TerrainElevationAbove = ElevationAt(elevations, i + 1, zFactor);
        }
    }

    private static void ApplyFullCavityElevations(VisualTopoModel model, List<VisualTopoData> data, List<GeoPoint>? elevations, float zFactor)
    {
        model.EntryPoint = model.EntryPoint with { Elevation = ElevationAt(elevations, 0, zFactor) };
        for (int i = 0; i < data.Count; i++)
        {
            data[i].TerrainElevationAbove = ElevationAt(elevations, i + 1, zFactor);
            data[i].Depth = data[i].TerrainElevationAbove - model.EntryPoint.Elevation.Value - data[i].VectorLocal.Z;
        }
    }

    private void Build3DTopology_Triangulation(VisualTopoModel model, IColorCalculator colorFunc)
    {
        // Build color function
        float minElevation = model.Graph.AllNodes.Min(n => n.Model.VectorLocal.Z);


        // Generate triangulation
        //
        TriangulationList<Vector3> markersTriangulation = new TriangulationList<Vector3>();
        TriangulationList<Vector3> triangulation = GraphTraversal_Triangulation(model, null, ref markersTriangulation, model.Graph.Root!, colorFunc);

        model.TriangulationFull3D = triangulation + markersTriangulation;
    }


    private TriangulationList<Vector3> GraphTraversal_Triangulation(VisualTopoModel visualTopoModel, TriangulationList<Vector3>? triangulation, ref TriangulationList<Vector3> markersTriangulation, Node<VisualTopoData> node, IColorCalculator colorFunc)
    {
        triangulation = triangulation ?? new TriangulationList<Vector3>();

        var model = node.Model;
        if (model.IsSectionStart && triangulation.NumPositions > 0)
        {
            // Cylinder height = point depth + (terrain height above - entry Z)
            float cylinderHeight = -model.VectorLocal.Z + (float)(model.TerrainElevationAbove - visualTopoModel.EntryPoint.Elevation!.Value);
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
    private TriangulationList<Vector3> AddCorridorRectangleSection(TriangulationList<Vector3> triangulation, VisualTopoData current, VisualTopoData? nextData, int startIndex, IColorCalculator colorFunc)
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

        triangulation.Colors!.AddRange(Enumerable.Repeat(colorFunc.GetColor(current, position), 4));

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

    private void Build3DTopology(VisualTopoModel model, float zFactor)
    {
        List<List<GeoPointRays>> branches = [];
        GraphTraversal_Lines(model.Graph.Root!, branches, null, Vector3.Zero, 0, zFactor);
        model.Topology3D = branches;
    }

    private void GraphTraversal_Lines(Node<VisualTopoData> node, List<List<GeoPointRays>> branches, List<GeoPointRays>? current, Vector3 local, double runningTotalLength, float zFactor)
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


        if (current == null) current = [];
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

    // Useful to debug : output graph as node names
    public List<List<string>> GetBranchesNodeNames(VisualTopoModel model)
    {
        List<List<string>> branches = [];
        GetBranches(model.Graph.Root!, branches, null, n => n.Sortie);
        return branches;
    }
    private void GetBranches<T>(Node<VisualTopoData> node, List<List<T>> branches, List<T>? current, Func<VisualTopoData, T> extractInfo)
    {
        if (current == null) current = [];

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
                            .Select(s => s.Split([' '], 2, StringSplitOptions.RemoveEmptyEntries))
                            .ToDictionary(s => s[0], s => s[1]);
        if (headerLines.TryGetValue("Trou", out string? trou))
        {
            ParseEntryHeader(model, trou);
        }
        if (headerLines.TryGetValue("Club", out string? club))
        {
            model.Author = club;
        }
        if (headerLines.TryGetValue("Entree", out string? entree))
        {
            model.Entree = entree;
        }
        if (headerLines.TryGetValue("Toporobot", out string? toporobot))
        {
            model.TopoRobot = toporobot == "1";
        }
        if (headerLines.TryGetValue("Couleur", out string? couleur))
        {
            model.DefaultColor = ParseColor(couleur);
        }
        return model;
    }

    private VisualTopoModel ParseSet(VisualTopoModel model, StreamReader sr, bool decimalDegrees, bool ignoreRadialBeams)
    {
        VisualTopoSet set = new VisualTopoSet();

        string setHeader = sr.ReadLine()!;

        if (setHeader.StartsWith("[Configuration "))
        {
            sr.ReadToEnd(); // skip until end of stream
            return model;
        }

        // Set header
        var data = setHeader.Split([';'], StringSplitOptions.RemoveEmptyEntries);
        var headerSlots = data[0].Split([' '], StringSplitOptions.RemoveEmptyEntries);
        set.Color = this.ParseColor(headerSlots);
        set.Name = data.Length > 1 ? data[1].Trim() : string.Empty;

        sr.Skip(1);
        var dataLine = sr.ReadLine()!;
        do
        {
            VisualTopoData? topoData = new VisualTopoData();

            var parts = dataLine.Split(';');
            if (parts.Length > 1) topoData.Comment = parts[1].Trim();
            var slots = parts[0].Split([' '], StringSplitOptions.RemoveEmptyEntries);

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

    private VisualTopoData? ParseData(VisualTopoData topoData, string[] slots, bool decimalDegrees, bool ignoreRadialBeams)
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
    private Vector4 ParseColor(string? rgbCommaSeparated)
    {
        if (string.IsNullOrEmpty(rgbCommaSeparated) || rgbCommaSeparated == "std")
            return VectorsExtensions.CreateColor(255, 255, 255);

        var slots = rgbCommaSeparated.Split(',')
                    .Select(s => byte.Parse(s))
                    .ToArray();
        return VectorsExtensions.CreateColor(slots[0], slots[1], slots[2]);
    }


}


