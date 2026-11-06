using System.Text.Json;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Tests;

/// <summary>Patches on the files' text: each op lands where it should and leaves the rest of the file as written.</summary>
public class DataPatchTests
{
    private const string Manifest = "{ \"documents\": { \"doc\": [\"doc/a.json\", \"doc/b.json\"] } }";

    private const string A = "{\n  \"items\": [\n    { \"id\": \"x\", \"n\": 1 },\n    { \"id\": \"y\", \"n\": 2 }\n  ],\n  \"menu\": [\"x\", \"y\"],\n  \"rules\": { \"speed\": 0.50, \"cost\": { \"duranium\": 5 } }\n}\n";
    private const string B = "{\n  \"items\": [\n    { \"id\": \"z\", \"n\": 3 }\n  ]\n}\n";

    private static Dictionary<string, string> Files() => new()
    {
        [DataManifest.FileName] = Manifest, ["doc/a.json"] = A, ["doc/b.json"] = B,
    };

    private static JsonElement V(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static (Dictionary<string, string> Files, JsonElement Doc) Run(params DataPatch[] patches)
    {
        var files = Files();
        var manifest = DataManifest.Parse(Manifest);
        foreach (var p in patches) Assert.Null(DataPatcher.Apply(files, manifest, p));
        var problems = new List<string>();
        var doc = manifest.Assemble(files, "doc", problems);
        Assert.Empty(problems);
        return (files, doc);
    }

    [Fact]
    public void SetReplacesOnlyThatValue()
    {
        var (files, doc) = Run(new DataPatch("set", "doc", "items/z/n", V("30")), new DataPatch("set", "doc", "rules/speed", V("0.75")));
        Assert.Equal(B.Replace("\"n\": 3", "\"n\": 30"), files["doc/b.json"]);
        Assert.Equal(A.Replace("0.50", "0.75"), files["doc/a.json"]);
        Assert.Equal("0.75", doc.GetProperty("rules").GetProperty("speed").GetRawText()); // number text as written
        Assert.Equal(new[] { "x", "y", "z" }, doc.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public void RecordsAndFieldsAreAddedAndRemovedInTheFilesLayout()
    {
        var (files, doc) = Run(
            new DataPatch("add_record", "doc", "items", V("{ \"id\": \"w\", \"n\": 4 }"), After: "x"),
            new DataPatch("add_record", "doc", "items", V("{ \"id\": \"v\", \"n\": 5 }")),
            new DataPatch("remove", "doc", "items/y"),
            new DataPatch("add_field", "doc", "rules/cost/tritanium", V("2")),
            new DataPatch("remove", "doc", "rules/speed"));
        Assert.Equal("{\n  \"items\": [\n    { \"id\": \"x\", \"n\": 1 },\n    { \"id\": \"w\", \"n\": 4 }\n  ],\n  \"menu\": [\"x\", \"y\"],\n  \"rules\": { \"cost\": { \"duranium\": 5, \"tritanium\": 2 } }\n}\n", files["doc/a.json"]);
        Assert.Equal("{\n  \"items\": [\n    { \"id\": \"z\", \"n\": 3 },\n    { \"id\": \"v\", \"n\": 5 }\n  ]\n}\n", files["doc/b.json"]);
        Assert.Equal(new[] { "x", "w", "z", "v" }, doc.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public void PlainListsTakeAndLoseValues()
    {
        var (files, _) = Run(new DataPatch("list_add", "doc", "menu", V("\"z\"")), new DataPatch("list_remove", "doc", "menu", V("\"x\"")));
        Assert.Contains("\"menu\": [\"y\", \"z\"],", files["doc/a.json"]);
    }

    [Theory]
    [InlineData("set", "items/q/n", "1", "isn't in the list")]
    [InlineData("set", "rules/nope", "1", "doesn't exist")]
    [InlineData("set", "items", "[]", "split over")]
    [InlineData("add_field", "rules/speed", "1", "already exists")]
    [InlineData("add_record", "items", "{ \"id\": \"z\" }", "already has a record 'z'")]
    [InlineData("add_record", "items", "{ \"n\": 1 }", "needs an id")]
    [InlineData("list_add", "menu", "\"x\"", "already has that value")]
    [InlineData("list_remove", "menu", "\"q\"", "doesn't have that value")]
    [InlineData("explode", "menu", "1", "unknown op")]
    public void PatchesThatDontFitAreRefused(string op, string path, string value, string expected)
    {
        var files = Files();
        var problem = DataPatcher.Apply(files, DataManifest.Parse(Manifest), new DataPatch(op, "doc", path, V(value)));
        Assert.NotNull(problem);
        Assert.Contains(expected, problem);
        Assert.Equal(Files(), files); // nothing changed
    }

    [Fact]
    public void UnknownDocumentIsRefused() =>
        Assert.Contains("no such document", DataPatcher.Apply(Files(), DataManifest.Parse(Manifest), new DataPatch("set", "nope", "a", V("1"))));

    [Fact]
    public void TheSameKeyInTwoFilesIsReported()
    {
        var files = Files();
        files["doc/b.json"] = "{ \"menu\": \"again\" }";
        var problems = new List<string>();
        DataManifest.Parse(Manifest).Assemble(files, "doc", problems);
        Assert.Contains(problems, p => p.Contains("'doc/menu' is given in both doc/a.json and doc/b.json"));
    }
}

/// <summary>Era / faction / scenario layers on the real data.</summary>
public class DataLayerTests
{
    [Fact]
    public void BaseDataHasNoLayersApplied()
    {
        Assert.Equal("tng", TestData.Real.Orbit!.Era);
        Assert.DoesNotContain(TestData.Real.Orbit.Ships, s => s.Faction == "earth_uec");
    }

    [Fact]
    public void EraLayerChangesTheShipsAndTheTransporter()
    {
        var data = GameData.Load(TestData.Source, new DataSetup(Era: "ent"));
        Assert.Empty(data.Validate());
        Assert.Equal("ent", data.Orbit!.Era);
        Assert.Contains(data.Orbit.Ships, s => s.Faction == "earth_uec" && s.Ship.ClassName == "NX");
        Assert.Contains(data.Orbit.Ships, s => s.Faction == "klingon" && s.Ship.ClassName == "Raptor");
        Assert.Equal(1, data.Orbit.TransporterLevelFor("earth_uec", data.Orbit.Era));
        Assert.Equal(3, TestData.Real.Orbit!.TransporterLevelFor("federation", TestData.Real.Orbit.Era));
        Assert.Equal(TestData.Real.Fingerprint, data.Fingerprint); // the same files, a different setup
    }

    [Fact]
    public void ScenarioLayerIsAppliedLastAndMissingOnesAreReported()
    {
        var data = GameData.Load(TestData.Source, new DataSetup(Era: "ent", Factions: new[] { "federation", "cardassian" }, Scenario: "quick_start"));
        Assert.Empty(data.Validate());
        Assert.Equal(5, data.Landing.AssembleSeconds);
        Assert.Equal(Fix64.FromInt(1500), data.Landing.StartStock.Single(a => a.Resource == data.Resource("duranium").Index).Amount);
        Assert.Contains("construction_rover", data.Landing.Units);
        Assert.Equal("ent", data.Orbit!.Era);
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        Assert.NotEqual(0UL, sim.StateHash());

        var missing = GameData.Load(TestData.Source, new DataSetup(Scenario: "nope"));
        Assert.Contains(missing.Validate(), p => p.Contains("layers/scenarios/nope.json: no such scenario layer"));
    }

    [Fact]
    public void BrokenLayersAndConflictsAreReported()
    {
        var dir = TestData.CopyData();
        Directory.CreateDirectory(Path.Combine(dir, "layers", "factions"));
        File.WriteAllText(Path.Combine(dir, "layers", "factions", "klingon.json"),
            "{ \"patches\": [ { \"op\": \"set\", \"doc\": \"surfaces\", \"path\": \"orbit/ships/klingon/class\", \"value\": \"D7\" },\n" +
            "  { \"op\": \"set\", \"doc\": \"surfaces\", \"path\": \"units/no_such_rover/speed\", \"value\": 3 } ] }");
        var problems = GameData.Load(dir).Validate();
        Assert.Contains(problems, p => p.Contains("data/layers/factions/klingon.json: set surfaces:units/no_such_rover/speed") && p.Contains("isn't in the list"));
        Assert.Contains(problems, p => p.StartsWith("Conflict: data/layers/eras/ent.json and data/layers/factions/klingon.json both change surfaces:orbit/ships/klingon"));

        // Applied in order, the later layer (the faction) wins.
        var data = GameData.Load(dir, new DataSetup(Era: "ent", Factions: new[] { "klingon" }));
        Assert.Equal("D7", data.Orbit!.Ships.Single(s => s.Faction == "klingon").Ship.ClassName);
    }

    [Fact]
    public void UnlistedFilesAreReported()
    {
        var dir = TestData.CopyData();
        File.WriteAllText(Path.Combine(dir, "buildings", "forgotten.json"), "{ \"buildings\": [] }");
        Assert.Contains(GameData.Load(dir).Validate(), p => p.Contains("data/buildings/forgotten.json isn't listed in data/manifest.json"));
    }
}

/// <summary>Edits go to the draft; Promote checks them before they reach the base files.</summary>
public class DraftPromoteTests
{
    private const string Economy = "surfaces/economy.json";
    private const string Crawler = "surfaces/units/mining_crawler.json";

    /// <summary>A data copy, its loaded data, and the loaded sources with one number changed (like the DATA tab).</summary>
    private static (string Dir, Dictionary<string, string> Edited) Edit(string file, string label, string value)
    {
        var dir = TestData.CopyData("ff-draft-");
        var data = GameData.Load(dir);
        var n = DataTuning.Numbers(data).Single(x => x.File == file && x.Label == label);
        Assert.Null(DataTuning.SetNumber(data.Sources[file], n.Path, value, out var text));
        var edited = new Dictionary<string, string>(data.Sources) { [file] = text };
        return (dir, edited);
    }

    [Fact]
    public void AGoodDraftIsPromotedWithABackupAndTheFormattingKept()
    {
        var (dir, edited) = Edit(Crawler, "units › mining_crawler › breakdown › chance_per_min", "0.08");
        var before = File.ReadAllText(Path.Combine(dir, Crawler));
        Assert.Equal(1, DataStore.SaveDraft(dir, edited));
        Assert.Contains("\"path\": \"units/mining_crawler/breakdown/chance_per_min\", \"value\": 0.08", DataStore.ReadDraft(dir));
        Assert.Equal(before, File.ReadAllText(Path.Combine(dir, Crawler))); // the base file is untouched

        var loaded = DataStore.LoadForGame(dir, DataSetup.Base);
        Assert.Empty(loaded.Refused);
        Assert.Contains("draft", loaded.From);
        Assert.Equal(Fix64.Parse("0.08"), loaded.Data.SurfaceUnit("mining_crawler").Breakdown!.ChancePerMinute);
        Assert.Equal(edited[Crawler], loaded.Data.Sources[Crawler]);

        var result = DataStore.Promote(dir, "20261106-120000");
        Assert.True(result.Promoted, result.Message + string.Join(" | ", result.Problems));
        Assert.Equal(new[] { Crawler }, result.ChangedFiles);
        var after = File.ReadAllText(Path.Combine(dir, Crawler));
        Assert.Equal(before.Replace("\"chance_per_min\": 0.05", "\"chance_per_min\": 0.08"), after); // only that token
        Assert.Equal(before, File.ReadAllText(Path.Combine(dir, "_backups", "20261106-120000", Crawler)));
        Assert.True(File.Exists(Path.Combine(dir, "_backups", "20261106-120000", "_draft", "draft.json")));
        Assert.Null(DataStore.ReadDraft(dir));
        Assert.Equal(Fix64.Parse("0.08"), GameData.Load(dir).SurfaceUnit("mining_crawler").Breakdown!.ChancePerMinute);
    }

    [Fact]
    public void ABadDraftNeverTouchesTheBase()
    {
        var (dir, edited) = Edit(Economy, "salvage_refund", "2"); // a refund over 100% fails a data check
        DataStore.SaveDraft(dir, edited);
        var before = File.ReadAllText(Path.Combine(dir, Economy));
        var result = DataStore.Promote(dir, "x");
        Assert.False(result.Promoted);
        Assert.NotEmpty(result.Problems);
        Assert.Equal(before, File.ReadAllText(Path.Combine(dir, Economy)));
        Assert.NotNull(DataStore.ReadDraft(dir)); // kept, to fix

        // The game refuses the draft and runs on the base files.
        var loaded = DataStore.LoadForGame(dir, DataSetup.Base);
        Assert.Contains(loaded.Refused, r => r.StartsWith("data/ with the draft fails the data checks"));
        Assert.Equal("data/", loaded.From);
        Assert.Equal(Fix64.Parse("0.5"), loaded.Data.SalvageRefund);
    }

    [Fact]
    public void ADraftThatBreaksThePlanetsIsRefused()
    {
        // 35% ocean passes GameData.Validate but not the playable-planet checks.
        var (dir, edited) = Edit("surfaces/planets.json", "planets › class_m › elevation › ocean", "0.35");
        DataStore.SaveDraft(dir, edited);
        Assert.Empty(DataStore.LoadForGame(dir, DataSetup.Base).Refused);
        var result = DataStore.Promote(dir, "x");
        Assert.False(result.Promoted);
        Assert.Contains(result.Problems, p => p.Contains("ocean"));
    }

    [Fact]
    public void ADraftThatDoesntFitIsRefused()
    {
        var dir = TestData.CopyData("ff-draft-");
        Directory.CreateDirectory(Path.Combine(dir, "_draft"));
        File.WriteAllText(DataStore.DraftFile(dir), DataLayer.Write("test", new[] { new DataPatch("set", "surfaces", "units/no_rover/speed", JsonDocument.Parse("3").RootElement) }));
        var result = DataStore.Promote(dir, "x");
        Assert.False(result.Promoted);
        Assert.Contains("doesn't fit", result.Message);
    }

    [Fact]
    public void RevertingAnEditEmptiesTheDraft()
    {
        var (dir, edited) = Edit(Crawler, "units › mining_crawler › breakdown › chance_per_min", "0.08");
        DataStore.SaveDraft(dir, edited);
        Assert.Equal(0, DataStore.SaveDraft(dir, GameData.Load(dir).Sources));
        Assert.Null(DataStore.ReadDraft(dir));
    }
}

/// <summary>The loader refuses data that fails its checks and falls back to the last good copy.</summary>
public class DataFallbackTests
{
    [Fact]
    public void BrokenBaseDataFallsBackToTheLastGoodCopy()
    {
        var dir = TestData.CopyData("ff-fallback-");
        var good = DataStore.LoadForGame(dir, DataSetup.Base);
        Assert.Empty(good.Refused);
        Assert.True(File.Exists(Path.Combine(dir, DataStore.LastGoodPack)));

        // A mass change that breaks the data: refused, the last good data runs instead.
        TestData.Replace(dir, "surfaces.json", "\"salvage_refund\": 0.5", "\"salvage_refund\": 2");
        TestData.Replace(dir, "buildings.json", "\"id\": \"refinery\"", "\"id\": \"refinery_x\"");
        var loaded = DataStore.LoadForGame(dir, DataSetup.Base);
        Assert.Contains(loaded.Refused, r => r.StartsWith("data/ fails the data checks"));
        Assert.Contains("last good", loaded.From);
        Assert.Equal(Fix64.Parse("0.5"), loaded.Data.SalvageRefund);
        Assert.Equal(good.Data.Fingerprint, loaded.Data.Fingerprint);
        Assert.Empty(loaded.Data.Validate());

        // A file that isn't even JSON: the same.
        File.WriteAllText(Path.Combine(dir, "surfaces", "lanes.json"), "{ \"lanes\": ");
        Assert.Contains("last good", DataStore.LoadForGame(dir, DataSetup.Base).From);
    }

    [Fact]
    public void WithNoLastGoodCopyTheFailingDataRunsWithItsProblems()
    {
        var dir = TestData.CopyData("ff-fallback-");
        TestData.Replace(dir, "surfaces.json", "\"salvage_refund\": 0.5", "\"salvage_refund\": 2");
        var loaded = DataStore.LoadForGame(dir, DataSetup.Base);
        Assert.NotEmpty(loaded.Refused);
        Assert.Contains("nothing better", loaded.From);
        Assert.NotEmpty(loaded.Data.Validate());
    }
}

/// <summary>The release pack: one binary file that loads the same data, and refuses to load when changed.</summary>
public class DataPackTests
{
    [Fact]
    public void PackRoundTripGivesTheSameData()
    {
        var data = TestData.Real;
        var pack = DataPack.Write(data.Sources);
        var loaded = GameData.LoadPack(pack);
        Assert.Equal(data.Fingerprint, loaded.Fingerprint);
        Assert.Equal(data.Sources, loaded.Sources);
        Assert.Equal(data.Files, loaded.Files);
        Assert.Empty(loaded.Validate());
        Assert.Equal(DataTuning.Numbers(data), DataTuning.Numbers(loaded));
        Assert.Equal(GoldenData.ScriptedGameHash(data), GoldenData.ScriptedGameHash(loaded));
        Assert.True(pack.Length < data.Sources.Values.Sum(t => t.Length) / 3, "the pack should be compressed");
        Assert.DoesNotContain("colony_hub", System.Text.Encoding.UTF8.GetString(pack)); // not readable as text
    }

    [Fact]
    public void LayersComeWithThePack()
    {
        var loaded = GameData.LoadPack(DataPack.Write(TestData.Real.Sources), new DataSetup(Era: "ent"));
        Assert.Equal("ent", loaded.Orbit!.Era);
    }

    [Theory]
    [InlineData(0)] // magic
    [InlineData(12)] // fingerprint in the header
    [InlineData(50)] // body hash
    [InlineData(-1)] // the body itself
    public void AChangedPackIsRefused(int at)
    {
        var pack = DataPack.Write(TestData.Real.Sources);
        var i = at < 0 ? pack.Length - 20 : at;
        pack[i] ^= 0x5a;
        Assert.Throws<InvalidDataException>(() => GameData.LoadPack(pack));
    }

    [Fact]
    public void ATruncatedPackIsRefused()
    {
        var pack = DataPack.Write(TestData.Real.Sources);
        Assert.Throws<InvalidDataException>(() => GameData.LoadPack(pack[..(pack.Length - 1)]));
        Assert.Throws<InvalidDataException>(() => GameData.LoadPack(new byte[10]));
    }

    [Fact]
    public void AnEditedRebuiltBodyIsCaughtByTheFingerprint()
    {
        // Someone rewrites the body and its hash but keeps the old fingerprint: refused.
        var sources = new Dictionary<string, string>(TestData.Real.Sources);
        var original = DataPack.Write(sources);
        sources["surfaces/economy.json"] = sources["surfaces/economy.json"].Replace("\"salvage_refund\": 0.5", "\"salvage_refund\": 0.9");
        var forged = DataPack.Write(sources);
        Array.Copy(original, 10, forged, 10, 32);
        Assert.Throws<InvalidDataException>(() => GameData.LoadPack(forged));
    }

    [Fact]
    public void TheFingerprintIgnoresLayoutButNotValues()
    {
        var sources = new Dictionary<string, string>(TestData.Real.Sources);
        var crlf = sources.ToDictionary(s => s.Key, s => s.Value.Replace("\n", "\r\n"));
        Assert.Equal(DataFingerprint.Of(sources), DataFingerprint.Of(crlf));
        sources["surfaces/lanes.json"] = sources["surfaces/lanes.json"].Replace("\"lane_offset\": 2.5", "\"lane_offset\": 2.6");
        Assert.NotEqual(DataFingerprint.Of(crlf), DataFingerprint.Of(sources));
    }
}
