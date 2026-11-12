using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Starting on Veyra (class L on the duel map's body west_l): the world resolves, the surface is a cold marginal
/// one, it is a Domes world, and the colony lands and assembles.</summary>
public class VeyraTests
{
    private static GameData Data => TestData.Real;

    internal static Simulation Veyra(ulong seed = 1701) => MatchSetup.CreatePlanetLanding(Data, seed, "federation", null, "veyra");

    internal static Simulation LandedOnVeyra(out int site, ulong seed = 1701)
    {
        var sim = Veyra(seed);
        var s = sim.Surface!;
        site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.LandProblem(sim, 0, t) == null);
        sim.Schedule(0, new LandColonyCommand(0, site));
        while (!s.UplinkOnline(0, sim.Tick)) sim.Step();
        sim.Step();
        return sim;
    }

    [Fact]
    public void WorldsResolveToClassAndClimateBody()
    {
        Assert.Equal(("class_l", "west_l"), MatchSetup.ResolveWorld(Data, "veyra"));
        Assert.Equal(("class_l", "west_l"), MatchSetup.ResolveWorld(Data, "Veyra"));
        Assert.Equal(("class_m", "tarsis"), MatchSetup.ResolveWorld(Data, MatchSetup.DefaultWorld));
        Assert.Throws<ArgumentException>(() => MatchSetup.ResolveWorld(Data, "risa"));
        Assert.Throws<ArgumentException>(() => MatchSetup.ResolveWorld(Data, "hesper")); // a gas giant: no surface
        Assert.False(MatchSetup.IsWorld(Data, "tarsis_a")); // class D: no surface yet
        // Tarsis by name is the same game as the default start.
        Assert.Equal(MatchSetup.CreatePlanetLanding(Data, 1701).StateHash(), MatchSetup.CreatePlanetLanding(Data, 1701, "federation", null, "tarsis").StateHash());
    }

    [Fact]
    public void VeyraIsAColdMarginalDomesWorld()
    {
        var s = Veyra().Surface!;
        Assert.Equal("class_l", s.Map.Def.ClassId);
        Assert.Equal("west_l", s.Map.ClimateBodyId);
        Assert.Equal(Fix64.Parse("0.9"), s.GravityG);
        Assert.Equal(HabitabilityTier.Domes, s.Tier);
        Assert.True(s.NeedsDomes);
        Assert.Contains("life_support_plant", s.BuildMenu);
        Assert.Contains("pressure_dome", s.BuildMenu);
        Assert.Equal(-1, s.Map.NativeTile); // no natives on Veyra

        var m = MatchSetup.CreatePlanetLanding(Data, 1701).Surface!.Map;
        double Share(SurfaceMap map, string id) => Enumerable.Range(0, map.Grid.Count).Count(t => map.Terrain(t).Id == id) / (double)map.Grid.Count;
        Assert.True(Share(s.Map, "forest") < Share(m, "forest") / 2, "less forest");
        Assert.True(Share(s.Map, "hills") > Share(m, "hills"), "more hills");
        Assert.True(Share(s.Map, "ice") > Share(m, "ice"), "more polar ice");
        Assert.True(Share(s.Map, "ocean") < Share(m, "ocean"), "smaller seas");
        var biomass = Enumerable.Range(0, s.Map.Grid.Count).Sum(t => s.Map.Biomass[t].ToDouble());
        var mBiomass = Enumerable.Range(0, m.Grid.Count).Sum(t => m.Biomass[t].ToDouble());
        Assert.True(biomass / s.Map.Grid.Count < mBiomass / m.Grid.Count / 2, "sparse life");
        // Only the three basic deposits for now (owner 2026-11-12; dilithium and zenite come back later).
        var mined = Enumerable.Range(0, s.Map.Grid.Count).Where(t => s.Map.DepositResource[t] >= 0).Select(t => Data.Resources[s.Map.DepositResource[t]].Id).Distinct().OrderBy(x => x);
        Assert.Equal(new[] { "deuterium", "duranium_ore", "tritanium_ore" }, mined);
    }

    [Fact]
    public void TheColonyLandsAndWorksOnVeyra()
    {
        var sim = LandedOnVeyra(out var site);
        var s = sim.Surface!;
        var hub = s.Bases(0).Single();
        Assert.Equal(site, hub.Tile);
        Assert.Equal(StructureState.Complete, hub.State);
        Assert.Equal(3, s.Units.Count);
        // The hub's own containment field covers its ring; its life support carries the hub itself.
        Assert.Equal(1, s.DomeRadiusOf(hub));
        Assert.True(s.Covered(0, site));
        Assert.All(s.Map.Grid.Neighbours(site), n => Assert.True(s.Covered(0, n)));
        Assert.Equal(Fix64.One, s.LifeSupportOf(hub.Id));
        // A few minutes on, nothing fails and the hub works.
        for (var i = 0; i < Simulation.TickRate * 120; i++) sim.Step();
        Assert.False(hub.LifeSupportFailing);
        Assert.Equal(StructureStatus.Working, hub.Status);
        Assert.True(sim.IncomePerMinute(0)[Data.Resource("duranium").Index] > Fix64.Zero);
    }

    [Fact]
    public void VeyraIsDeterministic()
    {
        ulong Run()
        {
            var sim = LandedOnVeyra(out _);
            ulong h = 0;
            for (var i = 0; i < Simulation.TickRate * 60; i++)
            {
                sim.Step();
                if (i % 150 == 0) h = h * 31 + sim.StateHash();
            }
            return h;
        }
        Assert.Equal(Run(), Run());
    }
}
