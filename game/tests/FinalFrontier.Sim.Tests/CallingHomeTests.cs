using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Surface;
using static FinalFrontier.Sim.Tests.MissionTests;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// First Light's Calling Home (owner 2026-11-12: "the relay didn't seem to work for the mission that said to return the
/// wreckage"). Played with real RelaySatelliteCommands after The Lost Pathfinder: the wreck keeps its marker in the
/// "Link the wreck" stage (it used to vanish because the wreck site was done in the previous mission), the tracker's
/// live detail says what is out of coverage and how far, each stage completes within a second of its relay reaching
/// its station, and a second base doesn't stand in the way of "a relay over the outpost".
/// </summary>
public class CallingHomeTests
{
    /// <summary>A game with The Lost Pathfinder done and Calling Home just started, stock topped up.</summary>
    private static (Simulation Sim, int Hub, int Wreck) Start()
    {
        var sim = PlayFirstLight("lost_pathfinder");
        TopUp(sim);
        Seconds(sim, 1);
        Assert.Equal(MissionState.Active, State(sim, "calling_home"));
        Assert.Equal("Put up a relay", View(sim, "calling_home").StageTitle);
        var s = sim.Surface!;
        return (sim, s.Bases(0)[0].Tile, Assert.Single(sim.Missions.Sites(0)).Tile);
    }

    private static string Detail(Simulation sim) => View(sim, "calling_home").Objectives.First(o => !o.Done).Detail;

    /// <summary>Waits for the player's newest relay to reach its station, then at most one more second for the missions.</summary>
    private static Satellite RelayOnStation(Simulation sim)
    {
        var s = sim.Surface!;
        var relay = s.Satellites.Last(x => x.Owner == 0 && x.Kind == SatelliteKind.Relay);
        StepUntil(sim, () => !relay.Deploying(sim.Tick), 30, "relay on station");
        return relay;
    }

    /// <summary>A tile within reach of every tile in `near` (hexes ≤ reach) and beyond reach of every tile in `far`.</summary>
    private static int StationFor(Simulation sim, int[] near, int[] far)
    {
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var reach = sim.Data.Comms!.Relay!.Radius;
        return Enumerable.Range(0, g.Count).First(t => near.All(n => g.Distance(t, n) <= reach) && far.All(f => g.Distance(t, f) > reach));
    }

    [Fact]
    public void TheWreckKeepsItsMarkerWhileItIsLinked()
    {
        var (sim, hub, wreck) = Start();
        Assert.True(Assert.Single(sim.Missions.Sites(0)).Done); // the recorder came out in The Lost Pathfinder
        Do(sim, new RelaySatelliteCommand(0, StationFor(sim, new[] { hub }, new[] { wreck })));
        RelayOnStation(sim);
        Seconds(sim, 1);
        Assert.Equal("Link the wreck", View(sim, "calling_home").StageTitle);
        var marker = Assert.Single(sim.Missions.Markers(0), m => m.MissionId == "calling_home");
        Assert.Equal(wreck, marker.Tile);
        Assert.Equal("Pathfinder IV", marker.Label);
        Assert.Contains($"{sim.Surface!.Map.Grid.Distance(hub, wreck)} hexes", View(sim, "calling_home").Hint);
    }

    [Fact]
    public void OneRelayBetweenTheOutpostAndTheWreckDoesBothStages()
    {
        var (sim, hub, wreck) = Start();
        Assert.StartsWith("No relay satellite up yet", Detail(sim));
        Do(sim, new RelaySatelliteCommand(0, StationFor(sim, new[] { hub, wreck }, System.Array.Empty<int>())));
        Assert.Contains("is drifting to its station", Detail(sim));
        var relay = RelayOnStation(sim);
        var onStation = sim.Tick;
        StepUntil(sim, () => State(sim, "calling_home") == MissionState.Completed, 3, "both stages");
        Assert.True(sim.Tick - onStation <= 2 * Simulation.TickRate + 1, "each stage completes within a second of being true");
        Assert.Contains("earth_link", sim.Missions.Flags(0));
        Assert.Equal(1, sim.Surface!.RelayCount(0));
        Assert.True(sim.Surface.CommsCovered(0, wreck) && sim.Surface.CommsCovered(0, hub));
        Assert.Equal(relay.StationTile, sim.Surface.CommsSources(0).Single(c => c.Kind == CommsSourceKind.Relay).Tile);
    }

    [Fact]
    public void AWreckOutOfReachSaysHowFarAndASecondRelayLinksIt()
    {
        var (sim, hub, wreck) = Start();
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var reach = sim.Data.Comms!.Relay!.Radius;
        var first = StationFor(sim, new[] { hub }, new[] { wreck });
        Do(sim, new RelaySatelliteCommand(0, first));
        RelayOnStation(sim);
        Seconds(sim, 1);
        Assert.Equal("Link the wreck", View(sim, "calling_home").StageTitle);
        Seconds(sim, 3);
        Assert.Equal(MissionState.Active, State(sim, "calling_home")); // it really is out of reach
        Assert.Equal($"Pathfinder IV is {g.Distance(first, wreck)} hexes from the nearest relay station; a relay reaches {reach}. " +
            $"From orbit: RELAY, then click within {reach} hexes of its marker.", Detail(sim));

        // The second relay over a tile next to the wreck (not on it: "on or near the marker" is enough).
        var near = g.Neighbours(wreck).First();
        Do(sim, new RelaySatelliteCommand(0, near));
        Assert.Contains("RELAY-2", Detail(sim));
        Assert.Contains("drifting to its station", Detail(sim));
        Assert.Equal(MissionState.Active, State(sim, "calling_home"));
        RelayOnStation(sim);
        var onStation = sim.Tick;
        StepUntil(sim, () => State(sim, "calling_home") == MissionState.Completed, 2, "wreck linked");
        Assert.True(sim.Tick - onStation <= Simulation.TickRate + 1);
        Assert.Equal(2, s.RelayCount(0));
    }

    [Fact]
    public void ASecondBaseDoesNotStopTheFirstStage()
    {
        var (sim, hub, wreck) = Start();
        var s = sim.Surface!;
        var g = s.Map.Grid;
        var reach = sim.Data.Comms!.Relay!.Radius;
        // A finished second core far from the hub (the test panel's placement: no drop or rover needed).
        var far = Enumerable.Range(0, g.Count).First(t => g.Distance(t, hub) > 2 * reach + 2 && g.Distance(t, wreck) > reach + 1
            && sim.DebugProblem(new DebugPlaceBuildingCommand(0, 0, "colony_hub", t, 0)) == null);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "colony_hub", far, 0));
        Seconds(sim, 1);
        Assert.Equal(2, s.Bases(0).Count(b => b.State == StructureState.Complete));
        Do(sim, new RelaySatelliteCommand(0, hub));
        RelayOnStation(sim);
        StepUntil(sim, () => View(sim, "calling_home").StageTitle != "Put up a relay", 2, "the outpost (the first base) is covered");
    }

    [Fact]
    public void TheDetailIsAViewOnly()
    {
        var (sim, hub, _) = Start();
        var hash = sim.StateHash();
        for (var i = 0; i < 5; i++) _ = Detail(sim);
        Assert.Equal(hash, sim.StateHash());
        Do(sim, new RelaySatelliteCommand(0, hub));
        var a = sim.StateHash();
        _ = sim.Missions.For(0);
        Assert.Equal(a, sim.StateHash());
    }
}
