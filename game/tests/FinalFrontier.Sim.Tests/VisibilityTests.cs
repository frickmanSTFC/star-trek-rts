using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Fog of war in the sim (Visibility.cs): what each player has in sight now is computed from their buildings and
/// vehicles; seen tiles and other players' structures are remembered (state, hashed). Two landed colonies far apart
/// (CommandFuzzTests.TwoPlayersLanded: more than 20 tiles), inside the climate's calm start, so no weather changes
/// the survey radii.
/// </summary>
public class VisibilityTests
{
    private static SurfaceStructure HubOf(PlanetSurface s, int player) => s.Bases(player).First();

    private static SurfaceUnit RoverOf(Simulation sim, int player) =>
        sim.Surface!.Units.First(u => u.Owner == player && !sim.Data.SurfaceUnit(u.DefId).CanMine);

    /// <summary>Teleports a vehicle (test panel command) and runs a second, so memory has caught up (it is refreshed
    /// once a second).</summary>
    private static void Teleport(Simulation sim, SurfaceUnit u, int tile)
    {
        sim.Schedule(sim.Tick, new DebugMoveUnitCommand(u.Owner, u.Id, tile));
        for (var i = 0; i < Simulation.TickRate; i++) sim.Step();
        Assert.Equal(tile, u.Tile);
    }

    /// <summary>Runs ticks up to and including the next once-a-second memory refresh.</summary>
    private static void Settle(Simulation sim)
    {
        do sim.Step();
        while ((sim.Tick - 1) % Simulation.TickRate != 0);
    }

    /// <summary>A passable tile next to the target with nothing standing on it.</summary>
    private static int BesideOf(PlanetSurface s, int tile) =>
        s.Map.Grid.Neighbours(tile).First(t => s.Map.Terrain(t).Passable && s.StructureAt(t) == null);

    [Fact]
    public void RivalStructuresOutOfRangeAreNotVisible()
    {
        var sim = CommandFuzzTests.TwoPlayersLanded();
        var s = sim.Surface!;
        var rival = HubOf(s, 1);
        Assert.False(s.IsVisible(0, rival.Tile));
        Assert.False(s.IsExplored(0, rival.Tile));
        Assert.False(s.CanSee(0, rival));
        Assert.False(s.Remembers(0, rival));
        Assert.False(s.Knows(0, rival));
        Assert.Empty(s.Players[0].SeenStructures);
        foreach (var u in s.Units.Where(u => u.Owner == 1)) Assert.False(s.CanSee(0, u));

        // Your own things always show, and your own base is in sight.
        var mine = HubOf(s, 0);
        Assert.True(s.CanSee(0, mine) && s.Knows(0, mine) && s.IsVisible(0, mine.Tile) && s.IsExplored(0, mine.Tile));
        Assert.All(s.Units.Where(u => u.Owner == 0), u => Assert.True(s.CanSee(0, u)));
    }

    [Fact]
    public void VisibilityUsesTheSameRadiiAsTheOldViewFog()
    {
        var sim = CommandFuzzTests.TwoPlayersLanded();
        Settle(sim);
        var s = sim.Surface!;
        var g = s.Map.Grid;
        for (var player = 0; player < 2; player++)
        {
            var expected = new bool[g.Count];
            foreach (var x in s.Structures.Where(x => x.Owner == player))
                foreach (var t in g.Within(x.Tile, PlanetSurface.StructureSight)) expected[t] = true;
            foreach (var u in s.Units.Where(u => u.Owner == player))
                foreach (var t in g.Within(u.Tile, s.SurveyRadiusOf(u) + 1)) expected[t] = true;
            var visible = s.VisibleTiles(player);
            Assert.Equal(expected, visible);
            for (var t = 0; t < g.Count; t++)
            {
                Assert.Equal(expected[t], s.IsVisible(player, t));
                if (expected[t]) Assert.True(s.Players[player].Seen[t], $"tile {t} in sight but not remembered");
            }
        }

        // Exactly the radius: a building sees StructureSight tiles, not one more (measured from the hub alone, with the
        // vehicles parked well away).
        var hub = HubOf(s, 0);
        foreach (var u in s.Units.Where(u => u.Owner == 0).ToList())
            Teleport(sim, u, g.Within(HubOf(s, 1).Tile, 6).Last(t => s.Map.Terrain(t).Passable && s.StructureAt(t) == null));
        var ring = g.Within(hub.Tile, PlanetSurface.StructureSight + 1).Where(t => g.Distance(hub.Tile, t) == PlanetSurface.StructureSight + 1).ToList();
        var edge = g.Within(hub.Tile, PlanetSurface.StructureSight).Where(t => g.Distance(hub.Tile, t) == PlanetSurface.StructureSight).ToList();
        Assert.All(edge, t => Assert.True(s.IsVisible(0, t)));
        Assert.All(ring, t => Assert.False(s.IsVisible(0, t)));
    }

