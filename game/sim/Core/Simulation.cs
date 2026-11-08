using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;

namespace FinalFrontier.Sim.Core;

/// <summary>
/// The whole game state, advanced in fixed ticks. Rules for code in here:
/// no float/double, no System.Random, no clocks, no Dictionary/HashSet iteration
/// (keep ordered lists), no engine types. Same inputs => same state on every machine.
/// </summary>
public sealed class Simulation
{
    /// <summary>Simulation ticks per second (GDD §11: 10–20 Hz).</summary>
    public const int TickRate = 15;

    public const int TicksPerMinute = TickRate * 60;

    public static readonly Fix64 SecondsPerTick = Fix64.Ratio(1, TickRate);

    /// <summary>The game data in use. Only the test panel's data tuning (DebugDataCommand) ever swaps it.</summary>
    public GameData Data { get; private set; }

    /// <summary>
    /// How many test-panel edits (DebugCommand) this game has had; 0 = untouched. In the state hash, so a tested
    /// replay or save never passes for a clean one.
    /// </summary>
    public int DebugEdits { get; private set; }

    /// <summary>A hash of each data change made by the test panel, in order (in the state hash).</summary>
    private readonly List<ulong> _dataChanges = new();

    /// <summary>
    /// How many commands were refused. A command that is malformed, names a player, id, tile or value that doesn't exist
    /// or doesn't fit, or isn't allowed right now is refused: it changes nothing and never throws (multiplayer safety: a
    /// peer can send anything). Commands that act on a list of vehicles skip the ones that can't and count as refused only
    /// when none can. Debugging info, deliberately not in the state hash, so a refused command leaves the hash exactly as
    /// it was (while it waits in the queue it is hashed like any other).
    /// </summary>
    public int RefusedCommands { get; private set; }

    /// <summary>Why the last command was refused ("tick 120: PlaceBuildingCommand from player 0: ..."), for debugging
    /// (not in the state hash, but deterministic like the rest).</summary>
    public string? LastRefusal { get; private set; }
    public DeterministicRng Rng { get; }
    public long Tick { get; private set; }

    /// <summary>The map this match is played on, or null before LoadMap.</summary>
    public MapDef? Map { get; private set; }

    /// <summary>The game's star system (generated or from a map), or null for planet-only games (balance runner, tests).
    /// CONTRACT stub (docs/system-contract.md): the sim agent sets it in MatchSetup.CreateSystemGame.</summary>
    public FinalFrontier.Sim.Space.StarSystemState? System { get; internal set; }

    /// <summary>The planet surface being played on, or null in a star-system match.</summary>
    public PlanetSurface? Surface { get; private set; }

    public IReadOnlyList<Player> Players => _players;
    public IReadOnlyList<Body> Bodies => _bodies;
    public IReadOnlyList<Colony> Colonies => _colonies;
    public IReadOnlyList<Ship> Ships => _ships;

    private readonly List<Player> _players = new();
    private readonly List<Body> _bodies = new();
    private readonly List<Colony> _colonies = new();
    private readonly List<Ship> _ships = new();
    private readonly SortedDictionary<long, List<Command>> _scheduled = new();
    private int _nextShipId = 1;
    private int _nextColonyId = 1;
    private int _nextBuildingId = 1;

    public Simulation(GameData data, ulong seed)
    {
        Data = data;
        Rng = new DeterministicRng(seed);
    }

    // ---------------------------------------------------------------- setup

