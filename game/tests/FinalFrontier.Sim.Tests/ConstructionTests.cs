using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

public class ConstructionTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static int Ticks(string building) => Data.Building(building).BuildSeconds * Simulation.TickRate;

    /// <summary>
    /// Lands next to a deposit on buildable ground (so mines can be tested), runs to touchdown,
    /// and returns the hub tile and that deposit tile.
    /// </summary>
    private static Simulation Landed(out int hub, out int deposit, ulong seed = 1701)
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, seed);
        var map = sim.Surface!.Map;
        var reveal = Data.Landing.RevealRadius;
        foreach (var site in Enumerable.Range(0, map.Grid.Count).Where(t => sim.Surface.CanLand(0, t)))
        {
            var near = map.Grid.Within(site, reveal).FirstOrDefault(t =>
                t != site && map.DepositResource[t] >= 0 && map.Terrain(t).Buildable, -1);
            if (near < 0) continue;
            sim.Schedule(0, new LandColonyCommand(0, site));
            for (var i = 0; i <= TestData.ReadyTicks(Data); i++) sim.Step();
            hub = site;
            deposit = near;
            return sim;
        }
        throw new InvalidOperationException("No landing site next to a buildable deposit.");
    }

    /// <summary>A surveyed, empty, buildable tile in range without a deposit.</summary>
    private static int FreeTile(Simulation sim, int hub, params int[] except)
    {
        var map = sim.Surface!.Map;
        return map.Grid.Within(hub, 2).First(t =>
            map.Terrain(t).Buildable && map.DepositResource[t] < 0 && sim.Surface.StructureAt(t) == null && !except.Contains(t));
    }

    private static void Place(Simulation sim, string building, int tile, int facing = 0)
    {
        sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, building, tile, facing));
        sim.Step();
    }

    [Fact]
    public void BuildMenuIsInTheData()
    {
        Assert.NotEmpty(Data.BuildMenu);
        Assert.All(Data.BuildMenu, id => Assert.True(Data.Building(id).BuildSeconds > 0));
    }

    [Fact]
    public void PlacingPaysTheCostAndFinishesAfterTheBuildTime()
    {
        var sim = Landed(out var hub, out _);
        var tile = FreeTile(sim, hub);
        var before = sim.Players[0].Stock[R("duranium")];
        var cost = Data.Building("prefab_habitat").Cost.Single(c => c.Resource == R("duranium")).Amount;

        Place(sim, "prefab_habitat", tile);
        var site = sim.Surface!.StructureAt(tile)!;
        Assert.Equal(StructureState.Building, site.State);
        // Paid in full, give or take one tick of hub income.
        Assert.InRange((before - sim.Players[0].Stock[R("duranium")]).ToDouble(), cost.ToDouble() - 1, cost.ToDouble());

        for (var i = 1; i < Ticks("prefab_habitat"); i++) sim.Step();
        Assert.Equal(StructureState.Complete, site.State);

        var crewIncome = sim.IncomePerMinute(0)[R("crew")];
        var hubCrew = Data.Building("colony_hub").OutputPerMinute.Single(o => o.Resource == R("crew")).Amount;
        Assert.True(crewIncome > hubCrew, "habitat should add crew income");
    }

    [Fact]
    public void OneBuildQueueMeansOneConstructionAtATime()
    {
        var sim = Landed(out var hub, out _);
        var a = FreeTile(sim, hub);
        var b = FreeTile(sim, hub, a);
        Place(sim, "prefab_habitat", a);
        Place(sim, "prefab_habitat", b);
        var first = sim.Surface!.StructureAt(a)!;
        var second = sim.Surface.StructureAt(b)!;
        Assert.Equal(1, sim.Surface.BuildQueues(0));
        Assert.Equal(StructureState.Building, first.State);
        Assert.Equal(StructureState.Queued, second.State);

        while (first.State != StructureState.Complete) sim.Step();
        sim.Step();
        Assert.Equal(StructureState.Building, second.State);
    }

    [Fact]
    public void CancellingRefundsInFullAndFreesTheTile()
    {
        var sim = Landed(out var hub, out _);
        var tile = FreeTile(sim, hub);
        Place(sim, "fusion_reactor", tile);
        var site = sim.Surface!.StructureAt(tile)!;
        var afterPlacing = sim.Players[0].Stock[R("deuterium")];

        sim.Schedule(sim.Tick, new CancelBuildingCommand(0, site.Id));
        sim.Step();
        Assert.Null(sim.Surface.StructureAt(tile));
        var refunded = Data.Building("fusion_reactor").Cost.Single(c => c.Resource == R("deuterium")).Amount;
        Assert.Equal(afterPlacing + refunded, sim.Players[0].Stock[R("deuterium")]);
    }

    [Fact]
    public void FinishedBuildingsCanNotBeCancelled()
    {
        var sim = Landed(out var hub, out _);
        var hubStructure = sim.Surface!.StructureAt(hub)!;
        sim.Schedule(sim.Tick, new CancelBuildingCommand(0, hubStructure.Id));
        sim.Step();
        Assert.NotNull(sim.Surface.StructureAt(hub));
    }

    [Fact]
    public void PlacementRulesGiveReasons()
    {
        var orbit = MatchSetup.CreatePlanetLanding(Data, 1701);
        Assert.Equal(PlacementProblem.NotLanded, orbit.Surface!.CheckPlacement(orbit, 0, "prefab_habitat", 100));

        var sim = Landed(out var hub, out var deposit);
        var s = sim.Surface!;
        var map = s.Map;
        var free = FreeTile(sim, hub);
        var unsurveyed = Enumerable.Range(0, map.Grid.Count).First(t => !s.Players[0].Surveyed[t]);

        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "prefab_habitat", free));
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "mine", deposit));
        Assert.Equal(PlacementProblem.NotInBuildMenu, s.CheckPlacement(sim, 0, "colony_hub", free));
        Assert.Equal(PlacementProblem.OffMap, s.CheckPlacement(sim, 0, "mine", -1));
        Assert.Equal(PlacementProblem.NotSurveyed, s.CheckPlacement(sim, 0, "prefab_habitat", unsurveyed));
        Assert.Equal(PlacementProblem.Occupied, s.CheckPlacement(sim, 0, "prefab_habitat", hub));
        Assert.Equal(PlacementProblem.NeedsDeposit, s.CheckPlacement(sim, 0, "mine", free));

        var unbuildable = map.Grid.Within(hub, 3).FirstOrDefault(t => s.Players[0].Surveyed[t] && !map.Terrain(t).Buildable, -1);
        if (unbuildable >= 0) Assert.Equal(PlacementProblem.TerrainUnbuildable, s.CheckPlacement(sim, 0, "prefab_habitat", unbuildable));

        var stock = sim.Players[0].Stock;
        stock.TrySpend(new[] { new ResourceAmount(R("duranium"), stock[R("duranium")]) });
        Assert.Equal(PlacementProblem.CannotAfford, s.CheckPlacement(sim, 0, "prefab_habitat", free));
    }

    [Fact]
    public void BuildingFarFromTheColonyIsOutOfRange()
    {
        var sim = Landed(out var hub, out _);
        var s = sim.Surface!;
        var map = s.Map;
        var radius = Data.Building("colony_hub").BuildRadius;
        var rover = s.Units[0];
        var goal = Enumerable.Range(0, map.Grid.Count)
            .Where(t => map.Grid.Distance(hub, t) is var d && d > radius + 2 && d < radius + 6 && map.Terrain(t).Buildable)
            .First(t => Pathfinder.Find(map, rover.Tile, t) != null);
        sim.Schedule(sim.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, goal));
        for (var i = 0; i < Simulation.TickRate * 180 && rover.Tile != goal; i++) sim.Step();

        Assert.True(s.Players[0].Surveyed[goal]);
        Assert.Equal(PlacementProblem.OutOfRange, s.CheckPlacement(sim, 0, "prefab_habitat", goal));
    }

    [Fact]
    public void MinesDrainTheirDepositIntoTheStockpileUntilDepleted()
    {
        var sim = Landed(out _, out var deposit);
        var s = sim.Surface!;
        var map = s.Map;
        var resource = map.DepositResource[deposit];
        var startDeposit = map.DepositAmount[deposit];

        Place(sim, "mine", deposit);
        var mine = s.StructureAt(deposit)!;
        while (mine.State != StructureState.Complete) sim.Step();

        var depositBefore = map.DepositAmount[deposit];
        var stockBefore = sim.Players[0].Stock[resource];
        var others = sim.IncomePerMinute(0)[resource] - Data.Building("mine").ExtractPerMinute; // hub output etc.
        for (var i = 0; i < Simulation.TicksPerMinute; i++) sim.Step();
        var mined = (depositBefore - map.DepositAmount[deposit]).ToDouble();
        Assert.InRange(mined, Data.Building("mine").ExtractPerMinute.ToDouble() - 0.01, Data.Building("mine").ExtractPerMinute.ToDouble() + 0.01);
        Assert.InRange((sim.Players[0].Stock[resource] - stockBefore).ToDouble(), mined + others.ToDouble() - 0.01, mined + others.ToDouble() + 0.01);

        var limit = (startDeposit / Data.Building("mine").ExtractPerMinute).FloorToInt() + 2;
        for (var i = 0; i < Simulation.TicksPerMinute * limit && mine.Status != StructureStatus.Depleted; i++) sim.Step();
        Assert.Equal(StructureStatus.Depleted, mine.Status);
        Assert.Equal(Fix64.Zero, map.DepositAmount[deposit]);
    }

    [Fact]
    public void BuildingsIdleWhenTheyCanNotPayUpkeep()
    {
        var sim = Landed(out var hub, out _);
        var tile = FreeTile(sim, hub);
        Place(sim, "fusion_reactor", tile);
        var reactor = sim.Surface!.StructureAt(tile)!;
        while (reactor.State != StructureState.Complete) sim.Step();
        sim.Step();
        Assert.Equal(StructureStatus.Working, reactor.Status);

        var baseId = reactor.BaseId;
        var supplyBefore = sim.Surface.PowerOf(baseId).Supply;

        var stock = sim.Players[0].Stock;
        stock.TrySpend(new[] { new ResourceAmount(R("deuterium"), stock[R("deuterium")]) });
        sim.Step();
        Assert.Equal(StructureStatus.NoUpkeep, reactor.Status);
        // An idle reactor supplies no power.
        Assert.Equal(supplyBefore - Data.Building("fusion_reactor").PowerSupply, sim.Surface.PowerOf(baseId).Supply);
    }

    [Fact]
    public void FacingIsKeptInSixSteps()
    {
        var sim = Landed(out var hub, out _);
        var a = FreeTile(sim, hub);
        var b = FreeTile(sim, hub, a);
        Place(sim, "prefab_habitat", a, 4);
        Place(sim, "prefab_habitat", b, -1);
        Assert.Equal(4, sim.Surface!.StructureAt(a)!.Facing);
        Assert.Equal(5, sim.Surface.StructureAt(b)!.Facing);
    }

    [Fact]
    public void BuildingIsDeterministic()
    {
        ulong Run()
        {
            var sim = Landed(out var hub, out var deposit);
            Place(sim, "mine", deposit, 2);
            Place(sim, "fusion_reactor", FreeTile(sim, hub), 1);
            for (var i = 0; i < Simulation.TicksPerMinute * 2; i++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