    [Fact]
    public void RivalStructuresBecomeVisibleInRangeAndAreRemembered()
    {
        var sim = CommandFuzzTests.TwoPlayersLanded();
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var rival = HubOf(s, 1);
        var rover = RoverOf(sim, 0);
        var home = rover.Tile;

        // Park the rover just out of sight of the rival hub: still hidden.
        var sight = s.SightOf(rover);
        var outside = g.Within(rival.Tile, sight + 2).First(t => g.Distance(rival.Tile, t) == sight + 2 && s.Map.Terrain(t).Passable && s.StructureAt(t) == null);
        Teleport(sim, rover, outside);
        Assert.Equal(sight, s.SightOf(rover));
        Assert.False(s.IsVisible(0, rival.Tile));
        Assert.False(s.Knows(0, rival));

        // Right beside it: in sight, seen, remembered.
        Teleport(sim, rover, BesideOf(s, rival.Tile));
        Assert.True(s.IsVisible(0, rival.Tile));
        Assert.True(s.CanSee(0, rival));
        Assert.True(s.Remembers(0, rival));
        Assert.True(s.Knows(0, rival));
        Assert.Contains(rival.Id, s.Players[0].SeenStructures);
        Assert.Contains(s.Units, u => u.Owner == 1 && s.CanSee(0, u)); // the rival's vehicles round its hub too
        // A tile at the edge of the rover's sight is seen but not surveyed (sight = survey radius + 1).
        var edge = g.Within(rover.Tile, s.SightOf(rover)).First(t => g.Distance(rover.Tile, t) == s.SightOf(rover) && !s.Players[0].Surveyed[t]);
        Assert.True(s.IsVisible(0, edge));

        // Back home: out of sight again, but the ground stays explored and the hub stays on the map.
        Teleport(sim, rover, home);
        Assert.False(s.IsVisible(0, rival.Tile));
        Assert.False(s.CanSee(0, rival));
        Assert.True(s.IsExplored(0, rival.Tile));
        Assert.True(s.IsExplored(0, edge)); // seen, never surveyed
        Assert.False(s.Players[0].Surveyed[edge]);
        Assert.True(s.Remembers(0, rival));
        Assert.True(s.Knows(0, rival));
        Assert.DoesNotContain(s.Units, u => u.Owner == 1 && s.CanSee(0, u)); // vehicles are only seen while in sight

        for (var i = 0; i < Simulation.TickRate * 10; i++) sim.Step();
        Assert.True(s.IsExplored(0, edge) && s.Remembers(0, rival));
        Assert.False(s.Remembers(1, HubOf(s, 0))); // the rival never came near
    }