    /// <summary>Creates the map's bodies. Call once, before adding players.</summary>
    public void LoadMap(MapDef map)
    {
        if (Map != null) throw new InvalidOperationException("A map is already loaded.");
        Map = map;
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < map.Bodies.Count; i++) indexById[map.Bodies[i].Id] = i;
        for (var i = 0; i < map.Bodies.Count; i++)
        {
            var b = map.Bodies[i];
            var parent = b.Parent != null && indexById.TryGetValue(b.Parent, out var p) ? p : -1;
            _bodies.Add(new Body(i, b.Id, b.Name, b.ClassId, b.Size, b.Position, parent,
                new ResourceBag(Data.Resources.Count, b.Deposits)));
        }
    }

    /// <summary>Generates the surface of a planet class from the seed. Call once, before adding players.</summary>
    /// <param name="climateBody">The map body it is, for its climate (climate.json bodies); null = the class's default
    /// body (<see cref="GameData.DefaultClimateBody"/>: the home body of the lowest player slot, Tarsis on mvp_duel).</param>
    /// <param name="system">When climateBody is a generated star system's body: that system's climate entries.</param>
    public void LoadSurface(string planetClassId, ulong seed, string? climateBody = null, SystemClimate? system = null)
    {
        if (Surface != null) throw new InvalidOperationException("A surface is already loaded.");
        if (_players.Count > 0) throw new InvalidOperationException("Load the surface before adding players.");
        Surface = new PlanetSurface(Data, SurfaceMap.Generate(Data, Data.Surface(planetClassId), seed, climateBody, system));
    }

    /// <summary>Makes this game's star system (MatchSetup.CreateSystemGame). Call once, before adding players.</summary>
    public void LoadSystem(FinalFrontier.Sim.Space.StarSystemState system)
    {
        if (System != null) throw new InvalidOperationException("A star system is already loaded.");
        if (_players.Count > 0) throw new InvalidOperationException("Load the star system before adding players.");
        System = system;
        system.Attach(this);
    }

    /// <summary>
    /// Adds the player for the next slot. Starting stock comes from the map (star system) or,
    /// on a planet surface, from what the colony pod brings (surfaces.json landing.start_stock).
    /// </summary>
    public Player AddPlayer(string factionId)
    {
        var startStock = Map?.StartStock ?? (Surface != null ? Data.Landing.StartStock : Array.Empty<ResourceAmount>());
        var player = new Player(_players.Count, factionId, new ResourceBag(Data.Resources.Count, startStock));
        _players.Add(player);
        Surface?.AddPlayer(factionId);
        System?.AddPlayer(this, player.Slot, factionId);
        return player;
    }

    /// <summary>Founds a colony with the given starting buildings (setup only; in play, colonies come from commands).</summary>
    public Colony FoundColony(int owner, int bodyIndex, params string[] buildingIds)
    {
        var body = _bodies[bodyIndex];
        if (body.ColonyId != 0) throw new InvalidOperationException($"{body.Name} already has a colony.");
        var colony = new Colony(_nextColonyId++, owner, bodyIndex);
        foreach (var id in buildingIds)
        {
            Data.Building(id); // throws on unknown ids
            colony.AddBuilding(new Building(_nextBuildingId++, id));
        }
        body.ColonyId = colony.Id;
        _colonies.Add(colony);
        return colony;
    }

    public Ship SpawnShip(int owner, string classKey, FixVec2 position, FixVec2 heading)
    {
        var cls = Data.ShipClass(classKey);
        var hull = Data.Hull(cls.HullId);
        var ship = new Ship(_nextShipId++, owner, classKey, hull.Size, hull.Hp, hull.Speed * SecondsPerTick, position, heading.Normalized());
        _ships.Add(ship);
        return ship;
    }

    // ---------------------------------------------------------------- queries

    public Ship? FindShip(int id)
    {
        foreach (var s in _ships)
            if (s.Id == id) return s;
        return null;
    }

    public Body? FindBody(string id)
    {
        foreach (var b in _bodies)
            if (b.Id == id) return b;
        return null;
    }

    public Colony? FindColony(int id)
    {
        foreach (var c in _colonies)
            if (c.Id == id) return c;
        return null;
    }

    /// <summary>
    /// What a player's buildings make per minute, net of upkeep (for the HUD; each tick moves 1/900th of this).
    /// Negative where upkeep outweighs output.
    /// </summary>
    public ResourceBag IncomePerMinute(int player)
    {
        var income = new ResourceBag(Data.Resources.Count);
        foreach (var colony in _colonies)
        {
            if (colony.Owner != player) continue;
            foreach (var b in colony.Buildings)
                foreach (var o in Data.Building(b.DefId).OutputPerMinute)
                    income.Add(o.Resource, o.Amount);
        }
        Surface?.AddIncome(this, player, income);
        return income;
    }

    // ---------------------------------------------------------------- ticking

    /// <summary>
    /// Queue a command to run at the start of <paramref name="tick"/>. In lockstep, local input is
    /// scheduled a few ticks ahead (input delay) so it can reach every peer in time.
    /// Commands for the same tick run in player order, then in the order they were scheduled.
    /// </summary>
    public void Schedule(long tick, Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (tick < Tick) throw new ArgumentOutOfRangeException(nameof(tick), $"Tick {tick} already ran (now {Tick}).");
        if (!_scheduled.TryGetValue(tick, out var list)) _scheduled[tick] = list = new List<Command>();
        list.Add(command);
    }

    /// <summary>Advance one tick.</summary>
    public void Step()
    {
        if (_scheduled.Remove(Tick, out var commands))
        {
            // OrderBy is a stable sort, so same-player commands keep their scheduling order.
            foreach (var c in commands.OrderBy(c => c.Player)) Apply(c);
        }

        Economy();
        System?.Step(this);
        Surface?.Step(this);
        foreach (var ship in _ships) Movement(ship);
        Tick++;
    }

    private void Apply(Command command)
    {
        if (command.Player < 0 || command.Player >= _players.Count)
        {
            Refuse(command, "no such player");
            return;
        }
        if (CommandChecks.Malformed(command) is { } malformed)
        {
            Refuse(command, malformed);
            return;
        }
        if (command is DebugCommand debug)
        {
            ApplyDebug(debug);
            return;
        }
        if (command is SetCourseCommand or LongRangeScanCommand or SystemSatelliteCommand)
        {
            if (System == null) Refuse(command, "no star system");
            else System.Apply(this, command);
            return;
        }
        if (Surface != null && Surface.Apply(this, command)) return;
        switch (command)
        {
            case MoveCommand move:
                foreach (var id in move.ShipIds)
                {
                    var ship = FindShip(id);
                    if (ship == null || ship.Owner != move.Player) continue;
                    ship.MoveTarget = move.Target;
                    ship.HasMoveTarget = true;
                }
                break;
            case LandColonyCommand:
            case MoveUnitsCommand:
            case PlaceBuildingCommand:
            case CancelBuildingCommand:
            case UpgradeCommand:
            case MineCommand:
            case PackUpCommand:
            case SalvageCommand:
            case RepairCommand:
            case ChangeTyreCommand:
            case BuildUnitCommand:
            case CancelUnitCommand:
            case ResearchCommand:
            case MaintainCommand:
            case PauseCommand:
            case OutpostDropCommand:
            case ShuttleCommand:
            case BeamCommand:
            case SatelliteCommand:
            case FoundOutpostCommand:
                Refuse(command, "no planet surface"); // surface commands with no surface loaded
                break;
            default:
                Refuse(command, "unknown command");
                break;
        }
    }

    /// <summary>Counts a refused command and notes why (see <see cref="RefusedCommands"/>). Returns true, so a surface
    /// handler can `return sim.Refuse(...)`.</summary>
    internal bool Refuse(Command command, string reason)
    {
        RefusedCommands++;
        LastRefusal = $"tick {Tick}: {command.GetType().Name} from player {command.Player}: {reason}";
        return true;
    }

    // ---------------------------------------------------------------- test panel

    /// <summary>Most of any one resource (or deposit) the test panel can set.</summary>
    public static readonly Fix64 DebugMaxAmount = Fix64.FromInt(1_000_000_000);

    /// <summary>Why a test-panel command would be refused right now (null = it would run). The sim checks again when it runs.</summary>
    public string? DebugProblem(DebugCommand command)
    {
        if (CommandChecks.Malformed(command) is { } malformed) return malformed;
        switch (command)
        {
            case DebugStockCommand stock:
                if (stock.Target < 0 || stock.Target >= _players.Count) return "no such player";
                if (stock.Values.Count == 0) return "nothing to set";
                foreach (var v in stock.Values)
                {
                    if (v.Resource < 0 || v.Resource >= Data.Resources.Count) return "unknown resource";
                    if (v.Amount < Fix64.Zero || v.Amount > DebugMaxAmount) return "amounts are 0 to 1 000 000 000";
                }
                return null;
            case DebugAddPlayerCommand add:
                if (_players.Count >= 8) return "8 players already";
                return Data.Factions.Any(f => f.Id == add.FactionId) ? null : "unknown faction";
            case DebugDataCommand data:
                return DataTuning.Rebuild(Data, data.Edits, Array.Empty<DataFile>(), out _);
            case DebugReloadDataCommand reload:
                return DataTuning.Rebuild(Data, Array.Empty<DataEdit>(), reload.Files, out _);
            default:
                return Surface == null ? "no planet surface" : Surface.DebugProblem(this, command);
        }
    }

    /// <summary>Runs a test-panel command if it is valid (otherwise nothing changes) and counts it.</summary>
    private void ApplyDebug(DebugCommand command)
    {
        if (command is DebugDataCommand or DebugReloadDataCommand)
        {
            var edits = command is DebugDataCommand d ? d.Edits : Array.Empty<DataEdit>();
            var files = command is DebugReloadDataCommand r ? r.Files : Array.Empty<DataFile>();
            if (DataTuning.Rebuild(Data, edits, files, out var next) is { } refused)
            {
                Refuse(command, refused);
                return;
            }
            var h = StateHasher.Create();
            foreach (var e in edits)
            {
                h.Add(e.File);
                h.Add(e.Path);
                h.Add(e.Value);
            }
            foreach (var f in files)
            {
                h.Add(f.File);
                h.Add(f.Text);
            }
            _dataChanges.Add(h.Value);
            Data = next!;
            Surface?.ReplaceData(next!);
            DebugEdits++;
            return;
        }
        if (DebugProblem(command) is { } problem)
        {
            Refuse(command, problem);
            return;
        }
        switch (command)
        {
            case DebugStockCommand stock:
                var bag = _players[stock.Target].Stock;
                foreach (var v in stock.Values) bag.Add(v.Resource, v.Amount - bag[v.Resource]);
                break;
            case DebugAddPlayerCommand add:
                AddPlayer(add.FactionId);
                break;
            default:
                Surface!.ApplyDebug(this, command);
                break;
        }
        DebugEdits++;
    }

    /// <summary>Buildings add their per-minute output in equal slices every tick.</summary>
    private void Economy()
    {
        foreach (var colony in _colonies)
        {
            var stock = _players[colony.Owner].Stock;
            foreach (var b in colony.Buildings)
                foreach (var o in Data.Building(b.DefId).OutputPerMinute)
                    stock.Add(o.Resource, o.Amount / TicksPerMinute);
        }
    }

    private static void Movement(Ship ship)
    {
        if (!ship.HasMoveTarget) return;
        var toTarget = ship.MoveTarget - ship.Position;
        var distance = toTarget.Length();
        if (distance <= ship.SpeedPerTick)
        {
            ship.Position = ship.MoveTarget;
            ship.HasMoveTarget = false;
            return;
        }
        var dir = toTarget * (Fix64.One / distance);
        ship.Heading = dir;
        ship.Position += dir * ship.SpeedPerTick;
    }

    /// <summary>Hash of the full game state. Peers exchange this to detect desyncs.</summary>
    public ulong StateHash()
    {
        var h = StateHasher.Create();
        h.Add(Tick);
        Rng.AddToHash(ref h);
        h.Add(_players.Count);
        foreach (var p in _players) p.AddToHash(ref h);
        h.Add(_bodies.Count);
        foreach (var b in _bodies) b.AddToHash(ref h);
        h.Add(_colonies.Count);
        foreach (var c in _colonies) c.AddToHash(ref h);
        h.Add(_ships.Count);
        foreach (var s in _ships) s.AddToHash(ref h);
        h.Add(Surface != null);
        Surface?.AddToHash(ref h);
        // The star system only where there is one, so planet-only games keep the hashes they always had.
        System?.AddToHash(ref h);
        h.Add(DebugEdits);
        h.Add(_dataChanges.Count);
        foreach (var c in _dataChanges) h.Add(c);
        // Ids handed out next: two games that agree on everything else would still split on the next ship or colony.
        h.Add(_nextShipId);
        h.Add(_nextColonyId);
        h.Add(_nextBuildingId);
        // Commands scheduled for later ticks (a peer that received a command the other didn't shows at once). The
        // SortedDictionary iterates by tick; within a tick, in scheduling order (Step runs them by player, keeping it).
        h.Add(_scheduled.Count);
        foreach (var (tick, commands) in _scheduled)
        {
            h.Add(tick);
            h.Add(commands.Count);
            foreach (var c in commands) CommandHash.Add(ref h, c);
        }
        return h.Value;
    }
}
