using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DEM.Net.Extension.Osm.Buildings;

/// <summary>
/// Odds of each roof shape by place and kind of building, for buildings without a mapped <c>roof:shape</c>.
/// Built from the roof shapes mapped in OpenStreetMap (through Overture) by <c>tools/roof-prior/build_roof_prior.py</c>,
/// which documents the file format. Cells with few mapped roofs fall back to coarser cells, then to their climate
/// zone (Köppen-Geiger, Beck et al. 2023). A lookup is a few array reads.
/// </summary>
public sealed class RoofPrior
{
    /// <summary>The order of the odds in the file.</summary>
    public static readonly RoofKind[] Kinds = [RoofKind.Flat, RoofKind.Gabled, RoofKind.Hipped, RoofKind.Pyramidal, RoofKind.Skillion];

    /// <summary>Building types counted as non-residential (same list as the builder).</summary>
    public static readonly FrozenSet<string> NonResidentialTypes = new[]
    {
        "commercial", "industrial", "retail", "warehouse", "office", "supermarket", "service", "garage", "garages",
        "parking", "carport", "hangar", "factory", "manufacture", "hospital", "school", "university", "college",
        "train_station", "transportation", "stadium",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private const int Sizes = 5, Heights = 5, Uses = 2;
    private readonly int _kinds, _strata;

    private readonly int _climateCols, _climateRows;
    private readonly double _climateDeg;
    private readonly byte[] _climate;
    private readonly byte[] _classOdds; // classes x strata x kinds

    private readonly Level[] _levels; // finest first

    private sealed record Level(double Degrees, int Cols, Dictionary<uint, int> Offsets, byte[] Odds);

    private RoofPrior(int kinds, int climateCols, int climateRows, double climateDeg, byte[] climate, byte[] classOdds, Level[] levels)
    {
        _kinds = kinds;
        _strata = Sizes * Heights * Uses;
        (_climateCols, _climateRows, _climateDeg, _climate, _classOdds, _levels) = (climateCols, climateRows, climateDeg, climate, classOdds, levels);
    }

    public static RoofPrior Load(string path)
    {
        using var file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var r = new BinaryReader(new BufferedStream(gz), Encoding.ASCII);

        if (new string(r.ReadChars(4)) != "DRP1") throw new InvalidDataException($"{path} is not a roof prior (DRP1).");
        int sizes = r.ReadByte(), heights = r.ReadByte(), uses = r.ReadByte(), kinds = r.ReadByte();
        if (sizes != Sizes || heights != Heights || uses != Uses || kinds != Kinds.Length)
            throw new InvalidDataException($"{path}: unexpected strata ({sizes} x {heights} x {uses}, {kinds} kinds).");
        int strataBytes = sizes * heights * uses * kinds;

        int cols = r.ReadUInt16(), rows = r.ReadUInt16();
        double deg = r.ReadSingle();
        byte[] climate = r.ReadBytes(cols * rows);
        int classes = r.ReadByte();
        byte[] classOdds = r.ReadBytes(classes * strataBytes);

        int levelCount = r.ReadByte();
        var levels = new Level[levelCount];
        for (int l = 0; l < levelCount; l++)
        {
            double ldeg = r.ReadSingle();
            int count = checked((int)r.ReadUInt32());
            var offsets = new Dictionary<uint, int>(count);
            var odds = new byte[count * strataBytes];
            for (int i = 0; i < count; i++)
            {
                offsets[r.ReadUInt32()] = i * strataBytes;
                byte[] chunk = r.ReadBytes(strataBytes);
                if (chunk.Length != strataBytes) throw new EndOfStreamException($"{path} is truncated.");
                chunk.CopyTo(odds, i * strataBytes);
            }
            levels[l] = new Level(ldeg, (int)Math.Round(360 / ldeg), offsets, odds);
        }
        return new RoofPrior(kinds, cols, rows, deg, climate, classOdds, levels);
    }

    /// <summary>Stratum index: footprint size, height (height, else floors × 3; null = unknown) and use.</summary>
    public static int Stratum(double areaM2, double? heightM, bool nonResidential)
    {
        int size = areaM2 < 50 ? 0 : areaM2 < 150 ? 1 : areaM2 < 300 ? 2 : areaM2 < 1000 ? 3 : 4;
        int height = heightM is not double h ? 0 : h <= 8 ? 1 : h <= 15 ? 2 : h <= 30 ? 3 : 4;
        return (size * Heights + height) * Uses + (nonResidential ? 1 : 0);
    }

    /// <summary>The odds (0–255 each, in <see cref="Kinds"/> order) at a place for a stratum.</summary>
    public ReadOnlySpan<byte> Odds(double lon, double lat, int stratum)
    {
        if ((uint)stratum >= (uint)_strata) throw new ArgumentOutOfRangeException(nameof(stratum));
        int stride = _strata * _kinds;
        foreach (var level in _levels)
        {
            uint index = CellIndex(lon, lat, level.Degrees, level.Cols);
            if (level.Offsets.TryGetValue(index, out int offset))
                return level.Odds.AsSpan(offset + stratum * _kinds, _kinds);
        }
        int cls = _climate[CellIndex(lon, lat, _climateDeg, _climateCols, _climateRows)];
        if ((cls + 1) * stride > _classOdds.Length) cls = 0;
        return _classOdds.AsSpan(cls * stride + stratum * _kinds, _kinds);
    }

    /// <summary>
    /// A roof shape drawn from the odds with a stable per-building number, so a village gets a realistic mix and a
    /// building gets the same roof every time.
    /// </summary>
    public RoofKind Choose(double lon, double lat, int stratum, ulong buildingHash)
    {
        var odds = Odds(lon, lat, stratum);
        int total = 0;
        foreach (byte o in odds) total += o;
        if (total == 0) return RoofKind.Flat;
        int pick = (int)(buildingHash % (ulong)total);
        for (int k = 0; k < odds.Length; k++)
        {
            pick -= odds[k];
            if (pick < 0) return Kinds[k];
        }
        return Kinds[^1];
    }

    /// <summary>FNV-1a of a building id: stable across processes (string.GetHashCode isn't).</summary>
    public static ulong Hash(string? id)
    {
        ulong h = 14695981039346656037;
        foreach (char c in id ?? "")
        {
            h ^= c;
            h *= 1099511628211;
        }
        // mix the bits: consecutive ids differ in their last characters only
        h ^= h >> 33; h *= 0xff51afd7ed558ccd; h ^= h >> 33;
        return h;
    }

    private static uint CellIndex(double lon, double lat, double deg, int cols, int rows = 0)
    {
        rows = rows > 0 ? rows : (int)Math.Round(180 / deg);
        int x = Math.Clamp((int)Math.Floor((lon + 180) / deg), 0, cols - 1);
        int y = Math.Clamp((int)Math.Floor((90 - lat) / deg), 0, rows - 1); // rows from the north
        return (uint)(y * cols + x);
    }
}

/// <summary>Loads the roof prior once (<see cref="OsmElevationOptions.RoofPriorFilePath"/>); null when not configured.</summary>
public sealed class RoofPriorProvider(IOptions<OsmElevationOptions> options, ILogger<RoofPriorProvider> logger)
{
    private readonly Lazy<RoofPrior?> _prior = new(() =>
    {
        string? path = options.Value.RoofPriorFilePath;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var prior = RoofPrior.Load(path);
        logger.LogInformation("Roof prior loaded from {Path}", path);
        return prior;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public RoofPrior? Prior => _prior.Value;
}
