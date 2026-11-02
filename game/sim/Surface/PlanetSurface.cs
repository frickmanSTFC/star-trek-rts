using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>Power at one base: what its working buildings supply and use, and how fast power users run.</summary>
public readonly record struct BasePower(Fix64 Supply, Fix64 Use)
{
    /// <summary>1 when supply covers use; otherwise supply / use (every power user at the base runs at this speed).</summary>
    public Fix64 Efficiency => Use <= Supply || Use == Fix64.Zero ? Fix64.One : Supply / Use;
}

/// <summary>
/// Play on one planet's surface: scan from orbit, land the colony pod, drive survey rovers, then place
/// and run buildings. A base is a colony core (the hub and its upgrades) plus the buildings in its radius;
/// each base has its own build queues and power budget. Owned by the Simulation, which routes surface
/// commands here and steps it every tick.
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

    /// <summary>The player's bases (their colony cores), oldest first.</summary>
    public IReadOnlyList<SurfaceStructure> Bases(int player) =>
        _structures.Where(s => s.Owner == player && s.Id == s.BaseId && _data.Building(s.DefId).IsCore).ToList();

    /// <summary>Every structure belonging to a base, the core first.</summary>
    public IReadOnlyList<SurfaceStructure> StructuresOf(int baseId) => _structures.Where(s => s.BaseId == baseId).ToList();

    /// <summary>Power supply and use at a base, from its buildings' state after the last tick.</summary>
    public BasePower PowerOf(int baseId)
    {
        var supply = Fix64.Zero;
        var use = Fix64.Zero;
        foreach (var s in _structures)
        {
            if (s.BaseId != baseId || s.State != StructureState.Complete) continue;
            if (s.Status is StructureStatus.NoUpkeep or StructureStatus.Depleted) continue;
            var def = _data.Building(s.DefId);
            supply += def.PowerSupply;
            use += def.PowerUse;
        }
        return new BasePower(supply, use);
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
        if (BaseFor(player, tile) == 0) return PlacementProblem.OutOfRange;
        var def = _data.Building(buildingId);
        if (def.RequiresDeposit && (Map.DepositResource[tile] < 0 || Map.DepositAmount[tile] <= Fix64.Zero)) return PlacementProblem.NeedsDeposit;
        if (!sim.Players[player].Stock.CanAfford(def.Cost)) return PlacementProblem.CannotAfford;
        return PlacementProblem.None;
    }

    public bool InBuildRange(int player, int tile) => BaseFor(player, tile) != 0;

    /// <summary>
    /// The base a new building on this tile would join: the nearest of the player's finished cores whose
    /// build radius reaches it (ties go to the older base). 0 = out of range of every base.
    /// </summary>
    public int BaseFor(int player, int tile)
    {
        var best = 0;
        var bestDist = int.MaxValue;
        foreach (var s in _structures)
        {
            if (s.Owner != player || s.State != StructureState.Complete || s.Id != s.BaseId) continue;
            var radius = _data.Building(s.DefId).BuildRadius;
            var d = Map.Grid.Distance(s.Tile, tile);
            if (radius > 0 && d <= radius && d < bestDist)
            {
                best = s.Id;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>How many constructions the player can run at once across all bases.</summary>
    public int BuildQueues(int player)
    {
        var n = 0;
        foreach (var b in Bases(player)) n += _data.Building(b.DefId).BuildQueues;
        return n;
    }

    /// <summary>Fraction of construction done, 0..1.</summary>
    public Fix64 BuildFraction(SurfaceStructure s)
    {
        if (s.State == StructureState.Complete) return Fix64.One;
        var total = _data.Building(s.DefId).BuildSeconds * Simulation.TickRate;
        return total <= 0 ? Fix64.One : Fix64.Ratio(s.BuildTicks, total);
    }

    /// <summary>Fraction of the current upgrade done, 0..1 (0 when not upgrading).</summary>
    public Fix64 UpgradeFraction(SurfaceStructure s)
    {
        if (s.UpgradingTo == null) return Fix64.Zero;
        var total = _data.Building(s.UpgradingTo).BuildSeconds * Simulation.TickRate;
        return total <= 0 ? Fix64.One : Fix64.Ratio(s.UpgradeTicks, total);
    }

    /// <summary>Why a core can't start upgrading right now (null = it can).</summary>
    public string? UpgradeProblem(Simulation sim, int player, int structureId)
    {
        var s = FindStructure(structureId);
        if (s == null || s.Owner != player) return "not yours";
        if (s.State != StructureState.Complete) return "still under construction";
        if (s.UpgradingTo != null) return "already upgrading";
        var next = _data.Building(s.DefId).UpgradesTo;
        if (next.Length == 0) return "top level";
        if (!sim.Players[player].Stock.CanAfford(_data.Building(next).Cost)) return "not enough resources";
        return null;
    }

    /// <summary>
    /// What the player's finished buildings make and use per minute right now (upkeep as negatives),
    /// with power users scaled by their base's power efficiency. Mines count extraction while their deposit lasts.
    /// </summary>
    internal void AddIncome(int player, ResourceBag income)
    {
        var efficiency = new Dictionary<int, Fix64>();
        foreach (var s in _structures)
        {
            if (s.Owner != player || s.State != StructureState.Complete) continue;
            if (s.Status is not (StructureStatus.Working or StructureStatus.LowPower)) continue;
            var def = _data.Building(s.DefId);
            if (!efficiency.TryGetValue(s.BaseId, out var eff)) efficiency[s.BaseId] = eff = PowerOf(s.BaseId).Efficiency;
            var speed = def.PowerUse > Fix64.Zero ? eff : Fix64.One;
            foreach (var o in def.OutputPerMinute) income.Add(o.Resource, o.Amount * speed);
            foreach (var u in def.UpkeepPerMinute) income.Add(u.Resource, -u.Amount);
            if (def.RequiresDeposit && Map.DepositResource[s.Tile] >= 0)
                income.Add(Map.DepositResource[s.Tile], def.ExtractPerMinute * speed);
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
                PlaceStructure(place.Player, place.BuildingId, place.Tile, ((place.Facing % 6) + 6) % 6,
                    StructureState.Queued, BaseFor(place.Player, place.Tile));
                return true;

            case UpgradeCommand upgrade:
                if (UpgradeProblem(sim, upgrade.Player, upgrade.StructureId) != null) return true;
                var core = FindStructure(upgrade.StructureId)!;
                var target = _data.Building(core.DefId).UpgradesTo;
                sim.Players[core.Owner].Stock.TrySpend(_data.Building(target).Cost);
                core.UpgradingTo = target;
                core.UpgradeTicks = 0;
                return true;

            case CancelBuildingCommand cancel:
                var s = FindStructure(cancel.StructureId);
                if (s == null || s.Owner != cancel.Player) return true;
                var stock = sim.Players[s.Owner].Stock;
                if (s.State == StructureState.Complete)
                {
                    // A finished building can only have its upgrade cancelled.
                    if (s.UpgradingTo == null) return true;
                    foreach (var c in _data.Building(s.UpgradingTo).Cost) stock.Add(c.Resource, c.Amount);
                    s.UpgradingTo = null;
                    s.UpgradeTicks = 0;
                    return true;
                }
                foreach (var c in _data.Building(s.DefId).Cost) stock.Add(c.Resource, c.Amount);
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
        Upgrades();
        Economy(sim);
        MoveUnits();
    }

    /// <summary>Each base's oldest unfinished buildings get that base's free build queues; the rest wait.</summary>
    private void Construction()
    {
        var finished = new List<SurfaceStructure>();
        foreach (var core in _structures.Where(s => s.Id == s.BaseId).ToList())
        {
            var free = core.State == StructureState.Complete ? _data.Building(core.DefId).BuildQueues : 0;
            foreach (var s in _structures)
            {
                if (s.BaseId != core.Id || s.State == StructureState.Complete) continue;
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
        }
        // Finish after the loop so nothing finished this tick changes this tick's queues.
        foreach (var s in finished)
        {
            s.State = StructureState.Complete;
            s.Status = StructureStatus.Working;
        }
    }

    /// <summary>Upgrading cores keep working at their current level until the upgrade completes.</summary>
    private void Upgrades()
    {
        foreach (var s in _structures)
        {
            if (s.UpgradingTo == null) continue;
            s.UpgradeTicks++;
            if (s.UpgradeTicks < _data.Building(s.UpgradingTo).BuildSeconds * Simulation.TickRate) continue;
            s.DefId = s.UpgradingTo;
            s.UpgradingTo = null;
            s.UpgradeTicks = 0;
        }
    }

    /// <summary>
    /// Finished buildings, in id order: (1) pay upkeep or idle, (2) work out each base's power budget from
    /// the buildings still running, (3) produce, with power users slowed by their base's efficiency;
    /// mines move ore from the deposit under them into the owner's stock until it runs out.
    /// </summary>
    private void Economy(Simulation sim)
    {
        foreach (var s in _structures)
        {
            if (s.State != StructureState.Complete) continue;
            var def = _data.Building(s.DefId);
            var stock = sim.Players[s.Owner].Stock;

            if (def.RequiresDeposit && (Map.DepositResource[s.Tile] < 0 || Map.DepositAmount[s.Tile] <= Fix64.Zero))
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
            s.Status = StructureStatus.Working;
        }

        var efficiency = new Dictionary<int, Fix64>();
        foreach (var s in _structures)
        {
            if (s.State != StructureState.Complete || s.Status != StructureStatus.Working) continue;
            var def = _data.Building(s.DefId);
            if (!efficiency.TryGetValue(s.BaseId, out var eff)) efficiency[s.BaseId] = eff = PowerOf(s.BaseId).Efficiency;
            var speed = def.PowerUse > Fix64.Zero ? eff : Fix64.One;
            if (speed < Fix64.One) s.Status = StructureStatus.LowPower;

            var stock = sim.Players[s.Owner].Stock;
            foreach (var o in def.OutputPerMinute) stock.Add(o.Resource, o.Amount * speed / Simulation.TicksPerMinute);
            if (def.RequiresDeposit)
            {
                var take = Fix64.Min(def.ExtractPerMinute * speed / Simulation.TicksPerMinute, Map.DepositAmount[s.Tile]);
                Map.DepositAmount[s.Tile] -= take;
                stock.Add(Map.DepositResource[s.Tile], take);
            }
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
        PlaceStructure(player, _data.Landing.HubBuilding, p.LandingTile, 0, StructureState.Complete, 0);
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

    /// <summary>baseId 0 = this structure is a new base of its own.</summary>
    private void PlaceStructure(int owner, string defId, int tile, int facing, StructureState state, int baseId)
    {
        var s = new SurfaceStructure(_nextStructureId++, owner, defId, tile, facing, state, baseId);
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
