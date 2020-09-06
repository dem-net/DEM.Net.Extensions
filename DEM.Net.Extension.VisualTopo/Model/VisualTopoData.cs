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
using DEM.Net.Core;
using System.Numerics;

namespace DEM.Net.Extension.VisualTopo
{
    public class VisualTopoData
    {
        public string Comment { get; internal set; }
        public string Entree { get; internal set; }
        public string Sortie { get; internal set; }
        public float Longueur { get; internal set; }
        public float Cap { get; internal set; }
        public float Pente { get; internal set; }
        public (float left, float right, float up, float down) CutSection { get; internal set; }
        public Vector3 VectorLocal { get; internal set; }
        public GeoPointRays GeoPointLocal { get; internal set; }
        public GeoPoint GeoPointGlobal_DEMProjection { get; internal set; }
        public GeoPoint GeoPointGlobal_ProjectedCoords { get; internal set; }
        public double TerrainElevationAbove { get; set; }
        public double Depth { get; set; }
        public bool IsRoot { get; internal set; }
        public VisualTopoSet Set { get; internal set; }
        public bool IsSectionStart { get; internal set; }
        public double DistanceFromEntry { get; internal set; }
        public bool IsDisconnected { get; internal set; } 
    }



}

