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
        public static MemoryStream ExportToCsv(this VisualTopoModel model)
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
    }
}