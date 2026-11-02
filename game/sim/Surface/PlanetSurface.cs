using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>
/// Play on one planet's surface: scan from orbit, land the colony pod, drive survey rovers.
/// Owned by the Simulation, which routes surface commands here and steps it every tick.
/// </summary>
public sealed class PlanetSurface
{
    public SurfaceMap Map { get; }
    public IReadOnlyList<SurfacePlayer> Players => _players;
    public IReadOnlyList<SurfaceUnit> Units => _units;
    public IReadOnlyList<SurfaceStructure> Structures => _structures;

    private readonly GameData _data;
    private readonly List<SurfacePlayer> _players = new();
    private readonly List<SurfaceUnit> _units = new();
    private readonly List<SurfaceStructure> _structures = new();
    private readonly int[] _structureAt; // structure id per tile, 0 = none
    private int _nextUnitId = 1;
    private int _nextStructureId = 1;

    internal PlanetSurface(GameData data, SurfaceMap map)
    {
        _data = data;
        Map = map;
        _structureAt = new int[map.Grid.Count];
    }

    internal void AddPlayer() => _players.Add(new SurfacePlayer(Map.Grid.Count));

    // ---------------------------------------------------------------- queries

    public SurfaceStructure? StructureAt(int tile)
    {
        var id = _structureAt[tile];
        if (id == 0) return null;
        foreach (var s in _structures)
            if (s.Id == id) return s;
        return null;
    }

    public SurfaceUnit? FindUnit(int id)
    {
        foreach (var u in _units)
            if (u.Id == id) return u;
        return null;
    }

    /// <summary>Whether the player could send their pod to this tile right now.</summary>
    public bool CanLand(int player, int tile)
    {
        if (player < 0 || player >= _players.Count) return false;
        if (_players[player].Phase != LandingPhase.InOrbit) return false;
        if (tile < 0 || tile >= Map.Grid.Count || !Map.IsLandingSite(tile)) return false;
        foreach (var t in Map.Grid.Within(tile, 1))
            if (_structureAt[t] != 0) return false;
        return true;
    }

    // ---------------------------------------------------------------- commands and ticking

    /// <summary>Applies a surface command; returns false if the command isn't a surface command.</summary>
    internal bool Apply(Simulation sim, Command command)
    {
        switch (command)
        {
            case LandColonyCommand land:
                if (!CanLand(land.Player, land.Tile)) return true;
                var p = _players[land.Player];
                p.Phase = LandingPhase.Descending;
                p.LandingTile = land.Tile;
                p.DescentStartTick = sim.Tick;
                p.LandsAtTick = sim.Tick + (long)_data.Landing.DescentSeconds * Simulation.TickRate;
                return true;

            case MoveUnitsCommand move:
                if (move.Tile < 0 || move.Tile >= Map.Grid.Count) return true;
                foreach (var id in move.UnitIds)
                {
                    var unit = FindUnit(id);
                    if (unit == null || unit.Owner != move.Player) continue;
                    var path = Pathfinder.Find(Map, unit.Tile, move.Tile);
                    if (path == null) continue;
                    unit._path.Clear();
                    unit._path.AddRange(path);
                    unit.Progress = Fix64.Zero;
                }
                return true;

            default:
                return false;
        }
    }

    internal void Step(Simulation sim)
    {
        for (var i = 0; i < _players.Count; i++)
        {
            var p = _players[i];
            if (p.Phase == LandingPhase.Descending && sim.Tick >= p.LandsAtTick) Touchdown(i, p);
        }

        foreach (var unit in _units)
        {
            if (unit._path.Count == 0) continue;
            unit.Progress += unit.SpeedPerTick;
            var next = unit._path[0];
            var cost = Fix64.FromInt(Map.Terrain(next).MoveCost);
            if (unit.Progress < cost) continue;
            unit.Progress -= cost;
            unit.Tile = next;
            unit._path.RemoveAt(0);
            if (unit._path.Count == 0) unit.Progress = Fix64.Zero;
            Reveal(unit.Owner, unit.Tile, unit.SurveyRadius);
        }
    }

    private void Touchdown(int player, SurfacePlayer p)
    {
        p.Phase = LandingPhase.Landed;
        PlaceStructure(player, _data.Landing.HubBuilding, p.LandingTile);
        Reveal(player, p.LandingTile, _data.Landing.RevealRadius);

        // Rovers roll out onto the first free passable neighbours, in neighbour order.
        var spots = new List<int>();
        foreach (var n in Map.Grid.Neighbours(p.LandingTile))
            if (Map.Terrain(n).Passable && _structureAt[n] == 0) spots.Add(n);
        for (var i = 0; i < _data.Landing.Units.Count && spots.Count > 0; i++)
        {
            var def = _data.SurfaceUnit(_data.Landing.Units[i]);
            var tile = spots[i % spots.Count];
            _units.Add(new SurfaceUnit(_nextUnitId++, player, def.Id, def.Speed * Simulation.SecondsPerTick, def.SurveyRadius, tile));
        }
    }

    private void PlaceStructure(int owner, string defId, int tile)
    {
        var s = new SurfaceStructure(_nextStructureId++, owner, defId, tile);
        _structures.Add(s);
        _structureAt[tile] = s.Id;
    }

    private void Reveal(int player, int centre, int radius)
    {
        var surveyed = _players[player].Surveyed;
        foreach (var t in Map.Grid.Within(centre, radius)) surveyed[t] = true;
    }

    internal void AddOutputs(int? player, System.Action<int, ResourceAmount> add)
    {
        foreach (var s in _structures)
        {
            if (player.HasValue && s.Owner != player.Value) continue;
            foreach (var o in _data.Building(s.DefId).OutputPerMinute) add(s.Owner, o);
        }
    }

    internal void AddToHash(ref StateHasher h)
    {
        Map.AddToHash(ref h);
        h.Add(_players.Count);
        foreach (var p in _players) p.AddToHash(ref h);
        h.Add(_units.Count);
        foreach (var u in _units) u.AddToHash(ref h);
        h.Add(_structures.Count);
        foreach (var s in _structures) s.AddToHash(ref h);
    }
}
