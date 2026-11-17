using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Missions;
using FinalFrontier.Sim.Surface;
using Xunit.Abstractions;

namespace FinalFrontier.Sim.Tests;

/// <summary>
/// First Light played the way a player has to play it: only orders a player can give, no test panel (no placed
/// buildings, no topped-up stock, no finished objectives), the keep-in-stock targets left on, and only what the player
/// is shown (a search area, not the hidden site in it). Owner 2026-11-17: "to build a drone you need parsteel … but I
/// couldn't find any. We need to make sure that missions can be accomplished." MissionTests' golden paths place
/// buildings and top up metal with the test panel, which is how Wings' missing foundry slipped through; this one has to
/// earn everything. GameData.MissionReach.cs checks the same on the data alone, for every chain.
/// </summary>
public class HonestCampaignTests
{
    private readonly ITestOutputHelper _out;

    public HonestCampaignTests(ITestOutputHelper output) => _out = output;

    private Simulation _sim = null!;
    private PlanetSurface S => _sim.Surface!;
    private GameData Data => _sim.Data;
    private SurfaceStructure Hub => S.Bases(0)[0];
    private readonly Dictionary<int, long> _ordered = new(); // unit id → tick of our last order to it

    private int R(string id) => Data.Resource(id).Index;

    private Fix64 Stock(string id) => _sim.Players[0].Stock[R(id)];

    private MissionState State(string id) => MissionTests.State(_sim, id);

    private string Stage(string id) => MissionTests.View(_sim, id).StageTitle;

    private void Log(string text) => _out.WriteLine($"{_sim.Tick / Simulation.TickRate / 60,3}:{_sim.Tick / Simulation.TickRate % 60:00}  {text}");

    /// <summary>An order a player gives; it must be accepted.</summary>
    private void Order(Command c)
    {
        Assert.False(c is DebugCommand, "a player has no test panel");
        MissionTests.Do(_sim, c);
    }

    private bool Due(int unitId, int seconds) => !_ordered.TryGetValue(unitId, out var at) || _sim.Tick - at >= seconds * Simulation.TickRate;

    private static bool Idle(SurfaceUnit u) => u.Mode == UnitMode.Mobile && u.Path.Count == 0;

    private string _ore = "duranium_ore"; // what the crawler digs: whichever metal is short
    private bool _prospect; // idle rovers look for tritanium ore (after Ground Truth, which has its own survey)
    private readonly HashSet<int> _busy = new(); // rovers a drive order is using, or parked for a mission
    private readonly HashSet<int> _tried = new(); // tiles a prospecting rover was sent to
    private List<(int Tile, bool Hinted)>? _hills; // hills a rover can drive to from the hub (tritanium ore lies in hills)

    /// <summary>Idle rovers look for tritanium ore the way a player does: toward the nearest hills not surveyed yet (the
    /// terrain shows from orbit), first in the regions where the orbital scan hints at tritanium.</summary>
    private void Prospect()
    {
        var map = S.Map;
        if (_hills == null)
        {
            var tritanium = R("tritanium_ore");
            var reach = new HashSet<int> { Hub.Tile };
            var queue = new Queue<int>(reach);
            while (queue.Count > 0)
                foreach (var n in map.Grid.Neighbours(queue.Dequeue()))
                    if (map.Terrain(n).Passable && reach.Add(n)) queue.Enqueue(n);
            _hills = reach.Where(t => map.Terrain(t).Id == "hills").OrderBy(t => t)
                .Select(t => (t, map.Hints(map.Region(t)).Any(h => h.Resource == tritanium))).ToList();
        }
        foreach (var u in Rovers().Where(u => !_busy.Contains(u.Id) && Idle(u) && Due(u.Id, 6)))
        {
            var best = -1;
            var bestKey = int.MaxValue;
            foreach (var (t, hinted) in _hills)
            {
                if (S.Players[0].Surveyed[t] || _tried.Contains(t)) continue;
                var key = (hinted ? 0 : 1_000_000) + map.Grid.Distance(u.Tile, t);
                if (key < bestKey) (best, bestKey) = (t, key);
            }
            if (best < 0) return;
            _tried.Add(best);
            _sim.Schedule(_sim.Tick, new MoveUnitsCommand(0, new[] { u.Id }, best));
            _ordered[u.Id] = _sim.Tick;
        }
    }

