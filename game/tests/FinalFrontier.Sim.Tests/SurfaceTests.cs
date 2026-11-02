using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

public class HexGridTests
{
    [Fact]
    public void NeighboursAreMutualAndAtDistanceOne()
    {
        var grid = new HexGrid(9, 7);
        for (var t = 0; t < grid.Count; t++)
            foreach (var n in grid.Neighbours(t))
            {
                Assert.Equal(1, grid.Distance(t, n));
                Assert.Contains(t, grid.Neighbours(n));
            }
        Assert.Equal(6, grid.NeighbourCount(grid.Index(4, 3)));
        Assert.Equal(6, grid.NeighbourCount(grid.Index(4, 4)));
    }

    [Fact]
    public void WithinRadiusCountsMatchHexRings()
    {
        var grid = new HexGrid(20, 20);
        var centre = grid.Index(10, 10);
        Assert.Equal(7, grid.Within(centre, 1).Count);
        Assert.Equal(19, grid.Within(centre, 2).Count);
        Assert.Equal(37, grid.Within(centre, 3).Count);
    }
}

public class SurfaceGenerationTests
{
    private static SurfaceMap Planet(ulong seed) => MatchSetup.CreatePlanetLanding(TestData.Real, seed).Surface!.Map;

    [Fact]
    public void SameSeedSamePlanet()
    {
        var a = MatchSetup.CreatePlanetLanding(TestData.Real, 42);
        var b = MatchSetup.CreatePlanetLanding(TestData.Real, 42);
        Assert.Equal(a.Surface!.Map.TerrainOf, b.Surface!.Map.TerrainOf);
        Assert.Equal(a.Surface.Map.DepositResource, b.Surface.Map.DepositResource);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEqual(a.Surface.Map.TerrainOf, Planet(43).TerrainOf);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(1701UL)]
    [InlineData(99999UL)]
    public void PlanetsArePlayable(ulong seed)
    {
        var data = TestData.Real;
        var map = Planet(seed);
        var def = map.Def;
        var tiles = Enumerable.Range(0, map.Grid.Count).ToList();

        // Terrain shares follow the thresholds (ranking makes them exact, minus polar rows).
        var ocean = tiles.Count(t => map.Terrain(t).Id == "ocean");
        Assert.InRange(ocean, map.Grid.Count * 20 / 100, map.Grid.Count * 31 / 100);

        // Enough places to land, and the basics are on the map.
        Assert.True(tiles.Count(map.IsLandingSite) >= 50, "too few landing sites");
        foreach (var basic in new[] { "duranium", "tritanium", "deuterium" })
            Assert.Contains(tiles, t => map.DepositResource[t] == data.Resource(basic).Index);

        // Deposits sit on terrain their rule allows.
        foreach (var t in tiles.Where(t => map.DepositResource[t] >= 0))
        {
            var res = data.Resources[map.DepositResource[t]].Id;
            Assert.Contains(def.Deposits, r => r.ResourceId == res && r.Terrain.Contains(map.Terrain(t).Id));
        }
    }

    [Fact]
    public void HintsReflectDepositsWithoutRevealingTiles()
    {
        var map = Planet(1701);
        for (var region = 0; region < map.RegionCount; region++)
        {
            var present = Enumerable.Range(0, map.Grid.Count)
                .Where(t => map.Region(t) == region && map.DepositResource[t] >= 0)
                .Select(t => map.DepositResource[t]).Distinct().OrderBy(r => r);
            Assert.Equal(present, map.Hints(region).Select(h => h.Resource).OrderBy(r => r));
        }
    }
}

