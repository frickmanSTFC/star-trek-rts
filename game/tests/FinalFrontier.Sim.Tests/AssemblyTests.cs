using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Cores that come down from orbit (the first landing's Colony Hub, outpost drops) stand on their tile from touchdown but
/// are <see cref="StructureState.Assembling"/> for landing.assemble_seconds, doing nothing until they turn Complete.
/// </summary>
public class AssemblyTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static int Sec(int seconds) => seconds * Simulation.TickRate;

    /// <summary>Lands next to a passable deposit and steps to just after touchdown: the hub is down and assembling.</summary>
    private static Simulation Touchdown(out SurfaceStructure hub, out int deposit, GameData? data = null)
    {
        data ??= Data;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var map = sim.Surface!.Map;
        foreach (var site in Enumerable.Range(0, map.Grid.Count).Where(t => sim.Surface.CanLand(0, t)))
        {
            var near = map.Grid.Within(site, data.Landing.RevealRadius).FirstOrDefault(t =>
                t != site && map.DepositResource[t] >= 0 && map.Terrain(t).Buildable && map.Terrain(t).Passable, -1);
            if (near < 0) continue;
            sim.Schedule(0, new LandColonyCommand(0, site));
            for (var i = 0; i <= Sec(data.Landing.DescentSeconds); i++) sim.Step();
            hub = sim.Surface.StructureAt(site)!;
            deposit = near;
            return sim;
        }
        throw new InvalidOperationException("No landing site next to a deposit.");
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    /// <summary>A surveyed, buildable, empty tile without a deposit within two tiles of a core.</summary>
    private static int FreeTileNear(Simulation sim, int core)
    {
        var s = sim.Surface!;
        return s.Map.Grid.Within(core, 2).First(t => t != core && s.Map.Terrain(t).Buildable && s.Players[0].Surveyed[t]
            && s.Map.DepositResource[t] < 0 && s.StructureAt(t) == null && s.Units.All(u => u.Tile != t) && !s.Map.NearNatives(t));
    }

    private static List<Fix64> Stock(Simulation sim) => Data.Resources.Select(r => sim.Players[0].Stock[r.Index]).ToList();

    [Fact]
    public void TheLandedHubDoesNothingUntilItHasAssembled()
    {
        var sim = Touchdown(out var hub, out _);
        var s = sim.Surface!;
        var p = s.Players[0];
        Assert.Equal(StructureState.Assembling, hub.State);
        Assert.Equal(p.UplinkTick, hub.ReadyTick);
        Assert.Equal(p.LandsAtTick + Sec(Data.Landing.AssembleSeconds), hub.ReadyTick);
        Assert.Equal(Data.Landing.Units.Count, s.Units.Count); // the vehicles are out at touchdown
        var start = Stock(sim);

        for (var i = 0; i < Sec(5); i++) sim.Step();
        Assert.True(hub.Assembling);
        Assert.Equal(Fix64.Zero, s.StorageCapacity(0));   // the vault isn't open yet...
        Assert.Equal(start, Stock(sim));                    // ...but nothing of the start stock is lost, and there's no trickle
        Assert.Equal(Fix64.Zero, sim.IncomePerMinute(0)[R("duranium")]);
        Assert.Equal(Fix64.Zero, s.PowerOf(hub.Id).Supply);
        Assert.Equal(0, s.BuildQueues(0));
        Assert.False(s.InBuildRange(0, hub.Tile));
        Assert.Null(s.NearestDepot(0, hub.Tile));
        Assert.Null(s.NearestRepairPad(0, hub.Tile));
        Assert.Equal(PlanetSurface.AssemblingProblem, s.UpgradeProblem(sim, 0, hub.Id));
        Assert.False(s.UplinkOnline(0, sim.Tick));
        Assert.Equal(Data.Landing.AssembleSeconds - 5, PlanetSurface.AssembleSecondsLeft(hub, sim.Tick));
        Assert.InRange(s.AssembleFraction(hub, sim.Tick).ToDouble(), 0.24, 0.27);
        Assert.Equal(Fix64.One, s.BuildFraction(hub)); // it isn't under construction

        // It came down from orbit: there's nothing to cancel for a refund.
        Do(sim, new CancelBuildingCommand(0, hub.Id));
        Assert.Same(hub, s.StructureAt(hub.Tile));
        Assert.Equal(start, Stock(sim));

        // Complete from the uplink tick on, the same tick the uplink comes online.
        while (sim.Tick < hub.ReadyTick - 1) sim.Step();
        Assert.True(hub.Assembling);
        Assert.False(s.UplinkOnline(0, sim.Tick));
        sim.Step();
        Assert.Equal(StructureState.Complete, hub.State);
        Assert.True(s.UplinkOnline(0, sim.Tick));
        var def = Data.Building(hub.DefId);
        Assert.Equal(def.Storage, s.StorageCapacity(0));
        Assert.Equal(def.BuildQueues, s.BuildQueues(0));
        Assert.True(s.InBuildRange(0, hub.Tile));
        Assert.Same(hub, s.NearestDepot(0, hub.Tile));
        Assert.Same(hub, s.NearestRepairPad(0, hub.Tile));
        Assert.NotEqual(PlanetSurface.AssemblingProblem, s.UpgradeProblem(sim, 0, hub.Id));
        var duranium = sim.Players[0].Stock[R("duranium")];
        sim.Step();
        Assert.True(sim.Players[0].Stock[R("duranium")] > duranium); // the trickle starts
        Assert.Equal(def.PowerSupply, s.PowerOf(hub.Id).Supply);
    }

    [Fact]
    public void BuildingIsRefusedUntilTheHubHasAssembled()
    {
        var sim = Touchdown(out var hub, out _);
        var s = sim.Surface!;
        var tile = FreeTileNear(sim, hub.Tile);
        Assert.Equal(PlacementProblem.CoreAssembling, s.CheckPlacement(sim, 0, "storage_container", tile));
        var start = Stock(sim);
        Do(sim, new PlaceBuildingCommand(0, "storage_container", tile, 0));
        Assert.Null(s.StructureAt(tile));
        Assert.Equal(start, Stock(sim));

        while (hub.Assembling) sim.Step();
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "storage_container", tile));
        Do(sim, new PlaceBuildingCommand(0, "storage_container", tile, 0));
        Assert.Equal(StructureState.Building, s.StructureAt(tile)!.State);
        Assert.Equal(hub.Id, s.StructureAt(tile)!.BaseId);
    }

    [Fact]
    public void AnOutpostDroppedFromOrbitAssemblesForTheSameTime()
    {
        var sim = Touchdown(out var hub, out _);
        var s = sim.Surface!;
        while (hub.Assembling) sim.Step();
        var stock = sim.Players[0].Stock;
        stock.Add(R("duranium"), Fix64.FromInt(1000));
        stock.Add(R("tritanium"), Fix64.FromInt(400));
        stock.Add(R("crew"), Fix64.FromInt(100));
        var far = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.OutpostProblem(sim, 0, t) == null);
        Do(sim, new OutpostDropCommand(0, far));
        var drop = s.Deliveries.Single();
        while (s.Deliveries.Count > 0) sim.Step();

        var outpost = s.StructureAt(far)!;
        var def = Data.Building(outpost.DefId);
        Assert.Equal(StructureState.Assembling, outpost.State);
        Assert.Equal(drop.ArriveTick + Sec(Data.Landing.AssembleSeconds), outpost.ReadyTick);
        Assert.Equal(2, s.Bases(0).Count); // it is a base (its tab shows), just not working yet
        Assert.Equal(Data.Building(hub.DefId).BuildQueues, s.BuildQueues(0));
        Assert.Equal(Data.Building(hub.DefId).Storage, s.StorageCapacity(0));
        Assert.Equal(Fix64.Zero, s.PowerOf(outpost.Id).Supply);
        var near = FreeTileNear(sim, far);
        Assert.Equal(PlacementProblem.CoreAssembling, s.CheckPlacement(sim, 0, "storage_container", near));
        Assert.Equal(PlanetSurface.AssemblingProblem, s.UpgradeProblem(sim, 0, outpost.Id));

        while (sim.Tick < outpost.ReadyTick) sim.Step();
        Assert.Equal(StructureState.Complete, outpost.State);
        Assert.Equal(Data.Building(hub.DefId).BuildQueues + def.BuildQueues, s.BuildQueues(0));
        Assert.Equal(Data.Building(hub.DefId).Storage + def.Storage, s.StorageCapacity(0));
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "storage_container", near));
        Assert.Equal(outpost.Id, s.BaseFor(0, near));
    }

    [Fact]
    public void CrawlerPodsWaitForTheHubToAssemble()
    {
        // A long assembly, so the crawler fills its hopper before there is anywhere to send it.
        var data = TestData.Patched(("surfaces.json", "\"assemble_seconds\": 20", "\"assemble_seconds\": 120"));
        var sim = Touchdown(out var hub, out var deposit, data);
        var s = sim.Surface!;
        var crawler = s.Units.Single(u => u.DefId == "mining_crawler");
        Do(sim, new MineCommand(0, crawler.Id, deposit)); // it can drive and unfurl meanwhile
        for (var i = 0; i < Sec(100) && crawler.MiningStatus != MiningStatus.NoDestination; i++) sim.Step();
        Assert.Equal(MiningStatus.NoDestination, crawler.MiningStatus);
        Assert.Equal(UnitMode.Deployed, crawler.Mode);
        Assert.True(hub.Assembling);
        Assert.Empty(s.Pods);
        Assert.Equal(data.SurfaceUnit("mining_crawler").Hopper, crawler.Hopper);

        while (hub.Assembling) sim.Step();
        sim.Step();
        var pod = Assert.Single(s.Pods);
        Assert.Equal(hub.Tile, pod.ToTile);
        Assert.Equal(MiningStatus.Mining, crawler.MiningStatus);
    }

    [Fact]
    public void TheTestPanelCanFinishTheAssemblyButNotScrubIt()
    {
        var sim = Touchdown(out var hub, out _);
        Assert.NotNull(sim.DebugProblem(new DebugStructureCommand(0, hub.Id, DebugStructureField.BuildProgress, Fix64.Ratio(1, 2))));
        Do(sim, new DebugStructureCommand(0, hub.Id, DebugStructureField.BuildProgress, Fix64.One));
        Assert.Equal(StructureState.Complete, hub.State);
    }

    [Fact]
    public void AssemblyIsDeterministic()
    {
        static List<ulong> Run()
        {
            var sim = Touchdown(out var hub, out _);
            var tile = FreeTileNear(sim, hub.Tile);
            sim.Schedule(sim.Tick + 3, new PlaceBuildingCommand(0, "storage_container", tile, 0)); // refused: still assembling
            sim.Schedule(hub.ReadyTick + 2, new PlaceBuildingCommand(0, "storage_container", tile, 0));
            var hashes = new List<ulong>();
            while (sim.Tick < hub.ReadyTick + Sec(10))
            {
                sim.Step();
                hashes.Add(sim.StateHash());
            }
            return hashes;
        }
        Assert.Equal(Run(), Run());
    }
}
