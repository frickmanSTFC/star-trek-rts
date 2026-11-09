using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;

namespace FinalFrontier.Sim.Tests;

internal static class TestData
{
    private static readonly Lazy<GameData> Cached = new(() =>
        GameData.Load(GameData.FindDataDirectory(AppContext.BaseDirectory)));

    /// <summary>Ticks from a landing order at tick 0 until the landed Colony Hub has assembled (descent + assemble_seconds):
    /// step through `i &lt;= ReadyTicks` and the hub is Complete.</summary>
    public static int ReadyTicks(GameData d) => (d.Landing.DescentSeconds + d.Landing.AssembleSeconds) * Simulation.TickRate;

    /// <summary>Sets every player's keep-in-stock targets to 0 (scheduled for the current tick), so the colony core's
    /// workshop doesn't start making spare kits from the stock: for tests that check stock amounts exactly.</summary>
    public static void NoStockTargets(Simulation sim)
    {
        if (sim.Surface == null || sim.Data.Fabrication == null) return;
        for (var p = 0; p < sim.Players.Count; p++)
            foreach (var id in sim.Surface.PartItems) sim.Schedule(sim.Tick, new StockTargetCommand(p, id, 0));
    }

    /// <summary>The real design data from the repo's data/ folder.</summary>
    public static GameData Real => Cached.Value;

    public static string Source => GameData.FindDataDirectory(AppContext.BaseDirectory);

    /// <summary>A temporary copy of the repo's data/ folder (base files and layers; no draft or backups).</summary>
    public static string CopyData(string prefix = "ff-data-")
    {
        var source = Source;
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        foreach (var f in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, f);
            if (rel.StartsWith(DataStore.DraftFolder) || rel.StartsWith(DataStore.BackupFolder)) continue;
            var target = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target);
        }
        return dir;
    }

    /// <summary>The files a document is split into ("surfaces.json" or "surfaces" = every file of that document; a path with '/' = that file).</summary>
    public static IReadOnlyList<string> FilesOf(string dir, string file)
    {
        if (file.Contains('/')) return new[] { file };
        var doc = file.EndsWith(".json") ? file[..^5] : file;
        return DataManifest.Parse(File.ReadAllText(Path.Combine(dir, DataManifest.FileName))).PartsOf(doc);
    }

    /// <summary>Replaces text in a document's files in a data copy (every occurrence, in every file of it); "from" must be found.</summary>
    public static void Replace(string dir, string file, string from, string to)
    {
        var found = false;
        foreach (var part in FilesOf(dir, file))
        {
            var path = Path.Combine(dir, part);
            var text = File.ReadAllText(path);
            if (!text.Contains(from)) continue;
            found = true;
            File.WriteAllText(path, text.Replace(from, to));
        }
        if (!found) throw new InvalidOperationException($"Test patch: '{from}' not found in {file}.");
    }

    /// <summary>
    /// A copy of the real data with exact text replacements in one or more files (e.g. a faster wear rate), loaded
    /// fresh. A file is a document name ("surfaces.json": all its files) or a path under data/. Each "from" must be
    /// found, so a data change that breaks a test patch fails loudly.
    /// </summary>
    public static GameData Patched(params (string File, string From, string To)[] patches)
    {
        var dir = CopyData();
        foreach (var (file, from, to) in patches) Replace(dir, file, from, to);
        return GameData.Load(dir);
    }

    /// <summary>A copy of the real data with regular-expression replacements in one document's files (every match), loaded fresh.
    /// Each pattern must match at least once.</summary>
    public static GameData Regexed(string file, params (string Pattern, string To)[] patches)
    {
        var dir = CopyData();
        var parts = FilesOf(dir, file);
        var texts = parts.ToDictionary(p => p, p => File.ReadAllText(Path.Combine(dir, p)));
        foreach (var (pattern, to) in patches)
        {
            if (!texts.Values.Any(t => System.Text.RegularExpressions.Regex.IsMatch(t, pattern))) throw new InvalidOperationException($"Test patch: /{pattern}/ not found in {file}.");
            foreach (var p in parts) texts[p] = System.Text.RegularExpressions.Regex.Replace(texts[p], pattern, to);
        }
        foreach (var (p, t) in texts) File.WriteAllText(Path.Combine(dir, p), t);
        return GameData.Load(dir);
    }

    /// <summary>The loaded text of the one file of a document that contains <paramref name="needle"/>.</summary>
    public static string FileWith(GameData data, string doc, string needle) =>
        data.Files.Where(f => f.StartsWith(doc + "/")).Single(f => data.Sources[f].Contains(needle));
}
