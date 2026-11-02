using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>
/// Play on one planet's surface: scan from orbit, land the colony pod, drive survey rovers, then place
/// and run buildings. Owned by the Simulation, which routes surface commands here and steps it every tick.
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
        var id = tile >= 0 && tile < _structureAt.Length ? _structureAt[tile] : 0;
        return id == 0 ? null : FindStructure(id);
    }

    public SurfaceStructure? FindStructure(int id)
    {
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

    /// <summary>
    /// Whether the player can place this building on this tile right now, and if not, why.
    /// The UI shows the reason; the sim re-checks when the command arrives.
    /// </summary>
    public PlacementProblem CheckPlacement(Simulation sim, int player, string buildingId, int tile)
    {
        if (!_data.BuildMenu.Contains(buildingId)) return PlacementProblem.NotInBuildMenu;
        if (tile < 0 || tile >= Map.Grid.Count) return PlacementProblem.OffMap;
        if (player < 0 || player >= _players.Count || _players[player].Phase != LandingPhase.Landed) return PlacementProblem.NotLanded;
        if (!_players[player].Surveyed[tile]) return PlacementProblem.NotSurveyed;
        if (!Map.Terrain(tile).Buildable) return PlacementProblem.TerrainUnbuildable;
        if (_structureAt[tile] != 0) return PlacementProblem.Occupied;
        if (!InBuildRange(player, tile)) return PlacementProblem.OutOfRange;
        var def = _data.Building(buildingId);
        if (def.RequiresDeposit && (Map.DepositResource[tile] < 0 || Map.DepositAmount[tile] <= Fix64.Zero)) return PlacementProblem.NeedsDeposit;
        if (!sim.Players[player].Stock.CanAfford(def.Cost)) return PlacementProblem.CannotAfford;
        return PlacementProblem.None;
    }

    /// <summary>Within build_radius of one of the player's finished colony cores.</summary>
    public bool InBuildRange(int player, int tile)
    {
        foreach (var s in _structures)
        {
            if (s.Owner != player || s.State != StructureState.Complete) continue;
            var radius = _data.Building(s.DefId).BuildRadius;
            if (radius > 0 && Map.Grid.Distance(s.Tile, tile) <= radius) return true;
        }
        return false;
    }

    /// <summary>How many constructions the player can run at once (sum of their cores' build_queues).</summary>
    public int BuildQueues(int player)
    {
        var n = 0;
        foreach (var s in _structures)
            if (s.Owner == player && s.State == StructureState.Complete) n += _data.Building(s.DefId).BuildQueues;
        return n;
    }

    /// <summary>Fraction of construction done, 0..1.</summary>
    public Fix64 BuildFraction(SurfaceStructure s)
    {
        if (s.State == StructureState.Complete) return Fix64.One;
        var total = _data.Building(s.DefId).BuildSeconds * Simulation.TickRate;
        return total <= 0 ? Fix64.One : Fix64.Ratio(s.BuildTicks, total);
    }

    /// <summary>
    /// What the player's finished, working buildings make and use per minute (upkeep as negatives).
    /// Mines count their extraction while their deposit lasts.
    /// </summary>
    internal void AddIncome(int player, ResourceBag income)
    {
        foreach (var s in _structures)
        {
            if (s.Owner != player || s.State != StructureState.Complete || s.Status != StructureStatus.Working) continue;
            var def = _data.Building(s.DefId);
            foreach (var o in def.OutputPerMinute) income.Add(o.Resource, o.Amount);
            foreach (var u in def.UpkeepPerMinute) income.Add(u.Resource, -u.Amount);
            if (def.RequiresDeposit && Map.DepositResource[s.Tile] >= 0)
                income.Add(Map.DepositResource[s.Tile], def.ExtractPerMinute);
        }
    }

    // ---------------------------------------------------------------- commands

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

            case PlaceBuildingCommand place:
                if (CheckPlacement(sim, place.Player, place.BuildingId, place.Tile) != PlacementProblem.None) return true;
                sim.Players[place.Player].Stock.TrySpend(_data.Building(place.BuildingId).Cost);
                PlaceStructure(place.Player, place.BuildingId, place.Tile, ((place.Facing % 6) + 6) % 6, StructureState.Queued);
                return true;

            case CancelBuildingCommand cancel:
                var s = FindStructure(cancel.StructureId);
                if (s == null || s.Owner != cancel.Player || s.State == StructureState.Complete) return true;
                foreach (var c in _data.Building(s.DefId).Cost) sim.Players[s.Owner].Stock.Add(c.Resource, c.Amount);
                _structures.Remove(s);
                _structureAt[s.Tile] = 0;
                return true;

            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- ticking

    internal void Step(Simulation sim)
    {
        for (var i = 0; i < _players.Count; i++)
        {
            var p = _players[i];
            if (p.Phase == LandingPhase.Descending && sim.Tick >= p.LandsAtTick) Touchdown(i, p);
        }

        Construction();
        Economy(sim);
        MoveUnits();
    }

    /// <summary>Each player's oldest unfinished buildings get the free build queues; the rest wait.</summary>
    private void Construction()
    {
        for (var player = 0; player < _players.Count; player++)
        {
            var free = BuildQueues(player);
            var finished = new List<SurfaceStructure>();
            foreach (var s in _structures)
            {
                if (s.Owner != player || s.State == StructureState.Complete) continue;
                if (free == 0)
                {
                    s.State = StructureState.Queued;
                    continue;
                }
                free--;
                s.State = StructureState.Building;
                s.BuildTicks++;
                if (s.BuildTicks >= _data.Building(s.DefId).BuildSeconds * Simulation.TickRate) finished.Add(s);
            }
            // Finish after the loop so a newly finished core doesn't add a queue mid-tick.
            foreach (var s in finished)
            {
                s.State = StructureState.Complete;
                s.Status = StructureStatus.Working;
            }
        }
    }

    /// <summary>
    /// Finished buildings, in id order: pay upkeep (or idle), then produce; mines move ore from the
    /// deposit under them into the owner's stock until it runs out.
    /// </summary>
    private void Economy(Simulation sim)
    {
        foreach (var s in _structures)
        {
            if (s.State != StructureState.Complete) continue;
            var def = _data.Building(s.DefId);
            var stock = sim.Players[s.Owner].Stock;

            var depositResource = Map.DepositResource[s.Tile];
            if (def.RequiresDeposit && (depositResource < 0 || Map.DepositAmount[s.Tile] <= Fix64.Zero))
            {
                s.Status = StructureStatus.Depleted;
                continue;
            }

            var affordable = true;
            foreach (var u in def.UpkeepPerMinute)
                if (stock[u.Resource] < u.Amount / Simulation.TicksPerMinute) affordable = false;
            if (!affordable)
            {
                s.Status = StructureStatus.NoUpkeep;
                continue;
            }
            foreach (var u in def.UpkeepPerMinute) stock.Add(u.Resource, -(u.Amount / Simulation.TicksPerMinute));

            foreach (var o in def.OutputPerMinute) stock.Add(o.Resource, o.Amount / Simulation.TicksPerMinute);
            if (def.RequiresDeposit)
            {
                var take = Fix64.Min(def.ExtractPerMinute / Simulation.TicksPerMinute, Map.DepositAmount[s.Tile]);
                Map.DepositAmount[s.Tile] -= take;
                stock.Add(depositResource, take);
            }
            s.Status = StructureStatus.Working;
        }
    }

    private void MoveUnits()
    {
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
        PlaceStructure(player, _data.Landing.HubBuilding, p.LandingTile, 0, StructureState.Complete);
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

    private void PlaceStructure(int owner, string defId, int tile, int facing, StructureState state)
    {
        var s = new SurfaceStructure(_nextStructureId++, owner, defId, tile, facing, state);
        _structures.Add(s);
        _structureAt[tile] = s.Id;
    }

    private void Reveal(int player, int centre, int radius)
    {
        var surveyed = _players[player].Surveyed;
        foreach (var t in Map.Grid.Within(centre, radius)) surveyed[t] = true;
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
