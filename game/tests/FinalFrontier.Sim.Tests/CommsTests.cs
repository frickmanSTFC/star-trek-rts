using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Comms coverage (docs/missions-contract.md B): each finished colony core's antenna, relay satellites put up from
/// orbit (cost, max, deploy drift, retasking the station, call signs, separate from survey satellites), the sources list,
/// refusals and the hash.</summary>
public class CommsTests
{
    private static GameData Data => TestData.Real;
    private static CommsRules Comms => Data.Comms!;
    private static RelayRules Relay => Comms.Relay!;
    private static int R(string id) => Data.Resource(id).Index;
    private static int Sec(int s) => s * Simulation.TickRate;

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    private static string Refused(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.Equal(refused + 1, sim.RefusedCommands);
        return sim.LastRefusal ?? "";
    }

    private static int AtDistance(PlanetSurface s, int centre, int d) =>
        s.Map.Grid.Within(centre, d).First(t => s.Map.Grid.Distance(centre, t) == d);

    [Fact]
    public void TheCoresAntennaCoversCoreRadius()
    {
        var sim = DroneTests.Landed(out var hub);
        var s = sim.Surface!;
        Assert.Equal(5, Comms.CoreRadius);
        foreach (var t in s.Map.Grid.Within(hub.Tile, 8))
            Assert.Equal(s.Map.Grid.Distance(hub.Tile, t) <= Comms.CoreRadius, s.CommsCovered(0, t));
        Assert.False(s.CommsCovered(0, -1));
        Assert.False(s.CommsCovered(0, s.Map.Grid.Count));
        Assert.False(s.CommsCovered(5, hub.Tile));
        var source = Assert.Single(s.CommsSources(0));
        Assert.Equal(new CommsSource(CommsSourceKind.Core, hub.Tile, 5, hub.Id, 0, true), source);
        Assert.Empty(s.CommsSources(9));
    }

