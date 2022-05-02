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

namespace DEM.Net.Extension.Osm
{
#if NET5_0
    using OpenStreetMapDotNet.VectorTiles;
    public class TiledFlatGeobufDataService : IOsmDataService
    {
        private readonly ILogger<TiledFlatGeobufDataService> _logger;
        const int TILE_ZOOM_LEVEL = 10;
        const int TILE_SIZE = 256;

        public TiledFlatGeobufDataService(ILogger<TiledFlatGeobufDataService> logger)
        {
            this._logger = logger;
        }

        public IEnumerable<IFeature> GetOsmDataAsGeoJson(BoundingBox bbox, IOsmDataSettings filter)
        {
            FeatureCollection fc = new FeatureCollection();
            Envelope envelope = new Envelope(bbox.xMin, bbox.xMax, bbox.yMin, bbox.yMax);

            Stopwatch sw = Stopwatch.StartNew();
            int numFeatures = 0;
            int numInside = 0;
            HashSet<long> idsYielded = new HashSet<long>();
            foreach (IFeature feature in EnumerateOsmDataAsGeoJson(bbox, filter))
            {
                numFeatures++;
                if (feature.Geometry.EnvelopeInternal.Intersects(envelope))
                {
                    long featureId = (long)feature.Attributes["osmid"];
                    numInside++;
                    if (!idsYielded.Contains(featureId))
                    {
                        idsYielded.Add(featureId);                        
                        yield return feature;
                    }
                }
            }

            _logger.LogInformation($"Read {numFeatures:N0}, {numInside:N0} inside, {idsYielded.Count} unique, in {sw.ElapsedMilliseconds:N} ms");

        }

        private IEnumerable<IFeature> EnumerateOsmDataAsGeoJson(BoundingBox bbox, IOsmDataSettings filter)
        {
            string directoryName = Path.Combine(filter.FlatGeobufTilesDirectory, filter.FilterIdentifier);

            var tiles = TileUtils.GetTilesInBoundingBox(bbox, TILE_ZOOM_LEVEL, TILE_SIZE)
                                .Select(tile => new OpenStreetMapDotNet.MapTileInfo(tile.X, tile.Y, tile.Zoom, tile.TileSize))
                                .Where(tile => FlatGeoBufFileSystem.FileExists(tile, directoryName))
                                .ToList();

            if (tiles.Count == 0)
            {
                throw new Exception($"All required tiles are missing");
            }

            int i = 0;
            foreach (var tile in tiles)
            {

                _logger.LogInformation($"Reading tiles from {directoryName}... {(++i / (float)tiles.Count):P1}");

                var osmTileInfo = new OpenStreetMapDotNet.MapTileInfo(tile.X, tile.Y, tile.Zoom, tile.TileSize);
                FlatGeobufTileReader reader = new FlatGeobufTileReader(osmTileInfo, directoryName);

                foreach (IFeature way in reader.GetEnumerator())
                {
                    yield return way;
                }
            }

        }

        public int GetOsmDataCount(BoundingBox bbox, IOsmDataSettings filter)
        {
            return EnumerateOsmDataAsGeoJson(bbox, filter).Count();
        }



    }

#endif
}
