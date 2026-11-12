using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Xunit.Abstractions;

namespace FinalFrontier.Sim.Tests;

/// <summary>A command type the sim has never heard of (as a newer or tampered peer might send).</summary>
public sealed record FakeCommand(int Player, int Junk) : Command(Player);

/// <summary>A test-panel command type the sim has never heard of.</summary>
public sealed record FakeDebugCommand(int Player) : DebugCommand(Player);

/// <summary>
/// Multiplayer safety: a peer can send anything. Thousands of random commands (every type, a fake unknown type, random
/// ids, tiles, players, enums and numbers from a seeded RNG) against a two-player landed game must never throw, must give
/// the same state hashes on two identical runs, and must leave the game in a sane state (no negative stock, units on
/// the map, the tile index matching the buildings, ...).
/// </summary>
public class CommandFuzzTests
{
    private readonly ITestOutputHelper _out;

    public CommandFuzzTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestData.Real;

    /// <summary>Two players (Federation and Cardassian) landed on seed 1701, both outposts assembled.</summary>
    internal static Simulation TwoPlayersLanded(GameData? data = null, bool missions = false)
    {
        data ??= Data;
        var sim = new Simulation(data, 1701);
        sim.LoadSurface(MatchSetup.MvpPlanetClass, 1701);
        sim.AddPlayer("federation");
        sim.AddPlayer("cardassian");
        if (missions) Assert.Null(sim.Missions.Enable("first_light")); // campaign missions on (MissionTests)
        var map = sim.Surface!.Map;
        var first = Enumerable.Range(0, map.Grid.Count).First(t => sim.Surface.CanLand(0, t));
        var second = Enumerable.Range(0, map.Grid.Count).Last(t => sim.Surface.CanLand(1, t) && map.Grid.Distance(t, first) > 20);
        sim.Schedule(0, new LandColonyCommand(0, first));
        sim.Schedule(0, new LandColonyCommand(1, second));
        for (var i = 0; i <= TestData.ReadyTicks(data); i++) sim.Step();
        return sim;
    }

    /// <summary>Makes random commands from a seeded RNG, looking at the game for ids and tiles that exist (most of the
    /// time) so that plenty of them are accepted, mixed with nonsense.</summary>
    private sealed class Fuzzer
    {
        private readonly DeterministicRng _rng;
        private readonly Simulation _sim;
        private readonly List<DataNumber> _numbers;

        public Fuzzer(Simulation sim, ulong seed)
        {
            _sim = sim;
            _rng = new DeterministicRng(seed);
            _numbers = DataTuning.Numbers(sim.Data).ToList();
        }

        private int N(int max) => _rng.NextInt(max);
        private bool Chance(int percent) => N(100) < percent;
        private T Pick<T>(IReadOnlyList<T> list) => list[N(list.Count)];

        private static readonly int[] Weird = { -1, -2, 0, int.MaxValue, int.MinValue, 1 << 20, 99999 };

        private int AnyInt() => Chance(50) ? Pick(Weird) : _rng.NextInt(-50, 50);

        private int Player() => Chance(85) ? N(_sim.Players.Count) : AnyInt();

        private PlanetSurface S => _sim.Surface!;

        private int Tile()
        {
            var count = S.Map.Grid.Count;
            if (Chance(10)) return Chance(50) ? Pick(Weird) : count + N(5);
            // Mostly near someone's base, where things happen.
            var cores = S.Structures.Where(s => s.Id == s.BaseId).ToList();
            if (cores.Count > 0 && Chance(70))
            {
                var near = S.Map.Grid.Within(Pick(cores).Tile, 6).ToList();
                return Pick(near);
            }
            return N(count);
        }

        private int UnitId() =>
            S.Units.Count > 0 && Chance(80) ? Pick(S.Units).Id : AnyInt();

        private int StructureId() =>
            S.Structures.Count > 0 && Chance(80) ? Pick(S.Structures).Id : AnyInt();

