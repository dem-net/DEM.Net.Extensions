﻿using DEM.Net.Core;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;

namespace DEM.Net.Extension.Osm.Buildings
{
    public class BuildingModel : CommonModel
    {
       public List<GeoPoint> ExteriorRing { get; set; }

        public List<List<GeoPoint>> InteriorRings { get; set; }

        public IEnumerable<GeoPoint> Points
        {
            get
            {
                return ExteriorRing.Concat(this.InteriorRings == null ? Enumerable.Empty<GeoPoint>() : this.InteriorRings.SelectMany(r => r));
            }
        }



        public BuildingModel(List<GeoPoint> exteriorRingPoints, List<List<GeoPoint>> interiorRings)
        {
            this.ExteriorRing = exteriorRingPoints;
            this.InteriorRings = interiorRings ?? new List<List<GeoPoint>>();
        }


        // building:levels
        // height
        // min_height
        public int? Levels { get; set; }
        public double? MinHeight { get; set; }
        public double? Height { get; set; }

        public double? ComputedFloorAltitude { get; set; }
        public double? ComputedRoofAltitude { get; set; }
        public bool HasHeightInformation { get; set; }
        public bool IsPart { get; set; }
        public Vector4? Color { get; set; }
        public Vector4? RoofColor { get; set; }
        public BuildingModel Parent { get; internal set; }

        public override string ToString()
        {
            string outStr = string.Concat(IsPart ? "part" : "", " "
                , Id, " "
                , "Height: ", Height, " "
                , "Levels: ", Levels, " "
                , "MinHeight: ", MinHeight, " ");
            return outStr;
        }
    }
}
