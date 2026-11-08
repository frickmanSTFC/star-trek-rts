using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;
using Xunit;

namespace FinalFrontier.Sim.Tests;

/// <summary>The signal delay to the colony while the flagship is away, and the orbital sensor sweep (owner 2026-11-08).</summary>
public class CommsAndSweepTests
{
    private static readonly GameData Data = TestData.Real;

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    /// <summary>A system game with the colony landed and assembled.</summary>
    private static Simulation Landed(ulong seed = 1701)
    {
        var sim = MatchSetup.CreateSystemGame(Data, seed);
        var tile = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(sim.Tick, new LandColonyCommand(0, tile));
        Run(sim, TestData.ReadyTicks(Data) + 2);
        return sim;
    }

    private static string FarthestFromHome(StarSystemState sys)
    {
        var home = sys.Body(sys.SurfaceBody)!.Position;
        return sys.Bodies.OrderBy(b => FixVec2.Distance(b.Position, home)).Last().Id;
    }

    [Fact]
    public void NoDelayInOrbitOfTheColonyAndUpToTenSecondsAway()
    {
        var sim = Landed();
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        Assert.Equal(0, sys.CommsDelayTicks(0));
        var far = FarthestFromHome(sys);
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, far));
        sim.Step();
        var delays = new System.Collections.Generic.List<long>();
        while (ship.AtBody == null)
        {
            delays.Add(sys.CommsDelayTicks(0));
            sim.Step();
        }
        Assert.All(delays, d => Assert.InRange(d, Simulation.TickRate, 10 * Simulation.TickRate));
        Assert.True(delays.Last() > delays.First(), "the delay grows with the distance");
        // In orbit of another body: still away.
        Assert.InRange(sys.CommsDelaySeconds(0), 2, 10);
        // Planet-only games have none (no system): orders arrive at once.
        Assert.Null(MatchSetup.CreatePlanetLanding(Data, 1701, "federation", "class_m").System);
    }

    [Fact]
    public void ColonyOrdersWaitForTheSignalDelayAndAreHashed()
    {
        var sim = Landed();
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        var rover = sim.Surface!.Units.First(u => u.Owner == 0 && u.DefId.StartsWith("survey_rover"));
        var goal = sim.Surface.Map.Grid.Neighbours(rover.Tile).First(t => sim.Surface.Map.Terrain(t).Passable);

        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, FarthestFromHome(sys)));
        Run(sim, 30);
        var delay = sys.CommsDelayTicks(0);
        Assert.True(delay > 0);
        var sent = sim.Tick;
        sim.Schedule(sim.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, goal));
        var before = sim.StateHash();
        sim.Step();
        Assert.Single(sim.OrdersInTransit);
        Assert.Equal(sent + delay, sim.OrdersInTransit[0].ArriveTick);
        Assert.Empty(rover.Path); // not there yet
        Assert.NotEqual(before, sim.StateHash());

        // Ship commands are not delayed: the ship is where the player is.
        sim.Schedule(sim.Tick, new LongRangeScanCommand(0, ship.Id));
        sim.Step();
        Assert.Single(sim.OrdersInTransit);

        while (sim.Tick <= sent + delay) sim.Step();
        Assert.Empty(sim.OrdersInTransit);
        Assert.NotEmpty(rover.Path); // the order arrived and the rover drives
    }

    [Fact]
    public void OrdersArriveAtOnceInOrbitOfTheColony()
    {
        var sim = Landed();
        var rover = sim.Surface!.Units.First(u => u.Owner == 0 && u.DefId.StartsWith("survey_rover"));
        var goal = sim.Surface.Map.Grid.Neighbours(rover.Tile).First(t => sim.Surface.Map.Terrain(t).Passable);
        sim.Schedule(sim.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, goal));
        sim.Step();
        Assert.Empty(sim.OrdersInTransit);
        Assert.NotEmpty(rover.Path);
    }

    [Fact]
    public void SameOrdersGiveTheSameGame()
    {
        long Play()
        {
            var sim = Landed(99999);
            var sys = sim.System!;
            var ship = sys.FlagshipOf(0)!;
            var rover = sim.Surface!.Units.First(u => u.Owner == 0 && u.DefId.StartsWith("survey_rover"));
            sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, FarthestFromHome(sys)));
            for (var i = 0; i < 6; i++)
            {
                Run(sim, 40);
                var goal = sim.Surface.Map.Grid.Neighbours(rover.Tile).First(t => sim.Surface.Map.Terrain(t).Passable);
                sim.Schedule(sim.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, goal));
            }
            Run(sim, 400);
            return (long)sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void LeavingOrbitCallsTheSweepOff()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 1701);
        var sys = sim.System!;
        var ship = sys.FlagshipOf(0)!;
        var home = sys.SurfaceBody!;
        var target = sys.Bodies.First(b => b.Id != home && sys.KnowledgeOf(0, b.Id) == BodyKnowledge.Detected).Id;
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, target));
        Run(sim, (int)sys.TravelTicks(home, target) + 2);
        Assert.True(ship.Sweeping);
        Assert.InRange(sys.SweepShare(ship).ToDouble(), 0, 0.5);
        sim.Schedule(sim.Tick, new SetCourseCommand(0, ship.Id, home));
        sim.Step();
        Assert.False(ship.Sweeping);
        Run(sim, (int)sys.SweepTicks + 10);
        Assert.Equal(BodyKnowledge.Scanned, sys.KnowledgeOf(0, target)); // identified, never charted
        // Back home: Charted already, so no sweep there.
        Run(sim, (int)sys.TravelTicks(home, target));
        Assert.Equal(home, ship.AtBody);
        Assert.False(ship.Sweeping);
    }

    [Fact]
    public void OrbitalSensorsSeeWhatIsBuiltOnlyWithLiveData()
    {
        var sim = MatchSetup.CreateSystemGame(Data, 1701);
        var sys = sim.System!;
        var home = sys.SurfaceBody!;
        // In orbit of the (Charted) colony body: the ship's sensors are live there, nothing built yet.
        Assert.True(sys.LiveFromShip(0, home));
        Assert.Empty(sys.Structures(0, home));
        var tile = Enumerable.Range(0, sim.Surface!.Map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(sim.Tick, new LandColonyCommand(0, tile));
        Run(sim, TestData.ReadyTicks(Data) + 2);
        var seen = Assert.Single(sys.Structures(0, home));
        Assert.Equal(0, seen.Owner);
        Assert.Equal(1, seen.Bases);
        Assert.Equal(sim.Surface.Units.Count(u => u.Owner == 0), seen.Vehicles);
        Assert.Equal(sys.Structures(0, home), sys.Report(0, home)!.Structures);
        // A body with no live data shows nothing.
        var other = sys.Bodies.First(b => b.Id != home).Id;
        Assert.Empty(sys.Structures(0, other));
        Assert.Empty(sys.Report(0, other)!.Structures);
    }

    [Fact]
    public void BadCommsAndSweepDataIsReported()
    {
        void Bad(string from, string to, string problem) =>
            Assert.Contains(TestData.Patched(("maps/system.json", from, to)).Validate(), p => p.Contains(problem, System.StringComparison.Ordinal));
        Bad("\"sweep\": { \"seconds\": 15 }", "\"sweep\": { \"seconds\": 0 }", "sweep seconds");
        Bad("\"delay_seconds\": [1, 10]", "\"delay_seconds\": [8, 2]", "comms delay_seconds");
        Bad("\"delay_seconds\": [1, 10], \"distance\": [0, 1000]", "\"delay_seconds\": [1, 10], \"distance\": [500, 100]", "comms distance");
    }
}
