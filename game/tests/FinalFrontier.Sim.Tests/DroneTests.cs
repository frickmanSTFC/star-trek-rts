using System.Reflection;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Tests;

/// <summary>Scout drones and the Drone Pad (docs/missions-contract.md B): made at a Machine Shop, unpacked by a finished
/// pad, charged, launched inside comms coverage, flying straight over anything while surveying, loitering, flying home,
/// landing and recharging; every refusal; recall, retarget, battery loss, salvage, determinism and the hash.</summary>
public class DroneTests
{
    private static GameData Data => TestData.Real;
    private static int R(string id) => Data.Resource(id).Index;
    private static Fix64 F(int v) => Fix64.FromInt(v);
    private static int Sec(int s) => s * Simulation.TickRate;
    private static DroneDef Drone => Data.DroneOf("scout_drone")!;

    /// <summary>A landed colony on seed 1701 with metal, parts and storage; stock targets off.</summary>
    internal static Simulation Landed(out SurfaceStructure hub, GameData? data = null, bool rival = false)
    {
        data ??= Data;
        var sim = MatchSetup.CreatePlanetLanding(data, 1701);
        if (rival) sim.AddPlayer("cardassian");
        TestData.NoStockTargets(sim);
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        hub = s.Bases(0).Single();
        var stock = sim.Players[0].Stock;
        foreach (var r in new[] { "duranium", "tritanium" }) stock.Add(data.Resource(r).Index, F(300));
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "storage_container", FreeTile(sim, hub, "storage_container", 4), 0));
        return sim;
    }

    internal static int FreeTile(Simulation sim, SurfaceStructure hub, string building, int skip = 0) =>
        sim.Surface!.Map.Grid.Within(hub.Tile, 3).Where(t => sim.Surface.CheckPlacement(sim, 0, building, t) == PlacementProblem.None).Skip(skip).First();

    /// <summary>A finished Drone Pad beside the hub (test panel: free, at once) with drones from stock in its bays.</summary>
    internal static SurfaceStructure Pad(Simulation sim, SurfaceStructure hub, int drones = 1, int skip = 0)
    {
        var tile = FreeTile(sim, hub, "drone_pad", skip);
        sim.Players[0].Stock.Add(R("scout_drone"), F(drones));
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "drone_pad", tile, 0));
        sim.Step();
        return sim.Surface!.StructureAt(tile)!;
    }

    internal static void Do(Simulation sim, Command c)
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

    internal static void StepUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks && !done(); i++) sim.Step();
        Assert.True(done(), "timed out");
    }

    private static void Run(Simulation sim, int ticks)
    {
        for (var i = 0; i < ticks; i++) sim.Step();
    }

    /// <summary>A tile at this distance from the hub that is covered (within the core's antenna reach).</summary>
    private static int CoveredTile(Simulation sim, SurfaceStructure hub, int distance, Func<int, bool>? also = null)
    {
        var s = sim.Surface!;
        return s.Map.Grid.Within(hub.Tile, distance).First(t => s.Map.Grid.Distance(hub.Tile, t) == distance && s.CommsCovered(0, t) && (also?.Invoke(t) ?? true));
    }

    private static SurfaceUnit Charged(Simulation sim, SurfaceStructure pad)
    {
        var s = sim.Surface!;
        StepUntil(sim, () => s.DronesOf(pad.Id).Count > 0 && s.DroneOf(s.DronesOf(pad.Id)[0]).Battery == Fix64.One, Sec((int)Drone.RechargeSeconds.FloorToInt() + 5));
        return s.DronesOf(pad.Id)[0];
    }

    // ---------------------------------------------------------------- data

    [Fact]
    public void DataDefinesTheDroneThePadTheRecipeAndComms()
    {
        Assert.True(Data.IsAerialUnit("scout_drone"));
        Assert.False(Data.IsAerialUnit("survey_rover"));
        Assert.Equal(F(180), Drone.BatterySeconds);
        Assert.Equal(F(45), Drone.RechargeSeconds);
        Assert.Equal(Fix64.Parse("0.2"), Drone.Reserve);
        Assert.Equal(2, Data.SurfaceUnit("scout_drone").SurveyRadius);
        Assert.Equal(F(3), Data.SurfaceUnit("scout_drone").Speed);
        Assert.Equal("drone", Data.Resource("scout_drone").Tier);
        Assert.Equal(F(3), Data.Resource("scout_drone").Volume);
        var recipe = Data.Fabrication!.Recipe("scout_drone")!;
        Assert.Equal(40, recipe.Seconds.FloorToInt());
        Assert.Equal(2, Data.DroneBaysOf("drone_pad"));
        Assert.Equal(0, Data.DroneBaysOf("vehicle_bay"));
        var menu = Data.BuildMenu.ToList();
        Assert.Equal(menu.IndexOf("machine_shop") + 1, menu.IndexOf("drone_pad"));
        Assert.False(Data.Building("drone_pad").Crewed);
        Assert.DoesNotContain(Data.Buildings, b => b.Produces.Contains("scout_drone"));
        Assert.Equal(5, Data.Comms!.CoreRadius);
        Assert.Equal(14, Data.Comms.Relay!.Radius);
        Assert.Equal(3, Data.Comms.Relay.Max);
        Assert.Equal(Fix64.Parse("2.5"), Data.Habitability!.Domes!.TransitSeconds);
    }

    [Fact]
    public void ADroneWithoutAPadOrABatteryFailsTheChecks()
    {
        var noBays = TestData.Patched(("buildings.json", "\"drone_bays\": 2", "\"drone_bays\": 0"));
        Assert.Contains(noBays.Validate(), p => p.Contains("drone_bays"));
        var flat = TestData.Patched(("surfaces.json", "\"battery_seconds\": 180", "\"battery_seconds\": 5"));
        Assert.Contains(flat.Validate(), p => p.Contains("leave battery to fly"));
        var grounded = TestData.Patched(("surfaces.json", "\"aerial\": true", "\"aerial\": false"));
        Assert.Contains(grounded.Validate(), p => p.Contains("must be aerial"));
    }

    // ---------------------------------------------------------------- the whole loop

    [Fact]
    public void FabricatedDroneLoadsLaunchesFliesOverAnythingSurveysLoitersReturnsAndRecharges()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var stock = sim.Players[0].Stock;

        // A Machine Shop makes one from parts.
        foreach (var (id, n) in new[] { ("structural_frame", 1), ("power_coupling", 1), ("control_module", 2) }) stock.Add(R(id), F(n));
        var shopTile = FreeTile(sim, hub, "machine_shop");
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "machine_shop", shopTile, 0));
        var shop = s.StructureAt(shopTile)!;
        Do(sim, new FabricateCommand(0, shop.Id, "scout_drone", 1));
        StepUntil(sim, () => stock[R("scout_drone")] >= Fix64.One, Sec(120));
        Assert.Equal(Fix64.Zero, stock[R("control_module")]);

        // A pad unpacks it into its first bay, uncharged, then charges it.
        var padTile = FreeTile(sim, hub, "drone_pad");
        Do(sim, new DebugPlaceBuildingCommand(0, 0, "drone_pad", padTile, 0));
        var pad = s.StructureAt(padTile)!;
        sim.Step();
        Assert.Equal(Fix64.Zero, stock[R("scout_drone")]);
        var drone = s.DronesOf(pad.Id).Single();
        var info = s.DroneOf(drone);
        Assert.Equal(DroneState.Docked, info.State);
        Assert.Equal(0, info.Bay);
        Assert.Equal(pad.Id, info.Pad);
        Assert.True(s.IsAerial(drone));
        Assert.Equal(pad.Tile, drone.Tile);
        Assert.Equal(s.DroneBayPos(pad, 0), drone.Pos);
        Assert.True(info.Battery < Fix64.Parse("0.1"));
        Assert.Equal("no drone charged enough yet", s.LaunchProblem(sim, 0, pad.Id, hub.Tile));
        var before = s.DroneOf(drone).Battery;
        Run(sim, Sec(5));
        Assert.True(s.DroneOf(drone).Battery > before);
        Charged(sim, pad);

        // A relay widens coverage so the drone can reach water or a mountain and unsurveyed ground.
        Do(sim, new RelaySatelliteCommand(0, hub.Tile));
        Run(sim, Sec(Data.Comms!.Relay!.DeploySeconds) + 1);
        var surveyed = s.Players[0].Surveyed;
        var target = s.Map.Grid.Within(hub.Tile, 12).Where(t => !s.Map.Terrain(t).Passable && s.CommsCovered(0, t)
                && s.CommsCoveredLine(0, s.DroneBayPos(pad, 0), s.CentreOf(t)))
            .OrderByDescending(t => s.Map.Grid.Distance(hub.Tile, t)).First();
        var unsurveyedNear = s.Map.Grid.Within(target, 1).Count(t => !surveyed[t]);
        Assert.Null(s.LaunchProblem(sim, 0, pad.Id, target));
        Do(sim, new LaunchDroneCommand(0, pad.Id, target));
        Assert.Equal(DroneState.Launching, s.DroneOf(drone).State);
        Assert.Equal(target, s.DroneOf(drone).Target);
        Run(sim, (int)(Drone.LaunchSeconds * Simulation.TickRate).FloorToInt());
        Assert.Equal(DroneState.Flying, s.DroneOf(drone).State);
        var path = new List<int>();
        StepUntil(sim, () =>
        {
            if (path.Count == 0 || path[^1] != drone.Tile) path.Add(drone.Tile);
            return s.DroneOf(drone).State == DroneState.Loitering;
        }, Sec(60));
        Assert.Equal(target, drone.Tile);
        Assert.False(s.Map.Terrain(drone.Tile).Passable); // over water or a mountain
        Assert.Equal(s.CentreOf(target), drone.Pos);
        Assert.All(s.Map.Grid.Within(target, 1), t => Assert.True(surveyed[t]));
        Assert.True(s.DroneRecord(drone).TilesSurveyed >= unsurveyedNear);
        Assert.Equal(1, s.DroneRecord(drone).Launches);
        // It flew in a straight line: each tile it passed is next to the one before.
        for (var i = 1; i < path.Count; i++) Assert.Equal(1, s.Map.Grid.Distance(path[i - 1], path[i]));

        StepUntil(sim, () => s.DroneOf(drone).State == DroneState.Returning, Sec((int)Drone.LoiterSeconds.FloorToInt() + 2));
        Assert.Equal("done", s.DroneOf(drone).ReturnReason);
        StepUntil(sim, () => s.DroneOf(drone).State == DroneState.Landing, Sec(60));
        StepUntil(sim, () => s.DroneOf(drone).State == DroneState.Docked, Sec(10));
        Assert.Equal(s.DroneBayPos(pad, 0), drone.Pos);
        Assert.Equal(pad.Tile, drone.Tile);
        var low = s.DroneOf(drone).Battery;
        Assert.True(low < Fix64.One);
        Run(sim, Sec(5));
        Assert.True(s.DroneOf(drone).Battery > low);
        Assert.Empty(FinalFrontier.Sim.Tests.CommandFuzzTests.Problems(sim));
    }

    [Fact]
    public void APadFillsBothBaysAndLoadsMoreWhenStockArrives()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var pad = Pad(sim, hub, drones: 0);
        Assert.Empty(s.DronesOf(pad.Id));
        sim.Players[0].Stock.Add(R("scout_drone"), F(3));
        sim.Step();
        Assert.Equal(new[] { 0, 1 }, s.DronesOf(pad.Id).Select(u => s.DroneOf(u).Bay));
        Assert.Equal(Fix64.One, sim.Players[0].Stock[R("scout_drone")]);
        Assert.NotEqual(s.DronesOf(pad.Id)[0].Pos, s.DronesOf(pad.Id)[1].Pos);
        Assert.Equal(2, s.DroneBays(pad));
    }

    [Fact]
    public void UnfinishedPadsLoadNothing()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        sim.Players[0].Stock.Add(R("scout_drone"), F(1));
        var tile = FreeTile(sim, hub, "drone_pad");
        Do(sim, new PlaceBuildingCommand(0, "drone_pad", tile, 0));
        var pad = s.StructureAt(tile)!;
        Run(sim, 5);
        Assert.NotEqual(StructureState.Complete, pad.State);
        Assert.Empty(s.DronesOf(pad.Id));
        Assert.Contains("isn't finished", s.LaunchProblem(sim, 0, pad.Id, hub.Tile));
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public void EveryLaunchRefusalChangesNothing()
    {
        var sim = Landed(out var hub, rival: true);
        var s = sim.Surface!;
        var empty = Pad(sim, hub, drones: 0);
        Assert.Contains("no drone on the pad", Refused(sim, new LaunchDroneCommand(0, empty.Id, hub.Tile)));
        Do(sim, new SalvageCommand(0, empty.Id));
        var pad = Pad(sim, hub);
        var drone = Charged(sim, pad);
        var far = s.Map.Grid.Within(hub.Tile, 9).First(t => !s.CommsCovered(0, t));

        Assert.Contains("no such drone pad", Refused(sim, new LaunchDroneCommand(0, hub.Id, hub.Tile)));
        Assert.Contains("no such drone pad", Refused(sim, new LaunchDroneCommand(0, 99999, hub.Tile)));
        Assert.Contains("not your drone pad", Refused(sim, new LaunchDroneCommand(1, pad.Id, hub.Tile)));
        Assert.Contains("no such player", Refused(sim, new LaunchDroneCommand(7, pad.Id, hub.Tile)));
        Assert.Contains("off the map", Refused(sim, new LaunchDroneCommand(0, pad.Id, -1)));
        Assert.Contains("off the map", Refused(sim, new LaunchDroneCommand(0, pad.Id, s.Map.Grid.Count)));
        Assert.Contains("out of comms range: put a relay satellite over it", Refused(sim, new LaunchDroneCommand(0, pad.Id, far)));
        Assert.Equal(DroneState.Docked, s.DroneOf(drone).State);
        Assert.Equal(Fix64.One, s.DroneOf(drone).Battery);

        // A small battery: a covered tile is still too far to get there and back.
        var weak = TestData.Patched(("surfaces.json", "\"battery_seconds\": 180", "\"battery_seconds\": 10"));
        var sim2 = Landed(out var hub2, weak);
        var pad2 = Pad(sim2, hub2);
        Charged(sim2, pad2);
        var edge = CoveredTile(sim2, hub2, 5);
        Assert.Contains("too far", Refused(sim2, new LaunchDroneCommand(0, pad2.Id, edge)));
        Assert.Null(sim2.Surface!.LaunchProblem(sim2, 0, pad2.Id, pad2.Tile)); // circling over the pad is fine
    }

    [Fact]
    public void ARelayFixesACoverageRefusal()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var pad = Pad(sim, hub);
        Charged(sim, pad);
        var tile = s.Map.Grid.Within(hub.Tile, 10).First(t => s.Map.Grid.Distance(hub.Tile, t) == 10);
        Assert.Contains("out of comms range", s.LaunchProblem(sim, 0, pad.Id, tile));
        Do(sim, new RelaySatelliteCommand(0, tile));
        Assert.Contains("out of comms range", s.LaunchProblem(sim, 0, pad.Id, tile)); // still drifting to its station
        Run(sim, Sec(Data.Comms!.Relay!.DeploySeconds));
        Assert.Null(s.LaunchProblem(sim, 0, pad.Id, tile));
        Do(sim, new LaunchDroneCommand(0, pad.Id, tile));
    }

    // ---------------------------------------------------------------- orders

    [Fact]
    public void RecallSendsItHomeAndMoveRetargetsOrLaunches()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var pad = Pad(sim, hub, drones: 2);
        StepUntil(sim, () => s.DronesOf(pad.Id).All(u => s.DroneOf(u).Battery == Fix64.One), Sec(50));
        var (a, b) = (s.DronesOf(pad.Id)[0], s.DronesOf(pad.Id)[1]);
        Assert.Contains("none of these drones are out", Refused(sim, new RecallDroneCommand(0, new[] { a.Id })));

        var t1 = CoveredTile(sim, hub, 5);
        var t2 = CoveredTile(sim, hub, 4, t => s.Map.Grid.Distance(t, t1) >= 3);
        Do(sim, new LaunchDroneCommand(0, pad.Id, t1));
        Assert.Equal(DroneState.Launching, s.DroneOf(a).State);
        Assert.Equal(DroneState.Docked, s.DroneOf(b).State);
        Run(sim, Sec(4));
        Assert.Equal(DroneState.Flying, s.DroneOf(a).State);

        // Move = retarget the flying one; launch the docked one; a rover in the same order drives as usual.
        var rover = s.Units.First(u => u.Owner == 0 && u.DefId == "survey_rover");
        Do(sim, new MoveUnitsCommand(0, new[] { a.Id, b.Id, rover.Id }, t2));
        Assert.Equal(t2, s.DroneOf(a).Target);
        Assert.Equal(DroneState.Flying, s.DroneOf(a).State);
        Assert.Equal(DroneState.Launching, s.DroneOf(b).State);
        Assert.Equal(t2, s.DroneOf(b).Target);
        Assert.True(rover.Driving);
        var far = s.Map.Grid.Within(hub.Tile, 9).First(t => !s.CommsCovered(0, t));
        Assert.Contains("out of comms range", Refused(sim, new MoveUnitsCommand(0, new[] { a.Id }, far)));
        Assert.Equal(t2, s.DroneOf(a).Target);

        Do(sim, new RecallDroneCommand(0, new[] { a.Id, b.Id, rover.Id }));
        Assert.Equal(DroneState.Returning, s.DroneOf(a).State);
        Assert.Equal("ordered", s.DroneOf(a).ReturnReason);
        Assert.Contains(s.DroneOf(b).State, new[] { DroneState.Returning, DroneState.Landing }); // still over its bay
        StepUntil(sim, () => s.DronesOf(pad.Id).All(u => s.DroneOf(u).State == DroneState.Docked), Sec(40));
        Assert.Equal(s.DroneBayPos(pad, 0), a.Pos);
        Assert.Equal(s.DroneBayPos(pad, 1), b.Pos);
    }

    [Fact]
    public void ItTurnsHomeWhenCoverageGoes()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var pad = Pad(sim, hub);
        var drone = Charged(sim, pad);
        Do(sim, new RelaySatelliteCommand(0, hub.Tile));
        Run(sim, Sec(Data.Comms!.Relay!.DeploySeconds) + 1);
        var tile = s.Map.Grid.Within(hub.Tile, 12).First(t => s.Map.Grid.Distance(hub.Tile, t) == 12 && s.CommsCovered(0, t));
        Do(sim, new LaunchDroneCommand(0, pad.Id, tile));
        StepUntil(sim, () => s.Map.Grid.Distance(hub.Tile, drone.Tile) >= 7, Sec(30));
        var relay = s.Satellites.Single(x => x.Kind == SatelliteKind.Relay);
        var other = s.Map.Grid.Within(hub.Tile, 40).First(t => s.Map.Grid.Distance(hub.Tile, t) == 40);
        Do(sim, new RetaskSatelliteCommand(0, relay.Id, other)); // the relay moves away: out of coverage
        Assert.Equal(DroneState.Returning, s.DroneOf(drone).State);
        Assert.Equal("coverage", s.DroneOf(drone).ReturnReason);
        StepUntil(sim, () => s.DroneOf(drone).State == DroneState.Docked, Sec(30));
    }

    [Fact]
    public void ItComesHomeOnLowBatteryBeforeLoiteringIsDone()
    {
        // A battery that only just covers the trip: loitering is cut short and it turns home on its reserve.
        var data = TestData.Patched(("surfaces.json", "\"loiter_seconds\": 12", "\"loiter_seconds\": 120"),
            ("surfaces.json", "\"battery_seconds\": 180", "\"battery_seconds\": 130"));
        var sim = Landed(out var hub, data);
        var s = sim.Surface!;
        var pad = Pad(sim, hub);
        var drone = s.DronesOf(pad.Id).Single();
        StepUntil(sim, () => s.DroneOf(drone).Battery == Fix64.One, Sec(50));
        Assert.Null(s.LaunchProblem(sim, 0, pad.Id, pad.Tile));
        Do(sim, new LaunchDroneCommand(0, pad.Id, pad.Tile));
        StepUntil(sim, () => s.DroneOf(drone).State == DroneState.Loitering, Sec(10));
        StepUntil(sim, () => s.DroneOf(drone).State != DroneState.Loitering, Sec(130));
        Assert.Equal("battery", s.DroneOf(drone).ReturnReason);
        StepUntil(sim, () => s.DroneOf(drone).State == DroneState.Docked, Sec(10));
        Assert.True(s.DroneOf(drone).Battery >= data.DroneOf("scout_drone")!.Reserve - Fix64.Parse("0.01"));
    }

    // ---------------------------------------------------------------- losing drones, salvage

    [Fact]
    public void SalvagingThePadPutsDockedDronesBackAndLosesTheOneOut()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var pad = Pad(sim, hub, drones: 2);
        StepUntil(sim, () => s.DronesOf(pad.Id).All(u => s.DroneOf(u).Battery == Fix64.One), Sec(50));
        var (a, b) = (s.DronesOf(pad.Id)[0], s.DronesOf(pad.Id)[1]);
        Do(sim, new LaunchDroneCommand(0, pad.Id, CoveredTile(sim, hub, 5)));
        Assert.Equal(DroneState.Launching, s.DroneOf(a).State);
        var stock = sim.Players[0].Stock;
        var had = stock[R("scout_drone")];
        Do(sim, new SalvageCommand(0, pad.Id));
        sim.Step();
        Assert.Equal(had + Fix64.One, stock[R("scout_drone")]); // the docked one
        Assert.DoesNotContain(b, s.Units);
        Assert.Contains(a, s.Units);
        StepUntil(sim, () => s.DroneOf(a).State == DroneState.Lost, Sec(60)); // home to nothing
        Assert.Equal("no pad", s.DroneOf(a).ReturnReason);
        var lostAt = s.DroneOf(a).LostAtTick;
        Assert.True(lostAt > 0);
        StepUntil(sim, () => !s.Units.Contains(a), Sec((int)Drone.LostSeconds.FloorToInt() + 1));
        Assert.True(sim.Tick - lostAt >= Sec(4));
    }

    [Fact]
    public void ItLandsOnAnotherPadOrRunsDryOnTheWay()
    {
        // Slow drones with a short battery, so a second pad far away is out of reach.
        var data = TestData.Patched(("surfaces.json", "\"speed\": 3, \"survey_radius\": 2, \"aerial\": true", "\"speed\": 1, \"survey_radius\": 2, \"aerial\": true"),
            ("surfaces.json", "\"battery_seconds\": 180", "\"battery_seconds\": 40"));
        foreach (var far in new[] { false, true })
        {
            var sim = Landed(out var hub, data);
            var s = sim.Surface!;
            var pad = Pad(sim, hub);
            var drone = s.DronesOf(pad.Id).Single();
            StepUntil(sim, () => s.DroneOf(drone).Battery == Fix64.One, Sec(50));
            // A second pad: next to the first, or at a debug base far away.
            int padTile;
            if (!far) padTile = FreeTile(sim, hub, "drone_pad", 2);
            else
            {
                var coreTile = s.Map.Grid.Within(hub.Tile, 30).First(t => s.Map.Grid.Distance(hub.Tile, t) == 30
                    && s.DebugProblem(sim, new DebugPlaceBuildingCommand(0, 0, "colony_hub", t, 0)) == null);
                Do(sim, new DebugPlaceBuildingCommand(0, 0, "colony_hub", coreTile, 0));
                foreach (var t in s.Map.Grid.Within(coreTile, 3)) s.Players[0].Surveyed[t] = true;
                padTile = s.Map.Grid.Within(coreTile, 2).First(t => s.CheckPlacement(sim, 0, "drone_pad", t) == PlacementProblem.None);
            }
            Do(sim, new DebugPlaceBuildingCommand(0, 0, "drone_pad", padTile, 0));
            var pad2 = s.StructureAt(padTile)!;
            Do(sim, new LaunchDroneCommand(0, pad.Id, CoveredTile(sim, hub, 2)));
            Do(sim, new SalvageCommand(0, pad.Id));
            if (!far)
            {
                StepUntil(sim, () => s.DroneOf(drone).State == DroneState.Docked, Sec(60));
                Assert.Equal(pad2.Id, s.DroneOf(drone).Pad);
                Assert.Equal(s.DroneBayPos(pad2, 0), drone.Pos);
            }
            else
            {
                StepUntil(sim, () => s.DroneOf(drone).State == DroneState.Lost, Sec(80));
                Assert.Equal("battery", s.DroneOf(drone).ReturnReason);
                Assert.Equal(pad2.Id, s.DroneOf(drone).Pad);
                Assert.Equal(Fix64.Zero, s.DroneOf(drone).Battery);
            }
        }
    }

    // ---------------------------------------------------------------- drones and the rest of the game

    [Fact]
    public void DronesAreNeverInTheWayAndSkipGroundSystems()
    {
        var sim = Landed(out var hub);
        var s = sim.Surface!;
        var pad = Pad(sim, hub, drones: 2);
        var drone = s.DronesOf(pad.Id)[0];
        // The test panel can't break, teleport or spawn drones.
        Assert.NotNull(s.DebugProblem(sim, new DebugUnitCommand(0, drone.Id, DebugUnitField.Broken, Fix64.One)));
        Assert.NotNull(s.DebugProblem(sim, new DebugMoveUnitCommand(0, drone.Id, hub.Tile)));
        Assert.NotNull(s.DebugProblem(sim, new DebugSpawnUnitCommand(0, 0, "scout_drone", hub.Tile)));
        // Ground orders that make no sense for a drone are refused or do nothing to it.
        Refused(sim, new MineCommand(0, drone.Id, hub.Tile));
        Refused(sim, new RepairCommand(0, new[] { drone.Id }));
        Refused(sim, new ChangeTyreCommand(0, new[] { drone.Id }));
        Refused(sim, new FoundOutpostCommand(0, drone.Id, hub.Tile));
        Assert.Equal(DroneState.Docked, s.DroneOf(drone).State);
        // A rover drives onto the pad tile and parks, ignoring the drones there.
        var rover = s.Units.First(u => u.Owner == 0 && u.DefId == "survey_rover");
        Do(sim, new MoveUnitsCommand(0, new[] { rover.Id }, pad.Tile));
        StepUntil(sim, () => !rover.Driving, Sec(60));
        Assert.Equal(pad.Tile, rover.Tile);
        Assert.Equal(0, rover.BlockedBy);
        Assert.Equal(s.DroneBayPos(pad, 0), drone.Pos);
        Assert.Equal(Fix64.FromInt(100), drone.Integrity);
    }

    [Fact]
    public void DronesAreDeterministicAndInTheHash()
    {
        (List<ulong> Hashes, string Log) Play()
        {
            var sim = Landed(out var hub);
            var s = sim.Surface!;
            var pad = Pad(sim, hub, drones: 2);
            var hashes = new List<ulong>();
            var log = new List<string>();
            for (var i = 0; i < Sec(120); i++)
            {
                if (i == Sec(48)) sim.Schedule(sim.Tick, new LaunchDroneCommand(0, pad.Id, CoveredTile(sim, hub, 5)));
                if (i == Sec(50)) sim.Schedule(sim.Tick, new LaunchDroneCommand(0, pad.Id, CoveredTile(sim, hub, 3)));
                if (i == Sec(54)) sim.Schedule(sim.Tick, new RecallDroneCommand(0, s.DronesOf(pad.Id).Select(u => u.Id).ToList()));
                sim.Step();
                if (i % 30 == 0)
                {
                    hashes.Add(sim.StateHash());
                    log.Add(string.Join(",", s.DronesOf(pad.Id).Select(u => $"{s.DroneOf(u).State}:{u.Pos}")));
                }
            }
            return (hashes, string.Join("|", log));
        }
        var a = Play();
        var b = Play();
        Assert.Equal(a.Hashes, b.Hashes);
        Assert.Equal(a.Log, b.Log);
        Assert.Contains("Launching", a.Log);
        Assert.Contains("Returning", a.Log);

        // Every drone field is hashed.
        var sim2 = Landed(out var hub2);
        var pad2 = Pad(sim2, hub2);
        var drone = sim2.Surface!.DronesOf(pad2.Id).Single();
        var flight = typeof(SurfaceUnit).GetProperty("Drone", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(drone)!;
        var names = new List<string>();
        foreach (var f in flight.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var before = sim2.StateHash();
            var value = f.GetValue(flight)!;
            object changed = value switch
            {
                int i => i + 1,
                long l => l + 1,
                Fix64 x => x + Fix64.Half,
                string str => str + "x",
                FixVec2 v => v + new FixVec2(Fix64.One, Fix64.Zero),
                DroneState st => st == DroneState.Flying ? DroneState.Docked : DroneState.Flying,
                _ => throw new InvalidOperationException($"DroneFlight.{f.Name}: add its type here"),
            };
            f.SetValue(flight, changed);
            Assert.True(before != sim2.StateHash(), $"DroneFlight.{f.Name} is not in the state hash");
            f.SetValue(flight, value);
            Assert.Equal(before, sim2.StateHash());
            names.Add(f.Name);
        }
        Assert.Equal(11, names.Count);
    }
}
