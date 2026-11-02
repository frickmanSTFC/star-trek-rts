using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

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

    public GameData Data { get; }
    public DeterministicRng Rng { get; }
    public long Tick { get; private set; }

    /// <summary>The map this match is played on, or null before LoadMap.</summary>
    public MapDef? Map { get; private set; }

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

    /// <summary>Adds the player for the next slot with the map's starting stock.</summary>
    public Player AddPlayer(string factionId)
    {
        var stock = new ResourceBag(Data.Resources.Count, Map?.StartStock ?? Array.Empty<ResourceAmount>());
        var player = new Player(_players.Count, factionId, stock);
        _players.Add(player);
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

    /// <summary>What a player's buildings produce per minute (for the HUD; the tick adds 1/900th of this).</summary>
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
        foreach (var ship in _ships) Movement(ship);
        Tick++;
    }

    private void Apply(Command command)
    {
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
            default:
                throw new NotSupportedException($"Unhandled command {command.GetType().Name}.");
        }
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
        return h.Value;
    }
}
