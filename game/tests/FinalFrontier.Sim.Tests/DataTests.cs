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

    /// <summary>A starting-set entry with building_ref copies that building, so the two costs can't drift apart.</summary>
    [Fact]
    public void StartingSetCopiesKeepTheirBuildingsCost() =>
        Assert.Contains(TestData.Patched(("buildings/starting_sets/federation.json", "\"duranium\": 120,", "\"duranium\": 100,")).Validate(),
            p => p.StartsWith("Starting set federation science_lab: costs 40 tritanium + 100 duranium but its building_ref science_lab costs 40 tritanium + 120 duranium"));

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
