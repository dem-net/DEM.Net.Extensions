using ClosedXML.Excel;
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
        public static MemoryStream ExportToExcel(this VisualTopoModel model, bool autoFitColumns = true)
        {
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
    }
}