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

    /// <summary>The real design data from the repo's data/ folder.</summary>
    public static GameData Real => Cached.Value;

    /// <summary>
    /// A copy of the real data with exact text replacements in one or more files (e.g. a faster wear rate), loaded
    /// fresh. Each "from" must be found, so a data change that breaks a test patch fails loudly.
    /// </summary>
    public static GameData Patched(params (string File, string From, string To)[] patches)
    {
        var source = GameData.FindDataDirectory(AppContext.BaseDirectory);
        var dir = Path.Combine(Path.GetTempPath(), "ff-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(source, "*.json")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        foreach (var (file, from, to) in patches)
        {
            var path = Path.Combine(dir, file);
            var text = File.ReadAllText(path);
            if (!text.Contains(from)) throw new InvalidOperationException($"Test patch: '{from}' not found in {file}.");
            File.WriteAllText(path, text.Replace(from, to));
        }
        return GameData.Load(dir);
    }

    /// <summary>A copy of the real data with regular-expression replacements in one file (every match), loaded fresh.
    /// Each pattern must match at least once.</summary>
    public static GameData Regexed(string file, params (string Pattern, string To)[] patches)
    {
        var source = GameData.FindDataDirectory(AppContext.BaseDirectory);
        var dir = Path.Combine(Path.GetTempPath(), "ff-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(source, "*.json")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        var path = Path.Combine(dir, file);
        var text = File.ReadAllText(path);
        foreach (var (pattern, to) in patches)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, pattern)) throw new InvalidOperationException($"Test patch: /{pattern}/ not found in {file}.");
            text = System.Text.RegularExpressions.Regex.Replace(text, pattern, to);
        }
        File.WriteAllText(path, text);
        return GameData.Load(dir);
    }
}
