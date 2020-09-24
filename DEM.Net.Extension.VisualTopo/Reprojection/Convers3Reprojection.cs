using ConversApi;
using DEM.Net.Core;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DEM.Net.Extension.VisualTopo
{
    public class Convers3Reprojection
    {
        private readonly Conversion converter;
        public Convers3Reprojection()
        {
            converter = new Conversion();
            converter.ReadXml(Path.Combine("Convers3", "Convers.xml"));
        }

        public void Reproject()
        {
            // point à convertir, défini en WGS84
            Location Depuis = new Location(2.8975941, 43.5435507, "WGS84", Units.DDEC, Meridians.GREENWICH);

            //résultat en LT3
            Location VersLambertCarto3 = new Location(0, 0, "LT3", Units.MT);

            //conversion
            converter.Convert(Depuis, VersLambertCarto3);

            Depuis.Format(converter);
            VersLambertCarto3.Format(converter);
        }

        public GeoPoint CreateGeoPoint(string inputProjectionCode, int outputSrid, double x, double y, double z)
        {
            try
            {
                Reproject();
                int srid = GetSridFromProjectionCode(inputProjectionCode, out double factor);
                string outProjectionCode = GetProjectionCodeFromSrid(outputSrid);

              
                Location from = new Location(x, y, inputProjectionCode, Units.MT); x *= factor;
                y *= factor;
                Location to = new Location(0, 0, outProjectionCode, Units.MT);
                Location towgs = new Location(0, 0, "WGS84", Units.DMS, Meridians.GREENWICH);

                converter.Convert(from, towgs);
                towgs.Format(converter);

                if (NeedsConversApi(inputProjectionCode) || NeedsConversApi(outProjectionCode))
                {
                    converter.Convert(from, to);
                    to.Format(converter);
                    return new GeoPoint(to.YLat, to.XLon);
                }
                else
                {
                    var destPoint = new GeoPoint(y, x, z);
                    return  destPoint.ReprojectTo(srid, outputSrid);                    
                }
            }
            catch
            {
                throw;
            }

        }
        internal bool NeedsConversApi(string projCode)
        {
            switch (projCode)
            {
                case "WGS84":
                case "WebMercator":
                case "UTM31":
                case "LT93":
                    return false;
                case "LT3":
                    return true;
                default: throw new NotImplementedException($"Projection not {projCode} not implemented");
            };
        }
        internal string GetProjectionCodeFromSrid(int srid)
        {
            switch (srid)
            {
                case 32631: return "UTM31";
                case 27573: return "LT3";
                case 4326: return "WGS84";
                case 3857: return "WebMercator";
                case 2154: return "LT93";
                default: throw new NotImplementedException($"Projection not {srid} not implemented in GetProjectionCodeFromSrid");
            };
        }

        internal int GetSridFromProjectionCode(string projectionCode, out double multiplicationFactor)
        {
            switch (projectionCode)
            {
                case "UTM31": multiplicationFactor = 1000d; return 32631;
                case "LT3": multiplicationFactor = 1000d; return 27573;
                case "WGS84": multiplicationFactor = 1d; return Reprojection.SRID_GEODETIC;
                case "WebMercator": multiplicationFactor = 1d; return Reprojection.SRID_PROJECTED_MERCATOR;
                case "LT93": multiplicationFactor = 1000d; return Reprojection.SRID_PROJECTED_LAMBERT_93;
                default: throw new NotImplementedException($"Projection not {projectionCode} not implemented");
            };
        }
    }
}
