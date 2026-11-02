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

    public static readonly Fix64 SecondsPerTick = Fix64.Ratio(1, TickRate);

    public GameData Data { get; }
    public DeterministicRng Rng { get; }
    public long Tick { get; private set; }
    public IReadOnlyList<Ship> Ships => _ships;

    private readonly List<Ship> _ships = new();
    private readonly SortedDictionary<long, List<Command>> _scheduled = new();
    private int _nextShipId = 1;

    public Simulation(GameData data, ulong seed)
    {
        Data = data;
        Rng = new DeterministicRng(seed);
    }

    public Ship SpawnShip(int owner, string classKey, FixVec2 position, FixVec2 heading)
    {
        var cls = Data.ShipClass(classKey);
        var hull = Data.Hull(cls.HullId);
        var ship = new Ship(_nextShipId++, owner, classKey, hull.Size, hull.Hp, hull.Speed * SecondsPerTick, position, heading.Normalized());
        _ships.Add(ship);
        return ship;
    }

    public Ship? FindShip(int id)
    {
        foreach (var s in _ships)
            if (s.Id == id) return s;
        return null;
    }

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
        h.Add(_ships.Count);
        foreach (var s in _ships) s.AddToHash(ref h);
        return h.Value;
    }
}
