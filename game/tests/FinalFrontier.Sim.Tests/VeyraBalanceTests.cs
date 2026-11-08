using FinalFrontier.Balance;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>The Veyra opener (balance/orders/veyra_opener.json) and the sealed-building extras on a Domes world.</summary>
public class VeyraBalanceTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;

    private static string OrderFile(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "balance", "orders", name + ".json");
            if (File.Exists(f)) return f;
        }
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void TheVeyraOpenerBuildsTheDomesKitAndKeepsItsCrew()
    {
        var order = BuildOrder.Load(OrderFile("veyra_opener"));
        Assert.Equal("veyra", order.World);
        foreach (var s in order.Steps.Where(s => s.Kind == StepKind.Build)) Assert.Contains(s.Target, Data.EveryBuildMenu);

        var r = Runner.Run(Data, order, 1701, 18);
        Assert.True(string.IsNullOrEmpty(r.Crash), r.Crash);
        Assert.True(r.Milestones.ContainsKey("built:life_support_plant"), "no life support plant");
        Assert.True(r.Milestones.ContainsKey("built:pressure_dome"), "no pressure dome");
        Assert.True(r.Milestones.ContainsKey("built:vehicle_bay"), "the bay never fitted under cover");
        Assert.True(r.Series["stock:crew"].Min() > 100, "crew lost to failed life support");
    }

    [Fact]
    public void CrewedBuildingsAreBuiltSealedOnDomesWorlds()
    {
        var veyra = VeyraTests.Veyra().Surface!;
        var tarsis = MatchSetup.CreatePlanetLanding(Data, 1701).Surface!;
        var g = veyra.Gravity.BuildCost;
        Fix64 Duranium(PlanetSurface s, string id) => s.BuildCostOf(id).Single(c => c.Resource == R("duranium")).Amount;
        var sealedX = Data.Habitability!.Domes!.CrewedMetalCost;
        // Crewed: × gravity × 1.15; automated: × gravity only; class M: the data's cost.
        Assert.Equal(Data.Building("science_lab").Cost.Single(c => c.Resource == R("duranium")).Amount * g * sealedX, Duranium(veyra, "science_lab"));
        Assert.Equal(Data.Building("refinery").Cost.Single(c => c.Resource == R("duranium")).Amount * g, Duranium(veyra, "refinery"));
        Assert.Equal(Data.Building("science_lab").Cost.Single(c => c.Resource == R("duranium")).Amount, Duranium(tarsis, "science_lab"));

        // And they draw crewed_extra_power more power there (not cores).
        var sim = VeyraTests.LandedOnVeyra(out var site);
        var s = sim.Surface!;
        var hub = s.Bases(0).Single();
        Assert.Equal(Fix64.Zero, s.CrewedExtraPower(hub));
        var tile = s.Map.Grid.Neighbours(site).First(t => s.Map.Terrain(t).Buildable && s.StructureAt(t) == null);
        sim.Schedule(sim.Tick, new DebugPlaceBuildingCommand(0, 0, "science_lab", tile, 0));
        sim.Step();
        sim.Schedule(sim.Tick, new ResearchCommand(0, Data.Techs[0].Id));
        sim.Step();
        sim.Step();
        var lab = s.StructureAt(tile)!;
        Assert.Equal(Data.Habitability.Domes.CrewedExtraPower, s.CrewedExtraPower(lab));
        Assert.Equal(Data.Building("science_lab").PowerUse + Data.Habitability.Domes.CrewedExtraPower, s.PowerOf(hub.Id).Use);
    }
}
