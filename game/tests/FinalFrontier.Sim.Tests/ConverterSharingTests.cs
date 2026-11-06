using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Two converters sharing one input: the first can use up the last of it in the same tick, so the second has no runnable
/// recipe left when it works (found by the balance runner: fast_expand's second refinery threw DivideByZeroException).
/// </summary>
public class ConverterSharingTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;

    [Fact]
    public void ASecondRefineryWithNothingLeftThisTickDoesNotCrash()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t)
            && s.Map.Grid.Within(t, 2).Count(x => x != t && s.Map.Terrain(x).Buildable && s.Map.DepositResource[x] < 0) >= 4);
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(Data); i++) sim.Step();
        sim.Players[0].Stock.Add(R("duranium"), Fix64.FromInt(500));
        var tiles = s.Map.Grid.Within(site, 2).Where(x => x != site && s.Map.Terrain(x).Buildable && s.Map.DepositResource[x] < 0
            && s.Units.All(u => u.Tile != x)).Take(2).ToList();
        foreach (var t in tiles)
        {
            sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, "refinery", t, 0));
            sim.Step();
        }
        var refineries = tiles.Select(t => s.StructureAt(t)!).ToList();
        while (refineries.Any(r => r.State != StructureState.Complete)) sim.Step();

        // Less ore than one refinery uses in a tick: both see input when their status is set, only the first gets any.
        var ore = R("duranium_ore");
        sim.Players[0].Stock.Add(ore, Fix64.FromRaw(2) - sim.Players[0].Stock[ore]); // 2 raw units: the first refinery takes exactly all
        var duranium = sim.Players[0].Stock[R("duranium")];
        sim.Step();
        Assert.Equal(Fix64.Zero, sim.Players[0].Stock[ore]);
        Assert.True(sim.Players[0].Stock[R("duranium")] >= duranium);
        sim.Step();
        Assert.All(refineries, r => Assert.Equal(StructureStatus.NoInput, r.Status));
    }
}
