﻿//
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
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Runtime.InteropServices;
using DEM.Net.Core.Graph;
using System;

namespace DEM.Net.Extension.VisualTopo
{
    public class VisualTopoModel
    {
        public string Name { get; internal set; }
        public GeoPoint EntryPoint { get; internal set; }
        public string EntryPointProjectionCode { get; internal set; }

        public Graph<VisualTopoData> Graph { get; set; } = new Graph<VisualTopoData>();
        public List<VisualTopoSet> Sets { get; set; } = new List<VisualTopoSet>();

        public Dictionary<string, VisualTopoData> GlobalPosPerSortie { get; internal set; }
        public string Author { get; internal set; }
        public bool TopoRobot { get; internal set; }
        public Vector4 DefaultColor { get; internal set; }
        public string Entree { get; internal set; }
        public int SRID { get; internal set; }

        public BoundingBox BoundingBox
        {
            get
            {
                var bbox = new BoundingBox() { SRID = this.SRID };
                Graph.AllNodes.ForEach(n => { if (n.Model.GeoPointLocal != null) bbox.UnionWith(n.Model.GeoPointLocal.Longitude, n.Model.GeoPointLocal.Latitude, n.Model.GeoPointLocal.Elevation ?? 0); });
                return bbox;
            }
        }

        public List<List<GeoPointRays>> Topology3D { get; internal set; }
        public TriangulationList<Vector3> TriangulationFull3D { get; internal set; }
    }



}