        private int FabricatorId()
        {
            var fabs = S.Structures.Where(S.IsFabricator).ToList();
            return fabs.Count > 0 ? Pick(fabs).Id : StructureId();
        }

        private int OrderId()
        {
            var orders = S.Structures.SelectMany(s => s.Fabrication).ToList();
            return orders.Count > 0 && Chance(80) ? Pick(orders).Id : AnyInt();
        }

        private IReadOnlyList<int> UnitIds()
        {
            if (Chance(5)) return null!; // a broken decoder
            var n = Chance(10) ? 0 : 1 + N(4);
            return Enumerable.Range(0, n).Select(_ => UnitId()).ToList();
        }

        private string? Text(IReadOnlyList<string> valid)
        {
            if (Chance(80) && valid.Count > 0) return Pick(valid);
            return N(4) switch { 0 => null, 1 => "", 2 => "no_such_thing", _ => OrbitRules.Supplies };
        }

        private string? BuildingId() => Text(Chance(75) ? Data.BuildMenu : Data.Buildings.Select(b => b.Id).ToList());
        private string? SurfaceUnitId() => Text(Data.SurfaceUnits.Select(u => u.Id).ToList());
        private string? TechId() => Text(Data.Techs.Select(t => t.Id).ToList());
        private string? MissionId() => Text(Data.Missions?.List.Select(m => m.Id).ToList() ?? new List<string>());

        private Fix64 Value()
        {
            switch (N(6))
            {
                case 0: return Fix64.FromInt(_rng.NextInt(-5, 120));
                case 1: return Fix64.FromRaw((long)_rng.NextULong());
                case 2: return Pick(new[] { Fix64.Zero, Fix64.One, Fix64.Half, Fix64.MaxValue, Fix64.MinValue, -Fix64.One });
                case 3: return _rng.NextFix();
                default: return Fix64.FromInt(N(101));
            }
        }

        private TEnum Enum<TEnum>() where TEnum : struct, System.Enum
        {
            var values = System.Enum.GetValues<TEnum>();
            return Chance(90) ? values[N(values.Length)] : (TEnum)(object)_rng.NextInt(-3, values.Length + 3);
        }

        private IReadOnlyList<ResourceAmount> Amounts()
        {
            if (Chance(5)) return null!;
            var n = N(4);
            return Enumerable.Range(0, n).Select(_ => new ResourceAmount(Chance(90) ? N(Data.Resources.Count) : AnyInt(), Value())).ToList();
        }

        /// <summary>How often each kind of command comes up (per mille-ish). Building things up outweighs taking them
        /// away, so the game keeps bases, bays, crawlers and rovers to fuzz.</summary>
        private static readonly int[] Weights =
        {
            5, 5, 60, 80, 8, 20, 30, 10, 40, 10, 10, 10, 10, 5, 15, 15, 15, 10, 6, 15, 5, 5, // 0..21 player commands
            40, 15, 4, 20, 50, 4, 50, 15, 25, 15, 15, 3, 10, 2, 1, 1, // 22..37 test panel
            15, // 38 pause / resume a building
            30, 10, 10, 10, 10, // 39..43 parts economy: fabricate, cancel an order, stock target, jury-rig a building / vehicles
            10, // 44 retask a survey satellite
            25, 8, 8, // 45..47 drones and comms: launch a drone, recall drones, put up a relay satellite
            20, 8, // 48, 49 missions: a choice (accept / decline / a branch), the test panel's mission edits
        };

        private static readonly int TotalWeight = Weights.Sum();

