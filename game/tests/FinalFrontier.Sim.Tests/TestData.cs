using FinalFrontier.Sim.Data;

namespace FinalFrontier.Sim.Tests;

internal static class TestData
{
    private static readonly Lazy<GameData> Cached = new(() =>
        GameData.Load(GameData.FindDataDirectory(AppContext.BaseDirectory)));

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
}
