using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Pausing a building (PauseCommand): any working building except a colony core can be switched off and on. Paused it
/// uses no input, makes nothing, pays no upkeep, doesn't wear and neither uses nor supplies power; the flag is in the hash.
/// </summary>
public class PauseTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;

    /// <summary>A landed, assembled colony on seed 1701 with plenty of stock and these buildings finished next to the hub.</summary>
    private static (Simulation Sim, PlanetSurface S, List<SurfaceStructure> Built) Colony(params string[] buildings)
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t)
            && s.Map.Grid.Within(t, 2).Count(x => x != t && s.Map.Terrain(x).Buildable && s.Map.DepositResource[x] < 0) >= 4);
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(Data); i++) sim.Step();
        foreach (var r in new[] { "duranium", "tritanium", "deuterium" }) sim.Players[0].Stock.Add(R(r), Fix64.FromInt(800));
        var tiles = s.Map.Grid.Within(site, 2).Where(x => x != site && s.Map.Terrain(x).Buildable && s.Map.DepositResource[x] < 0
            && s.Units.All(u => u.Tile != x)).Take(buildings.Length).ToList();
        for (var i = 0; i < buildings.Length; i++)
        {
            sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, buildings[i], tiles[i], 0));
            sim.Step();
        }
        var built = tiles.Select(t => s.StructureAt(t)!).ToList();
        while (built.Any(b => b.State != StructureState.Complete)) sim.Step();
        sim.Step();
        return (sim, s, built);
    }

    private static void Pause(Simulation sim, SurfaceStructure b, bool on)
    {
        sim.Schedule(sim.Tick, new PauseCommand(0, b.Id, on));
        sim.Step();
    }

    [Fact]
    public void APausedFoundryUsesNoMetalMakesNoParasteelAndDrawsNoPower()
    {
        var (sim, s, built) = Colony("foundry");
        var foundry = built[0];
        Assert.Equal(StructureStatus.Working, foundry.Status);
        var stock = sim.Players[0].Stock;
        var use = s.PowerOf(foundry.BaseId).Use;

        Pause(sim, foundry, true);
        Assert.True(foundry.Paused);
        Assert.Equal(StructureStatus.Paused, foundry.Status);
        Assert.Equal(use - Data.Building("foundry").PowerUse, s.PowerOf(foundry.BaseId).Use);
        var (dur, tri, para, condition) = (stock[R("duranium")], stock[R("tritanium")], stock[R("parasteel")], foundry.Condition);
        for (var i = 0; i < 60 * Simulation.TickRate; i++) sim.Step();
        Assert.Equal(para, stock[R("parasteel")]);
        Assert.True(stock[R("duranium")] >= dur && stock[R("tritanium")] >= tri); // nothing spent, only the hub's trickle came in
        Assert.Equal(condition, foundry.Condition); // no wear
        Assert.Equal(Fix64.Zero, sim.IncomePerMinute(0)[R("parasteel")]);

        Pause(sim, foundry, false);
        Assert.False(foundry.Paused);
        for (var i = 0; i < 20 * Simulation.TickRate; i++) sim.Step();
        Assert.Equal(StructureStatus.Working, foundry.Status);
        Assert.True(stock[R("parasteel")] > para);
        Assert.True(foundry.Condition < condition);
    }

    [Fact]
    public void APausedReactorBurnsNoDeuteriumAndSuppliesNoPower()
    {
        var (sim, s, built) = Colony("fusion_reactor");
        var reactor = built[0];
        var supply = s.PowerOf(reactor.BaseId).Supply;
        Pause(sim, reactor, true);
        Assert.Equal(StructureStatus.Paused, reactor.Status);
        Assert.True(s.PowerOf(reactor.BaseId).Supply < supply);
        var deuterium = sim.Players[0].Stock[R("deuterium")];
        for (var i = 0; i < 60 * Simulation.TickRate; i++) sim.Step();
        Assert.Equal(deuterium, sim.Players[0].Stock[R("deuterium")]);
    }

    [Fact]
    public void ABuildingPausedWhileUnderConstructionStartsPaused()
    {
        var (sim, s, _) = Colony();
        var site = Me(s).LandingTile;
        var tile = s.Map.Grid.Within(site, 2).First(x => x != site && s.CheckPlacement(sim, 0, "refinery", x) == PlacementProblem.None);
        sim.Schedule(sim.Tick, new PlaceBuildingCommand(0, "refinery", tile, 0));
        sim.Step();
        var refinery = s.StructureAt(tile)!;
        Assert.NotEqual(StructureState.Complete, refinery.State);
        Pause(sim, refinery, true);
        while (refinery.State != StructureState.Complete) sim.Step();
        sim.Players[0].Stock.Add(R("duranium_ore"), Fix64.FromInt(100));
        sim.Step();
        Assert.Equal(StructureStatus.Paused, refinery.Status);
        Assert.Equal(Fix64.FromInt(100), sim.Players[0].Stock[R("duranium_ore")]);
    }

    [Fact]
    public void RefusedPausesChangeNothing()
    {
        var (sim, s, built) = Colony("storage_container", "refinery");
        var hub = s.Bases(0)[0];
        Assert.Equal("a colony core can't be paused", s.PauseProblem(0, hub.Id, true));
        Assert.Equal("nothing to pause", s.PauseProblem(0, built[0].Id, true)); // a storage container only stores
        Assert.Equal("not yours", s.PauseProblem(1, built[1].Id, true));
        Assert.Equal("not paused", s.PauseProblem(0, built[1].Id, false));
        var hash = sim.StateHash();
        foreach (var c in new Command[] { new PauseCommand(0, hub.Id, true), new PauseCommand(0, built[0].Id, true), new PauseCommand(0, built[1].Id, false), new PauseCommand(0, 9999, true) })
        {
            var a = Clone(sim, c);
            var b = Clone(sim, null);
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        Assert.Equal(hash, sim.StateHash());
        Assert.False(hub.Paused);
        Assert.False(built[0].Paused);
    }

    [Fact]
    public void PausingIsDeterministicAndInTheHash()
    {
        var (a, sa, ba) = Colony("refinery");
        var (b, _, bb) = Colony("refinery");
        Assert.Equal(a.StateHash(), b.StateHash());
        Pause(a, ba[0], true);
        b.Step();
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Assert.True(sa.CanPause("refinery") && sa.CanPause("foundry") && sa.CanPause("mine") && sa.CanPause("vehicle_bay")
            && sa.CanPause("science_lab") && sa.CanPause("fusion_reactor"));
        Assert.False(sa.CanPause("colony_hub") || sa.CanPause("storage_container"));
    }

    private static SurfacePlayer Me(PlanetSurface s) => s.Players[0];

    /// <summary>Replays the same colony and applies one extra command (or none) on top, for refusal checks.</summary>
    private static Simulation Clone(Simulation original, Command? extra)
    {
        _ = original;
        var (sim, _, _) = Colony("storage_container", "refinery");
        if (extra != null) sim.Schedule(sim.Tick, extra);
        sim.Step();
        return sim;
    }
}
