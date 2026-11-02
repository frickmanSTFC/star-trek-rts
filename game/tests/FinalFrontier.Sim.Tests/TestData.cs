using FinalFrontier.Sim.Data;

namespace FinalFrontier.Sim.Tests;

internal static class TestData
{
    private static readonly Lazy<GameData> Cached = new(() =>
        GameData.Load(GameData.FindDataDirectory(AppContext.BaseDirectory)));

    /// <summary>The real design data from the repo's data/ folder.</summary>
    public static GameData Real => Cached.Value;
}
