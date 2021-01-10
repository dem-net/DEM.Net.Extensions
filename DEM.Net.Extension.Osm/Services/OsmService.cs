using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DEM.Net.Core;
using DEM.Net.Extension.Osm.Model;
using DEM.Net.Extension.Osm.OverpassAPI;
using GeoJSON.Net.Feature;
using Microsoft.Extensions.Logging;

namespace DEM.Net.Extension.Osm
{
    public class OsmService
    {
        private readonly ILogger<OsmService> _logger;

        public OsmService(ILogger<OsmService> logger)
        {
            this._logger = logger;
        }

        public FeatureCollection GetOsmDataAsGeoJson(BoundingBox bbox, Func<OverpassQuery, OverpassQuery> filter = null)
        {
            try
            {
                using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(GetOsmDataAsGeoJson), _logger, LogLevel.Debug))
                {
                    OverpassQuery query = new OverpassQuery(bbox, _logger);
                    if (filter != null)
                    {
                        query = filter(query);
                    }

                    var task = query.ToGeoJSONAsync();

                    FeatureCollection ways = task.GetAwaiter().GetResult();

                    _logger.LogInformation($"{ways?.Features?.Count} features downloaded");

                    return ways;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetOsmDataAsGeoJson)} error: {ex.Message}");
                throw;
            }

        }
        public FeatureCollection GetOsmDataAsGeoJson(BoundingBox bbox, string fullQueryBody)
        {
            try
            {
                using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(GetOsmDataAsGeoJson), _logger, LogLevel.Debug))
                {
                    var task = new OverpassQuery(bbox, _logger)
                        .RunQueryQLAsync(fullQueryBody)
                        .ToGeoJSONAsync();

                    FeatureCollection ways = task.GetAwaiter().GetResult();

                    _logger.LogInformation($"{ways?.Features?.Count} features downloaded");

                    return ways;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetOsmDataAsGeoJson)} error: {ex.Message}");
                throw;
            }

        }
        //public OverpassCountResult GetOsmDataCount(BoundingBox bbox, string fullQueryBody)
        //{
        //    try
        //    {
        //        using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(GetOsmDataAsGeoJson), _logger, LogLevel.Debug))
        //        {
        //            var task = new OverpassQuery(bbox).AsCount()
        //                .RunQueryQL(fullQueryBody)
        //                .ToCount();

        //            OverpassCountResult count = task.GetAwaiter().GetResult();

        //            return count;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"{nameof(GetOsmDataAsGeoJson)} error: {ex.Message}");
        //        throw;
        //    }

        //}
        public OverpassCountResult GetOsmDataCount(BoundingBox bbox, OverpassQuery query)
        {
            try
            {
                using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(GetOsmDataAsGeoJson), _logger, LogLevel.Debug))
                {
                    var task = query.AsCount()
                        .RunQueryAsync()
                        .ToCountAsync();

                    OverpassCountResult count = task.GetAwaiter().GetResult();

                    return count;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetOsmDataAsGeoJson)} error: {ex.Message}");
                throw;
            }

        }

        public OsmModelList<T> CreateModelsFromGeoJson<T>(FeatureCollection features, OsmModelFactory<T> validator) where T : CommonModel
        {

            OsmModelList<T> models = new OsmModelList<T>(features.Features.Count);
            using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(CreateModelsFromGeoJson), _logger, LogLevel.Debug))
            {
                int count = 0;
                foreach (var feature in features.Features)
                {
                    count++;
                    validator.RegisterTags(feature);
                    T model = validator.CreateModel(feature);

                    if (model == null)
                    {
                        _logger.LogWarning($"{nameof(CreateModelsFromGeoJson)}: {feature.Id}, type {feature.Geometry.Type} not supported.");
                    }
                    else if (validator.ParseTags(model)) // Model not processed further if tag parsing fails
                    {
                        models.Add(model);
                    }
                }
            }

            //#if DEBUG
            //            File.WriteAllText($"{typeof(T).Name}_osm_tag_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt", validator.GetTagsReport(), Encoding.UTF8);
            //#endif

            _logger.LogInformation($"{nameof(CreateModelsFromGeoJson)} done for {validator._totalPoints} points.");

            models.TotalPoints = validator._totalPoints;

            return models;

        }

        public string ConvertOsmosisPolyToWkt(Stream osmosisPOLY)
        {
            try
            {


                List<List<GeoPoint>> polyParts = new List<List<GeoPoint>>();
                using (StreamReader sr = new StreamReader(osmosisPOLY))
                {
                    // skip 2 first lines (polyname and poly part)
                    sr.ReadLine(); sr.ReadLine();
                    List<GeoPoint> part = new List<GeoPoint>();
                    var separators = new[] { ' ' };
                    var line = sr.ReadLine();
                    while (!sr.EndOfStream)
                    {
                        if (line.Equals("END", StringComparison.OrdinalIgnoreCase))
                        {
                            polyParts.Add(part);
                            part = new List<GeoPoint>();
                        }
                        else
                        {
                            var coords = line.Split(separators, StringSplitOptions.RemoveEmptyEntries).Select(s => decimal.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                            part.Add(new GeoPoint((double)coords[1], (double)coords[0]));
                        }
                        line = sr.ReadLine();
                    }
                }
                

                return "MULTIPOLYGON((" + string.Join("", polyParts.ReverseAndReturn().Select(part => "(" + string.Join(", ", part.ReverseAndReturn().Select(p =>  
                $"{p.Longitude.ToString("F7",CultureInfo.InvariantCulture)} {p.Latitude.ToString("F7", CultureInfo.InvariantCulture)}")) + ")"))
                + "))";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error converting POLY to WKT: {ex.Message}");
            }
            return null;

        }

    }
}
