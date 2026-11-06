using System.Security.Cryptography;
using System.Text.Json;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Fingerprints used by the split-data golden check: a document's canonical text (compact JSON, number text as written,
/// nested key order kept; only the top-level tables are sorted, since the split files group them by topic) and the
/// state hash of a scripted game.
/// </summary>
internal static class GoldenData
{
    public static string DocumentHash(JsonElement root)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var p in root.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }
            w.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
    }

    /// <summary>A landed colony on seed 1701 with a crawler mining and the rovers driving, plus the star-system skirmish.</summary>
    public static ulong ScriptedGameHash(GameData data)
    {
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var map = sim.Surface!.Map;
        var site = Enumerable.Range(0, map.Grid.Count).First(t => sim.Surface.CanLand(0, t)
            && map.Grid.Within(t, data.Landing.RevealRadius).Any(n => n != t && map.DepositResource[n] >= 0 && map.Terrain(n).Passable));
        var deposit = map.Grid.Within(site, data.Landing.RevealRadius).First(n => n != site && map.DepositResource[n] >= 0 && map.Terrain(n).Passable);
        sim.Schedule(0, new LandColonyCommand(0, site));
        var ready = (data.Landing.DescentSeconds + data.Landing.AssembleSeconds) * Simulation.TickRate + 1;
        for (var i = 0; i < ready; i++) sim.Step();
        var crawler = sim.Surface.Units.First(u => data.SurfaceUnit(u.DefId).CanMine);
        var rovers = sim.Surface.Units.Where(u => u != crawler).Select(u => u.Id).ToList();
        sim.Schedule(sim.Tick, new MineCommand(0, crawler.Id, deposit));
        var far = map.Grid.Within(site, 6).Last(t => map.Terrain(t).Passable);
        sim.Schedule(sim.Tick, new MoveUnitsCommand(0, rovers, far));
        ulong hash = 0;
        for (var i = 0; i < Simulation.TickRate * 240; i++)
        {
            sim.Step();
            if (i % 300 == 0) hash = hash * 31 + sim.StateHash();
        }
        hash = hash * 31 + sim.StateHash();

        var sky = MatchSetup.CreateMvpSkirmish(data, 99);
        var fed = sky.Ships.Where(s => s.Owner == 0).Select(s => s.Id).ToArray();
        sky.Schedule(3, new MoveCommand(0, fed, FixVec2.FromInts(50, 40)));
        for (var i = 0; i < 600; i++) sky.Step();
        return hash * 31 + sky.StateHash();
    }
}

/// <summary>
/// The split (2026-11-06, tools/split_data.py) must not change the data: the 13 documents assembled from the small
/// files hash the same as the flat files did (Golden/presplit.json, written from the old files before the split), and
/// a scripted game gives the same state hash. Once anyone edits the data on purpose the snapshot no longer applies:
/// the test then only checks that every document still assembles without problems.
/// </summary>
public class SplitGoldenTests
{
    private static JsonElement Snapshot()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "presplit.json")));
        return doc.RootElement.Clone();
    }

    /// <summary>A fingerprint of every document file (not the manifest or layers), to tell whether the data was edited since the split.</summary>
    public static string DocumentFilesFingerprint(IReadOnlyDictionary<string, string> files, DataManifest manifest) =>
        DataFingerprint.Of(manifest.Documents.SelectMany(manifest.PartsOf).ToDictionary(p => p, p => files[p]));

    [Fact]
    public void SplitDataIsTheSameAsBeforeTheSplit()
    {
        var problems = new List<string>();
        var files = DataStore.ReadBase(TestData.Source, problems);
        Assert.Empty(problems);
        var manifest = DataManifest.Parse(files[DataManifest.FileName]);
        var assembled = manifest.Documents.ToDictionary(d => d, d => manifest.Assemble(files, d, problems));
        Assert.Empty(problems);
        var snap = Snapshot();
        Assert.Equal(snap.GetProperty("documents").EnumerateObject().Select(p => p.Name).OrderBy(n => n), manifest.Documents.OrderBy(n => n));
        if (DocumentFilesFingerprint(files, manifest) != snap.GetProperty("split_fingerprint").GetString()) return; // edited since the split
        foreach (var p in snap.GetProperty("documents").EnumerateObject())
            Assert.True(p.Value.GetString() == GoldenData.DocumentHash(assembled[p.Name]), $"{p.Name} differs from the pre-split file");
        Assert.Equal(snap.GetProperty("scripted_game").GetString(), GoldenData.ScriptedGameHash(TestData.Real).ToString());
    }
}
