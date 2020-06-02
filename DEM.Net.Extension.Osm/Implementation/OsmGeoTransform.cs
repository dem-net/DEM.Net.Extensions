//
// OsmGeoTransform.cs
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
using DEM.Net.Core;

namespace DEM.Net.Extension.Osm
{

    public delegate IEnumerable<GeoPoint> OsmGeoTransform(IEnumerable<GeoPoint> geoPoints);

    public static class OsmGeoTransformations
    {
        public static OsmGeoTransform Create(BoundingBox bbox, float zScale, int outputSrid, bool centerOnOrigin)
        {
            return new OsmGeoTransform(pts =>
            {
                pts = pts.ReprojectTo(4326, outputSrid);

                if (centerOnOrigin)
                {
                    pts = pts.CenterOnOrigin(bbox.ReprojectTo(bbox.SRID, outputSrid));
                }

                pts = pts.ZScale(zScale);

                return pts;
            });
        }
    }
}
