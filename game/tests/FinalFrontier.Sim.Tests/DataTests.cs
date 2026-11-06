using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;

namespace FinalFrontier.Sim.Tests;

public class DataTests
{
    [Fact]
    public void RealDataLoads()
    {
        var data = TestData.Real;
        Assert.NotEmpty(data.Factions);
        Assert.NotEmpty(data.Hulls);
        Assert.NotEmpty(data.ShipClasses);
    }

    [Fact]
    public void RealDataPassesChecks() => Assert.Empty(TestData.Real.Validate());

    /// <summary>The checks Promote and the release packer run: Validate plus playable planets, map symmetry and twin climates.</summary>
    [Fact]
    public void RealDataPassesTheSharedChecks() => Assert.Empty(DataChecks.All(TestData.Real));

    [Fact]
    public void MvpFactionsArePlayableWithValidStarterFleets()
    {
        var data = TestData.Real;
        foreach (var faction in MatchSetup.MvpFactions)
        {
            Assert.True(data.Faction(faction).Playable, faction);
            foreach (var key in MatchSetup.StarterFleets[faction])
            {
                Assert.True(data.HasShipClass(key), $"Missing ship class {key}");
                Assert.Equal(faction, data.ShipClass(key).Faction);
            }
        }
    }
}