        public Command Next()
        {
            var p = Player();
            var roll = N(TotalWeight);
            var kind = 0;
            while (roll >= Weights[kind]) roll -= Weights[kind++];
            switch (kind)
            {
                case 0: return new MoveCommand(p, UnitIds(), new FixVec2(Value(), Value()));
                case 1: return new LandColonyCommand(p, Tile());
                case 2: return new MoveUnitsCommand(p, UnitIds(), Tile());
                case 3: return new PlaceBuildingCommand(p, BuildingId()!, Tile(), AnyInt());
                case 4: return new CancelBuildingCommand(p, StructureId());
                case 5: return new UpgradeCommand(p, StructureId());
                case 6: return new MineCommand(p, UnitId(), Tile());
                case 7: return new PackUpCommand(p, UnitIds());
                case 8: return new BuildUnitCommand(p, StructureId(), SurfaceUnitId()!);
                case 9: return new CancelUnitCommand(p, StructureId());
                case 10: return new OutpostDropCommand(p, Tile());
                case 11: return new ShuttleCommand(p, Tile(), SurfaceUnitId()!);
                case 12: return new BeamCommand(p, Tile(), SurfaceUnitId()!);
                case 13: return new SatelliteCommand(p, Chance(80) ? N(S.Map.Grid.Height) : AnyInt(), Chance(50) ? 0 : Chance(80) ? N(S.Map.Grid.Width) : AnyInt());
                case 14: return new ResearchCommand(p, TechId()!);
                case 15: return new MaintainCommand(p, StructureId());
                case 16: return new RepairCommand(p, UnitIds());
                case 17: return new ChangeTyreCommand(p, UnitIds());
                case 18: return new SalvageCommand(p, StructureId());
                case 19: return new FoundOutpostCommand(p, UnitId(), Tile());
                case 20: return new FakeCommand(p, AnyInt());
                case 21: return new FakeDebugCommand(p);
                // Test-panel commands.
                case 22: return new DebugUnitCommand(p, UnitId(), Enum<DebugUnitField>(), Value());
                case 23: return new DebugMoveUnitCommand(p, UnitId(), Tile());
                case 24: return new DebugRemoveUnitCommand(p, UnitId());
                case 25: return new DebugSpawnUnitCommand(p, Player(), SurfaceUnitId()!, Tile());
                case 26: return new DebugStructureCommand(p, StructureId(), Enum<DebugStructureField>(), Value());
                case 27: return new DebugRemoveStructureCommand(p, StructureId());
                case 28: return new DebugPlaceBuildingCommand(p, Player(), BuildingId()!, Tile(), AnyInt());
                case 29: return new DebugBaseCommand(p, StructureId(), Enum<DebugBaseAction>(), Value());
                case 30:
                    return Chance(50) // plenty of everything, so orders get paid for
                        ? new DebugStockCommand(p, Player(), Data.Resources.Select(r => new ResourceAmount(r.Index, Fix64.FromInt(5000))).ToList())
                        : new DebugStockCommand(p, Player(), Amounts());
                case 31: return new DebugFlagCommand(p, Player(), Enum<DebugFlag>(), Chance(50));
                case 32: return new DebugTechCommand(p, Player(), TechId()!, Chance(50), Value());
                case 33: return new DebugSurveyAllCommand(p, Player());
                case 34: return new DebugDepositCommand(p, Tile(), Value());
                case 35: return new DebugAddPlayerCommand(p, Text(Data.Factions.Select(f => f.Id).ToList())!);
                case 36: return new DebugDataCommand(p, Chance(10) ? null! : new[] { DataEdit() }); // slow (reloads the data): rare
                case 37: return new DebugReloadDataCommand(p, Chance(30) ? null! : new[] { new DataFile(Pick(_numbers).File, Chance(50) ? "{ nonsense" : null!) });
                case 38: return new PauseCommand(p, StructureId(), Chance(50));
                // Parts economy.
                case 39: return new FabricateCommand(p, Chance(30) ? FabricatorId() : StructureId(), Text(S.PartItems)!, Chance(80) ? 1 + N(6) : AnyInt());
                case 40: return new CancelFabricateCommand(p, StructureId(), OrderId());
                case 41: return new StockTargetCommand(p, Text(S.PartItems)!, Chance(80) ? N(8) : AnyInt());
                case 42: return new JuryRigCommand(p, StructureId());
                case 43: return new JuryRigUnitsCommand(p, UnitIds());
                case 44:
                {
                    // Relays take a station tile in the row field, survey satellites a row.
                    var sats = S.Satellites;
                    var sat = sats.Count > 0 && Chance(80) ? Pick(sats) : null;
                    var row = sat?.Kind == SatelliteKind.Relay && Chance(80) ? Tile() : Chance(80) ? N(S.Map.Grid.Height) : AnyInt();
                    return new RetaskSatelliteCommand(p, sat?.Id ?? (Chance(80) ? 1 + N(6) : AnyInt()), row);
                }
                case 45:
                {
                    var pads = S.Structures.Where(x => S.DroneBays(x) > 0).ToList();
                    return new LaunchDroneCommand(p, pads.Count > 0 && Chance(80) ? Pick(pads).Id : StructureId(), Tile());
                }
                case 46:
                {
                    var drones = S.Units.Where(u => S.IsAerial(u)).Select(u => u.Id).ToList();
                    return new RecallDroneCommand(p, drones.Count > 0 && Chance(70) ? new[] { Pick(drones) } : UnitIds());
                }
                case 47: return new RelaySatelliteCommand(p, Tile());
                case 48: return new MissionChoiceCommand(p, MissionId()!, Text(new[] { "accept", "accept", "decline", "launch" })!);
                default:
                    return new DebugMissionCommand(p, Player(), MissionId()!, Chance(3) ? DebugMissionAction.Enable : Enum<DebugMissionAction>(),
                        Text(Data.Missions?.List.SelectMany(m => m.Stages.Select(st => st.Id)).ToList() ?? new List<string>())!,
                        Text(Data.Missions?.List.SelectMany(m => m.Stages.SelectMany(st => st.Objectives.Select(o => o.Id))).ToList() ?? new List<string>())!,
                        Chance(70) ? Tile() : AnyInt());
            }
        }

