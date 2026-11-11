using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// Campaign missions (docs/missions-contract.md C; game/sim/Missions/). The drone and relay missions read drones and
/// comms coverage through MissionBook.World: until agent S1's drones, Drone Pad and relay satellites are merged the
/// surface's stubs return none, so these tests use a FakeWorld (drones and relays that exist only for the missions) and,
/// for Wings / First Flight / Beyond the Ridge, a data copy where drone_pad → storage_container and scout_drone →
/// control_module (so the missions are available and buildable today). After S1 merges: re-check the tests marked
/// "S1" against the real drones and relays (and drop the patches).
/// </summary>
public class MissionTests
{
    private static GameData Data => TestData.Real;

    /// <summary>Drones and relay satellites for the missions only (player 0), on top of what the surface reports.</summary>
    private sealed class FakeWorld : MissionWorld
    {
        public readonly List<MissionDrone> DroneList = new();
        public readonly List<CommsSource> Relays = new();

        public override IEnumerable<MissionDrone> Drones(PlanetSurface s, int player) =>
            player == 0 ? DroneList.Concat(base.Drones(s, player)).ToList() : base.Drones(s, player);

        public override bool Covered(PlanetSurface s, int player, int tile) =>
            player == 0 && Relays.Any(r => r.Ready && s.Map.Grid.Distance(r.Tile, tile) <= r.Radius) || base.Covered(s, player, tile);

        public override IReadOnlyList<CommsSource> Sources(PlanetSurface s, int player) =>
            player == 0 ? base.Sources(s, player).Concat(Relays).ToList() : base.Sources(s, player);

        public void Relay(int tile) => Relays.Add(new CommsSource(CommsSourceKind.Relay, tile, 14, 0, Relays.Count + 1, true));
    }

    /// <summary>The real data with the drone ids swapped for ones that exist today (S1: remove once drones are merged).</summary>
    private static readonly Lazy<GameData> DroneStandIns = new(() => TestData.Patched(
        ("missions", "\"drone_pad\"", "\"storage_container\""),
        ("missions", "\"scout_drone\"", "\"control_module\"")));