    /// <summary>The nearest surveyed deposit of this ore the crawler could dig, or null.</summary>
    private int? Seam(SurfaceUnit crawler, string ore) => Enumerable.Range(0, S.Map.Grid.Count)
        .Where(t => S.Map.DepositResource[t] == R(ore) && S.Map.DepositAmount[t] > Fix64.Zero && S.Players[0].Surveyed[t]
                    && (t == crawler.Tile || S.MineProblem(0, crawler.Id, t) == null))
        .OrderBy(t => S.Map.Grid.Distance(Hub.Tile, t)).ThenBy(t => t).Cast<int?>().FirstOrDefault();

    /// <summary>What a player keeps an eye on all the time: the crawler digging the ore of the metal that is short (and
    /// going to the pad when it breaks down), the foundry not eating all the metal.</summary>
    private void Chores()
    {
        var map = S.Map;
        // The hub only trickles tritanium (2 a minute) and the chain needs more than the landing brings: dig some.
        if (_ore == "duranium_ore" && Stock("tritanium") < Fix64.FromInt(60) && Stock("duranium") > Fix64.FromInt(150)) _ore = "tritanium_ore";
        else if (_ore == "tritanium_ore" && (Stock("tritanium") > Fix64.FromInt(200) || Stock("duranium") < Fix64.FromInt(60))) _ore = "duranium_ore";
        foreach (var u in S.Units.Where(u => u.Owner == 0 && Data.SurfaceUnit(u.DefId).CanMine).ToList())
        {
            if (_prospect && _sim.Tick % (3 * Simulation.TickRate) == 0 && Seam(u, "tritanium_ore") == null) Prospect();
            if (!Due(u.Id, 8)) continue;
            if (u.Broken || S.IsRepairing(u))
            {
                if (u.Broken && Idle(u) && u.Tile != Hub.Tile)
                {
                    _sim.Schedule(_sim.Tick, new RepairCommand(0, new[] { u.Id }));
                    _ordered[u.Id] = _sim.Tick;
                    Log($"{u.Name} broke down: back to the pad");
                }
                continue;
            }
            var ore = Seam(u, _ore) != null ? _ore : "duranium_ore";
            var digging = u.Mode == UnitMode.Deployed && map.DepositResource[u.Tile] == R(ore) && map.DepositAmount[u.Tile] > Fix64.Zero;
            if (digging || (!Idle(u) && u.Mode != UnitMode.Deployed) || Seam(u, ore) is not { } tile || tile == u.Tile) continue;
            _sim.Schedule(_sim.Tick, new MineCommand(0, u.Id, tile));
            _ordered[u.Id] = _sim.Tick;
            Log($"{u.Name} sent to the {Data.Resource(ore).Name} at {tile} ({map.Grid.Distance(Hub.Tile, tile)} from the hub)");
        }
        // A disabled rover limps home (R); with no Rover Spares to be had (they need parsteel) the pad crew jury-rig it (J).
        foreach (var u in S.Units.Where(u => u.Owner == 0 && u.DefId.StartsWith("survey_rover") && u.Broken && Due(u.Id, 30)).ToList())
        {
            if (u.Tile != Hub.Tile && Idle(u)) _sim.Schedule(_sim.Tick, new RepairCommand(0, new[] { u.Id }));
            else if (u.Tile == Hub.Tile && u.RepairWaiting && !u.JuryRigOrdered) _sim.Schedule(_sim.Tick, new JuryRigUnitsCommand(0, new[] { u.Id }));
            else continue;
            _ordered[u.Id] = _sim.Tick;
        }
        // Buildings wear: send the crew (M) before one breaks, as the event feed asks. It takes spare kits.
        foreach (var b in S.Structures.Where(x => x.Owner == 0 && x.State == StructureState.Complete && !x.MaintenanceOrdered
                                                  && (x.Broken || x.Condition < Fix64.FromInt(60)) && Data.Building(x.DefId).Wears))
            _sim.Schedule(_sim.Tick, new MaintainCommand(0, b.Id));
        // The foundry runs only while Wings' power couplings need its parsteel; left on, it eats the metal (P pauses it).
        foreach (var f in S.Structures.Where(x => x.Owner == 0 && x.DefId == "foundry" && x.State == StructureState.Complete))
        {
            var pause = State("wings") == MissionState.Completed || Stock("parsteel") >= Fix64.FromInt(4);
            if (f.Paused != pause && (pause || Stock("parsteel") < Fix64.FromInt(2))) _sim.Schedule(_sim.Tick, new PauseCommand(0, f.Id, pause));
        }
    }

