using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DEM.Net.Core;
using DEM.Net.Extension.Osm.Buildings;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Xunit;

namespace DEM.Net.Extension.Tests;

public class RoofPriorTests
{
    const int Strata = 5 * 5 * 2, Kinds = 5;

    // odds in RoofPrior.Kinds order: flat, gabled, hipped, pyramidal, skillion
    static byte[] Odds(params byte[] k) => [.. Enumerable.Range(0, Strata).SelectMany(_ => k)];

    /// <summary>
    /// A small prior: climate grid of 90° cells (class 1 in the north-east quarter, 2 elsewhere, 0 never used);
    /// a 1° cell around (6.5, 45.5) all gabled; a 5° cell around (2.5, 47.5) all hipped, with one stratum all flat.
    /// </summary>
    static string WritePrior()
    {
        string path = Path.Combine(Path.GetTempPath(), $"roof-prior-{Guid.NewGuid():N}.bin");
        using (var f = new GZipStream(File.Create(path), CompressionLevel.Fastest))
        using (var w = new BinaryWriter(f, Encoding.ASCII))
        {
            w.Write("DRP1"u8.ToArray());
            w.Write(new byte[] { 5, 5, 2, Kinds });
            w.Write((ushort)4); w.Write((ushort)2); w.Write(90f);
            w.Write(new byte[] { 2, 2, 2, 1, 2, 2, 2, 2 }); // rows from the north
            w.Write((byte)3);
            w.Write(Odds(255, 0, 0, 0, 0));     // class 0
            w.Write(Odds(0, 0, 0, 0, 255));     // class 1: skillion
            w.Write(Odds(0, 0, 0, 255, 0));     // class 2: pyramidal
            w.Write((byte)2);
            // 1°: cell of (6.5, 45.5): x = 186, y = 90 - 46 = 44
            w.Write(1f); w.Write(1u);
            w.Write((uint)(44 * 360 + 186)); w.Write(Odds(0, 255, 0, 0, 0));
            // 5°: cell of (2.5, 47.5): x = 36, y = (90 - 50) / 5 = 8
            w.Write(5f); w.Write(1u);
            w.Write((uint)(8 * 72 + 36));
            var five = Odds(0, 0, 255, 0, 0);
            Array.Copy(new byte[] { 255, 0, 0, 0, 0 }, 0, five, RoofPrior.Stratum(2000, 50, true) * Kinds, Kinds);
            w.Write(five);
        }
        return path;
    }

    [Theory]
    [InlineData(40, null, false, 0)]
    [InlineData(100, 6.0, false, (1 * 5 + 1) * 2)]
    [InlineData(299, 15.0, true, (2 * 5 + 2) * 2 + 1)]
    [InlineData(300, 31.0, false, (3 * 5 + 4) * 2)]
    [InlineData(5000, 20.0, true, (4 * 5 + 3) * 2 + 1)]
    public void StrataFollowTheBuilder(double area, double? height, bool nonRes, int expected)
        => Assert.Equal(expected, RoofPrior.Stratum(area, height, nonRes));