    [Fact]
    public void NobodySeesAnythingFromOrbitAndTheFallingPodSeesItsSite()
    {
        var data = TestData.Real;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        var s = sim.Surface!;
        for (var i = 0; i < 30; i++) sim.Step();
        Assert.DoesNotContain(true, s.VisibleTiles(0));
        Assert.DoesNotContain(true, s.Players[0].Seen);

        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t));
        sim.Schedule(sim.Tick, new LandColonyCommand(0, site));
        sim.Step();
        Assert.Equal(LandingPhase.Descending, s.Players[0].Phase);
        var expected = s.Map.Grid.Within(site, PlanetSurface.DescentSight);
        Assert.Equal(expected, Enumerable.Range(0, s.Map.Grid.Count).Where(t => s.IsVisible(0, t)));
        Assert.All(expected, t => Assert.True(s.Players[0].Seen[t]));
    }

    [Fact]
    public void BadPlayersAndTilesSeeNothing()
    {
        var s = CommandFuzzTests.TwoPlayersLanded().Surface!;
        var hub = HubOf(s, 0);
        Assert.False(s.IsVisible(-1, hub.Tile));
        Assert.False(s.IsVisible(5, hub.Tile));
        Assert.False(s.IsVisible(0, -1));
        Assert.False(s.IsVisible(0, s.Map.Grid.Count));
        Assert.False(s.IsExplored(7, hub.Tile));
        Assert.False(s.Remembers(-3, HubOf(s, 1)));
        Assert.DoesNotContain(true, s.VisibleTiles(9));
    }

    [Fact]
    public void FogMemoryIsInTheStateHash()
    {
        var sim = CommandFuzzTests.TwoPlayersLanded();
        var p = sim.Surface!.Players[0];
        var before = sim.StateHash();
        var t = Array.IndexOf(p.Seen, false);
        p.Seen[t] = true;
        Assert.NotEqual(before, sim.StateHash());
        p.Seen[t] = false;
        Assert.Equal(before, sim.StateHash());

        // Remembering a rival structure changes it too (seen by walking a rover up to the rival hub).
        var rover = RoverOf(sim, 0);
        var home = rover.Tile;
        Teleport(sim, rover, BesideOf(sim.Surface, HubOf(sim.Surface, 1).Tile));
        Teleport(sim, rover, home);
        Assert.NotEmpty(p.SeenStructures);
        var twin = CommandFuzzTests.TwoPlayersLanded();
        var twinRover = RoverOf(twin, 0);
        Teleport(twin, twinRover, BesideOf(twin.Surface!, HubOf(twin.Surface!, 1).Tile));
        Teleport(twin, twinRover, home);
        Assert.Equal(sim.StateHash(), twin.StateHash());
        Array.Fill(p.Seen, false);
        Array.Fill(twin.Surface!.Players[0].Seen, false);
        Assert.Equal(sim.StateHash(), twin.StateHash()); // same tiles, same structures remembered
    }

    [Fact]
    public void FogIsDeterministic()
    {
        static List<ulong> Run()
        {
            var sim = CommandFuzzTests.TwoPlayersLanded();
            var s = sim.Surface!;
            var g = s.Map.Grid;
            // Both players' rovers drive toward the other base, so each sees the other's buildings on the way.
            for (var player = 0; player < 2; player++)
            {
                var goal = g.Within(HubOf(s, 1 - player).Tile, 4).Last(t => s.Map.Terrain(t).Passable && s.StructureAt(t) == null);
                var rovers = s.Units.Where(u => u.Owner == player && !sim.Data.SurfaceUnit(u.DefId).CanMine).Select(u => u.Id).ToList();
                sim.Schedule(sim.Tick, new MoveUnitsCommand(player, rovers, goal));
            }
            var hashes = new List<ulong>();
            for (var i = 0; i < Simulation.TickRate * 120; i++)
            {
                sim.Step();
                if (i % 60 == 0) hashes.Add(sim.StateHash());
            }
            hashes.Add((ulong)s.Players[0].Seen.Count(x => x));
            hashes.Add((ulong)s.Players[1].Seen.Count(x => x));
            return hashes;
        }
        var a = Run();
        Assert.Equal(a, Run());
        Assert.True(a[^2] > 0 && a[^1] > 0);
    }
}