        private DataEdit DataEdit()
        {
            var n = Pick(_numbers);
            var value = N(4) switch
            {
                0 => "abc",
                1 => "-1",
                2 => "0",
                _ => (Fix64.Parse(n.Value) * Fix64.Ratio(N(4) + 1, 2)).ToString(),
            };
            return Chance(5) ? new DataEdit(n.File, null!, value) : new DataEdit(n.File, n.Path, value);
        }
    }

    /// <summary>What must hold in any game, however it was played.</summary>
    internal static List<string> Problems(Simulation sim)
    {
        var problems = new List<string>();
        var s = sim.Surface!;
        var grid = s.Map.Grid;
        for (var p = 0; p < sim.Players.Count; p++)
        {
            var stock = sim.Players[p].Stock;
            for (var r = 0; r < stock.Count; r++)
                if (stock[r] < Fix64.Zero) problems.Add($"player {p} has {stock[r]} of {sim.Data.Resources[r].Id}");
            if (p < s.Players.Count)
                for (var r = 0; r < s.Players[p].Hold.Length; r++)
                    if (s.Players[p].Hold[r] < Fix64.Zero) problems.Add($"player {p}'s hold has {s.Players[p].Hold[r]} of {r}");
        }
        if (s.Players.Count != sim.Players.Count) problems.Add("surface and sim player counts differ");

        var unitIds = new HashSet<int>();
        foreach (var u in s.Units)
        {
            if (!unitIds.Add(u.Id)) problems.Add($"unit id {u.Id} twice");
            if (u.Owner < 0 || u.Owner >= sim.Players.Count) problems.Add($"unit {u.Id} owner {u.Owner}");
            if (u.Tile < 0 || u.Tile >= grid.Count) { problems.Add($"unit {u.Id} off the map at {u.Tile}"); continue; }
            if (!s.Map.Terrain(u.Tile).Passable && !s.IsAerial(u)) problems.Add($"unit {u.Id} on impassable {s.Map.Terrain(u.Tile).Id} tile {u.Tile}");
            if (u.Integrity < Fix64.Zero || u.Integrity > Fix64.FromInt(100)) problems.Add($"unit {u.Id} integrity {u.Integrity}");
            if (u.Spares < 0) problems.Add($"unit {u.Id} spares {u.Spares}");
            if (u.Hopper < Fix64.Zero) problems.Add($"unit {u.Id} hopper {u.Hopper}");
            foreach (var t in u.Path)
                if (t < 0 || t >= grid.Count) problems.Add($"unit {u.Id} path through {t}");
            var drone = s.DroneOf(u);
            if (s.IsAerial(u) != (drone.State != DroneState.None)) problems.Add($"unit {u.Id} aerial without a drone state");
            if (drone.Battery < Fix64.Zero || drone.Battery > Fix64.One) problems.Add($"drone {u.Id} battery {drone.Battery}");
            if (drone.State == DroneState.Docked && (s.FindStructure(drone.Pad) is not { } pad || pad.Tile != u.Tile)) problems.Add($"drone {u.Id} docked off its pad");
            if (drone.State != DroneState.None && s.Units.Any(o => o != u && s.DroneOf(o) is { State: not DroneState.None } od && od.Pad == drone.Pad && od.Bay == drone.Bay))
                problems.Add($"drones share bay {drone.Bay} of pad {drone.Pad}");
            if (s.DomeTransitOf(u) is { } tr && (!s.NeedsDomes || tr.FromTile != u.Tile)) problems.Add($"unit {u.Id} transit {tr}");
        }

        var structureIds = new HashSet<int>();
        var tiles = new HashSet<int>();
        foreach (var st in s.Structures)
        {
            if (!structureIds.Add(st.Id)) problems.Add($"structure id {st.Id} twice");
            if (st.Tile < 0 || st.Tile >= grid.Count) { problems.Add($"structure {st.Id} off the map"); continue; }
            if (!tiles.Add(st.Tile)) problems.Add($"two structures on tile {st.Tile}");
            if (s.StructureAt(st.Tile) != st) problems.Add($"tile index doesn't point at structure {st.Id}");
            if (st.Owner < 0 || st.Owner >= sim.Players.Count) problems.Add($"structure {st.Id} owner {st.Owner}");
            if (st.Condition < Fix64.Zero || st.Condition > Fix64.FromInt(100)) problems.Add($"structure {st.Id} condition {st.Condition}");
            if (s.FindStructure(st.BaseId) == null) problems.Add($"structure {st.Id} belongs to missing base {st.BaseId}");
            foreach (var order in st.Production)
                if (sim.Data.SurfaceUnits.All(d => d.Id != order)) problems.Add($"bay {st.Id} builds unknown {order}");
            if (st.Fabrication.Count > PlanetSurface.MaxFabQueue) problems.Add($"fabricator {st.Id} queue {st.Fabrication.Count}");
            if (st.Fabrication.Count > 0 && !s.HasFabricator(st)) problems.Add($"structure {st.Id} has orders but no fabricator");
            foreach (var o in st.Fabrication)
            {
                if (!s.RecipesAt(st).Contains(o.RecipeId)) problems.Add($"fabricator {st.Id} makes {o.RecipeId}, not one of its recipes");
                if (o.Progress < Fix64.Zero || o.Progress > Fix64.One) problems.Add($"order {o.Id} progress {o.Progress}");
                if (!o.Started && o.Progress != Fix64.Zero) problems.Add($"order {o.Id} moved before it started");
            }
        }
        for (var t = 0; t < grid.Count; t++)
        {
            var at = s.StructureAt(t);
            if (at != null && at.Tile != t) problems.Add($"tile {t} points at structure {at.Id} on {at.Tile}");
            if (s.Map.DepositAmount[t] < Fix64.Zero) problems.Add($"deposit {t} is {s.Map.DepositAmount[t]}");
        }
        var orderIds = s.Structures.SelectMany(st => st.Fabrication).Select(o => o.Id).ToList();
        if (orderIds.Distinct().Count() != orderIds.Count) problems.Add("two fabrication orders with one id");
        foreach (var d in s.Deliveries)
            if (d.Tile < 0 || d.Tile >= grid.Count) problems.Add($"delivery {d.Id} to {d.Tile}");
        foreach (var pod in s.Pods)
            if (pod.Amount < Fix64.Zero) problems.Add($"pod {pod.Id} carries {pod.Amount}");
        foreach (var sat in s.Satellites)
        {
            if (sat.Row < 0 || sat.Row >= grid.Height) problems.Add($"satellite {sat.Id} over row {sat.Row}");
            if (sat.Kind == SatelliteKind.Relay && (sat.StationTile < 0 || sat.StationTile >= grid.Count || grid.Row(sat.StationTile) != sat.Row))
                problems.Add($"relay {sat.Id} station {sat.StationTile}");
        }
        return problems;
    }