    /// <summary>A landed one-player game on seed 1701 with missions on (or off), outpost assembled.</summary>
    private static Simulation Game(GameData? data = null, bool missions = true, FakeWorld? world = null, int players = 1)
    {
        data ??= Data;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        if (players > 1) sim.AddPlayer("cardassian");
        if (missions) Assert.Null(sim.Missions.Enable("dawn_of_warp"));
        if (world != null) sim.Missions.World = world;
        TestData.NoStockTargets(sim);
        var map = sim.Surface!.Map;
        var first = Enumerable.Range(0, map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, first));
        if (players > 1)
            sim.Schedule(0, new LandColonyCommand(1, Enumerable.Range(0, map.Grid.Count).Last(t => sim.Surface.CanLand(1, t) && map.Grid.Distance(t, first) > 20)));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.True(refused == sim.RefusedCommands, $"refused: {sim.LastRefusal}");
    }

    private static string Refused(Simulation sim, Command c)
    {
        var refused = sim.RefusedCommands;
        sim.Schedule(sim.Tick, c);
        sim.Step();
        Assert.Equal(refused + 1, sim.RefusedCommands);
        return sim.LastRefusal ?? "";
    }

    private static void Seconds(Simulation sim, int seconds)
    {
        for (var i = 0; i < seconds * Simulation.TickRate; i++) sim.Step();
    }

    private static void StepUntil(Simulation sim, Func<bool> done, int maxSeconds, string what)
    {
        for (var i = 0; i < maxSeconds * Simulation.TickRate && !done(); i++) sim.Step();
        Assert.True(done(), $"timed out: {what}");
    }

    private static MissionState State(Simulation sim, string id, int player = 0) =>
        sim.Missions.States(player).FirstOrDefault(x => x.Id == id).State;

    private static MissionView View(Simulation sim, string id, int player = 0) => sim.Missions.View(player, id)!;

    private static DebugMissionCommand Debug(string mission, DebugMissionAction action, string stage = "", string objective = "", int tile = -1, int target = 0) =>
        new(0, target, mission, action, stage, objective, tile);

    private static int FreeTile(Simulation sim, string building, int skip = 0)
    {
        var hub = sim.Surface!.Bases(0)[0];
        return sim.Surface.Map.Grid.Within(hub.Tile, 3).Where(t => sim.Surface.CheckPlacement(sim, 0, building, t) == PlacementProblem.None).Skip(skip).First();
    }

    private static int R(string id) => Data.Resource(id).Index;

    /// <summary>Drives the player's rovers outward in turn (each idle one to the next spot of a ring round the hub) until done.</summary>
    private static void Explore(Simulation sim, Func<bool> done, int maxSeconds)
    {
        var s = sim.Surface!;
        var hub = s.Bases(0)[0].Tile;
        var ring = Enumerable.Range(0, s.Map.Grid.Count)
            .Where(t => s.Map.Grid.Distance(hub, t) is >= 5 and <= 8 && s.Map.Terrain(t).Passable).ToList();
        var next = 0;
        for (var sec = 0; sec < maxSeconds && !done(); sec++)
        {
            if (sec % 5 == 0)
                foreach (var u in s.Units.Where(u => u.Owner == 0 && u.DefId.StartsWith("survey_rover") && u.Path.Count == 0 && !u.Broken).ToList())
                {
                    sim.Schedule(sim.Tick, new MoveUnitsCommand(0, new[] { u.Id }, ring[next * 37 % ring.Count]));
                    next++;
                }
            Seconds(sim, 1);
        }
    }

    /// <summary>A rover of player 0's that can drive.</summary>
    private static SurfaceUnit Rover(Simulation sim) =>
        sim.Surface!.Units.First(u => u.Owner == 0 && u.DefId.StartsWith("survey_rover") && !u.Broken);

    /// <summary>Sends a rover to a tile and keeps it going (re-ordering if it stops short) until it is parked there.</summary>
    private static void DriveTo(Simulation sim, SurfaceUnit rover, int tile, int maxSeconds)
    {
        var s = sim.Surface!;
        for (var sec = 0; sec < maxSeconds; sec++)
        {
            if (rover.Tile == tile && rover.Path.Count == 0) return;
            if (sec % 10 == 0 && rover.Path.Count == 0) sim.Schedule(sim.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, tile));
            Seconds(sim, 1);
        }
        Assert.Fail($"{rover.Name} didn't reach {tile} (at {rover.Tile}, {s.Map.Grid.Distance(rover.Tile, tile)} away)");
    }

    // ---------------------------------------------------------------- the main chain, golden path

    /// <summary>
    /// Plays First Light along a scripted path until the mission stopAfter completes (null: all seven). Ground Truth and
    /// The Lost Pathfinder are played with real orders (rovers drive, survey, park); Calling Home, First Flight, Beyond the
    /// Ridge and Foothold use the FakeWorld's relays and drones (S1); Wings fabricates real control modules standing in
    /// for scout drones (S1).
    /// </summary>
    private static (Simulation Sim, FakeWorld World) PlayFirstLight(string? stopAfter, List<ulong>? hashes = null)
    {
        var world = new FakeWorld();
        var sim = Game(DroneStandIns.Value, world: world);
        var s = sim.Surface!;
        var hub = s.Bases(0)[0];
        void Done(string id)
        {
            Assert.Equal(MissionState.Completed, State(sim, id));
            hashes?.Add(sim.StateHash());
        }

        // 1. Ground Truth: starts when a Research Lab is placed; survey 60 tiles and find 3 different resources.
        Assert.Equal(MissionState.Locked, State(sim, "ground_truth"));
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "science_lab", FreeTile(sim, "science_lab"), 0));
        Seconds(sim, 1);
        Assert.Equal(MissionState.Active, State(sim, "ground_truth"));
        Assert.Contains(sim.Missions.Messages(0), m => m.MissionId == "ground_truth" && m.Channel == "comms" && m.GiverName == "Flight Director Moreau");
        Explore(sim, () => State(sim, "ground_truth") == MissionState.Completed, 900);
        Done("ground_truth");
        Assert.Equal(Fix64.FromInt(30), s.DigResearchBank(0)); // no tech started: banked
        if (stopAfter == "ground_truth") return (sim, world);

        // 2. The Lost Pathfinder: a wreck 10–18 hexes out on passable land, inside a search area; find it, park beside it 20 s.
        Seconds(sim, 1);
        Assert.Equal(MissionState.Active, State(sim, "lost_pathfinder"));
        var wreck = Assert.Single(sim.Missions.Sites(0));
        Assert.Equal("probe_wreck", wreck.Kind);
        Assert.InRange(s.Map.Grid.Distance(hub.Tile, wreck.Tile), 10, 18);
        Assert.True(s.Map.Terrain(wreck.Tile).Passable);
        Assert.False(wreck.Found);
        var area = Assert.Single(sim.Missions.Markers(0));
        Assert.Equal("area", area.Style);
        Assert.InRange(s.Map.Grid.Distance(area.Tile, wreck.Tile), 1, area.RadiusTiles - 1); // inside the ring, not at its centre
        var modules = sim.Players[0].Stock[R("control_module")];
        DriveTo(sim, Rover(sim), wreck.Tile, 900);
        Assert.True(sim.Missions.Sites(0)[0].Found);
        StepUntil(sim, () => State(sim, "lost_pathfinder") == MissionState.Completed, 40, "recorder recovered");
        Done("lost_pathfinder");
        Assert.True(sim.Missions.Sites(0)[0].Done);
        Assert.Equal(modules + Fix64.FromInt(2), sim.Players[0].Stock[R("control_module")]);
        Assert.True(s.Map.Grid.Within(wreck.Tile, 6).All(t => s.Players[0].Surveyed[t])); // reveal 6
        Assert.Contains(sim.Missions.Messages(0), m => m.Text.Contains("Pathfinder IV,") && m.Channel == "feed"); // {wreck.distance} filled
        if (stopAfter == "lost_pathfinder") return (sim, world);

        // 3. Calling Home (S1: relays are fakes): a relay over the base, then the wreck under coverage.
        Seconds(sim, 1);
        Assert.Equal(MissionState.Active, State(sim, "calling_home"));
        Assert.Equal("Put up a relay", View(sim, "calling_home").StageTitle);
        world.Relay(hub.Tile);
        Seconds(sim, 1);
        if (s.Map.Grid.Distance(hub.Tile, wreck.Tile) > 14)
        {
            Assert.Equal("Link the wreck", View(sim, "calling_home").StageTitle);
            world.Relay(wreck.Tile);
            Seconds(sim, 1);
        }
        StepUntil(sim, () => State(sim, "calling_home") == MissionState.Completed, 3, "wreck linked");
        Done("calling_home");
        Assert.Contains("earth_link", sim.Missions.Flags(0));
        if (stopAfter == "calling_home") return (sim, world);

        // 4. Wings (S1: storage_container stands in for the Drone Pad, control modules for scout drones).
        Seconds(sim, 1);
        Assert.Equal(MissionState.Active, State(sim, "wings"));
        Assert.Equal("Chief Okafor", View(sim, "wings").GiverName);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "machine_shop", FreeTile(sim, "machine_shop"), 0));
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "storage_container", FreeTile(sim, "storage_container"), 0));
        var shop = s.Structures.First(x => x.Owner == 0 && x.DefId == "machine_shop");
        Do(sim, new FabricateCommand(0, shop.Id, "control_module", 2));
        Seconds(sim, 2);
        var drones = View(sim, "wings").Objectives.Single(o => o.Id == "drones");
        Assert.Equal(Fix64.FromInt(2), drones.Target);
        Assert.False(drones.Done);
        var couplings = sim.Players[0].Stock[R("power_coupling")];
        StepUntil(sim, () => State(sim, "wings") == MissionState.Completed, 200, "two fabricated");
        Done("wings");
        Assert.Equal(couplings + Fix64.FromInt(2), sim.Players[0].Stock[R("power_coupling")]);
        if (stopAfter == "wings") return (sim, world);

        // 5. First Flight (S1: the drone is a fake): launch, then a drone loitering over each of three photo points.
        Seconds(sim, 1);
        Assert.Equal(MissionState.Active, State(sim, "first_flight"));
        world.DroneList.Add(new MissionDrone(DroneState.Flying, hub.Tile));
        Seconds(sim, 1);
        Assert.Equal("Photo survey", View(sim, "first_flight").StageTitle);
        var photos = sim.Missions.Sites(0).Where(x => x.Kind == "photo_point").ToList();
        Assert.Equal(3, photos.Count);
        Assert.All(photos, p => Assert.InRange(s.Map.Grid.Distance(hub.Tile, p.Tile), 6, 13));
        Assert.Equal(3, sim.Missions.Markers(0).Count(m => m.MissionId == "first_flight"));
        for (var k = 0; k < 3; k++)
        {
            world.DroneList[0] = new MissionDrone(DroneState.Loitering, photos[k].Tile);
            Seconds(sim, 1);
            if (k < 2) Assert.Equal(Fix64.FromInt(k + 1), View(sim, "first_flight").Objectives[0].Progress);
            Assert.Equal(2 - k, sim.Missions.Markers(0).Count(m => m.MissionId == "first_flight")); // done points drop their marker
        }
        Done("first_flight");
        world.DroneList[0] = new MissionDrone(DroneState.Returning, hub.Tile);
        if (stopAfter == "first_flight") return (sim, world);

        // 6. Beyond the Ridge (S1: fake drone): bearings from 2 of 3 points, then find and reach the source with a rover.
        Seconds(sim, 1);
        Assert.Equal(MissionState.Active, State(sim, "beyond_the_ridge"));
        var bearings = sim.Missions.Markers(0).Where(m => m.MissionId == "beyond_the_ridge").ToList();
        Assert.Equal(3, bearings.Count);
        world.DroneList[0] = new MissionDrone(DroneState.Loitering, bearings[0].Tile);
        Seconds(sim, 1);
        Assert.Equal("Take bearings", View(sim, "beyond_the_ridge").StageTitle);
        world.DroneList[0] = new MissionDrone(DroneState.Loitering, bearings[2].Tile);
        Seconds(sim, 1);
        Assert.Equal("Find the source", View(sim, "beyond_the_ridge").StageTitle);
        var source = sim.Missions.Sites(0).Single(x => x.Kind == "signal_source");
        Assert.True(bearings.Min(b => s.Map.Grid.Distance(b.Tile, source.Tile)) <= 6);
        world.DroneList[0] = new MissionDrone(DroneState.Docked, hub.Tile);
        var rover = Rover(sim);
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, source.Tile));
        StepUntil(sim, () => State(sim, "beyond_the_ridge") == MissionState.Completed, 10, "signal source reached");
        Done("beyond_the_ridge");
        if (stopAfter == "beyond_the_ridge") return (sim, world);

        // 7. Foothold (S1: the relay is a fake): a second outpost inside coverage (not its own antenna's).
        Seconds(sim, 1);
        Assert.Equal(MissionState.Active, State(sim, "foothold"));
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.Map.Grid.Distance(hub.Tile, t) == 10 && s.Map.Terrain(t).Buildable
            && s.StructureAt(t) == null && !s.Map.NearNatives(t) && s.Map.Grid.Within(t, 1).All(n => s.StructureAt(n) == null));
        world.Relays.Clear();
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "colony_hub", site, 0));
        Seconds(sim, 2);
        Assert.Equal(MissionState.Active, State(sim, "foothold")); // not covered: its own antenna doesn't count
        world.Relay(site);
        Seconds(sim, 1);
        Done("foothold");
        Assert.Contains(sim.Missions.Messages(0), m => m.MissionId == "foothold" && m.Text.Contains("First Light is complete"));
        Assert.All(new[] { "ground_truth", "lost_pathfinder", "calling_home", "wings", "first_flight", "beyond_the_ridge", "foothold" },
            id => Assert.Equal(MissionState.Completed, State(sim, id)));
        return (sim, world);
    }

    [Theory]
    [InlineData("ground_truth")]
    [InlineData("lost_pathfinder")]
    [InlineData("calling_home")]
    [InlineData("wings")]
    [InlineData("first_flight")]
    [InlineData("beyond_the_ridge")]
    public void EachMainMissionCompletesAlongItsGoldenPath(string mission)
    {
        var (sim, _) = PlayFirstLight(mission);
        Assert.Equal(MissionState.Completed, State(sim, mission));
        // The view of a completed mission: debrief filled, rewards listed.
        var v = View(sim, mission);
        Assert.DoesNotContain("{", v.Debrief);
        Assert.NotEmpty(v.Rewards);
        Assert.True(v.EndTick > v.StartTick);
    }

    [Fact]
    public void TheWholeChainCompletesAndIsDeterministic()
    {
        var a = new List<ulong>();
        var b = new List<ulong>();
        var (simA, _) = PlayFirstLight(null, a);
        var (simB, _) = PlayFirstLight(null, b);
        Assert.Equal(7, a.Count);
        Assert.Equal(a, b);
        Assert.Equal(simA.StateHash(), simB.StateHash());
        // Only one main mission was ever active at a time: each started after the last ended.
        var main = simA.Missions.For(0).Where(v => v.Kind == MissionKind.Main).ToList();
        for (var i = 1; i < main.Count; i++) Assert.True(main[i].StartTick > main[i - 1].EndTick);
    }

    [Fact]
    public void DroneMissionsAreLeftOutUntilTheDroneDataExists()
    {
        var sim = Game();
        var ids = sim.Missions.States(0).Select(x => x.Id).ToList();
        Assert.Contains("ground_truth", ids);
        Assert.Contains("foothold", ids);
        // S1: scout_drone / drone_pad don't exist yet, so these are left out (they come in with the drone data).
        if (!Data.Buildings.Any(x => x.Id == "drone_pad")) Assert.DoesNotContain("wings", ids);
        if (!Data.Resources.Any(x => x.Id == "scout_drone")) Assert.DoesNotContain("first_flight", ids);
    }

    [Fact]
    public void TheTimeTriggerStartsGroundTruthTenMinutesAfterTheUplink()
    {
        var sim = Game();
        Seconds(sim, 590);
        Assert.Equal(MissionState.Locked, State(sim, "ground_truth"));
        Seconds(sim, 12);
        Assert.Equal(MissionState.Active, State(sim, "ground_truth"));
        var v = View(sim, "ground_truth");
        Assert.Equal(Fix64.FromInt(60), v.Objectives[0].Target);
        Assert.True(v.Objectives[0].Progress > Fix64.Zero); // the landing revealed some
        Assert.Equal("Send the rovers out: every tile they drive past is surveyed.", v.Hint);
    }

    // ---------------------------------------------------------------- on / off

    [Fact]
    public void MissionsOffLeaveTheHashAsItWas()
    {
        var off = Game(missions: false);
        var untouched = MatchSetup.CreatePlanetLanding(Data, 1701);
        TestData.NoStockTargets(untouched);
        var map = untouched.Surface!.Map;
        untouched.Schedule(0, new LandColonyCommand(0, Enumerable.Range(0, map.Grid.Count).First(t => untouched.Surface.CanLand(0, t))));
        for (var i = 0; i <= TestData.ReadyTicks(Data); i++) untouched.Step();
        // "off" asked the book for its views (as the UI does); "untouched" never looked at it.
        Assert.Empty(off.Missions.For(0));
        Assert.Empty(off.Missions.Messages(0));
        Assert.False(off.Missions.Enabled);
        Do(off, new DebugPlaceBuildingCommand(0, 0, "science_lab", FreeTile(off, "science_lab"), 0));
        Do(untouched, new DebugPlaceBuildingCommand(0, 0, "science_lab", FreeTile(untouched, "science_lab"), 0));
        Seconds(off, 30);
        Seconds(untouched, 30);
        Assert.Equal(untouched.StateHash(), off.StateHash());
        Assert.Empty(off.Missions.For(0));

        var on = Game();
        Assert.NotEqual(off.StateHash(), on.StateHash());
    }

    [Fact]
    public void EnableIsForTheSetUpOnly()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        Assert.Equal("no mission chain 'no_such_chain'", sim.Missions.Enable("no_such_chain"));
        Assert.False(sim.Missions.Enabled);
        Assert.Null(sim.Missions.Enable("first_light"));
        Assert.Equal("first_light", sim.Missions.ChainId);
        Assert.NotNull(sim.Missions.Enable("first_light")); // already on

        var late = MatchSetup.CreatePlanetLanding(Data, 1701);
        late.Step();
        Assert.NotNull(late.Missions.Enable("dawn_of_warp"));
        Assert.False(late.Missions.Enabled);
        // The test panel can turn them on mid-game (counted as a test edit).
        Do(late, new DebugMissionCommand(0, 0, "dawn_of_warp", DebugMissionAction.Enable, "", "", -1));
        Assert.True(late.Missions.Enabled);
        Assert.Equal(1, late.DebugEdits);
    }

    [Fact]
    public void NothingStartsBeforeTheUplink()
    {
        var sim = MatchSetup.CreatePlanetLanding(Data, 1701);
        Assert.Null(sim.Missions.Enable("first_light"));
        Seconds(sim, 30); // still in orbit
        Assert.Empty(sim.Missions.For(0));
        Assert.Empty(sim.Missions.Messages(0));
    }

    // ---------------------------------------------------------------- side jobs

    /// <summary>Storm Watch and Sky Survey offered at once (their triggers swapped for "uplink"), Rescue as it is.</summary>
    private static readonly Lazy<GameData> EasySides = new(() => TestData.Patched(
        ("missions/side_jobs.json", "\"trigger\": { \"weather\": { \"kinds\": [\"storm\", \"blizzard\", \"dust_storm\"], \"near\": \"base\", \"within_seconds\": 60 } }", "\"trigger\": { \"uplink\": {} }"),
        ("missions/side_jobs.json", "\"trigger\": { \"satellite_share\": { \"share\": 0.25 } }", "\"trigger\": { \"uplink\": {} }")));

    /// <summary>Disables a rover far from the hub (Rescue's trigger: more than 6 hexes from any pad).</summary>
    private static SurfaceUnit Strand(Simulation sim, int distance = 9)
    {
        var s = sim.Surface!;
        var hub = s.Bases(0)[0].Tile;
        var rover = Rover(sim);
        var tile = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.Map.Grid.Distance(hub, t) == distance && s.Map.Terrain(t).Passable && s.StructureAt(t) == null);
        Do(sim, new DebugMoveUnitCommand(0, rover.Id, tile));
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Integrity, Fix64.Zero));
        Do(sim, new DebugUnitCommand(0, rover.Id, DebugUnitField.Broken, Fix64.One));
        return rover;
    }

    [Fact]
    public void SideJobsAreOfferedAcceptedDeclinedAndLimitedToTwo()
    {
        var sim = Game(EasySides.Value);
        Seconds(sim, 1);
        Assert.Equal(MissionState.Offered, State(sim, "storm_watch"));
        Assert.Equal(MissionState.Offered, State(sim, "sky_survey"));
        var offer = View(sim, "storm_watch");
        Assert.Equal(new[] { "accept", "decline" }, offer.Options.Select(o => o.Id));
        Assert.True(offer.OfferEndTick > sim.Tick);
        Assert.Contains(sim.Missions.Messages(0), m => m.MissionId == "storm_watch" && m.Channel == "comms");

        var victim = Strand(sim);
        Seconds(sim, 1);
        Assert.Equal(MissionState.Offered, State(sim, "rescue"));
        Assert.Contains(victim.Name[1..].ToLowerInvariant(), View(sim, "rescue").Briefing); // {victim} = its call sign
        Assert.Equal(MissionState.Locked, State(sim, "rich_vein")); // nothing rich surveyed yet

        Do(sim, new MissionChoiceCommand(0, "storm_watch", "accept"));
        Do(sim, new MissionChoiceCommand(0, "sky_survey", "accept"));
        Assert.Equal(MissionState.Active, State(sim, "storm_watch"));
        Assert.Equal(MissionState.Active, State(sim, "sky_survey"));
        Assert.Contains("side jobs are active already", Refused(sim, new MissionChoiceCommand(0, "rescue", "accept")));
        Assert.Equal(MissionState.Offered, State(sim, "rescue"));

        // Declining one frees a slot, but Rescue's offer still lapses on its own after 90 s.
        Do(sim, new MissionChoiceCommand(0, "rescue", "decline"));
        Assert.Equal(MissionState.Declined, State(sim, "rescue"));
        Assert.Contains("not offered", Refused(sim, new MissionChoiceCommand(0, "rescue", "accept")));
    }

    [Fact]
    public void AnOfferLapsesAfterNinetySeconds()
    {
        var sim = Game();
        Strand(sim);
        Seconds(sim, 1);
        Assert.Equal(MissionState.Offered, State(sim, "rescue"));
        var end = View(sim, "rescue").OfferEndTick;
        StepUntil(sim, () => State(sim, "rescue") != MissionState.Offered, 95, "offer lapses");
        Assert.Equal(MissionState.Declined, State(sim, "rescue"));
        Assert.Equal("expired", View(sim, "rescue").Outcome);
        Assert.InRange(sim.Tick - (end - 90 * Simulation.TickRate), 90 * Simulation.TickRate, 92 * Simulation.TickRate);
        Seconds(sim, 5);
        Assert.Equal(MissionState.Declined, State(sim, "rescue")); // never offered again
    }

    [Fact]
    public void RescueSucceedsWhenAnotherVehicleReachesTheVictim()
    {
        var sim = Game();
        var victim = Strand(sim);
        Seconds(sim, 1);
        Do(sim, new MissionChoiceCommand(0, "rescue", "accept"));
        var v = View(sim, "rescue");
        Assert.Equal(MissionState.Active, v.State);
        Assert.InRange(v.TimerEndTick - sim.Tick, 298 * Simulation.TickRate, 300 * Simulation.TickRate);
        var marker = Assert.Single(sim.Missions.Markers(0), m => m.MissionId == "rescue");
        Assert.Equal(victim.Id, marker.UnitId);
        Assert.Equal(victim.Tile, marker.Tile);
        var helper = sim.Surface!.Units.First(u => u.Owner == 0 && u.Id != victim.Id && !u.Broken && u.DefId.StartsWith("survey_rover"));
        Do(sim, new DebugMoveUnitCommand(0, helper.Id, sim.Surface.Map.Grid.Neighbours(victim.Tile).First(t => sim.Surface.Map.Terrain(t).Passable)));
        Seconds(sim, 2);
        Assert.Equal(MissionState.Completed, State(sim, "rescue"));
    }

    // ---------------------------------------------------------------- timers, rewards

    [Fact]
    public void ATimerRunningOutFailsTheMissionAndPaysNothing()
    {
        var sim = Game();
        Strand(sim);
        Seconds(sim, 1);
        Do(sim, new MissionChoiceCommand(0, "rescue", "accept"));
        var spares = sim.Players[0].Stock[R("rover_spares")];
        Seconds(sim, 290);
        Assert.Equal(MissionState.Active, State(sim, "rescue"));
        Seconds(sim, 12);
        Assert.Equal(MissionState.Failed, State(sim, "rescue"));
        Assert.Equal("timeout", View(sim, "rescue").Outcome);
        Assert.Equal(spares, sim.Players[0].Stock[R("rover_spares")]);
        Assert.Empty(sim.Missions.Markers(0));
    }

    [Fact]
    public void RewardsArePaidOnce()
    {
        var sim = Game();
        Seconds(sim, 1);
        Do(sim, Debug("lost_pathfinder", DebugMissionAction.Start));
        Assert.Equal(MissionState.Active, State(sim, "lost_pathfinder"));
        var modules = sim.Players[0].Stock[R("control_module")];
        Do(sim, Debug("lost_pathfinder", DebugMissionAction.Complete));
        Assert.Equal(modules + Fix64.FromInt(2), sim.Players[0].Stock[R("control_module")]);
        Assert.Equal(Fix64.FromInt(60), sim.Surface!.DigResearchBank(0));
        Assert.Contains("ended already", Refused(sim, Debug("lost_pathfinder", DebugMissionAction.Complete)));
        Seconds(sim, 10);
        Assert.Equal(modules + Fix64.FromInt(2), sim.Players[0].Stock[R("control_module")]);
        Assert.Equal(Fix64.FromInt(60), sim.Surface.DigResearchBank(0));
        Assert.Contains(sim.Missions.Messages(0), m => m.MissionId == "lost_pathfinder" && m.Text.Contains("+60 research") && m.Text.Contains("2 Control Module"));
    }

    [Fact]
    public void ResearchRewardsGoIntoTheActiveTech()
    {
        var sim = Game();
        var tech = Data.Techs.First(t => t.Requires.Count == 0);
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "science_lab", FreeTile(sim, "science_lab"), 0));
        Do(sim, new ResearchCommand(0, tech.Id));
        var before = sim.Surface!.Players[0].ResearchProgress[tech.Index];
        Seconds(sim, 1);
        Do(sim, Debug("ground_truth", DebugMissionAction.Complete));
        var after = sim.Surface.Players[0].ResearchProgress[tech.Index];
        Assert.True(after - before >= Fix64.FromInt(30) || sim.Surface.Players[0].Known[tech.Index]);
    }

    // ---------------------------------------------------------------- refusals, debug

    [Fact]
    public void BadChoicesAreRefusedAndChangeNothing()
    {
        var off = Game(missions: false);
        Assert.Contains("missions are off", Refused(off, new MissionChoiceCommand(0, "rescue", "accept")));
        Assert.Contains("missions are off", Refused(off, Debug("rescue", DebugMissionAction.Start)));

        var sim = Game(players: 2);
        Strand(sim);
        Seconds(sim, 1);
        Assert.Equal(MissionState.Offered, State(sim, "rescue"));
        var hash = sim.StateHash();
        void No(Command c, string why)
        {
            Assert.Contains(why, Refused(sim, c));
        }
        No(new MissionChoiceCommand(0, "no_such_mission", "accept"), "no mission");
        No(new MissionChoiceCommand(0, "rescue", "launch"), "not active");
        No(new MissionChoiceCommand(0, "ground_truth", "decline"), "not offered");
        No(new MissionChoiceCommand(1, "rescue", "accept"), "not offered"); // not yours: player 1's copy isn't offered
        No(new MissionChoiceCommand(7, "rescue", "accept"), "no such player");
        No(new MissionChoiceCommand(0, null!, "accept"), "no id");
        No(Debug("rescue", DebugMissionAction.GotoStage, stage: "nowhere"), "no stage");
        No(Debug("rescue", DebugMissionAction.CompleteObjective, objective: "reach"), "not active");
        No(Debug("rescue", (DebugMissionAction)99), "unknown action");
        No(Debug("rescue", DebugMissionAction.Start, target: 5), "no such player");
        Assert.Equal(MissionState.Offered, State(sim, "rescue"));
        _ = hash;
    }

    [Fact]
    public void RefusedCommandsLeaveNoTrace()
    {
        var a = Game();
        var b = Game();
        Seconds(a, 3);
        Seconds(b, 3);
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Schedule(a.Tick, new MissionChoiceCommand(0, "foothold", "accept"));
        a.Schedule(a.Tick, Debug("ground_truth", DebugMissionAction.CompleteObjective, objective: "nope"));
        Seconds(a, 2);
        Seconds(b, 2);
        Assert.Equal(b.StateHash(), a.StateHash());
    }

    [Fact]
    public void DebugCommandsDriveAMissionAndCount()
    {
        var sim = Game();
        Seconds(sim, 1);
        Do(sim, Debug("lost_pathfinder", DebugMissionAction.Start));
        Assert.Equal(MissionState.Active, State(sim, "lost_pathfinder"));
        Assert.Single(sim.Missions.Sites(0));
        Do(sim, Debug("lost_pathfinder", DebugMissionAction.CompleteObjective, objective: "find"));
        Seconds(sim, 1);
        Assert.Equal("Recover the data recorder", View(sim, "lost_pathfinder").StageTitle);
        Do(sim, Debug("lost_pathfinder", DebugMissionAction.GotoStage, stage: "search"));
        Assert.Equal("Find the probe", View(sim, "lost_pathfinder").StageTitle);
        Do(sim, Debug("lost_pathfinder", DebugMissionAction.Fail));
        Assert.Equal(MissionState.Failed, State(sim, "lost_pathfinder"));
        Do(sim, Debug("lost_pathfinder", DebugMissionAction.Reset));
        Assert.Equal(MissionState.Locked, State(sim, "lost_pathfinder"));
        Assert.Empty(sim.Missions.Sites(0));
        Assert.True(sim.DebugEdits >= 5);

        // Start binds what a trigger can't to the given tile (Rich Vein's seam).
        var s = sim.Surface!;
        var deposit = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.Map.DepositResource[t] >= 0);
        Do(sim, Debug("rich_vein", DebugMissionAction.Start, tile: deposit));
        var vein = View(sim, "rich_vein");
        Assert.Equal(MissionState.Active, vein.State);
        Assert.Contains(MissionBook.TileText(s, deposit), vein.Briefing);
        Assert.Contains(Data.Resources[s.Map.DepositResource[deposit]].Name.ToLowerInvariant(), vein.Briefing);
        Assert.Equal(deposit, Assert.Single(sim.Missions.Markers(0), m => m.MissionId == "rich_vein").Tile);
    }

    [Fact]
    public void RichVeinIsOfferedForARichDepositAndCountsWhatIsMinedFromIt()
    {
        var sim = Game();
        var s = sim.Surface!;
        var rich = Data.Missions!.Rules.RichDeposit;
        // Make a surveyed deposit rich (the test panel's deposit edit only sets the amount, so raise its start too).
        var tile = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.Map.DepositResource[t] >= 0 && s.Players[0].Surveyed[t]);
        s.Map.DepositStart[tile] = rich;
        s.Map.DepositAmount[tile] = rich;
        Seconds(sim, 1);
        Assert.Equal(MissionState.Offered, State(sim, "rich_vein"));
        Do(sim, new MissionChoiceCommand(0, "rich_vein", "accept"));
        Assert.Equal("Stake the claim", View(sim, "rich_vein").StageTitle);
        Do(sim, Debug("rich_vein", DebugMissionAction.CompleteObjective, objective: "mine"));
        Seconds(sim, 1);
        Assert.Equal("Haul it home", View(sim, "rich_vein").StageTitle);
        s.Map.DepositAmount[tile] = rich - Fix64.FromInt(120); // what a mine took
        Seconds(sim, 1);
        Assert.Equal(Fix64.FromInt(120), View(sim, "rich_vein").Objectives[0].Progress);
        s.Map.DepositAmount[tile] = rich - Fix64.FromInt(300);
        Seconds(sim, 1);
        Assert.Equal(MissionState.Completed, State(sim, "rich_vein"));
    }

    // ---------------------------------------------------------------- data checks

    [Fact]
    public void TheMissionDataIsReadAndChecked()
    {
        var m = Data.Missions!;
        Assert.Contains("missions", GameData.Documents);
        Assert.Equal(2, m.Rules.MaxSideActive);
        Assert.Equal(90, m.Rules.OfferSeconds);
        Assert.Equal(new[] { "control", "ops", "science" }, m.Givers.Select(g => g.Id));
        Assert.Equal(7, m.List.Count(x => x.IsMain));
        Assert.Equal(5, m.List.Count(x => !x.IsMain));
        Assert.Equal(new[] { ("lost_pathfinder", "wreck") }, m.Find("calling_home")!.Imports);
        Assert.Empty(Data.Validate());
    }

    [Theory]
    [InlineData("\"next\": \"recover\"", "\"next\": \"nowhere\"", "next 'nowhere'")]
    [InlineData("{ \"grant\": { \"duranium\": 150 } }", "{ \"grant\": { \"energy\": 150 } }", "never energy")]
    [InlineData("\"site\": { \"site\": \"wreck\", \"state\": \"found\" }", "\"site\": { \"site\": \"wrek\", \"state\": \"found\" }", "'wrek' is not bound")]
    [InlineData("{ \"building\": { \"id\": \"science_lab\", \"state\": \"any\" } }", "{ \"building\": { \"id\": \"science_labb\", \"state\": \"any\" } }", "unknown building 'science_labb'")]
    [InlineData("\"giver\": \"ops\",\n      \"briefing\": \"One outpost", "\"giver\": \"nobody\",\n      \"briefing\": \"One outpost", "unknown giver 'nobody'")]
    [InlineData("\"text\": \"Reyes: That's it, Pathfinder IV, {wreck.distance}", "\"text\": \"Reyes: That's it, Pathfinder IV, {source.distance}", "{source} is not bound")]
    [InlineData("\"surveyed\": { \"count\": 60 }", "\"surveyed\": { \"count\": 60, \"colour\": 1 }", "unknown field 'colour'")]
    public void BadMissionDataIsReported(string from, string to, string problem)
    {
        var data = TestData.Patched(("missions/first_light.json", from, to));
        Assert.Contains(data.Validate(), p => p.Contains(problem));
    }

    [Fact]
    public void AStageThatCanNeverCompleteIsReported()
    {
        var data = TestData.Patched(("missions/first_light.json",
            "\"hint\": \"Keep it parked there while the crew pull the recorder.\" } ],\n          \"next\": \"complete\" } ],",
            "\"hint\": \"Keep it parked there while the crew pull the recorder.\" } ],\n          \"next\": \"search\" } ],"));
        Assert.Contains(data.Validate(), p => p.Contains("lost_pathfinder") && p.Contains("reaches complete"));
    }

    [Fact]
    public void ADataSetWithoutMissionsHasNone()
    {
        var dir = TestData.CopyData("ff-nomissions-");
        Directory.Delete(Path.Combine(dir, "missions"), true);
        var manifest = Path.Combine(dir, DataManifest.FileName);
        var text = File.ReadAllText(manifest);
        var start = text.IndexOf(",\n    \"missions\": [", StringComparison.Ordinal);
        var end = text.IndexOf(']', start);
        File.WriteAllText(manifest, text[..start] + text[(end + 1)..]);
        var data = GameData.Load(dir);
        Assert.Null(data.Missions);
        Assert.Empty(data.Validate());
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        Assert.Equal("this data set has no missions", sim.Missions.Enable("first_light"));
    }
}