    private void Wait(Func<bool> done, int maxSeconds, string what, Action? everySecond = null)
    {
        for (var sec = 0; sec < maxSeconds && !done(); sec++)
        {
            Chores();
            everySecond?.Invoke();
            MissionTests.Seconds(_sim, 1);
        }
        if (!done()) Log(Report());
        Assert.True(done(), $"timed out after {maxSeconds} s: {what} (last refusal: {_sim.LastRefusal})");
    }

    /// <summary>Where the colony stands, for a timeout's message.</summary>
    private string Report()
    {
        var stock = string.Join(", ", Data.Resources.Where(r => _sim.Players[0].Stock[r.Index] > Fix64.Zero).Select(r => $"{r.Id} {_sim.Players[0].Stock[r.Index].FloorToInt()}"));
        var buildings = string.Join(", ", S.Structures.Where(x => x.Owner == 0).Select(x => $"{x.DefId} {x.State}/{x.Status} {x.Condition.FloorToInt()}%{(x.Broken ? " broken" : "")}{(x.Paused ? " paused" : "")}"));
        var units = string.Join(", ", S.Units.Where(u => u.Owner == 0).Select(u => $"{u.DefId} {u.Mode}/{u.MiningStatus} at {u.Tile} {u.Integrity.FloorToInt()}%{(u.Broken ? " broken" : "")}"));
        var queues = string.Join("; ", S.Structures.Where(x => x.Owner == 0 && x.Fabrication.Count > 0).Select(x => $"{x.DefId}: {string.Join(" ", x.Fabrication.Select(o => o.RecipeId + (o.Auto ? "*" : "")))}"));
        return $"stock: {stock}\n       storage {S.Stored(_sim, 0).FloorToInt()} of {S.StorageCapacity(0).FloorToInt()}\n       buildings: {buildings}\n       units: {units}\n       orders: {queues}";
    }

    private List<SurfaceUnit> Rovers() => S.Units.Where(u => u.Owner == 0 && u.DefId.StartsWith("survey_rover") && !u.Broken).ToList();

