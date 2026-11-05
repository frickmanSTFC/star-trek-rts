using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>The Vehicle Bay: ordering, paying, queueing, assembling and rolling out survey rovers and mining crawlers.</summary>
public class VehicleBayTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static int Sec(int seconds) => seconds * Simulation.TickRate;

    private static Simulation Landed(out int hub)
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var site = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(Data); i++) sim.Step();
        hub = site;
        // Plenty to spend, so costs never get in the way unless a test wants them to.
        sim.Players[0].Stock.Add(R("duranium"), Fix64.FromInt(1000));
        sim.Players[0].Stock.Add(R("tritanium"), Fix64.FromInt(400));
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    /// <summary>A finished Vehicle Bay two tiles from the hub.</summary>
    private static SurfaceStructure Bay(Simulation sim, int hub)
    {
        var map = sim.Surface!.Map;
        var tile = map.Grid.Within(hub, 2).First(t => map.Grid.Distance(t, hub) == 2 &&
            sim.Surface.CheckPlacement(sim, 0, "vehicle_bay", t) == PlacementProblem.None);
        Do(sim, new PlaceBuildingCommand(0, "vehicle_bay", tile, 0));
        var bay = sim.Surface.StructureAt(tile)!;
        while (bay.State != StructureState.Complete) sim.Step();
        sim.Step();
        return bay;
    }

    [Fact]
    public void TheBayIsInTheBuildMenuAndMakesRoversAndCrawlers()
    {
        Assert.Contains("vehicle_bay", Data.BuildMenu);
        var def = Data.Building("vehicle_bay");
        Assert.Equal(new[] { "survey_rover", "mining_crawler", "survey_rover_mk2", "survey_rover_mk3" }, def.Produces);
        Assert.All(def.Produces, id => Assert.True(Data.SurfaceUnit(id).BuildSeconds > 0));
    }

    [Fact]
    public void AnIdleBayDrawsNoPower()
    {
        var sim = Landed(out var hub);
        var bay = Bay(sim, hub);
        Assert.Equal(StructureStatus.NoInput, bay.Status);
        Assert.Equal(Fix64.Zero, sim.Surface!.PowerOf(bay.BaseId).Use);
    }

    [Fact]
    public void OrderingPaysUpFrontAndTheCrawlerRollsOutAfterItsBuildTime()
    {
        var sim = Landed(out var hub);
        var bay = Bay(sim, hub);
        var s = sim.Surface!;
        var crawler = Data.SurfaceUnit("mining_crawler");
        var duranium = sim.Players[0].Stock[R("duranium")];
        var before = s.Units.Count;

        Do(sim, new BuildUnitCommand(0, bay.Id, "mining_crawler"));
        Assert.Equal(new[] { "mining_crawler" }, bay.Production);
        var paid = crawler.Cost.Single(c => c.Resource == R("duranium")).Amount;
        Assert.InRange((duranium - sim.Players[0].Stock[R("duranium")] - paid).ToDouble(), -0.5, 0.01); // minus a tick of hub trickle
        Assert.Equal(StructureStatus.Working, bay.Status);

        for (var i = 0; i < Sec(crawler.BuildSeconds) - 3; i++) sim.Step();
        Assert.Equal(before, s.Units.Count);
        Assert.True(s.ProductionFraction(bay) > Fix64.FromRaw(Fix64.One.Raw * 9 / 10));
        for (var i = 0; i < 4; i++) sim.Step();

        Assert.Equal(before + 1, s.Units.Count);
        var made = s.Units[^1];
        Assert.Equal("mining_crawler", made.DefId);
        Assert.Equal(bay.Tile, made.BuiltAtTile);
        Assert.Equal(1, s.Map.Grid.Distance(made.Tile, bay.Tile));
        Assert.True(s.Map.Terrain(made.Tile).Passable);
        Assert.Empty(bay.Production);
        Assert.Equal(Fix64.FromInt(100), made.Integrity);
        // It starts in perfect condition, folded up and ready to drive.
        Assert.False(made.Broken);
        Assert.Equal(UnitMode.Mobile, made.Mode);
    }

    [Fact]
    public void OrdersQueueAndRunOneAtATime()
    {
        var sim = Landed(out var hub);
        var bay = Bay(sim, hub);
        var s = sim.Surface!;
        Do(sim, new BuildUnitCommand(0, bay.Id, "survey_rover"));
        Do(sim, new BuildUnitCommand(0, bay.Id, "survey_rover"));
        Assert.Equal(2, bay.Production.Count);
        var before = s.Units.Count;
        var rover = Data.SurfaceUnit("survey_rover");
        for (var i = 0; i < Sec(rover.BuildSeconds) + 2; i++) sim.Step();
        Assert.Equal(before + 1, s.Units.Count);
        for (var i = 0; i < Sec(rover.BuildSeconds) + 2; i++) sim.Step();
        Assert.Equal(before + 2, s.Units.Count);
        // They don't stack on one tile when there's room beside the bay.
        Assert.NotEqual(s.Units[^1].Tile, s.Units[^2].Tile);
    }

    [Fact]
    public void TheQueueHasALimitAndOrdersNeedTheResources()
    {
        var sim = Landed(out var hub);
        var bay = Bay(sim, hub);
        var s = sim.Surface!;
        for (var i = 0; i < Data.Building("vehicle_bay").ProductionQueue; i++) Do(sim, new BuildUnitCommand(0, bay.Id, "survey_rover"));
        Assert.Equal("queue full", s.ProductionProblem(sim, 0, bay.Id, "survey_rover"));
        Do(sim, new BuildUnitCommand(0, bay.Id, "survey_rover"));
        Assert.Equal(Data.Building("vehicle_bay").ProductionQueue, bay.Production.Count);

        var poor = Landed(out var hub2);
        var bay2 = Bay(poor, hub2);
        var stock = poor.Players[0].Stock;
        stock.TrySpend(new[] { new ResourceAmount(R("duranium"), stock[R("duranium")]) });
        Assert.Equal("not enough resources", poor.Surface!.ProductionProblem(poor, 0, bay2.Id, "mining_crawler"));
        Assert.Equal("can't build that", poor.Surface.ProductionProblem(poor, 0, bay2.Id, "terraforming_rover"));
    }

    [Fact]
    public void CancellingTheLastOrderRefundsItInFull()
    {
        var sim = Landed(out var hub);
        var bay = Bay(sim, hub);
        Do(sim, new BuildUnitCommand(0, bay.Id, "survey_rover"));
        Do(sim, new BuildUnitCommand(0, bay.Id, "mining_crawler"));
        var tritanium = sim.Players[0].Stock[R("tritanium")];
        Do(sim, new CancelUnitCommand(0, bay.Id));
        Assert.Equal(new[] { "survey_rover" }, bay.Production);
        var refund = Data.SurfaceUnit("mining_crawler").Cost.Single(c => c.Resource == R("tritanium")).Amount;
        Assert.InRange((sim.Players[0].Stock[R("tritanium")] - tritanium - refund).ToDouble(), 0, 0.1);

        // Cancelling the one in progress resets its work.
        for (var i = 0; i < Sec(5); i++) sim.Step();
        Do(sim, new CancelUnitCommand(0, bay.Id));
        Assert.Empty(bay.Production);
        Assert.Equal(Fix64.Zero, bay.ProductionProgress);
    }

    [Fact]
    public void LowPowerSlowsProduction()
    {
        var sim = Landed(out var hub);
        var bay = Bay(sim, hub);
        var s = sim.Surface!;
        // Pile on power users until the base is short of power.
        var map = s.Map;
        foreach (var t in map.Grid.Within(hub, 2).Where(t => s.CheckPlacement(sim, 0, "refinery", t) == PlacementProblem.None).Take(4).ToList())
            Do(sim, new PlaceBuildingCommand(0, "refinery", t, 0));
        while (s.StructuresOf(bay.BaseId).Any(x => x.State != StructureState.Complete)) sim.Step();
        sim.Players[0].Stock.Add(R("duranium_ore"), Fix64.FromInt(500));
        Do(sim, new BuildUnitCommand(0, bay.Id, "survey_rover"));
        sim.Step();
        var eff = s.PowerOf(bay.BaseId).Efficiency;
        Assert.True(eff < Fix64.One, "base should be short of power");
        var before = bay.ProductionProgress;
        sim.Step();
        Assert.Equal(StructureStatus.LowPower, bay.Status);
        Assert.True(bay.ProductionProgress - before < Fix64.One);
    }

    [Fact]
    public void SalvagingABayRefundsItsUnbuiltOrders()
    {
        var sim = Landed(out var hub);
        var bay = Bay(sim, hub);
        Do(sim, new BuildUnitCommand(0, bay.Id, "mining_crawler"));
        var tritanium = sim.Players[0].Stock[R("tritanium")];
        Do(sim, new SalvageCommand(0, bay.Id));
        var expected = Data.Building("vehicle_bay").Cost.Single(c => c.Resource == R("tritanium")).Amount * Data.SalvageRefund
            + Data.SurfaceUnit("mining_crawler").Cost.Single(c => c.Resource == R("tritanium")).Amount;
        Assert.InRange((sim.Players[0].Stock[R("tritanium")] - tritanium - expected).ToDouble(), 0, 0.1);
    }

    [Fact]
    public void ProductionIsDeterministic()
    {
        ulong Run()
        {
            var sim = Landed(out var hub);
            var bay = Bay(sim, hub);
            sim.Schedule(sim.Tick + 1, new BuildUnitCommand(0, bay.Id, "mining_crawler"));
            sim.Schedule(sim.Tick + 2, new BuildUnitCommand(0, bay.Id, "survey_rover"));
            for (var i = 0; i < Sec(80); i++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