    [Fact]
    public void AnAssemblingHubCoversNothing()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        Run(sim, Data.Landing.DescentSeconds * Simulation.TickRate + 5);
        Assert.Equal(StructureState.Assembling, s.Bases(0).Single().State);
        Assert.False(s.CommsCovered(0, site));
        Assert.Empty(s.CommsSources(0));
        Assert.Equal("the uplink isn't online yet", s.RelayProblem(sim, 0, site));
    }

    [Fact]
    public void ARelayDriftsToStationThenCoversItsRadius()
    {
        var sim = DroneTests.Landed(out var hub);
        var s = sim.Surface!;
        var stock = sim.Players[0].Stock;
        var station = AtDistance(s, hub.Tile, 20);
        var duranium = stock[R("duranium")];
        DroneTests.Do(sim, new RelaySatelliteCommand(0, station));
        var relay = s.Satellites.Single();
        Assert.Equal(SatelliteKind.Relay, relay.Kind);
        Assert.Equal(station, relay.StationTile);
        Assert.Equal(s.Map.Grid.Row(station), relay.Row);
        Assert.StartsWith("RELAY-1 · ", relay.CallSign);
        Assert.True(duranium - stock[R("duranium")] >= Fix64.FromInt(119)); // 120 duranium (less a tick of the hub's trickle)
        Assert.True(relay.Deploying(sim.Tick));
        Assert.False(s.CommsCovered(0, station));
        Assert.Equal(2, s.CommsSources(0).Count);
        Assert.False(s.CommsSources(0)[1].Ready);

        Run(sim, (int)(relay.ReadyTick - sim.Tick) + 1);
        Assert.True(s.CommsCovered(0, station));
        foreach (var t in s.Map.Grid.Within(station, Relay.Radius + 2))
            if (s.Map.Grid.Distance(hub.Tile, t) > Comms.CoreRadius)
                Assert.Equal(s.Map.Grid.Distance(station, t) <= Relay.Radius, s.CommsCovered(0, t));
        Assert.Equal(new CommsSource(CommsSourceKind.Relay, station, 14, 0, relay.Id, true), s.CommsSources(0)[1]);
        // Relays don't sweep or survey.
        Run(sim, Sec(20));
        Assert.Equal(0, relay.Swept);
        Assert.Equal(0, relay.TilesSurveyed);
    }

    [Fact]
    public void RetaskingARelayMovesItsStation()
    {
        var sim = DroneTests.Landed(out var hub);
        var s = sim.Surface!;
        DroneTests.Do(sim, new RelaySatelliteCommand(0, hub.Tile));
        var relay = s.Satellites.Single();
        Assert.Equal("still drifting to its station", s.RetaskProblem(sim, 0, relay.Id, hub.Tile));
        Run(sim, Sec(Relay.DeploySeconds) + 1);
        Assert.Equal("already over that tile", s.RetaskProblem(sim, 0, relay.Id, hub.Tile));
        Assert.Equal("off the map", s.RetaskProblem(sim, 0, relay.Id, -3));
        var next = AtDistance(s, hub.Tile, 30);
        DroneTests.Do(sim, new RetaskSatelliteCommand(0, relay.Id, next));
        Assert.Equal(next, relay.StationTile);
        Assert.Equal(s.Map.Grid.Row(next), relay.Row);
        Assert.Equal(1, relay.Retasks);
        Assert.False(s.CommsCovered(0, next));
        Assert.Equal("still moving to its new station", s.RetaskProblem(sim, 0, relay.Id, hub.Tile));
        Run(sim, Sec(Relay.RetaskSeconds) + 1);
        Assert.True(s.CommsCovered(0, next));
        Assert.False(s.CommsCovered(0, AtDistance(s, hub.Tile, 8)));
    }

    [Fact]
    public void RelaysAndSurveySatellitesCountSeparately()
    {
        var sim = DroneTests.Landed(out var hub);
        var s = sim.Surface!;
        foreach (var r in new[] { "duranium", "tritanium" }) sim.Players[0].Stock.Add(R(r), Fix64.FromInt(500));
        for (var i = 0; i < Relay.Max; i++) DroneTests.Do(sim, new RelaySatelliteCommand(0, hub.Tile));
        Assert.EndsWith($"all {Relay.Max} relay satellites are up", Refused(sim, new RelaySatelliteCommand(0, hub.Tile)));
        Assert.Equal(new[] { "RELAY-1", "RELAY-2", "RELAY-3" }, s.Satellites.Select(x => x.CallSign.Split(' ')[0]));
        Assert.Equal(Relay.Max, s.RelayCount(0));
        // A survey satellite still goes up and is SAT-1.
        Assert.Null(s.SatelliteProblem(sim, 0, 3));
        DroneTests.Do(sim, new SatelliteCommand(0, 3));
        var sat = s.Satellites.Last();
        Assert.Equal(SatelliteKind.Survey, sat.Kind);
        Assert.Equal(-1, sat.StationTile);
        Assert.StartsWith("SAT-1 · ", sat.CallSign);
        Assert.Equal(Relay.Max, s.RelayCount(0));
    }

    [Fact]
    public void BadRelayOrdersAreRefused()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        var s = sim.Surface!;
        Assert.Contains("land first", Refused(sim, new RelaySatelliteCommand(0, 10)));
        Assert.Equal("no such player", s.RelayProblem(sim, 4, 10));
        var landed = DroneTests.Landed(out var hub);
        var ls = landed.Surface!;
        Assert.EndsWith("off the map", Refused(landed, new RelaySatelliteCommand(0, -1)));
        Assert.EndsWith("off the map", Refused(landed, new RelaySatelliteCommand(0, ls.Map.Grid.Count)));
        var stock = landed.Players[0].Stock;
        stock.Add(R("duranium"), -stock[R("duranium")]);
        Assert.EndsWith("not enough resources", Refused(landed, new RelaySatelliteCommand(0, hub.Tile)));
        Assert.Empty(ls.Satellites);
        var noRelays = TestData.Patched(("surfaces.json", "\"relay\": { \"cost\"", "\"relay_off\": { \"cost\""));
        var sim3 = MatchSetup.CreatePlanetLanding(noRelays, 1701);
        Assert.Equal("no relay satellites in this game", sim3.Surface!.RelayProblem(sim3, 0, 10));
    }

    [Fact]
    public void RelayStateIsInTheHashAndDeterministic()
    {
        var sim = DroneTests.Landed(out var hub);
        var s = sim.Surface!;
        DroneTests.Do(sim, new RelaySatelliteCommand(0, hub.Tile));
        var relay = s.Satellites.Single();
        var before = sim.StateHash();
        var station = typeof(Satellite).GetProperty(nameof(Satellite.StationTile))!;
        station.SetValue(relay, relay.StationTile + 1);
        Assert.NotEqual(before, sim.StateHash());
        station.SetValue(relay, relay.StationTile - 1);
        Assert.Equal(before, sim.StateHash());

        ulong Play()
        {
            var g = DroneTests.Landed(out var h);
            g.Schedule(g.Tick, new RelaySatelliteCommand(0, AtDistance(g.Surface!, h.Tile, 9)));
            Run(g, Sec(15));
            g.Schedule(g.Tick, new RetaskSatelliteCommand(0, g.Surface!.Satellites.Single().Id, h.Tile));
            Run(g, Sec(15));
            return g.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void CoverageAlongALineAndTileAtPos()
    {
        var sim = DroneTests.Landed(out var hub);
        var s = sim.Surface!;
        for (var t = 0; t < s.Map.Grid.Count; t += 97) Assert.Equal(t, s.TileAtPos(s.CentreOf(t)));
        var inside = AtDistance(s, hub.Tile, 4);
        Assert.True(s.CommsCoveredLine(0, s.CentreOf(hub.Tile), s.CentreOf(inside)));
        Assert.False(s.CommsCoveredLine(0, s.CentreOf(hub.Tile), s.CentreOf(AtDistance(s, hub.Tile, 7))));
    }
}