    /// <summary>Drives a rover to a tile (re-ordering when it stops short); false when it can't get there in time.</summary>
    private bool Drive(SurfaceUnit rover, int tile, int maxSeconds, Func<bool>? stop = null)
    {
        _busy.Add(rover.Id);
        try
        {
            _sim.Schedule(_sim.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, tile));
            for (var sec = 0; sec < maxSeconds; sec++)
            {
                if (stop?.Invoke() == true || (rover.Tile == tile && rover.Path.Count == 0)) return true;
                if (rover.Broken) return false;
                if (sec % 10 == 9 && rover.Path.Count == 0) _sim.Schedule(_sim.Tick, new MoveUnitsCommand(0, new[] { rover.Id }, tile));
                Chores();
                MissionTests.Seconds(_sim, 1);
            }
            return rover.Tile == tile && rover.Path.Count == 0;
        }
        finally
        {
            _busy.Remove(rover.Id);
        }
    }

    /// <summary>Finds a mission's hidden site the way a player does: rovers drive through its marked search area until it
    /// shows, then one parks at it.</summary>
    private MissionSite SearchAndPark(string mission, string kind)
    {
        var map = S.Map;
        var area = Assert.Single(_sim.Missions.Markers(0), m => m.MissionId == mission && m.Style == "area");
        bool Found() => _sim.Missions.Sites(0).Any(x => x.Kind == kind && x.Found);
        Wait(() => Rovers().Count > 0, 1800, "a rover that can drive");
        foreach (var tile in map.Grid.Within(area.Tile, area.RadiusTiles).Where(t => map.Terrain(t).Passable).OrderBy(t => map.Grid.Distance(area.Tile, t)).ThenBy(t => t))
        {
            if (Found()) break;
            if (S.Players[0].Surveyed[tile] || Rovers().Count == 0) continue; // seen already: nothing there
            Drive(Rovers().OrderBy(u => map.Grid.Distance(u.Tile, tile)).First(), tile, 600, Found);
        }
        Assert.True(Found(), $"the {kind} was not found by driving through the search area");
        var site = _sim.Missions.Sites(0).Single(x => x.Kind == kind);
        Log($"{kind} found at {site.Tile}, {map.Grid.Distance(Hub.Tile, site.Tile)} from the hub");
        var parked = false;
        Wait(() => Rovers().Count > 0, 1800, "a rover that can drive");
        foreach (var rover in Rovers().OrderBy(u => map.Grid.Distance(u.Tile, site.Tile)))
        {
            if (!(parked = Drive(rover, site.Tile, 900))) continue;
            _busy.Add(rover.Id); // it stays there while the crew work (Release once the mission is done)
            break;
        }
        Assert.True(parked, $"no rover could park at the {kind}");
        return site;
    }

    /// <summary>Places a building near the hub once a spot is free and it can be paid for.</summary>
    private void Build(string id, int maxSeconds)
    {
        int? Spot() => S.Map.Grid.Within(Hub.Tile, 3).OrderBy(t => S.Map.Grid.Distance(Hub.Tile, t)).ThenBy(t => t)
            .Where(t => S.Map.DepositResource[t] < 0 && S.CheckPlacement(_sim, 0, id, t) == PlacementProblem.None).Cast<int?>().FirstOrDefault();
        Wait(() => Spot() != null, maxSeconds, $"a spot and the price of a {Data.Building(id).Name}");
        Order(new PlaceBuildingCommand(0, id, Spot()!.Value, 0));
        Log($"{Data.Building(id).Name} placed");
    }

    private SurfaceStructure Finished(string id, int maxSeconds)
    {
        Wait(() => S.Structures.Any(x => x.Owner == 0 && x.DefId == id && x.State == StructureState.Complete), maxSeconds, $"the {Data.Building(id).Name} built");
        return S.Structures.First(x => x.Owner == 0 && x.DefId == id && x.State == StructureState.Complete);
    }

    private bool RelayCovers(int tile) =>
        S.CommsSources(0).Any(x => x.Kind == CommsSourceKind.Relay && x.Ready && S.Map.Grid.Distance(x.Tile, tile) <= x.Radius);

    /// <summary>Flies the pad's drones over the wanted tiles, one launch at a time, until done: a tile out of comms range
    /// gets a relay satellite (a new one while there are some left and it can be paid for, else the newest one moved).</summary>
    private void Fly(SurfaceStructure pad, Func<List<int>> wanted, Func<bool> done, int maxSeconds, string what)
    {
        var sent = new List<int>();
        var sec = 0;
        Wait(done, maxSeconds, what, () =>
        {
            if (sec++ % 3 != 0) return;
            foreach (var tile in wanted().OrderBy(t => sent.Contains(t)).ThenBy(t => S.Map.Grid.Distance(Hub.Tile, t)))
            {
                if (!S.CommsCovered(0, tile))
                {
                    var relays = S.CommsSources(0).Where(x => x.Kind == CommsSourceKind.Relay).ToList();
                    if (relays.Any(x => !x.Ready)) return; // one is still drifting to its station
                    if (S.RelayCount(0) < Data.Comms!.Relay!.Max)
                    {
                        if (S.RelayProblem(_sim, 0, tile) != null) continue; // can't pay for it yet: try another tile meanwhile
                        Order(new RelaySatelliteCommand(0, tile));
                    }
                    else Order(new RetaskSatelliteCommand(0, relays[^1].SatelliteId, tile));
                    Log($"relay satellite for {tile}");
                    return;
                }
                var flying = S.Units.Any(u => u.Owner == 0 && S.DroneOf(u).State is DroneState.Launching or DroneState.Flying or DroneState.Loitering && S.DroneOf(u).Target == tile);
                if (flying || S.LaunchProblem(_sim, 0, pad.Id, tile) != null) continue;
                Order(new LaunchDroneCommand(0, pad.Id, tile));
                if (!sent.Contains(tile)) sent.Add(tile);
                return;
            }
        });
    }

    /// <summary>Plays First Light from the landing with a player's orders only, until the mission stopAfter is done.</summary>
    private void Play(string stopAfter)
    {
        _sim = MissionTests.Game(stockTargets: true);
        var map = S.Map;
        Assert.Equal(new[] { "fusion_reactor", "mine", "refinery", "storage_container" }, S.Players[0].UnlockedBuildings);

        // 1. Ground Truth: an Ore Refinery (the campaign's first building), then the rovers out to survey.
        Build("refinery", 60);
        Wait(() => State("ground_truth") == MissionState.Active, 10, "Ground Truth starts with the refinery");
        var ring = Enumerable.Range(0, map.Grid.Count).Where(t => map.Grid.Distance(Hub.Tile, t) is >= 5 and <= 9 && map.Terrain(t).Passable).ToList();
        var next = 0;
        void Explore()
        {
            foreach (var u in Rovers().Where(u => Idle(u) && Due(u.Id, 5)))
            {
                _sim.Schedule(_sim.Tick, new MoveUnitsCommand(0, new[] { u.Id }, ring[next++ * 37 % ring.Count]));
                _ordered[u.Id] = _sim.Tick;
            }
        }
        Wait(() => State("ground_truth") == MissionState.Completed, 1500, "60 tiles surveyed and 2 resources found", Explore);
        Log("Ground Truth done");
        _prospect = true;
        if (stopAfter == "ground_truth") return;

        // 2. The Lost Pathfinder: the player only knows the search area; drive through it until the wreck shows, then park by it.
        Wait(() => State("lost_pathfinder") == MissionState.Active, 10, "The Lost Pathfinder starts");
        var wreck = SearchAndPark("lost_pathfinder", "probe_wreck");
        Wait(() => State("lost_pathfinder") == MissionState.Completed, 60, "the recorder recovered");
        _busy.Clear();
        Log("The Lost Pathfinder done");
        if (stopAfter == "lost_pathfinder") return;

        // 3. Calling Home: a relay over the outpost, and one over the wreck if the first doesn't reach it. Paid from stock.
        Wait(() => State("calling_home") == MissionState.Active, 10, "Calling Home starts");
        Wait(() => S.RelayProblem(_sim, 0, Hub.Tile) == null, 1200, "the price of a relay satellite");
        Order(new RelaySatelliteCommand(0, Hub.Tile));
        Wait(() => State("calling_home") == MissionState.Completed || Stage("calling_home") != "Put up a relay", 60, "the relay on station");
        if (State("calling_home") == MissionState.Active && !RelayCovers(wreck.Tile))
        {
            Wait(() => S.RelayProblem(_sim, 0, wreck.Tile) == null, 1200, "the price of a second relay");
            Order(new RelaySatelliteCommand(0, wreck.Tile));
        }
        Wait(() => State("calling_home") == MissionState.Completed, 60, "the wreck linked");
        Log("Calling Home done");
        if (stopAfter == "calling_home") return;

        // 4. Wings: the Machine Shop, the Drone Pad and the Alloy Foundry open now; build them, make the parts, then the drones.
        Wait(() => State("wings") == MissionState.Active, 10, "Wings starts");
        Assert.True(S.BuildingUnlocked(0, "foundry"));
        Assert.Equal(Fix64.Zero, Stock("parsteel"));
        Assert.Equal(Fix64.Zero, Stock("power_coupling"));
        foreach (var id in new[] { "machine_shop", "foundry", "drone_pad" }) Build(id, 2400);
        var shop = Finished("machine_shop", 600);
        Finished("foundry", 600);
        var pad = Finished("drone_pad", 600);
        Log("Wings: all three built");
        // A drone takes a frame, a power coupling and two control modules (two modules came with the Pathfinder's recorder).
        Order(new FabricateCommand(0, shop.Id, "structural_frame", 2));
        Order(new FabricateCommand(0, shop.Id, "power_coupling", 2));
        var modules = 4 - System.Math.Min(4, Stock("control_module").FloorToInt());
        if (modules > 0) Order(new FabricateCommand(0, shop.Id, "control_module", modules));
        Order(new FabricateCommand(0, shop.Id, "scout_drone", 2));
        Wait(() => State("wings") == MissionState.Completed, 2400, "two scout drones fabricated");
        Log("Wings done");
        if (stopAfter == "wings") return;

        // 5. First Flight: launch a drone, then one over each of the three photo points.
        Wait(() => State("first_flight") == MissionState.Active, 10, "First Flight starts");
        Wait(() => S.LaunchProblem(_sim, 0, pad.Id, Hub.Tile) == null, 600, "a drone charged on the pad");
        Order(new LaunchDroneCommand(0, pad.Id, Hub.Tile));
        Wait(() => State("first_flight") == MissionState.Completed || Stage("first_flight") == "Photo survey", 30, "the launch counted");
        Fly(pad, () => _sim.Missions.Sites(0).Where(x => x.Kind == "photo_point" && !x.Done).Select(x => x.Tile).ToList(),
            () => State("first_flight") == MissionState.Completed, 2400, "a drone over each photo point");
        Log("First Flight done");
        if (stopAfter == "first_flight") return;

        // 6. Beyond the Ridge: bearings from two of the three marked points by drone, then the source by rover.
        Wait(() => State("beyond_the_ridge") == MissionState.Active, 10, "Beyond the Ridge starts");
        var bearings = _sim.Missions.Markers(0).Where(m => m.MissionId == "beyond_the_ridge").Select(m => m.Tile).ToList();
        Assert.Equal(3, bearings.Count);
        Fly(pad, () => bearings, () => Stage("beyond_the_ridge") != "Take bearings", 2400, "bearings from two points");
        SearchAndPark("beyond_the_ridge", "signal_source");
        Wait(() => State("beyond_the_ridge") == MissionState.Completed, 60, "the signal source reached");
        _busy.Clear();
        Log("Beyond the Ridge done");
        if (stopAfter == "beyond_the_ridge") return;

        // 7. Foothold: a Vehicle Bay, a construction rover, and a second outpost founded where a relay reaches.
        Wait(() => State("foothold") == MissionState.Active, 10, "Foothold starts");
        Build("vehicle_bay", 2400);
        var bay = Finished("vehicle_bay", 600);
        var unit = Data.SurfaceUnit("construction_rover");
        Wait(() => _sim.Players[0].Stock.CanAfford(unit.Cost), 2400, "the price of a construction rover");
        Order(new BuildUnitCommand(0, bay.Id, unit.Id));
        Wait(() => S.Units.Any(u => u.Owner == 0 && u.DefId == unit.Id), 600, "the construction rover built");
        var rover = S.Units.First(u => u.Owner == 0 && u.DefId == unit.Id);
        int? Site() => Enumerable.Range(0, map.Grid.Count).Where(t => RelayCovers(t) && S.SiteProblem(_sim, 0, t, unit.Found!, rover) == FoundProblem.None)
            .OrderBy(t => map.Grid.Distance(Hub.Tile, t)).ThenBy(t => t).Cast<int?>().FirstOrDefault();
        Wait(() => State("foothold") == MissionState.Completed, 3600, "a second outpost under relay coverage", () =>
        {
            if (S.Units.All(u => u.Id != rover.Id) || !Idle(rover) || !Due(rover.Id, 20) || Site() is not { } site) return;
            _sim.Schedule(_sim.Tick, new FoundOutpostCommand(0, rover.Id, site));
            _ordered[rover.Id] = _sim.Tick;
            Log($"construction rover sent to found an outpost at {site} ({map.Grid.Distance(Hub.Tile, site)} from the hub)");
        });
        Log("Foothold done: First Light complete");
    }

    [Fact]
    public void WingsCanBeFinishedWithOnlyAPlayersOrders()
    {
        Play("wings");
        Assert.Equal(0, _sim.DebugEdits); // nothing came from the test panel
        Assert.All(new[] { "ground_truth", "lost_pathfinder", "calling_home", "wings" }, id => Assert.Equal(MissionState.Completed, State(id)));
        Wait(() => State("first_flight") == MissionState.Active, 10, "First Flight starts"); // and the chain goes on
    }

    [Fact]
    public void AllOfFirstLightCanBeFinishedWithOnlyAPlayersOrders()
    {
        Play("foothold");
        Assert.Equal(0, _sim.DebugEdits);
        Assert.All(Data.Missions!.List.Where(m => m.Chain == "first_light" && m.IsMain), m => Assert.Equal(MissionState.Completed, State(m.Id)));
        Assert.All(S.BuildMenu, id => Assert.True(S.BuildingUnlocked(0, id), id)); // the whole menu is open by the end
    }
}
