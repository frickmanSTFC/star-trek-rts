using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Life on the planet (wildlife, natives, biomass, toxic zones), landing safety, and the east–west wrap.</summary>
public class LifeTests
{
    private static GameData Data => TestData.Real;

    private static SurfaceMap Planet(ulong seed) => SurfaceMap.Generate(Data, Data.Surface("class_m"), seed);

    [Theory]
    [InlineData(1UL)]
    [InlineData(1701UL)]
    [InlineData(99999UL)]
    public void LifeIsGeneratedOnPassableGround(ulong seed)
    {
        var map = Planet(seed);
        var tiles = Enumerable.Range(0, map.Grid.Count).ToList();
        Assert.Contains(tiles, t => map.WildlifeAt[t] == Wildlife.Herds);
        Assert.Contains(tiles, t => map.Toxic[t]);
        Assert.All(tiles.Where(t => map.Toxic[t] || map.WildlifeAt[t] != Wildlife.None), t => Assert.True(map.Terrain(t).Passable));
        // Forests are greener than deserts.
        var forest = tiles.Where(t => map.Terrain(t).Id == "forest").Average(t => map.Biomass[t].ToDouble());
        var desert = tiles.Where(t => map.Terrain(t).Id == "desert").Average(t => map.Biomass[t].ToDouble());
        Assert.True(forest > desert);
    }

    [Fact]
    public void SomeWorldsHaveNativesAndTheirLandIsOffLimits()
    {
        var withNatives = Enumerable.Range(1, 20).Select(i => Planet((ulong)i)).Where(m => m.NativeTile >= 0).ToList();
        Assert.NotEmpty(withNatives);
        var map = withNatives[0];
        Assert.True(map.NearNatives(map.NativeTile));
        Assert.Equal(LandingSafety.Unsafe, map.Safety(map.NativeTile));
        foreach (var t in map.Grid.Within(map.NativeTile, map.NativeRadius)) Assert.Equal(LandingSafety.Unsafe, map.Safety(t));
    }

    [Fact]
    public void BuildingsCanNotGoOnNativeLand()
    {
        // Find a seed with natives, land as close to them as allowed, and try to build inside their land.
        for (ulong seed = 1; seed < 40; seed++)
        {
            var sim = MatchSetup.CreatePlanetLanding(Data, seed);
            var map = sim.Surface!.Map;
            if (map.NativeTile < 0) continue;
            var site = Enumerable.Range(0, map.Grid.Count)
                .Where(t => sim.Surface.CanLand(0, t) && map.Grid.Distance(t, map.NativeTile) <= map.NativeRadius + 3)
                .DefaultIfEmpty(-1).First();
            if (site < 0) continue;
            sim.Schedule(0, new LandColonyCommand(0, site));
            for (var i = 0; i <= TestData.ReadyTicks(Data); i++) sim.Step();
            var inside = map.Grid.Within(site, 4).FirstOrDefault(t => map.NearNatives(t) && sim.Surface.Players[0].Surveyed[t] && map.Terrain(t).Buildable, -1);
            if (inside < 0) continue;
            Assert.Equal(PlacementProblem.NativeLand, sim.Surface.CheckPlacement(sim, 0, "storage_container", inside));
            return;
        }
        Assert.Fail("no seed with buildable native land near a landing site");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(1701UL)]
    public void LandingSitesAvoidToxicGroundAndPredators(ulong seed)
    {
        var map = Planet(seed);
        for (var t = 0; t < map.Grid.Count; t++)
        {
            var safety = map.Safety(t);
            if (map.Toxic[t] || map.WildlifeAt[t] == Wildlife.Predators || !map.IsLandingSite(t)) Assert.Equal(LandingSafety.Unsafe, safety);
            if (safety == LandingSafety.Safe)
                Assert.All(map.Grid.Within(t, 2), n => Assert.False(map.Toxic[n] || map.WildlifeAt[n] == Wildlife.Predators));
        }
        Assert.True(Enumerable.Range(0, map.Grid.Count).Count(t => map.Safety(t) == LandingSafety.Safe) >= 30, "too few safe landing sites");
    }

    [Fact]
    public void TheMapWrapsEastToWest()
    {
        // Terrain on the east edge should match the west edge far more often than chance.
        var map = Planet(1701);
        var g = map.Grid;
        var same = 0;
        var rows = 0;
        for (var r = map.Def.PolarRows; r < g.Height - map.Def.PolarRows; r++)
        {
            rows++;
            var east = map.TerrainOf[g.Index(g.Width - 1, r)];
            var west = map.TerrainOf[g.Index(0, r)];
            if (east == west) same++;
        }
        Assert.True(same > rows * 6 / 10, $"edges match on {same} of {rows} rows");
    }

    [Fact]
    public void LifeIsDeterministic()
    {
        var a = Planet(4242);
        var b = Planet(4242);
        Assert.Equal(a.NativeTile, b.NativeTile);
        Assert.Equal(a.Toxic, b.Toxic);
        Assert.Equal(a.WildlifeAt, b.WildlifeAt);
        Assert.Equal(a.Biomass, b.Biomass);
    }
}
