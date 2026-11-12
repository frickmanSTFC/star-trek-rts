using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Deploying a 2nd, 3rd … satellite (owner 2026-11-12: "we were able to deploy only 1. The 2nd one didn't seem to exit
/// the ship even though the orbit does show"). Every survey satellite unfolded over column 0 whatever was clicked, so
/// once the globe had turned to the colony a new one flew off behind the planet; it now unfolds over the clicked column
/// (SatelliteCommand.Column) and sweeps on from there. Each satellite and each relay is its own object with its own
/// id, call sign, band or station and timing; the max is refused (not stuck), each one is paid for.
/// </summary>
public class SatelliteDeployTests
{
    private static GameData Data => TestData.Real;
    private static OrbitRules Orbit => Data.Orbit!;
    private static int Sec(int s) => s * Simulation.TickRate;

    private static bool Do(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        return sim.RefusedCommands == refused;
    }

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    private static Simulation Game() => MatchSetup.CreatePlanetLanding(Data, 1701);

    /// <summary>A landed game with the outpost's uplink up and plenty of stock (relays need both).</summary>
    private static Simulation Landed()
    {
        var sim = Game();
        var map = sim.Surface!.Map;
        sim.Schedule(0, new LandColonyCommand(0, Enumerable.Range(0, map.Grid.Count).First(t => sim.Surface.CanLand(0, t))));
        Run(sim, TestData.ReadyTicks(Data) + 1);
        Do(sim, new DebugStockCommand(0, 0, new[] { new ResourceAmount(Data.Resource("duranium").Index, Fix64.FromInt(5000)),
            new ResourceAmount(Data.Resource("tritanium").Index, Fix64.FromInt(5000)) }));
        Do(sim, new DebugFlagCommand(0, 0, DebugFlag.NoStorageCap, true));
        return sim;
    }

    [Fact]
    public void EachSatelliteUnfoldsOverTheColumnClickedAndSweepsOnFromThere()
    {
        var sim = Game();
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var orders = new[] { (Row: g.Height / 2, Column: 0), (Row: 12, Column: 70), (Row: g.Height - 14, Column: 101) };
        foreach (var (row, column) in orders) Assert.True(Do(sim, new SatelliteCommand(0, row, column)), sim.LastRefusal);
        var sats = s.Satellites.Where(x => x.Owner == 0).ToList();
        Assert.Equal(3, sats.Count);
        Assert.Equal(3, sats.Select(x => x.Id).Distinct().Count());
        Assert.Equal(new[] { 1, 2, 3 }, sats.Select(x => x.Ordinal));
        Assert.Equal(3, sats.Select(x => x.CallSign).Distinct().Count());
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(orders[i].Row, sats[i].Row);
            Assert.Equal(orders[i].Column, sats[i].StartColumn);
            Assert.Equal(orders[i].Column, s.SatelliteColumn(sats[i]));
            Assert.Equal(sats[i].LaunchTick + Sec(Orbit.SatelliteDeploySeconds), sats[i].ReadyTick);
        }

        // A few seconds of sweeping each: the tiles under the second satellite's start column are surveyed, column 0 of
        // its band (behind where it unfolded) is not.
        Run(sim, (int)(sats[2].ReadyTick - sim.Tick) + Sec(3));
        var second = sats[1];
        Assert.True(second.Swept > 0 && second.Swept < 20);
        Assert.True(s.Players[0].Surveyed[g.Index(70, second.Row)]);
        Assert.True(s.Players[0].Surveyed[g.Index(70 + second.Swept - 1, second.Row)]);
        Assert.False(s.Players[0].Surveyed[g.Index(68, second.Row)]);
        Assert.Equal((70 + second.Swept) % g.Width, s.SatelliteColumn(second));
    }

    [Fact]
    public void AFullLapFromAnyColumnSurveysTheWholeBandOnce()
    {
        var sim = Game();
        var s = sim.Surface!;
        var g = s.Map.Grid;
        Assert.True(Do(sim, new SatelliteCommand(0, 30, 90)));
        var sat = s.Satellites.Single();
        Run(sim, (int)(sat.ReadyTick - sim.Tick) + Sec(Orbit.SatelliteSweepSeconds) + 2);
        Assert.Equal(1, sat.Passes);
        Assert.Equal(Fix64.One, s.SatelliteCoverage(sat));
        var (first, last) = s.SatelliteBand(sat);
        Assert.Equal((last - first + 1) * g.Width, sat.TilesSurveyed);
    }

    [Fact]
    public void TheMaxIsRefusedEachIsPaidForAndBadColumnsChangeNothing()
    {
        var sim = Game();
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var stock = sim.Players[0].Stock;
        var dur = Data.Resource("duranium").Index;
        var cost = Orbit.SatelliteCost.First(c => c.Resource == dur).Amount;
        for (var i = 0; i < Orbit.SatelliteMax; i++)
        {
            var before = stock[dur];
            Assert.True(Do(sim, new SatelliteCommand(0, 10 + i * 20, i * 30)), sim.LastRefusal);
            Assert.Equal(before - cost, stock[dur]);
            if (i + 1 < Orbit.SatelliteMax) Assert.Null(s.SatelliteProblem(sim, 0, 5, 5));
        }
        Assert.Equal($"all {Orbit.SatelliteMax} satellites are up", s.SatelliteProblem(sim, 0, 5, 5));
        Assert.False(Do(sim, new SatelliteCommand(0, 5, 5)));
        Assert.Contains("satellites are up", sim.LastRefusal);

        // A fresh game: off-map columns are refused and leave the game as it was.
        var other = Game();
        foreach (var column in new[] { -1, g.Width, int.MaxValue, int.MinValue })
        {
            other.Step();
            var before = other.Surface!.Satellites.Count;
            Assert.False(Do(other, new SatelliteCommand(0, 5, column)));
            Assert.Contains("off the map", other.LastRefusal);
            Assert.Equal(before, other.Surface!.Satellites.Count);
        }
    }

    [Fact]
    public void TheColumnIsPartOfTheQueuedCommandsHash()
    {
        var a = Game();
        var b = Game();
        a.Schedule(5, new SatelliteCommand(0, 10, 3));
        b.Schedule(5, new SatelliteCommand(0, 10, 4));
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Run(a, 6);
        Run(b, 6);
        Assert.NotEqual(a.StateHash(), b.StateHash()); // and of the satellite's state once it is up
    }

    [Fact]
    public void ThreeRelaysEachGoUpToTheirOwnStation()
    {
        var sim = Landed();
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var relay = Data.Comms!.Relay!;
        var hub = s.Bases(0)[0].Tile;
        var stations = g.Within(hub, 20).Where(t => g.Distance(t, hub) is 0 or 10 or 20).Take(relay.Max).ToList();
        Assert.Equal(relay.Max, stations.Count);
        foreach (var t in stations)
        {
            Assert.True(Do(sim, new RelaySatelliteCommand(0, t)), sim.LastRefusal);
            Run(sim, Sec(2)); // launched a little apart, like a player would
        }
        var relays = s.Satellites.Where(x => x.Kind == SatelliteKind.Relay).ToList();
        Assert.Equal(stations, relays.Select(x => x.StationTile));
        Assert.Equal(relay.Max, relays.Select(x => x.Id).Distinct().Count());
        Assert.Equal(relay.Max, relays.Select(x => x.CallSign).Distinct().Count());
        Assert.StartsWith("RELAY-2 · ", relays[1].CallSign);
        Assert.Equal($"all {relay.Max} relay satellites are up", s.RelayProblem(sim, 0, hub));
        Assert.False(Do(sim, new RelaySatelliteCommand(0, hub)));

        Run(sim, Sec(relay.DeploySeconds));
        var sources = s.CommsSources(0).Where(c => c.Kind == CommsSourceKind.Relay).ToList();
        Assert.Equal(relay.Max, sources.Count);
        Assert.All(sources, c => Assert.True(c.Ready));
        Assert.All(stations, t => Assert.True(s.CommsCovered(0, t)));
    }
}
