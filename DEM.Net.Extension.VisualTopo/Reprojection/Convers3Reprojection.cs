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
            converter.ReadXml(Path.Combine("ConversApi3", "Convers.xml"));
        }

        public void Reproject(double x, double y)
        {
            // point à convertir, défini en WGS84
            Location Depuis = new Location(2.8975941, 43.5435507, "WGS84", Units.DDEC, Meridians.GREENWICH);
            //Location DepuisLT3 = new Location(x,y, "LT3C", Units.MT);
            Location DepuisUTM31 = new Location(x, y,31,'\0', "UTM.WGS84", Units.MT);

            //résultat en LT3
            Location VersLambertCarto3 = new Location(0, 0, "LT3C", Units.MT);
            Location VersLatLon = new Location(0, 0, "WGS84", Units.DDEC, Meridians.GREENWICH);


            //conversion
            converter.Convert(Depuis, VersLambertCarto3);

            //converter.Convert(DepuisLT3, VersLatLon);
            converter.Convert(DepuisUTM31, VersLatLon);

            Depuis.Format(converter);
            VersLambertCarto3.Format(converter);
        }

        public GeoPoint CreateGeoPoint(string inputProjectionCode, int outputSrid, double x, double y, double z)
        {
            try
            { 
                // HACK: replace LT3 (Lambert III) by LT3C (Lambert III Carto). LT3C files have all LT3 faultly set by legacy software
                inputProjectionCode = inputProjectionCode == "LT3" ? "LT3C" : inputProjectionCode;

                int srid = GetSridFromProjectionCode(ref inputProjectionCode, out double factor, out int zone);
                string outProjectionCode = GetProjectionCodeFromSrid(outputSrid);
                x *= factor;
                y *= factor;

                if (NeedsConversApi(inputProjectionCode) || NeedsConversApi(outProjectionCode))
                {
                    Location from = new Location(x, y, inputProjectionCode, Units.MT);
                    Location to;
                    if (outProjectionCode.StartsWith("UTM"))
                    {
                        to = new Location(0, 0, zone, '\0', "UTM.WGS84", Units.MT);
                    }
                    else
                    {
                        to = new Location(0, 0, outProjectionCode, Units.MT);
                    }


                    converter.Convert(from, to);
                    to.Format(converter);
                    return new GeoPoint(to.YLat, to.XLon);
                }
                else
                {
                    var destPoint = new GeoPoint(y, x, z);
                    return destPoint.ReprojectTo(srid, outputSrid);
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
                case "UTM":
                case "LT93":
                    return false;
                case "LT3":
                case "LT3C":
                    return true;
                default: throw new NotImplementedException($"Projection not {projCode} not implemented");
            };
        }
        internal string GetProjectionCodeFromSrid(int srid)
        {
            switch (srid)
            {
                case 32631: return "UTM";
                case 27573: return "LT3C"; // HACK: replace LT3 (Lambert III) by LT3C (Lambert III Carto). LT3C files have all LT3 faultly set by legacy software
                case 4326: return "WGS84";
                case 3857: return "WebMercator";
                case 2154: return "LT93";
                default: throw new NotImplementedException($"Projection not {srid} not implemented in GetProjectionCodeFromSrid");
            };
        }

        internal int GetSridFromProjectionCode(ref string projectionCode, out double multiplicationFactor, out int zone)
        {
            zone = 0;
            if (projectionCode.StartsWith("UTM"))
            {
                multiplicationFactor = 1000d;
                zone = int.Parse(projectionCode.Replace("UTM", ""));
                projectionCode = "UTM";
                return 32600 + zone;
            }
            else
            {
                switch (projectionCode)
                {
                    case "LT3":
                    case "LT3C": // HACK: replace LT3 (Lambert III) by LT3C (Lambert III Carto). LT3C files have all LT3 faultly set by legacy software
                        multiplicationFactor = 1000d; return 27573;
                    case "WGS84": multiplicationFactor = 1d; return Reprojection.SRID_GEODETIC;
                    case "WebMercator": multiplicationFactor = 1d; return Reprojection.SRID_PROJECTED_MERCATOR;
                    case "LT93": multiplicationFactor = 1000d; return Reprojection.SRID_PROJECTED_LAMBERT_93;
                    default: throw new NotImplementedException($"Projection not {projectionCode} not implemented");
                };
            }
        }
    }
}
