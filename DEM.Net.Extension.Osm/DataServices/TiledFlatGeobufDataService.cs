using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using DEM.Net.Core;
using DEM.Net.Core.Imagery;
using DEM.Net.Extension.Osm.Model;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OpenStreetMapDotNet.VectorTiles;

namespace DEM.Net.Extension.Osm
{
    public class TiledFlatGeobufDataService : IOsmDataService
    {
        private readonly ILogger<TiledFlatGeobufDataService> _logger;
        const int TILE_ZOOM_LEVEL = 10;
        const int TILE_SIZE = 256;

        public TiledFlatGeobufDataService(ILogger<TiledFlatGeobufDataService> logger)
        {
            this._logger = logger;
        }

        public FeatureCollection GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataSettings filter)
        {
            FeatureCollection fc = new FeatureCollection();
            Envelope envelope = new Envelope(bbox.xMin, bbox.xMax, bbox.yMin, bbox.yMax);

            Stopwatch sw = Stopwatch.StartNew();
            int numFeatures = 0;
            int numInside = 0;
            try
            {
                foreach (IFeature feature in EnumerateOsmDataAsGeoJson(bbox, filter))
                {
                    numFeatures++;
                    if (feature.Geometry.EnvelopeInternal.Intersects(envelope))
                    {
                        numInside++;
                        fc.Add(feature);
                    }
                }

                _logger.LogInformation($"Read {numFeatures:N0}, {numInside:N0} inside, in {sw.ElapsedMilliseconds:N} ms");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{nameof(GetOsmDataAsGeoJson)} error: {ex.Message}");
                throw;
            }
            return fc;
        }

        private IEnumerable<IFeature> EnumerateOsmDataAsGeoJson(BoundingBox bbox, IOsmDataSettings filter)
        {
            var tiles = TileUtils.GetTilesInBoundingBox(bbox, TILE_ZOOM_LEVEL, TILE_SIZE).ToList();
            int i = 0;
            foreach (var tile in tiles)
            {

                _logger.LogInformation($"Reading tiles from {filter.FlatGeobufTilesDirectory}... {(i / (float)tiles.Count):P1}");

                var osmTileInfo = new OpenStreetMapDotNet.MapTileInfo(tile.X, tile.Y, tile.Zoom, tile.TileSize);
                if (!FlatGeobufTileReader.FileExists(osmTileInfo, filter.FlatGeobufTilesDirectory))
                {
                    _logger.LogWarning($"Missing tile in {filter.FlatGeobufTilesDirectory} : {osmTileInfo}");
                    continue;
                }
                FlatGeobufTileReader reader = new FlatGeobufTileReader(osmTileInfo, filter.FlatGeobufTilesDirectory);

                foreach (IFeature way in reader.GetEnumerator())
                {
                    yield return way;
                }
                i++;
            }

        }

        public int GetOsmDataCount(BoundingBox bbox, IOsmDataSettings filter)
        {
            return EnumerateOsmDataAsGeoJson(bbox, filter).Count();
        }



    }
}
