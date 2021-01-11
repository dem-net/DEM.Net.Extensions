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
    public class OsmServiceOverpassAPI : IOsmDataService
    {
        private readonly ILogger<OsmServiceOverpassAPI> _logger;

        public OsmServiceOverpassAPI(ILogger<OsmServiceOverpassAPI> logger)
        {
            this._logger = logger;
        }

        public FeatureCollection GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataFilter filter)
        {
            try
            {
                using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(GetOsmDataAsGeoJson), _logger, LogLevel.Debug))
                {
                    OverpassQuery query = new OverpassQuery(bbox, _logger);
                    if (filter != null)
                    {
                        if (filter.WaysFilter != null) foreach (var tagFilter in filter.WaysFilter) query.WithWays(tagFilter);
                        if (filter.RelationsFilter != null) foreach (var tagFilter in filter.RelationsFilter) query.WithRelations(tagFilter);
                        if (filter.NodesFilter != null) foreach (var tagFilter in filter.NodesFilter) query.WithNodes(tagFilter);
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
        //public FeatureCollection GetOsmDataAsGeoJson(BoundingBox bbox, string fullQueryBody)
        //{
        //    try
        //    {
        //        using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(GetOsmDataAsGeoJson), _logger, LogLevel.Debug))
        //        {
        //            var task = new OverpassQuery(bbox, _logger)
        //                .RunQueryQLAsync(fullQueryBody)
        //                .ToGeoJSONAsync();

        //            FeatureCollection ways = task.GetAwaiter().GetResult();

        //            _logger.LogInformation($"{ways?.Features?.Count} features downloaded");

        //            return ways;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"{nameof(GetOsmDataAsGeoJson)} error: {ex.Message}");
        //        throw;
        //    }

        //}

        public int GetOsmDataCount(BoundingBox bbox, IOsmDataFilter filter)
        {
            try
            {
                using (TimeSpanBlock timeSpanBlock = new TimeSpanBlock(nameof(GetOsmDataAsGeoJson), _logger, LogLevel.Debug))
                {
                    OverpassQuery query = new OverpassQuery(bbox, _logger);
                    if (filter != null)
                    {
                        if (filter.WaysFilter != null) foreach (var tagFilter in filter.WaysFilter) query.WithWays(tagFilter);
                        if (filter.RelationsFilter != null) foreach (var tagFilter in filter.RelationsFilter) query.WithRelations(tagFilter);
                        if (filter.NodesFilter != null) foreach (var tagFilter in filter.NodesFilter) query.WithNodes(tagFilter);
                    }

                    var task = query.AsCount()
                        .RunQueryAsync()
                        .ToCountAsync();

                    OverpassCountResult count = task.GetAwaiter().GetResult();

                    return count.Tags.Nodes * 2;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetOsmDataAsGeoJson)} error: {ex.Message}");
                throw;
            }

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
                $"{p.Longitude.ToString("F7", CultureInfo.InvariantCulture)} {p.Latitude.ToString("F7", CultureInfo.InvariantCulture)}")) + ")"))
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