    [Fact]
    public void PitchedRoofChance_FollowsTheBuilderRules()
    {
        string path = WritePrior();
        try
        {
            var prior = RoofPrior.Load(path);
            static Dictionary<string, object> Tags(params (string, object)[] t) => t.ToDictionary(x => x.Item1, x => x.Item2);
            double Chance(RoofPrior? p, Dictionary<string, object> tags, double area = 120, bool holes = false, double lon = 6.5, double lat = 45.5)
                => OsmBuildingProcessor.PitchedRoofChance(p, tags, lon, lat, area, holes);

            // the prior's odds: an all gabled cell, and its all flat tower stratum elsewhere
            Assert.Equal(1, Chance(prior, Tags(("building", "house"))));
            Assert.Equal(0, Chance(prior, Tags(("building", "commercial"), ("height", "50")), area: 2000, lon: 2.5, lat: 47.5));
            Assert.Equal(1, Chance(prior, Tags(("building", "commercial"), ("height", "50")), area: 2000)); // all gabled cell
            // mapped shapes first, then the flat cases
            Assert.Equal(0, Chance(prior, Tags(("roof:shape", "flat"))));
            Assert.Equal(1, Chance(prior, Tags(("roof:shape", "hipped")), holes: true));
            Assert.Equal(0, Chance(prior, Tags(("building:part", "yes"))));
            Assert.Equal(0, Chance(prior, Tags(("building", "house")), holes: true));
            Assert.Equal(0, Chance(prior, Tags(("min_height", 3.0))));
            Assert.Equal(0, Chance(prior, Tags(("building:min_level", 2))));

            // without a prior: the default gabled roof on small, low houses
            Assert.Equal(1, Chance(null, Tags(("building", "house"))));
            Assert.Equal(0, Chance(null, Tags(("building", "house")), area: 400));
            Assert.Equal(0, Chance(null, Tags(("building", "warehouse"))));
            Assert.Equal(0, Chance(null, Tags(("height", 20.0))));
            Assert.Equal(1, Chance(null, Tags(("height", 20.0), ("dem:height_estimated", "yes"))));
            Assert.Equal(0, Chance(null, Tags(("building:levels", 4))));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FinestCellFirstThenCoarserThenClimate()
    {
        string path = WritePrior();
        try
        {
            var prior = RoofPrior.Load(path);
            int house = RoofPrior.Stratum(120, 7, false);
            Assert.Equal(RoofKind.Gabled, prior.Choose(6.5, 45.5, house, 12345));       // 1° cell
            Assert.Equal(RoofKind.Hipped, prior.Choose(2.5, 47.5, house, 12345));       // 5° cell
            Assert.Equal(RoofKind.Flat, prior.Choose(2.5, 47.5, RoofPrior.Stratum(2000, 50, true), 1)); // its tower stratum
            Assert.Equal(RoofKind.Skillion, prior.Choose(120, 45, house, 12345));       // climate class 1 (north-east quarter)
            Assert.Equal(RoofKind.Pyramidal, prior.Choose(-60, -10, house, 12345));     // climate class 2
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ChoicesFollowTheOddsAndAreStable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"roof-prior-{Guid.NewGuid():N}.bin");
        using (var f = new GZipStream(File.Create(path), CompressionLevel.Fastest))
        using (var w = new BinaryWriter(f))
        {
            w.Write("DRP1"u8.ToArray()); w.Write(new byte[] { 5, 5, 2, Kinds });
            w.Write((ushort)1); w.Write((ushort)1); w.Write(360f); w.Write((byte)1);
            w.Write((byte)2);
            w.Write(Odds(255, 0, 0, 0, 0));
            w.Write(Odds(51, 153, 51, 0, 0)); // 20 % flat, 60 % gabled, 20 % hipped
            w.Write((byte)0);
        }
        try
        {
            var prior = RoofPrior.Load(path);
            var counts = Enumerable.Range(0, 10000)
                .GroupBy(i => prior.Choose(0, 0, 0, RoofPrior.Hash($"w{1000000 + i}")))
                .ToDictionary(g => g.Key, g => g.Count());
            Assert.InRange(counts[RoofKind.Gabled], 5700, 6300);
            Assert.InRange(counts[RoofKind.Flat], 1700, 2300);
            Assert.InRange(counts[RoofKind.Hipped], 1700, 2300);
            Assert.Equal(RoofPrior.Hash("w123"), RoofPrior.Hash("w123"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsOtherFiles()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var f = new GZipStream(File.Create(path), CompressionLevel.Fastest)) f.Write("NOPE...."u8);
            Assert.Throws<InvalidDataException>(() => RoofPrior.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UntaggedBuildingsTakeTheirRoofFromThePrior()
    {
        string path = WritePrior();
        try
        {
            var processor = new OsmBuildingProcessor(GeoTransformPipeline.Default, false, null, RoofPrior.Load(path));
            processor.Init(null!, null!, new MeshService(), null!, NullLogger.Instance);
            var factory = new GeometryFactory();
            var rect = factory.CreatePolygon([new(0, 0), new(30, 0), new(30, 20), new(0, 20), new(0, 0)]); // 600 m²
            var lShape = factory.CreatePolygon([new(0, 0), new(20, 0), new(20, 5), new(5, 5), new(5, 20), new(0, 20), new(0, 0)]);

            var house = new BuildingModel([], null) { Id = "w1", Centre = (6.5, 45.5) };
            Assert.Equal(RoofKind.Gabled, processor.DefaultRoof(house, rect, 1)); // no 300 m² limit with a prior
            Assert.Equal(RoofKind.Flat, processor.DefaultRoof(house, lShape, 1));  // not rectangular enough
            Assert.Equal(RoofKind.Flat, processor.DefaultRoof(new BuildingModel([], null) { Id = "w1", Centre = (6.5, 45.5), IsPart = true }, rect, 1));
            Assert.Equal(RoofKind.Hipped, processor.DefaultRoof(new BuildingModel([], null) { Id = "w2", Centre = (2.5, 47.5) }, rect, 1));
        }
        finally { File.Delete(path); }
    }
}
