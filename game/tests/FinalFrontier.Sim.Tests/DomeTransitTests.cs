using System.Reflection;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Dome transits (docs/missions-contract.md A): on a Domes world a ground vehicle stops at the edge between
/// covered and uncovered ground for transit_seconds, leaving and entering a core's field or a Pressure Dome; queueing,
/// keeping and cancelling it, determinism and the hash. On class M nothing of it happens.</summary>
public class DomeTransitTests
{
    private static GameData Data => TestData.Real;
    private static int Pause => (Data.Habitability!.Domes!.TransitSeconds * Simulation.TickRate).FloorToInt();

    private static Simulation Veyra(out SurfaceStructure hub)
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701, "federation", null, "veyra");
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.LandProblem(sim, 0, t) == null
            && s.Map.Grid.Within(t, 4).Count(n => s.Map.Terrain(n).Buildable && s.Map.Grid.Distance(t, n) >= 2) >= 12);
        sim.Schedule(0, new LandColonyCommand(0, site));
        while (!s.UplinkOnline(0, sim.Tick)) sim.Step();
        sim.Step();
        hub = s.Bases(0).Single();
        foreach (var t in s.Map.Grid.Within(site, 8)) s.Players[0].Surveyed[t] = true;
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.True(refused == sim.RefusedCommands, $"refused: {sim.LastRefusal}");
    }

    private static SurfaceUnit Rover(Simulation sim, int k = 0) => sim.Surface!.Units.Where(u => u.Owner == 0 && u.DefId == "survey_rover").ElementAt(k);

    /// <summary>A passable tile at this distance from the hub with no building, that a rover can drive to.</summary>
    private static int Ring(Simulation sim, SurfaceStructure hub, int distance, Func<int, bool>? also = null)
    {
        var s = sim.Surface!;
        return s.Map.Grid.Within(hub.Tile, distance).First(t => s.Map.Grid.Distance(hub.Tile, t) == distance && s.Map.Terrain(t).Passable
            && s.StructureAt(t) == null && (also?.Invoke(t) ?? true));
    }

    /// <summary>Steps until the unit starts a transit (returns it) or the time runs out (null).</summary>
    private static DomeTransit? NextTransit(Simulation sim, SurfaceUnit u, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            sim.Step();
            if (sim.Surface!.DomeTransitOf(u) is { } t) return t;
        }
        return null;
    }

    [Fact]
    public void LeavingAndEnteringTheCoresFieldPausesAtTheEdge()
    {
        var sim = Veyra(out var hub);
        var s = sim.Surface!;
        var rover = Rover(sim);
        Assert.True(s.Covered(0, rover.Tile));
        var outside = Ring(sim, hub, 3);
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, outside));
        var t = NextTransit(sim, rover, 60 * Simulation.TickRate)!.Value;
        Assert.False(t.Entering);
        Assert.Equal(hub.Id, t.StructureId);
        Assert.True(s.Covered(0, t.FromTile));
        Assert.False(s.Covered(0, t.ToTile));
        Assert.Equal(rover.Tile, t.FromTile);
        Assert.Equal(1, s.Map.Grid.Distance(t.FromTile, t.ToTile));
        Assert.Equal(rover.Pos, t.Contact);
        Assert.Equal(s.Crossing(t.FromTile, t.ToTile), t.Contact); // the edge where the shell (r + ½) stands
        Assert.Equal(Fix64.Zero, t.Fraction);
        Assert.Equal(t.StartTick + Pause, t.EndTick);
        Assert.Null(s.LastDomeTransitOf(rover));

        // It waits there for transit_seconds, the fraction rising, then drives over.
        var ticks = 1;
        var last = Fix64.Zero;
        while (s.DomeTransitOf(rover) is { } now)
        {
            Assert.Equal(t.Contact, rover.Pos);
            Assert.Equal(t.FromTile, rover.Tile);
            Assert.True(now.Fraction >= last);
            last = now.Fraction;
            sim.Step();
            ticks++;
        }
        Assert.Equal(Pause, ticks - 1);
        Assert.Equal(37, Pause); // 2.5 s
        Assert.Equal(t.ToTile, rover.Tile);
        var done = s.LastDomeTransitOf(rover)!.Value;
        Assert.Equal(Fix64.One, done.Fraction);
        Assert.Equal(t.StartTick, done.StartTick);
        Assert.Equal(t.ToTile, done.ToTile);
        Assert.NotEqual(t.Contact, rover.Pos);

        // Out there it drives on with no more stops, then back in through the field.
        Assert.Null(NextTransit(sim, rover, 40 * Simulation.TickRate));
        Assert.Equal(outside, rover.Tile);
        var inside = Ring(sim, hub, 1);
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, inside));
        var back = NextTransit(sim, rover, 60 * Simulation.TickRate)!.Value;
        Assert.True(back.Entering);
        Assert.Equal(hub.Id, back.StructureId);
        Assert.False(s.Covered(0, back.FromTile));
        Assert.True(s.Covered(0, back.ToTile));
        while (rover.Driving) sim.Step();
        Assert.Equal(inside, rover.Tile);
        Assert.Empty(CommandFuzzTests.Problems(sim));
    }

    [Fact]
    public void EnteringAPressureDomeUsesTheDomesField()
    {
        var sim = Veyra(out var hub);
        var s = sim.Surface!;
        var domeTile = s.Map.Grid.Within(hub.Tile, 3).First(t => s.Map.Grid.Distance(hub.Tile, t) == 3
            && s.DebugProblem(sim, new DebugPlaceBuildingCommand(0, 0, "pressure_dome", t, 0)) == null);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "pressure_dome", domeTile, 0));
        var dome = s.StructureAt(domeTile)!;
        Assert.Equal(2, s.DomeRadiusOf(dome));
        var rover = Rover(sim);
        var start = s.Map.Grid.Within(domeTile, 4).First(t => s.Map.Grid.Distance(domeTile, t) == 4 && s.Map.Grid.Distance(hub.Tile, t) >= 6
            && s.Map.Terrain(t).Passable && s.StructureAt(t) == null && !s.Covered(0, t));
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, start));
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, domeTile));
        Assert.True(rover.Driving);
        var t = NextTransit(sim, rover, 80 * Simulation.TickRate)!.Value;
        Assert.True(t.Entering);
        Assert.Equal(dome.Id, t.StructureId);
        Assert.Equal(2, s.Map.Grid.Distance(domeTile, t.ToTile));
    }

    [Fact]
    public void ANewOrderOverTheSameEdgeKeepsItAnyOtherCancelsIt()
    {
        var sim = Veyra(out var hub);
        var s = sim.Surface!;
        var rover = Rover(sim);
        var outside = Ring(sim, hub, 3);
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, outside));
        var t = NextTransit(sim, rover, 60 * Simulation.TickRate)!.Value;
        sim.Step();
        // The same order again: the path still starts over this edge, so the transit goes on.
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, outside));
        Assert.Equal(t.StartTick, s.DomeTransitOf(rover)!.Value.StartTick);
        // An order back in: cancelled, no after-glow recorded, it turns round.
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, hub.Tile));
        Assert.Null(s.DomeTransitOf(rover));
        Assert.Null(s.LastDomeTransitOf(rover));
        Assert.Equal(t.FromTile, rover.Tile);

        // A halt (the test panel's teleport) cancels it too.
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, outside));
        Assert.NotNull(NextTransit(sim, rover, 60 * Simulation.TickRate));
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, Ring(sim, hub, 1)));
        Assert.Null(s.DomeTransitOf(rover));
        Assert.False(rover.Driving);
    }

    [Fact]
    public void AVehicleBehindQueuesUntilTheTransitIsDone()
    {
        var sim = Veyra(out var hub);
        var s = sim.Surface!;
        var (a, b) = (Rover(sim, 0), Rover(sim, 1));
        var near = Ring(sim, hub, 1);
        var outside = Ring(sim, hub, 4, t => s.Map.Grid.Distance(near, t) == 3);
        Do(sim, new DebugMoveUnitCommand(0, a.Id, near));
        Do(sim, new DebugMoveUnitCommand(0, b.Id, near));
        Do(sim, new MoveUnitsCommand(0, new[] { a.Id }, outside));
        for (var i = 0; i < 8; i++) sim.Step();
        Do(sim, new MoveUnitsCommand(0, new[] { b.Id }, outside));
        var queued = false;
        for (var i = 0; i < 60 * Simulation.TickRate && (a.Driving || b.Driving); i++)
        {
            sim.Step();
            if (s.DomeTransitOf(a) != null && b.BlockedBy == a.Id) queued = true;
            if (s.DomeTransitOf(b) != null && a.BlockedBy == b.Id) queued = true;
        }
        Assert.True(queued, "the second rover never waited behind the first's transit");
        Assert.NotNull(s.LastDomeTransitOf(a));
        Assert.NotNull(s.LastDomeTransitOf(b));
    }

    [Fact]
    public void TransitsAreDeterministicAndInTheHash()
    {
        ulong Play()
        {
            var sim = Veyra(out var hub);
            sim.Schedule(sim.Tick, new MoveUnitsCommand(0, new[] { Rover(sim).Id }, Ring(sim, hub, 3)));
            for (var i = 0; i < 30 * Simulation.TickRate; i++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());

        var g = Veyra(out var h);
        var rover = Rover(g);
        g.Schedule(g.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, Ring(g, h, 3)));
        Assert.NotNull(NextTransit(g, rover, 60 * Simulation.TickRate));
        var left = typeof(SurfaceUnit).GetProperty("TransitLeft", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var transit = typeof(SurfaceUnit).GetProperty("Transit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var before = g.StateHash();
        var value = (int)left.GetValue(rover)!;
        left.SetValue(rover, value + 1);
        Assert.NotEqual(before, g.StateHash());
        left.SetValue(rover, value);
        var t = (DomeTransit)transit.GetValue(rover)!;
        foreach (var edited in new[] { t with { StructureId = t.StructureId + 1 }, t with { Entering = !t.Entering }, t with { Fraction = t.Fraction + Fix64.Half },
                     t with { Contact = t.Contact + new FixVec2(Fix64.One, Fix64.Zero) }, t with { FromTile = t.FromTile + 1 }, t with { ToTile = t.ToTile + 1 },
                     t with { StartTick = t.StartTick + 1 }, t with { EndTick = t.EndTick + 1 } })
        {
            transit.SetValue(rover, edited);
            Assert.NotEqual(before, g.StateHash());
        }
        transit.SetValue(rover, t);
        Assert.Equal(before, g.StateHash());
    }

    [Fact]
    public void OnClassMNothingTransits()
    {
        var sim = DroneTests.Landed(out var hub);
        var s = sim.Surface!;
        Assert.False(s.NeedsDomes);
        var rover = s.Units.First(u => u.Owner == 0 && u.DefId == "survey_rover");
        var far = s.Map.Grid.Within(hub.Tile, 5).First(t => s.Map.Grid.Distance(hub.Tile, t) == 5 && s.Map.Terrain(t).Passable && s.StructureAt(t) == null);
        DroneTests.Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, far));
        for (var i = 0; i < 40 * Simulation.TickRate && rover.Driving; i++)
        {
            sim.Step();
            Assert.Null(s.DomeTransitOf(rover));
        }
        Assert.Equal(far, rover.Tile);
        Assert.Null(s.LastDomeTransitOf(rover));
    }
}
