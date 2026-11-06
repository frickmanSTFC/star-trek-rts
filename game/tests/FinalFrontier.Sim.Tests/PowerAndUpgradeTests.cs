using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

public class PowerAndUpgradeTests
{
    private static int R(GameData data, string id) => data.Resource(id).Index;

    /// <summary>Landed next to a buildable deposit (same approach as ConstructionTests).</summary>
    private static Simulation Landed(GameData data, out int hub, out int deposit)
    {
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var map = sim.Surface!.Map;
        foreach (var site in Enumerable.Range(0, map.Grid.Count).Where(t => sim.Surface.CanLand(0, t)))
        {
            var near = map.Grid.Within(site, data.Landing.RevealRadius)
                .FirstOrDefault(t => t != site && map.DepositResource[t] >= 0 && map.Terrain(t).Buildable, -1);
            if (near < 0) continue;
            sim.Schedule(0, new LandColonyCommand(0, site));
            for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
            hub = site;
            deposit = near;
            return sim;
        }
        throw new InvalidOperationException("No landing site next to a buildable deposit.");
    }

    private static void BuildAndFinish(Simulation sim, string building, int tile)
    {
        sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, building, tile, 0));
        sim.Step();
        var s = sim.Surface!.StructureAt(tile)!;
        while (s.State != StructureState.Complete) sim.Step();
        sim.Step();
    }

    private static int FreeTile(Simulation sim, int hub, params int[] except)
    {
        var map = sim.Surface!.Map;
        return map.Grid.Within(hub, 2).First(t =>
            map.Terrain(t).Buildable && map.DepositResource[t] < 0 && sim.Surface.StructureAt(t) == null && !except.Contains(t));
    }

    [Fact]
    public void EnergyIsAPowerBudgetNotAStockpile()
    {
        var data = TestData.Real;
        Assert.DoesNotContain(data.HudResources, r => r.Id == data.PowerResource);
        var sim = Landed(data, out var hub, out _);
        Assert.Equal(Fix64.Zero, sim.Players[0].Stock[R(data, "energy")]);
        for (var i = 0; i < 300; i++) sim.Step();
        Assert.Equal(Fix64.Zero, sim.Players[0].Stock[R(data, "energy")]);

        var core = sim.Surface!.StructureAt(hub)!;
        Assert.Equal(data.Building("colony_hub").PowerSupply, sim.Surface.PowerOf(core.Id).Supply);
        Assert.Equal(Fix64.Zero, sim.Surface.PowerOf(core.Id).Use);
    }

    [Fact]
    public void BuildingsJoinTheBaseThatCanReachThem()
    {
        var data = TestData.Real;
        var sim = Landed(data, out var hub, out var deposit);
        var core = sim.Surface!.StructureAt(hub)!;
        Assert.Equal(core.Id, core.BaseId);
        Assert.Single(sim.Surface.Bases(0));

        BuildAndFinish(sim, "mine", deposit);
        var mine = sim.Surface.StructureAt(deposit)!;
        Assert.Equal(core.Id, mine.BaseId);
        Assert.Equal(data.Building("mine").PowerUse, sim.Surface.PowerOf(core.Id).Use);
        Assert.Contains(mine, sim.Surface.StructuresOf(core.Id));
    }

    [Fact]
    public void OverloadedBasesSlowEveryPowerUser()
    {
        // Make one mine draw more power than the hub supplies, in a copy of the data.
        var dir = TestData.CopyData("ff-power-");
        try
        {
            var path = Path.Combine(dir, "buildings/surface/mine.json");
            var text = File.ReadAllText(path);
            Assert.Contains("\"extract_per_min\": 40, \"power_use\": 6", text);
            File.WriteAllText(path, text.Replace("\"extract_per_min\": 40, \"power_use\": 6", "\"extract_per_min\": 40, \"power_use\": 50"));
            var data = GameData.Load(dir);

            var sim = Landed(data, out var hub, out var deposit);
            BuildAndFinish(sim, "mine", deposit);
            var mine = sim.Surface!.StructureAt(deposit)!;
            var power = sim.Surface.PowerOf(mine.BaseId);
            Assert.Equal(Fix64.FromInt(30), power.Supply);
            Assert.Equal(Fix64.FromInt(50), power.Use);
            Assert.Equal(StructureStatus.LowPower, mine.Status);

            // Extraction runs at 30/50 of full speed.
            var before = sim.Surface.Map.DepositAmount[deposit];
            for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
            var mined = (before - sim.Surface.Map.DepositAmount[deposit]).ToDouble();
            Assert.InRange(mined, 40 * 0.6 - 0.05, 40 * 0.6 + 0.05);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReactorsAddPowerSupply()
    {
        var data = TestData.Real;
        var sim = Landed(data, out var hub, out _);
        var core = sim.Surface!.StructureAt(hub)!;
        BuildAndFinish(sim, "fusion_reactor", FreeTile(sim, hub));
        Assert.Equal(data.Building("colony_hub").PowerSupply + data.Building("fusion_reactor").PowerSupply, sim.Surface.PowerOf(core.Id).Supply);
    }

    [Fact]
    public void UpgradingPaysTakesTimeAndRaisesTheLevel()
    {
        var data = TestData.Real;
        var sim = Landed(data, out var hub, out _);
        var core = sim.Surface!.StructureAt(hub)!;
        var hubDef = data.Building(core.DefId);
        var next = data.Building(hubDef.UpgradesTo);
        Assert.Equal(1, hubDef.Level);
        Assert.Equal(2, next.Level);

        // Give the player enough to upgrade.
        foreach (var c in next.Cost) sim.Players[0].Stock.Add(c.Resource, c.Amount);
        var duranium = sim.Players[0].Stock[R(data, "duranium")];
        Assert.Null(sim.Surface.UpgradeProblem(sim, 0, core.Id));
        sim.Schedule(sim.Tick, new UpgradeCommand(0, core.Id));
        sim.Step();
        Assert.Equal(next.Id, core.UpgradingTo);
        Assert.True(sim.Players[0].Stock[R(data, "duranium")] < duranium - Fix64.FromInt(250), "upgrade cost should be paid");
        Assert.Equal("already upgrading", sim.Surface.UpgradeProblem(sim, 0, core.Id));

        for (var i = 1; i < next.BuildSeconds * Simulation.TickRate; i++) sim.Step();
        Assert.Equal(next.Id, core.DefId);
        Assert.Null(core.UpgradingTo);
        Assert.Equal(next.BuildQueues, sim.Surface.BuildQueues(0));
        Assert.Equal(next.PowerSupply, sim.Surface.PowerOf(core.Id).Supply);

        // Radius grew: a tile at the new edge is now in range.
        var map = sim.Surface.Map;
        var edge = Enumerable.Range(0, map.Grid.Count).FirstOrDefault(t => map.Grid.Distance(hub, t) == next.BuildRadius, -1);
        if (edge >= 0) Assert.True(sim.Surface.InBuildRange(0, edge));
    }

    [Fact]
    public void CancellingAnUpgradeRefundsIt()
    {
        var data = TestData.Real;
        var sim = Landed(data, out var hub, out _);
        var core = sim.Surface!.StructureAt(hub)!;
        var next = data.Building(data.Building(core.DefId).UpgradesTo);
        foreach (var c in next.Cost) sim.Players[0].Stock.Add(c.Resource, c.Amount);
        sim.Schedule(sim.Tick, new UpgradeCommand(0, core.Id));
        sim.Step();
        var afterPaying = sim.Players[0].Stock[R(data, "tritanium")];

        sim.Schedule(sim.Tick, new CancelBuildingCommand(0, core.Id));
        sim.Step();
        Assert.Null(core.UpgradingTo);
        Assert.Equal("colony_hub", core.DefId);
        var refund = next.Cost.Single(c => c.Resource == R(data, "tritanium")).Amount;
        var hubTritanium = data.Building("colony_hub").OutputPerMinute.Single(o => o.Resource == R(data, "tritanium")).Amount / Simulation.TicksPerMinute;
        Assert.Equal(afterPaying + refund + hubTritanium, sim.Players[0].Stock[R(data, "tritanium")]);
        Assert.NotNull(sim.Surface.StructureAt(hub));
    }

    [Fact]
    public void UpgradesNeedTheResources()
    {
        var data = TestData.Real;
        var sim = Landed(data, out var hub, out _);
        var core = sim.Surface!.StructureAt(hub)!;
        var stock = sim.Players[0].Stock;
        stock.TrySpend(new[] { new ResourceAmount(R(data, "tritanium"), stock[R(data, "tritanium")]) });
        Assert.Equal("not enough resources", sim.Surface.UpgradeProblem(sim, 0, core.Id));
        sim.Schedule(sim.Tick, new UpgradeCommand(0, core.Id));
        sim.Step();
        Assert.Null(core.UpgradingTo);
    }

    [Fact]
    public void PowerAndUpgradesAreDeterministic()
    {
        ulong Run()
        {
            var data = TestData.Real;
            var sim = Landed(data, out var hub, out var deposit);
            var core = sim.Surface!.StructureAt(hub)!;
            foreach (var c in data.Building("colonial_administration").Cost) sim.Players[0].Stock.Add(c.Resource, c.Amount);
            sim.Schedule(sim.Tick, new UpgradeCommand(0, core.Id));
            sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, "mine", deposit, 3));
            sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, "fusion_reactor", FreeTile(sim, hub), 1));
            for (var i = 0; i < Simulation.TicksPerMinute * 2; i++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
