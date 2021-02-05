using DEM.Net.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DEM.Net.Extension.VisualTopo
{
    public static partial class Export
    {
        public static MemoryStream ExportToCsv(this VisualTopoModel model, string separator)
        {
            MemoryStream ms = new MemoryStream();
            using (StreamWriter sw = new StreamWriter(ms))
            {
                sw.WriteLine(string.Join(separator, headers));

                foreach (var set in model.Sets)
                {
                    sw.WriteLine(string.Join(separator, GetSectionHeader(set)));

                    foreach (var data in set.Data)
                    {
                        sw.WriteLine(string.Join(separator,
                             data.Entree,
                        data.Sortie,
                        data.Longueur,
                        data.Cap,
                        data.Pente,
                        data.CutSection.left,
                        data.CutSection.right,
                        data.CutSection.up,
                        data.CutSection.down,
                        data.VectorLocal.X,
                        data.VectorLocal.Y,
                        data.VectorLocal.Z,
                        // Latitude, Long, X,Y
                        data.GeoPointGlobal_DEMProjection?.Latitude,
                        data.GeoPointGlobal_DEMProjection?.Longitude,
                        data.GeoPointGlobal_ProjectedCoords?.Longitude,
                        data.GeoPointGlobal_ProjectedCoords?.Latitude,

                        data.DistanceFromEntry,
                        data.VectorLocal.Z,
                        data.TerrainElevationAbove,
                        data.TerrainElevationAbove - (model.EntryPoint.Elevation ?? 0) - data.VectorLocal.Z,
                        // comment row (red for disconnected node)
                        data.IsDisconnected ? string.Concat("[DISCONNECTED] ", data.Comment) : data.Comment));
                    }
                }
            }
            return ms;
        }
    }
}