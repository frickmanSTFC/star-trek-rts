using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>The domes kit on a Domes world (Veyra): crewed buildings only under cover, cover from a core and from a
/// finished Pressure Dome, life support slowing crewed work, crew loss after the grace time at zero, and its state in the
/// hash. On class M nothing of it applies.</summary>
public class DomesTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;

    private static Simulation Landed(out SurfaceStructure hub, GameData? data = null)
    {
        data ??= Data;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701, "federation", null, "veyra");
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.LandProblem(sim, 0, t) == null
            && s.Map.Grid.Within(t, 4).Count(n => s.Map.Terrain(n).Buildable && s.Map.Grid.Distance(t, n) >= 2) >= 12);
        sim.Schedule(0, new LandColonyCommand(0, site));
        while (!s.UplinkOnline(0, sim.Tick)) sim.Step();
        sim.Step();
        hub = s.Bases(0).Single();
        foreach (var r in new[] { "duranium", "tritanium" }) sim.Players[0].Stock.Add(R(r), Fix64.FromInt(3000));
        foreach (var t in s.Map.Grid.Within(site, 6)) s.Players[0].Surveyed[t] = true;
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    /// <summary>A free buildable tile at exactly this distance from the hub (no building, no deposit).</summary>
    private static int Ring(Simulation sim, SurfaceStructure hub, int distance, int skip = 0)
    {
        var s = sim.Surface!;
        return s.Map.Grid.Within(hub.Tile, distance).Where(t => s.Map.Grid.Distance(hub.Tile, t) == distance && s.Map.Terrain(t).Buildable
            && s.StructureAt(t) == null && s.Map.DepositResource[t] < 0 && !s.Units.Any(u => u.Tile == t)).Skip(skip).First();
    }

    private static void DebugPlace(Simulation sim, string building, int tile) => Do(sim, new DebugPlaceBuildingCommand(0, 0, building, tile, 0));

    [Fact]
    public void CrewedBuildingsNeedCoverAutomatedOnesDont()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var near = Ring(sim, hub, 1);
        var far = Ring(sim, hub, 3);
        Assert.False(s.Covered(0, far));
        Assert.Equal(PlacementProblem.NeedsDome, s.CheckPlacement(sim, 0, "science_lab", far));
        Assert.Equal(PlacementProblem.NeedsDome, s.CheckPlacement(sim, 0, "prefab_habitat", far));
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "science_lab", near)); // inside the hub's field
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "storage_container", far)); // automated: anywhere
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "life_support_plant", far));
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "pressure_dome", far));

        // The refusal changes nothing.
        var count = s.Structures.Count;
        var stock = sim.Players[0].Stock[R("duranium")];
        Do(sim, new PlaceBuildingCommand(0, "science_lab", far, 0));
        Assert.Equal(count, s.Structures.Count);
        Assert.Contains("NeedsDome", sim.LastRefusal ?? "");
        Assert.True(sim.Players[0].Stock[R("duranium")] >= stock);
        Assert.False(string.IsNullOrEmpty(PlanetSurface.NeedsDomeProblem));
    }

    [Fact]
    public void AFinishedPressureDomeCoversTwoTilesOut()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var map = s.Map;
        // A dome two tiles out, and a tile two further on (four from the hub) that only the dome reaches.
        var domeTile = Ring(sim, hub, 2);
        var beyond = map.Grid.Within(domeTile, 2).First(t => map.Grid.Distance(domeTile, t) == 2 && map.Grid.Distance(hub.Tile, t) == 4
            && map.Terrain(t).Buildable && s.StructureAt(t) == null && map.DepositResource[t] < 0);
        Assert.Equal(PlacementProblem.NeedsDome, s.CheckPlacement(sim, 0, "prefab_habitat", beyond));

        Do(sim, new PlaceBuildingCommand(0, "pressure_dome", domeTile, 0));
        var dome = s.StructureAt(domeTile)!;
        Assert.NotEqual(StructureState.Complete, dome.State);
        Assert.Equal(0, s.DomeRadiusOf(dome)); // only finished domes cover
        Assert.Equal(PlacementProblem.NeedsDome, s.CheckPlacement(sim, 0, "prefab_habitat", beyond));

        while (dome.State != StructureState.Complete) sim.Step();
        Assert.Equal(2, s.DomeRadiusOf(dome));
        Assert.Equal(1, s.DomeRadiusOf(hub));
        Assert.True(s.Covered(0, beyond));
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "prefab_habitat", beyond));
        Assert.False(s.Covered(1, beyond)); // someone else's dome doesn't cover you
    }

    [Fact]
    public void LifeSupportShortfallSlowsCrewedWork()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        DebugPlace(sim, "science_lab", Ring(sim, hub, 1));
        Do(sim, new ResearchCommand(0, Data.Techs[0].Id));
        sim.Step();
        Assert.Equal(new BaseLifeSupport(Fix64.FromInt(2), Fix64.FromInt(2)), s.LifeSupportBudget(hub.Id)); // hub + lab on the hub's 2
        var full = s.ResearchPerMinute(0);
        Assert.True(full > Fix64.Zero);

        DebugPlace(sim, "prefab_habitat", Ring(sim, hub, 1));
        DebugPlace(sim, "prefab_habitat", Ring(sim, hub, 1));
        sim.Step();
        Assert.Equal(Fix64.Half, s.LifeSupportOf(hub.Id)); // 2 supply for 4 crewed buildings
        Assert.Equal(full * Fix64.Half, s.ResearchPerMinute(0));

        DebugPlace(sim, "life_support_plant", Ring(sim, hub, 3));
        sim.Step();
        Assert.Equal(Fix64.One, s.LifeSupportOf(hub.Id)); // 2 + 4 for 4
        Assert.Equal(full, s.ResearchPerMinute(0));

        // A plant that is paused supplies nothing; an automated building uses none.
        var plant = s.Structures.Single(x => x.DefId == "life_support_plant");
        Do(sim, new PauseCommand(0, plant.Id, true));
        sim.Step();
        Assert.Equal(Fix64.Half, s.LifeSupportOf(hub.Id));
        DebugPlace(sim, "storage_container", Ring(sim, hub, 3));
        sim.Step();
        Assert.Equal(Fix64.FromInt(4), s.LifeSupportBudget(hub.Id).Use);
    }

    [Fact]
    public void ABaseWithNoAirLosesCrewAfterTheGraceTime()
    {
        // A hub without its own life support: the base has none at all from the start.
        var data = TestData.Patched(("buildings/surface/cores.json", "\"storage\": 1500, \"life_support\": 2,", "\"storage\": 1500, \"life_support\": 0,"));
        var sim = Landed(out var hub, data);
        var s = sim.Surface!;
        var crew = R("crew");
        Assert.Equal(Fix64.Zero, s.LifeSupportOf(hub.Id));
        Assert.False(hub.LifeSupportFailing);
        Assert.True(s.AirlessSecondsLeft(hub) > 0);
        var before = sim.Players[0].Stock[crew];
        var grace = data.Habitability!.Domes!.GraceSeconds * Simulation.TickRate;
        for (var i = 0; i < grace; i++) sim.Step();
        Assert.True(hub.LifeSupportFailing);
        Assert.Equal(0, s.AirlessSecondsLeft(hub));
        for (var i = 0; i < Simulation.TickRate * 60; i++) sim.Step();
        var lost = before - sim.Players[0].Stock[crew];
        Assert.True(lost > Fix64.FromInt(1), lost.ToString()); // 2 a minute for the one crewed building (no crew made meanwhile: the hub works at 0)
        Assert.Equal(lost, hub.CrewLost);

        // Air comes back with a Life Support Plant: the clock and the flag reset, the loss stops.
        DebugPlace(sim, "life_support_plant", Ring(sim, hub, 2));
        sim.Step();
        Assert.False(hub.LifeSupportFailing);
        Assert.Equal(0, hub.AirlessTicks);
        Assert.Equal(Fix64.One, s.LifeSupportOf(hub.Id));
    }

    [Fact]
    public void TheFailureStateIsInTheHashOnDomesWorldsOnly()
    {
        var setter = typeof(SurfaceStructure).GetProperty(nameof(SurfaceStructure.AirlessTicks))!;
        var flag = typeof(SurfaceStructure).GetProperty(nameof(SurfaceStructure.LifeSupportFailing))!;

        var veyra = Landed(out var hub);
        var h = veyra.StateHash();
        setter.SetValue(hub, 7);
        Assert.NotEqual(h, veyra.StateHash());
        setter.SetValue(hub, 0);
        Assert.Equal(h, veyra.StateHash());
        flag.SetValue(hub, true);
        Assert.NotEqual(h, veyra.StateHash());

        // On class M the kit doesn't apply: never set there, and not hashed (M hashes exactly as before).
        var tarsis = MatchSetup.CreatePlanetLanding(Data, 1701);
        var site = Enumerable.Range(0, tarsis.Surface!.Map.Grid.Count).First(t => tarsis.Surface.CanLand(0, t));
        tarsis.Schedule(0, new LandColonyCommand(0, site));
        while (!tarsis.Surface.UplinkOnline(0, tarsis.Tick)) tarsis.Step();
        var mHub = tarsis.Surface.Bases(0).Single();
        var mh = tarsis.StateHash();
        setter.SetValue(mHub, 7);
        Assert.Equal(mh, tarsis.StateHash());
    }

    [Fact]
    public void TheDomesKitIsDeterministic()
    {
        ulong Run()
        {
            var data = TestData.Patched(("buildings/surface/cores.json", "\"storage\": 1500, \"life_support\": 2,", "\"storage\": 1500, \"life_support\": 1,"));
            var sim = Landed(out var hub, data);
            DebugPlace(sim, "science_lab", Ring(sim, hub, 1));
            Do(sim, new ResearchCommand(0, Data.Techs[0].Id));
            Do(sim, new PlaceBuildingCommand(0, "pressure_dome", Ring(sim, hub, 2), 0));
            Do(sim, new PlaceBuildingCommand(0, "life_support_plant", Ring(sim, hub, 3), 0));
            ulong h = 0;
            for (var i = 0; i < Simulation.TickRate * 120; i++)
            {
                sim.Step();
                if (i % 100 == 0) h = h * 31 + sim.StateHash();
            }
            return h;
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void OnClassMTheDomesKitIsNotThere()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        while (!s.UplinkOnline(0, sim.Tick)) sim.Step();
        sim.Step();
        var hub = s.Bases(0).Single();
        foreach (var t in s.Map.Grid.Within(site, 4)) s.Players[0].Surveyed[t] = true;
        sim.Players[0].Stock.Add(R("duranium"), Fix64.FromInt(3000));
        sim.Players[0].Stock.Add(R("tritanium"), Fix64.FromInt(3000));
        var far = Ring(sim, hub, 3);
        Assert.Equal(PlacementProblem.NotInBuildMenu, s.CheckPlacement(sim, 0, "pressure_dome", far));
        Assert.Equal(PlacementProblem.NotInBuildMenu, s.CheckPlacement(sim, 0, "life_support_plant", far));
        Assert.Equal(PlacementProblem.None, s.CheckPlacement(sim, 0, "science_lab", far));
        Assert.Equal(0, s.DomeRadiusOf(hub));
        Assert.Equal(new BaseLifeSupport(Fix64.Zero, Fix64.Zero), s.LifeSupportBudget(hub.Id));
    }
}