    /// <summary>Plays one fuzzed game: returns the state hashes along the way and the number of commands sent.</summary>
    private static (List<ulong> Hashes, int Commands, int Refused) Play(ulong seed, int ticks, List<string> problems)
    {
        var sim = TwoPlayersLanded(missions: true);
        var fuzz = new Fuzzer(sim, seed);
        var hashes = new List<ulong>();
        var sent = 0;
        for (var i = 0; i < ticks; i++)
        {
            var n = (int)((seed + (ulong)i) % 4); // 0..3 commands this tick
            for (var k = 0; k < n; k++)
            {
                var c = fuzz.Next();
                // Sometimes a little ahead, as with input delay.
                sim.Schedule(sim.Tick + (k == 2 ? 3 : 0), c);
                sent++;
            }
            sim.Step();
            if (i % 50 == 0)
            {
                hashes.Add(sim.StateHash());
                foreach (var p in Problems(sim)) problems.Add($"tick {sim.Tick}: {p}");
                if (problems.Count > 20) break;
            }
        }
        hashes.Add(sim.StateHash());
        problems.AddRange(Problems(sim));
        return (hashes, sent, sim.RefusedCommands);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(1701UL)]
    [InlineData(424242UL)]
    public void RandomCommandsNeverCrashAndStayDeterministic(ulong seed)
    {
        var problems = new List<string>();
        var a = Play(seed, 2400, problems);
        Assert.Empty(problems);
        var b = Play(seed, 2400, new List<string>());
        Assert.Equal(a.Hashes, b.Hashes);
        Assert.True(a.Commands > 3000, $"only {a.Commands} commands");
        Assert.True(a.Refused > 0 && a.Refused < a.Commands, $"{a.Refused} of {a.Commands} refused");
        _out.WriteLine($"seed {seed}: {a.Commands} commands, {a.Refused} refused, final hash {a.Hashes[^1]:x16}");
    }