public class LandingTests
{
    private static int FirstLandingSite(Simulation sim) =>
        Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));

    private static Simulation Landed(out int site)
    {
        var sim = MatchSetup.CreatePlanetLanding(TestData.Real, 1701);
        site = FirstLandingSite(sim);
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.Real.Landing.DescentSeconds * Simulation.TickRate; i++) sim.Step();
        return sim;
    }

    [Fact]
    public void PlayerStartsInOrbitWithNothingSurveyed()
    {
        var sim = MatchSetup.CreatePlanetLanding(TestData.Real, 1701);
        var p = sim.Surface!.Players[0];
        Assert.Equal(LandingPhase.InOrbit, p.Phase);
        Assert.DoesNotContain(true, p.Surveyed);
        Assert.Empty(sim.Surface.Structures);
        Assert.Equal(Fix64.Zero, sim.IncomePerMinute(0)[TestData.Real.Resource("duranium").Index]);
    }

    [Fact]
    public void PodDescendsThenFoundsTheColony()
    {
        var data = TestData.Real;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var site = FirstLandingSite(sim);
        sim.Schedule(0, new LandColonyCommand(0, site));
        sim.Step();
        Assert.Equal(LandingPhase.Descending, sim.Surface!.Players[0].Phase);
        Assert.Empty(sim.Surface.Structures);

        for (var i = 0; i < data.Landing.DescentSeconds * Simulation.TickRate; i++) sim.Step();
        var p = sim.Surface.Players[0];
        Assert.Equal(LandingPhase.Landed, p.Phase);
        Assert.Equal(data.Landing.HubBuilding, sim.Surface.StructureAt(site)!.DefId);
        Assert.Equal(data.Landing.Units.Count, sim.Surface.Units.Count);
        Assert.All(sim.Surface.Units, u => Assert.Equal(1, sim.Surface.Map.Grid.Distance(site, u.Tile)));
        foreach (var t in sim.Surface.Map.Grid.Within(site, data.Landing.RevealRadius)) Assert.True(p.Surveyed[t]);
        Assert.True(sim.IncomePerMinute(0)[data.Resource("duranium").Index] > Fix64.Zero);
    }

    [Fact]
    public void InvalidLandingsAreIgnored()
    {
        var sim = MatchSetup.CreatePlanetLanding(TestData.Real, 1701);
        var map = sim.Surface!.Map;
        var water = Enumerable.Range(0, map.Grid.Count).First(t => map.Terrain(t).Id == "ocean");
        sim.Schedule(0, new LandColonyCommand(0, water));
        sim.Schedule(0, new LandColonyCommand(0, -5));
        sim.Schedule(0, new LandColonyCommand(3, FirstLandingSite(sim))); // no such player
        sim.Step();
        Assert.Equal(LandingPhase.InOrbit, sim.Surface.Players[0].Phase);
    }

    [Fact]
    public void YouOnlyLandOnce()
    {
        var sim = Landed(out var site);
        var other = Enumerable.Range(0, sim.Surface!.Map.Grid.Count)
            .First(t => sim.Surface.Map.IsLandingSite(t) && sim.Surface.Map.Grid.Distance(t, site) > 5);
        sim.Schedule(sim.Tick, new LandColonyCommand(0, other));
        sim.Step();
        Assert.Single(sim.Surface.Structures);
    }

    [Fact]
    public void RoversDriveAlongPassableTilesAndSurveyAsTheyGo()
    {
        var sim = Landed(out var site);
        var surface = sim.Surface!;
        var map = surface.Map;
        var rover = surface.Units[0];

        // A reachable tile some distance away.
        var goal = Enumerable.Range(0, map.Grid.Count)
            .Where(t => map.Grid.Distance(site, t) is >= 6 and <= 9 && map.Terrain(t).Passable)
            .First(t => Pathfinder.Find(map, rover.Tile, t) != null);
        var path = Pathfinder.Find(map, rover.Tile, goal)!;
        Assert.All(path, t => Assert.True(map.Terrain(t).Passable));
        for (var i = 1; i < path.Count; i++) Assert.Equal(1, map.Grid.Distance(path[i - 1], path[i]));

        sim.Schedule(sim.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, goal));
        for (var i = 0; i < Simulation.TickRate * 120 && rover.Tile != goal; i++) sim.Step();

        Assert.Equal(goal, rover.Tile);
        Assert.Empty(rover.Path);
        foreach (var t in map.Grid.Within(goal, rover.SurveyRadius)) Assert.True(surface.Players[0].Surveyed[t]);
    }

    [Fact]
    public void RoversIgnoreUnreachableTargets()
    {
        var sim = Landed(out _);
        var map = sim.Surface!.Map;
        var rover = sim.Surface.Units[0];
        var mountain = Enumerable.Range(0, map.Grid.Count).First(t => map.Terrain(t).Id == "mountains");
        var start = rover.Tile;
        sim.Schedule(sim.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, mountain));
        for (var i = 0; i < 60; i++) sim.Step();
        Assert.Equal(start, rover.Tile);
    }

    [Fact]
    public void LandingAndSurveyingAreDeterministic()
    {
        ulong Run()
        {
            var sim = Landed(out var site);
            var map = sim.Surface!.Map;
            var goal = Enumerable.Range(0, map.Grid.Count).Where(t => map.Terrain(t).Passable)
                .OrderByDescending(t => map.Grid.Distance(site, t) <= 10 ? map.Grid.Distance(site, t) : -1).First();
            sim.Schedule(sim.Tick, new MoveUnitsCommand(0, sim.Surface.Units.Select(u => u.Id).ToArray(), goal));
            for (var i = 0; i < 900; i++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