    // ---------------------------------------------------------------- bugs the fuzzer found

    /// <summary>Two players sending their colony pods to the same site in the same tick used to put two Colony Hubs on
    /// one tile (touchdown didn't look); now the second order is refused.</summary>
    [Fact]
    public void TwoColoniesCantLandOnTheSameSite()
    {
        var sim = new Simulation(Data, 1701);
        sim.LoadSurface(MatchSetup.MvpPlanetClass, 1701);
        sim.AddPlayer("federation");
        sim.AddPlayer("cardassian");
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t));
        var next = s.Map.Grid.Neighbours(site).First(t => s.CanLand(1, t));
        sim.Schedule(0, new LandColonyCommand(0, site));
        sim.Schedule(0, new LandColonyCommand(1, site));
        sim.Schedule(1, new LandColonyCommand(1, next)); // right beside it: refused too
        for (var i = 0; i <= TestData.ReadyTicks(Data); i++) sim.Step();
        Assert.Equal(2, sim.RefusedCommands);
        Assert.Single(s.Structures);
        Assert.Equal(0, s.StructureAt(site)!.Owner);
        Assert.Equal(LandingPhase.InOrbit, s.Players[1].Phase);
        Assert.False(s.CanLand(1, next)); // the hub's neighbours stay clear, as for any landing
        Assert.Empty(Problems(sim));
    }

    /// <summary>A building put down on (or beside) the site of a colony pod that is still coming down used to end up
    /// sharing its tile with the Colony Hub at touchdown; the site is now kept clear until the pod is down.</summary>
    [Fact]
    public void NothingCanBePutUnderAFallingPod()
    {
        var sim = new Simulation(Data, 1701);
        sim.LoadSurface(MatchSetup.MvpPlanetClass, 1701);
        sim.AddPlayer("federation");
        sim.AddPlayer("cardassian");
        var s = sim.Surface!;
        var site = Enumerable.Range(0, s.Map.Grid.Count).First(t => s.CanLand(0, t));
        sim.Schedule(0, new LandColonyCommand(1, site));
        sim.Step();
        Assert.Equal(LandingPhase.Descending, s.Players[1].Phase);
        Assert.False(s.CanLand(0, site));
        var beside = s.Map.Grid.Neighbours(site).First(t => s.Map.Terrain(t).Buildable);
        Assert.Equal("a colony is landing there", s.DebugProblem(sim, new DebugPlaceBuildingCommand(0, 0, "colony_hub", site, 0)));
        Assert.Equal("a colony is landing there", s.DebugProblem(sim, new DebugPlaceBuildingCommand(0, 0, "colony_hub", beside, 0)));
        sim.Schedule(sim.Tick, new DebugPlaceBuildingCommand(0, 0, "colony_hub", site, 0));
        sim.Schedule(sim.Tick, new LandColonyCommand(0, site));
        for (var i = 0; i <= TestData.ReadyTicks(Data); i++) sim.Step();
        Assert.Equal(2, sim.RefusedCommands);
        Assert.Single(s.Structures);
        Assert.Equal(1, s.StructureAt(site)!.Owner);
        Assert.Empty(Problems(sim));
    }

    /// <summary>The fuzzer's data edits (test panel DATA tab) set a start stock or the ship's hold to -1 and Validate let
    /// it through, so players started below zero. Negative stocks, holds and costs are now refused data.</summary>
    [Fact]
    public void NegativeStocksHoldsAndCostsAreRefusedData()
    {
        foreach (var label in new[] { "landing › start_stock › duranium", "orbit › hold › deuterium" })
        {
            var n = DataTuning.Numbers(Data).First(x => x.Label == label);
            Assert.NotNull(DataTuning.Rebuild(Data, new[] { new DataEdit(n.File, n.Path, "-1") }, Array.Empty<DataFile>(), out _));
            Assert.Null(DataTuning.Rebuild(Data, new[] { new DataEdit(n.File, n.Path, "0") }, Array.Empty<DataFile>(), out _));
        }
    }

    /// <summary>
    /// A refused command changes nothing: game A gets one random command a tick; game B gets only the ones A accepted.
    /// Once each command has run the two hashes must be equal (a refusal that changed something on its way out would
    /// split them).
    /// </summary>
    [Fact]
    public void RefusedCommandsChangeNothing()
    {
        var a = TwoPlayersLanded(missions: true);
        var b = TwoPlayersLanded(missions: true);
        var fuzz = new Fuzzer(a, 99);
        var refused = 0;
        for (var i = 0; i < 1200; i++)
        {
            var c = fuzz.Next();
            var before = a.RefusedCommands;
            a.Schedule(a.Tick, c);
            a.Step();
            if (a.RefusedCommands == before) b.Schedule(b.Tick, c);
            else refused++;
            b.Step();
            Assert.True(a.StateHash() == b.StateHash(), $"tick {a.Tick}: {c} (refused: {a.RefusedCommands != before}, {a.LastRefusal}) left a trace");
        }
        Assert.InRange(refused, 100, 1100);
        _out.WriteLine($"{refused} of 1200 refused");
    }
}
