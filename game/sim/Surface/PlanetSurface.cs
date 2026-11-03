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
/// each base has its own build queues and power budget. Stockpiled materials share one storage capacity per
/// player; mines and mining crawlers stop when it is full. Crawlers mine anywhere and send their loads home
/// as cargo pods. Owned by the Simulation, which routes surface commands here and steps it every tick.
/// </summary>
public sealed class PlanetSurface
{
    public SurfaceMap Map { get; }
    public IReadOnlyList<SurfacePlayer> Players => _players;
    public IReadOnlyList<SurfaceUnit> Units => _units;
    public IReadOnlyList<SurfaceStructure> Structures => _structures;

    /// <summary>Cargo pods in flight, oldest first.</summary>
    public IReadOnlyList<CargoPod> Pods => _pods;

    private readonly GameData _data;
    private readonly List<SurfacePlayer> _players = new();
    private readonly List<SurfaceUnit> _units = new();
    private readonly List<SurfaceStructure> _structures = new();
    private readonly List<CargoPod> _pods = new();
    private readonly int[] _structureAt; // structure id per tile, 0 = none
    private int _nextUnitId = 1;
    private int _nextStructureId = 1;
    private int _nextPodId = 1;

    internal PlanetSurface(GameData data, SurfaceMap map)
    {
        _data = data;
        Map = map;
        _structureAt = new int[map.Grid.Count];
    }

    internal void AddPlayer() => _players.Add(new SurfacePlayer(Map.Grid.Count, _data.Techs.Count));

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
            if (!DrawsPower(s.Status)) continue;
            var def = _data.Building(s.DefId);
            supply += def.PowerSupply * ConditionFactor(s); // worn reactors supply less
            use += def.PowerUse;
        }
        return new BasePower(supply, use);
    }

    /// <summary>Idle buildings (no upkeep, used-up deposit, full storage, no input) neither supply nor use power.</summary>
    private static bool DrawsPower(StructureStatus status) => status is StructureStatus.Working or StructureStatus.LowPower;

    // ---------------------------------------------------------------- storage

    /// <summary>How much the player can stockpile: the storage of their finished buildings.</summary>
    public Fix64 StorageCapacity(int player)
    {
        var cap = Fix64.Zero;
        foreach (var s in _structures)
            if (s.Owner == player && s.State == StructureState.Complete) cap += _data.Building(s.DefId).Storage;
        return cap;
    }

    /// <summary>How much of the player's stock takes storage space.</summary>
    public Fix64 Stored(Simulation sim, int player)
    {
        var stock = sim.Players[player].Stock;
        var total = Fix64.Zero;
        for (var r = 0; r < stock.Count; r++)
            if (_data.IsStored(r)) total += stock[r];
        return total;
    }

    /// <summary>Ore on its way home in cargo pods (space is kept for it).</summary>
    public Fix64 InFlight(int player)
    {
        var total = Fix64.Zero;
        foreach (var p in _pods)
            if (p.Owner == player && _data.IsStored(p.Resource)) total += p.Amount;
        return total;
    }

    /// <summary>Room left for new materials (never negative).</summary>
    public Fix64 FreeStorage(Simulation sim, int player) =>
        Fix64.Max(Fix64.Zero, StorageCapacity(player) - Stored(sim, player) - InFlight(player));

    /// <summary>Adds what fits (resources that take no space always fit); returns how much went in.</summary>
    private Fix64 Store(Simulation sim, int player, int resource, Fix64 amount)
    {
        if (_data.IsStored(resource)) amount = Fix64.Min(amount, FreeStorage(sim, player));
        if (amount <= Fix64.Zero) return Fix64.Zero;
        sim.Players[player].Stock.Add(resource, amount);
        return amount;
    }

    /// <summary>The player's nearest finished building with storage to a tile (where a crawler's pods go), or null.</summary>
    public SurfaceStructure? NearestDepot(int player, int tile)
    {
        SurfaceStructure? best = null;
        var bestDist = int.MaxValue;
        foreach (var s in _structures)
        {
            if (s.Owner != player || s.State != StructureState.Complete || _data.Building(s.DefId).Storage <= Fix64.Zero) continue;
            var d = Map.Grid.Distance(s.Tile, tile);
            if (d < bestDist)
            {
                best = s;
                bestDist = d;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- mining crawlers

    /// <summary>Why this unit can't be sent to mine this tile (null = it can).</summary>
    public string? MineProblem(int player, int unitId, int tile)
    {
        var unit = FindUnit(unitId);
        if (unit == null || unit.Owner != player) return "not yours";
        if (!_data.SurfaceUnit(unit.DefId).CanMine) return "can't mine";
        if (unit.Broken) return "broken down: repair it at the Colony Hub first";
        if (tile < 0 || tile >= Map.Grid.Count) return "off the map";
        if (!_players[player].Surveyed[tile]) return "not surveyed yet";
        return DeployProblem(unit, tile);
    }

    private string? DeployProblem(SurfaceUnit unit, int tile)
    {
        if (Map.DepositResource[tile] < 0) return "no deposit there";
        if (Map.DepositAmount[tile] <= Fix64.Zero) return "that deposit is used up";
        if (_structureAt[tile] != 0) return "a building is on it";
        foreach (var u in _units)
            if (u != unit && u.Tile == tile && u.Mode != UnitMode.Mobile) return "another crawler is mining it";
        return null;
    }

    private int DeployTicks(SurfaceUnit unit) => _data.SurfaceUnit(unit.DefId).DeploySeconds * Simulation.TickRate;

    /// <summary>Fold up a deployed or unfurling crawler (packing takes as long as it had unfurled).</summary>
    private void StartPacking(SurfaceUnit unit)
    {
        if (unit.Mode == UnitMode.Deploying)
        {
            unit.ModeTicks = DeployTicks(unit) - unit.ModeTicks;
            unit.Mode = UnitMode.Packing;
        }
        else if (unit.Mode == UnitMode.Deployed)
        {
            unit.ModeTicks = 0;
            unit.Mode = UnitMode.Packing;
        }
    }

    private void StartDeploying(SurfaceUnit unit)
    {
        if (unit.Mode == UnitMode.Packing)
        {
            unit.ModeTicks = DeployTicks(unit) - unit.ModeTicks;
            unit.Mode = UnitMode.Deploying;
        }
        else if (unit.Mode == UnitMode.Mobile)
        {
            unit.ModeTicks = 0;
            unit.Mode = UnitMode.Deploying;
        }
        unit.MiningStatus = MiningStatus.None;
    }

    /// <summary>Fraction of the way unfurled, 0 (folded) to 1 (deployed), for drawing.</summary>
    public Fix64 DeployFraction(SurfaceUnit unit)
    {
        var total = DeployTicks(unit);
        return unit.Mode switch
        {
            UnitMode.Deployed => Fix64.One,
            UnitMode.Deploying when total > 0 => Fix64.Ratio(unit.ModeTicks, total),
            UnitMode.Packing when total > 0 => Fix64.One - Fix64.Ratio(unit.ModeTicks, total),
            _ => Fix64.Zero,
        };
    }

    // ---------------------------------------------------------------- breakdowns and repair

    private static readonly Fix64 Hundred = Fix64.FromInt(100);

    /// <summary>How fast a unit drives now: full speed, or slower while broken (worse damage, slower).</summary>
    public Fix64 SpeedPerTick(SurfaceUnit unit)
    {
        var rules = _data.SurfaceUnit(unit.DefId).Breakdown;
        if (!unit.Broken || rules == null) return unit.SpeedPerTick;
        var share = rules.SlowestDrive + (Fix64.One - rules.SlowestDrive) * unit.Integrity / Hundred;
        return unit.SpeedPerTick * share;
    }

    /// <summary>The player's finished colony core on this tile (where the repair pad is), or null.</summary>
    private SurfaceStructure? RepairPadAt(int player, int tile)
    {
        var s = StructureAt(tile);
        return s != null && s.Owner == player && s.State == StructureState.Complete && _data.Building(s.DefId).IsCore ? s : null;
    }

    /// <summary>Parked on a repair pad and being worked on (crew walking out, repairing, or waiting for resources).</summary>
    public bool IsRepairing(SurfaceUnit unit) =>
        unit.Broken && unit.Mode == UnitMode.Mobile && unit._path.Count == 0 && RepairPadAt(unit.Owner, unit.Tile) != null;

    /// <summary>How far the repair crew has walked out, 0..1 (1 = at work).</summary>
    public Fix64 CrewOut(SurfaceUnit unit)
    {
        var total = _data.SurfaceUnit(unit.DefId).Repair?.CrewSeconds * Simulation.TickRate ?? 0;
        return total <= 0 ? Fix64.One : Fix64.Min(Fix64.One, Fix64.Ratio(unit.RepairTicks, total));
    }

    /// <summary>What it would cost to repair the unit back to 100 from here.</summary>
    public IReadOnlyList<ResourceAmount> RepairCost(SurfaceUnit unit)
    {
        var repair = _data.SurfaceUnit(unit.DefId).Repair;
        if (repair == null) return System.Array.Empty<ResourceAmount>();
        var missing = Hundred - unit.Integrity;
        return repair.CostPerPoint.Select(c => new ResourceAmount(c.Resource, c.Amount * missing)).ToList();
    }

    /// <summary>The player's nearest finished colony core a unit can drive to (for repair), or null.</summary>
    public SurfaceStructure? NearestRepairPad(int player, int tile)
    {
        SurfaceStructure? best = null;
        var bestDist = int.MaxValue;
        foreach (var s in _structures)
        {
            if (RepairPadAt(player, s.Tile) != s) continue;
            var d = Map.Grid.Distance(s.Tile, tile);
            if (d < bestDist)
            {
                best = s;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>A breakdown: pick the severity by weight, knock its damage off integrity, fold up; an explosion loses the load.</summary>
    private void Break(Simulation sim, SurfaceUnit unit, BreakdownRules rules)
    {
        var total = 0;
        foreach (var v in rules.Severities) total += v.Weight;
        var roll = sim.Rng.NextInt(total);
        var index = 0;
        while (roll >= rules.Severities[index].Weight)
        {
            roll -= rules.Severities[index].Weight;
            index++;
        }
        var severity = rules.Severities[index];
        var damage = Fix64.FromInt(sim.Rng.NextInt(severity.DamageMin, severity.DamageMax + 1));
        unit.Integrity = Fix64.Max(Fix64.Zero, unit.Integrity - damage);
        unit.Broken = true;
        unit.Breakdown = index;
        unit.BrokeAtTick = sim.Tick;
        unit.RepairTicks = 0;
        unit.RepairWaiting = false;
        if (severity.LosesLoad)
        {
            unit.Hopper = Fix64.Zero;
            unit.HopperResource = -1;
        }
        StartPacking(unit);
        unit.DeployOnArrival = false;
        unit.MiningStatus = MiningStatus.None;
    }

    /// <summary>On a repair pad: the crew walk out, then each tick restores integrity while the player can pay for it.</summary>
    private void RepairStep(Simulation sim, SurfaceUnit unit, RepairRules repair)
    {
        unit.RepairTicks++;
        if (unit.RepairTicks <= repair.CrewSeconds * Simulation.TickRate) return;
        var heal = Fix64.Min(repair.PerSecond / Simulation.TickRate, Hundred - unit.Integrity);
        var cost = repair.CostPerPoint.Select(c => new ResourceAmount(c.Resource, c.Amount * heal)).ToList();
        var stock = sim.Players[unit.Owner].Stock;
        if (!stock.TrySpend(cost))
        {
            unit.RepairWaiting = true;
            return;
        }
        unit.RepairWaiting = false;
        unit.Integrity += heal;
        if (unit.Integrity < Hundred) return;
        unit.Integrity = Hundred;
        unit.Broken = false;
        unit.RepairTicks = 0;
    }

    // ---------------------------------------------------------------- stats: levels, research, condition

    private static readonly Fix64 Full = Fix64.FromInt(100);

    /// <summary>Whether a modifier from a tech applies to this building (one naming no buildings or units applies to every building).</summary>
    private static bool AppliesTo(Modifier m, string buildingId) =>
        m.Buildings.Count == 0 ? m.Units.Count == 0 : m.Buildings.Contains(buildingId);

    /// <summary>The modifiers of the building's current level (none at Mk I).</summary>
    public IReadOnlyList<Modifier> LevelModifiers(SurfaceStructure s)
    {
        var def = _data.Building(s.DefId);
        return s.Level >= 2 && s.Level - 2 < def.Levels.Count ? def.Levels[s.Level - 2].Modifiers : System.Array.Empty<Modifier>();
    }

    /// <summary>What the building's level adds to a stat (e.g. 0.3 = +30%).</summary>
    public Fix64 LevelBonus(SurfaceStructure s, string stat)
    {
        var sum = Fix64.Zero;
        foreach (var m in LevelModifiers(s))
            if (m.Stat == stat) sum += m.Add;
        return sum;
    }

    /// <summary>What the owner's research adds to a building's stat.</summary>
    public Fix64 ResearchBonus(int player, string buildingId, string stat)
    {
        var sum = Fix64.Zero;
        var known = _players[player].Known;
        foreach (var t in _data.Techs)
            if (known[t.Index])
                foreach (var m in t.Modifiers)
                    if (m.Stat == stat && AppliesTo(m, buildingId)) sum += m.Add;
        return sum;
    }

    /// <summary>What the owner's research adds to a unit's stat (e.g. breakdown -0.5).</summary>
    public Fix64 UnitResearchBonus(int player, string unitId, string stat)
    {
        var sum = Fix64.Zero;
        var known = _players[player].Known;
        foreach (var t in _data.Techs)
            if (known[t.Index])
                foreach (var m in t.Modifiers)
                    if (m.Stat == stat && m.Units.Contains(unitId)) sum += m.Add;
        return sum;
    }

    /// <summary>Speed of its work (extraction, cycles, assembly, research): 1 + level + research bonuses.</summary>
    public Fix64 SpeedMultiplier(SurfaceStructure s) => Fix64.Max(Fix64.Zero, Fix64.One + LevelBonus(s, Modifier.Speed) + ResearchBonus(s.Owner, s.DefId, Modifier.Speed));

    /// <summary>Share of recipe output it gets: 1 + level + research bonuses.</summary>
    public Fix64 YieldMultiplier(SurfaceStructure s) => Fix64.Max(Fix64.Zero, Fix64.One + LevelBonus(s, Modifier.Yield) + ResearchBonus(s.Owner, s.DefId, Modifier.Yield));

    /// <summary>How fast it wears: 1 + level + research bonuses (never below 10%).</summary>
    public Fix64 WearMultiplier(SurfaceStructure s) => Fix64.Max(Fix64.Ratio(1, 10), Fix64.One + LevelBonus(s, Modifier.Wear) + ResearchBonus(s.Owner, s.DefId, Modifier.Wear));

    /// <summary>Fixed by drones instead of the crew: its level or the owner's research turns this on.</summary>
    public bool AutoMaintained(SurfaceStructure s)
    {
        foreach (var m in LevelModifiers(s))
            if (m.Stat == Modifier.AutoMaintain) return true;
        var known = _players[s.Owner].Known;
        foreach (var t in _data.Techs)
            if (known[t.Index])
                foreach (var m in t.Modifiers)
                    if (m.Stat == Modifier.AutoMaintain && AppliesTo(m, s.DefId)) return true;
        return false;
    }

    /// <summary>Share of its output a building's condition allows: 1 down to slowest_output as condition falls below decline_below.</summary>
    public Fix64 ConditionFactor(SurfaceStructure s)
    {
        var rules = _data.Maintenance;
        if (rules == null || !_data.Building(s.DefId).Wears || s.Condition >= rules.DeclineBelow) return Fix64.One;
        return rules.SlowestOutput + (Fix64.One - rules.SlowestOutput) * s.Condition / rules.DeclineBelow;
    }

    /// <summary>Condition lost per minute while it works, after level and research.</summary>
    public Fix64 WearPerMinute(SurfaceStructure s) => _data.Building(s.DefId).WearPerMinute * WearMultiplier(s);

    /// <summary>Chance per minute that it breaks down now (0 above decline_below).</summary>
    public Fix64 BreakdownChancePerMinute(SurfaceStructure s)
    {
        var rules = _data.Maintenance;
        if (rules == null || !_data.Building(s.DefId).Wears || s.Condition >= rules.DeclineBelow) return Fix64.Zero;
        return rules.BreakChanceAtZero * (rules.DeclineBelow - s.Condition) / rules.DeclineBelow;
    }

    /// <summary>Name of its current level: "Mk I", "Mk II", ...</summary>
    public string LevelName(SurfaceStructure s)
    {
        var def = _data.Building(s.DefId);
        return s.Level >= 2 && s.Level - 2 < def.Levels.Count ? def.Levels[s.Level - 2].Name : "Mk I";
    }

    /// <summary>The next level it can be upgraded to, or null at the top (or for buildings without levels).</summary>
    public BuildingLevel? NextLevel(SurfaceStructure s)
    {
        var def = _data.Building(s.DefId);
        return s.Level - 1 < def.Levels.Count ? def.Levels[s.Level - 1] : null;
    }

    /// <summary>What a full repair from here would cost (condition back to 100).</summary>
    public IReadOnlyList<ResourceAmount> MaintenanceCost(SurfaceStructure s)
    {
        var rules = _data.Maintenance;
        if (rules == null) return System.Array.Empty<ResourceAmount>();
        var missing = Full - s.Condition;
        return rules.CostPerPoint.Select(c => new ResourceAmount(c.Resource, c.Amount * missing)).ToList();
    }

    /// <summary>The colony core whose crew looks after this building (its base's core).</summary>
    public SurfaceStructure? CrewBase(SurfaceStructure s) => FindStructure(s.BaseId);

    /// <summary>Why the crew can't be sent to maintain this building (null = they can).</summary>
    public string? MaintenanceProblem(int player, int structureId)
    {
        var s = FindStructure(structureId);
        if (s == null || s.Owner != player) return "not yours";
        if (!_data.Building(s.DefId).Wears || _data.Maintenance == null) return "doesn't need maintenance";
        if (s.State != StructureState.Complete) return "still under construction";
        if (s.Condition >= Full && !s.Broken) return "in perfect condition";
        if (s.AutoRepairing) return "drones are already fixing it";
        return null;
    }

    /// <summary>Restores up to one tick of repair, paying for it; false (and MaintenanceWaiting) if the player can't pay.</summary>
    private bool RepairTick(Simulation sim, SurfaceStructure s, MaintenanceRules rules)
    {
        var heal = Fix64.Min(rules.RepairPerSecond / Simulation.TickRate, Full - s.Condition);
        var cost = rules.CostPerPoint.Select(c => new ResourceAmount(c.Resource, c.Amount * heal)).ToList();
        if (!sim.Players[s.Owner].Stock.TrySpend(cost))
        {
            s.MaintenanceWaiting = true;
            return false;
        }
        s.MaintenanceWaiting = false;
        s.Condition += heal;
        return true;
    }

    private bool Fixed(SurfaceStructure s)
    {
        if (s.Condition < Full) return false;
        s.Condition = Full;
        s.Broken = false;
        s.MaintenanceOrdered = false;
        s.MaintenanceWaiting = false;
        return true;
    }

    /// <summary>
    /// Drones fix auto-maintained buildings that drop below auto_below (or break). Each base's crew works through
    /// its ordered jobs one at a time, oldest order first: walk out, fix to 100, walk back.
    /// </summary>
    private void Maintenance(Simulation sim)
    {
        var rules = _data.Maintenance;
        if (rules == null) return;
        foreach (var s in _structures)
        {
            if (s.State != StructureState.Complete || !_data.Building(s.DefId).Wears) continue;
            if (!s.AutoRepairing && AutoMaintained(s) && (s.Condition < rules.AutoBelow || s.Broken))
            {
                var core = CrewBase(s);
                if (core == null || core.CrewJob != s.Id) s.AutoRepairing = true; // don't take a job the crew is already on
            }
            if (!s.AutoRepairing) continue;
            s.MaintenanceOrdered = false;
            if (RepairTick(sim, s, rules) && Fixed(s)) s.AutoRepairing = false;
        }

        foreach (var core in _structures)
        {
            if (core.Id != core.BaseId || core.State != StructureState.Complete || !_data.Building(core.DefId).IsCore) continue;
            switch (core.CrewPhase)
            {
                case CrewPhase.None:
                    SurfaceStructure? next = null;
                    foreach (var s in _structures)
                        if (s.BaseId == core.Id && s.MaintenanceOrdered && !s.AutoRepairing && s.State == StructureState.Complete &&
                            (next == null || s.MaintenanceOrderTick < next.MaintenanceOrderTick)) next = s;
                    if (next == null) break;
                    core.CrewJob = next.Id;
                    core.CrewPhase = CrewPhase.Out;
                    core.CrewTicks = 0;
                    var walk = (rules.CrewWalkSeconds + rules.CrewSecondsPerTile * Fix64.FromInt(Map.Grid.Distance(core.Tile, next.Tile))) * Simulation.TickRate;
                    core.CrewWalkTicks = System.Math.Max(1, walk.FloorToInt());
                    break;
                case CrewPhase.Out:
                    if (++core.CrewTicks >= core.CrewWalkTicks)
                    {
                        core.CrewPhase = CrewPhase.Working;
                        core.CrewTicks = 0;
                    }
                    break;
                case CrewPhase.Working:
                    var job = FindStructure(core.CrewJob);
                    if (job == null || job.AutoRepairing || !job.MaintenanceOrdered)
                    {
                        core.CrewPhase = CrewPhase.Back; // salvaged, taken over by drones, or cancelled
                        core.CrewTicks = 0;
                        break;
                    }
                    core.CrewTicks++;
                    if (RepairTick(sim, job, rules) && Fixed(job))
                    {
                        core.CrewPhase = CrewPhase.Back;
                        core.CrewTicks = 0;
                    }
                    break;
                case CrewPhase.Back:
                    if (++core.CrewTicks >= core.CrewWalkTicks)
                    {
                        core.CrewPhase = CrewPhase.None;
                        core.CrewJob = 0;
                        core.CrewTicks = 0;
                    }
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- research

    /// <summary>Why the player can't start this tech (null = they can).</summary>
    public string? ResearchProblem(int player, string techId)
    {
        if (!_data.HasTech(techId)) return "unknown tech";
        var t = _data.Tech(techId);
        var p = _players[player];
        if (p.Known[t.Index]) return "already researched";
        foreach (var r in t.Requires)
            if (!p.Known[_data.Tech(r).Index]) return $"needs {_data.Tech(r).Name} first";
        return null;
    }

    /// <summary>Research points a minute the player's labs make right now (after power, condition and bonuses).</summary>
    public Fix64 ResearchPerMinute(int player)
    {
        var total = Fix64.Zero;
        foreach (var s in _structures)
        {
            if (s.Owner != player || s.State != StructureState.Complete || s.Status is not (StructureStatus.Working or StructureStatus.LowPower)) continue;
            var def = _data.Building(s.DefId);
            if (def.IsLab) total += def.ResearchPerMinute * WorkRate(s, PowerOf(s.BaseId).Efficiency);
        }
        return total;
    }

    /// <summary>How fast a working building gets things done: its power share, condition and speed bonuses together.</summary>
    private Fix64 WorkRate(SurfaceStructure s, Fix64 powerEfficiency)
    {
        var def = _data.Building(s.DefId);
        var power = def.PowerUse > Fix64.Zero ? powerEfficiency : Fix64.One;
        return power * ConditionFactor(s) * SpeedMultiplier(s);
    }

    // ---------------------------------------------------------------- vehicle production

    /// <summary>Why this unit can't be ordered at this building right now (null = it can).</summary>
    public string? ProductionProblem(Simulation sim, int player, int structureId, string unitId)
    {
        var s = FindStructure(structureId);
        if (s == null || s.Owner != player) return "not yours";
        var def = _data.Building(s.DefId);
        if (!def.Produces.Contains(unitId)) return "can't build that";
        if (s.State != StructureState.Complete) return "still under construction";
        if (s._production.Count >= def.ProductionQueue) return "queue full";
        if (!sim.Players[player].Stock.CanAfford(_data.SurfaceUnit(unitId).Cost)) return "not enough resources";
        return null;
    }

    /// <summary>Fraction of the first order done, 0..1 (0 when idle).</summary>
    public Fix64 ProductionFraction(SurfaceStructure s)
    {
        if (s._production.Count == 0) return Fix64.Zero;
        var total = _data.SurfaceUnit(s._production[0]).BuildSeconds * Simulation.TickRate;
        return total <= 0 ? Fix64.One : Fix64.Min(Fix64.One, s.ProductionProgress / Fix64.FromInt(total));
    }

    /// <summary>Rolls a finished unit out beside the bay: the first free passable neighbour (no building, no vehicle), else any passable one, else the bay's own tile.</summary>
    private void RollOut(Simulation sim, SurfaceStructure bay, string unitId)
    {
        var tile = -1;
        foreach (var n in Map.Grid.Neighbours(bay.Tile))
        {
            if (!Map.Terrain(n).Passable || _structureAt[n] != 0) continue;
            var taken = false;
            foreach (var u in _units)
                if (u.Tile == n) taken = true;
            if (!taken)
            {
                tile = n;
                break;
            }
            if (tile < 0) tile = n;
        }
        if (tile < 0) tile = bay.Tile;
        var def = _data.SurfaceUnit(unitId);
        var unit = new SurfaceUnit(_nextUnitId++, bay.Owner, def.Id, def.Speed * Simulation.SecondsPerTick, def.SurveyRadius, tile)
        {
            BuiltAtTick = sim.Tick,
            BuiltAtTile = bay.Tile,
        };
        _units.Add(unit);
        Reveal(bay.Owner, tile, def.SurveyRadius);
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
        foreach (var u in _units)
            if (u.Tile == tile && u.Mode != UnitMode.Mobile) return PlacementProblem.Occupied;
        var baseId = BaseFor(player, tile);
        if (baseId == 0) return PlacementProblem.OutOfRange;
        var def = _data.Building(buildingId);
        if (def.RequiresLevel > 0 && _data.Building(FindStructure(baseId)!.DefId).Level < def.RequiresLevel) return PlacementProblem.NeedsLevel;
        if (def.RequiresTech.Length > 0 && (!_data.HasTech(def.RequiresTech) || !_players[player].Known[_data.Tech(def.RequiresTech).Index])) return PlacementProblem.NeedsTech;
        if (def.RequiresDeposit && (Map.DepositResource[tile] < 0 || Map.DepositAmount[tile] <= Fix64.Zero)) return PlacementProblem.NeedsDeposit;
        if (def.RequiresDepleted && (!Map.IsDepleted(tile) || Map.DepositReopened[tile])) return PlacementProblem.NeedsDepletedDeposit;
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
        if (s.UpgradingLevel > 0)
        {
            var lt = _data.Building(s.DefId).Levels[s.UpgradingLevel - 2].BuildSeconds * Simulation.TickRate;
            return lt <= 0 ? Fix64.One : Fix64.Ratio(s.UpgradeTicks, lt);
        }
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
        if (s.UpgradingTo != null || s.UpgradingLevel > 0) return "already upgrading";
        var def = _data.Building(s.DefId);
        if (def.Levels.Count > 0)
        {
            var level = NextLevel(s);
            if (level == null) return "top level";
            var core = FindStructure(s.BaseId);
            if (level.RequiresLevel > 0 && (core == null || _data.Building(core.DefId).Level < level.RequiresLevel))
                return $"needs a level {level.RequiresLevel} base (Colonial Administration)";
            if (!sim.Players[player].Stock.CanAfford(level.Cost)) return "not enough resources";
            return null;
        }
        var next = def.UpgradesTo;
        if (next.Length == 0) return "top level";
        if (!sim.Players[player].Stock.CanAfford(_data.Building(next).Cost)) return "not enough resources";
        return null;
    }

    /// <summary>
    /// What the player's finished buildings make and use per minute right now (upkeep as negatives),
    /// with power users scaled by their base's power efficiency. Mines count extraction while their deposit
    /// lasts, converters their inputs (negative) and outputs, and mining crawlers what they dig.
    /// </summary>
    internal void AddIncome(Simulation sim, int player, ResourceBag income)
    {
        var stock = sim.Players[player].Stock;
        foreach (var u in _units)
            if (u.Owner == player && u.Mode == UnitMode.Deployed && u.MiningStatus == MiningStatus.Mining && Map.DepositResource[u.Tile] >= 0)
                income.Add(Map.DepositResource[u.Tile], _data.SurfaceUnit(u.DefId).ExtractPerMinute);
        var efficiency = new Dictionary<int, Fix64>();
        foreach (var s in _structures)
        {
            if (s.Owner != player || s.State != StructureState.Complete) continue;
            if (s.Status is not (StructureStatus.Working or StructureStatus.LowPower)) continue;
            var def = _data.Building(s.DefId);
            if (!efficiency.TryGetValue(s.BaseId, out var eff)) efficiency[s.BaseId] = eff = PowerOf(s.BaseId).Efficiency;
            var output = (def.PowerUse > Fix64.Zero ? eff : Fix64.One) * ConditionFactor(s);
            var speed = WorkRate(s, eff);
            foreach (var o in def.OutputPerMinute) income.Add(o.Resource, o.Amount * output);
            foreach (var u in def.UpkeepPerMinute) income.Add(u.Resource, -u.Amount);
            if (def.IsMine && Map.DepositResource[s.Tile] >= 0)
                income.Add(Map.DepositResource[s.Tile], def.ExtractPerMinute * speed);
            if (def.IsConverter)
            {
                var runnable = def.Recipes.Where(r => HasInput(stock, r)).ToList();
                foreach (var r in runnable)
                {
                    var cycles = def.CyclesPerMinute * speed / Fix64.FromInt(runnable.Count);
                    foreach (var a in r.In) income.Add(a.Resource, -a.Amount * cycles);
                    foreach (var a in r.Out) income.Add(a.Resource, a.Amount * cycles * YieldMultiplier(s));
                }
            }
        }
    }

    private static bool HasInput(ResourceBag stock, Recipe r)
    {
        foreach (var a in r.In)
            if (stock[a.Resource] <= Fix64.Zero) return false;
        return true;
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
                    SetPath(unit, path);
                    unit.DeployOnArrival = false;
                    StartPacking(unit); // a deployed crawler folds up, then drives off
                    unit.MiningStatus = MiningStatus.None;
                }
                return true;

            case MineCommand mine:
            {
                if (MineProblem(mine.Player, mine.UnitId, mine.Tile) != null) return true;
                var unit = FindUnit(mine.UnitId)!;
                if (unit.Tile == mine.Tile)
                {
                    unit._path.Clear();
                    unit.Progress = Fix64.Zero;
                    unit.DeployOnArrival = false;
                    StartDeploying(unit);
                    return true;
                }
                var path = Pathfinder.Find(Map, unit.Tile, mine.Tile);
                if (path == null) return true;
                SetPath(unit, path);
                StartPacking(unit);
                unit.DeployOnArrival = true;
                unit.MiningStatus = MiningStatus.None;
                return true;
            }

            case ResearchCommand research:
                if (ResearchProblem(research.Player, research.TechId) != null) return true;
                _players[research.Player].ActiveTech = _data.Tech(research.TechId).Index;
                return true;

            case MaintainCommand maintain:
            {
                var job = FindStructure(maintain.StructureId);
                if (job == null || job.Owner != maintain.Player) return true;
                var crewCore = CrewBase(job);
                if (job.MaintenanceOrdered)
                {
                    // Cancel, unless the crew are already on it.
                    if (crewCore == null || crewCore.CrewJob != job.Id) job.MaintenanceOrdered = false;
                    return true;
                }
                if (MaintenanceProblem(maintain.Player, maintain.StructureId) != null) return true;
                job.MaintenanceOrdered = true;
                job.MaintenanceOrderTick = sim.Tick;
                return true;
            }

            case BuildUnitCommand order:
            {
                if (ProductionProblem(sim, order.Player, order.StructureId, order.UnitId) != null) return true;
                var bay = FindStructure(order.StructureId)!;
                sim.Players[order.Player].Stock.TrySpend(_data.SurfaceUnit(order.UnitId).Cost);
                bay._production.Add(order.UnitId);
                return true;
            }

            case CancelUnitCommand cancelUnit:
            {
                var bay = FindStructure(cancelUnit.StructureId);
                if (bay == null || bay.Owner != cancelUnit.Player || bay._production.Count == 0) return true;
                var last = bay._production.Count - 1;
                foreach (var c in _data.SurfaceUnit(bay._production[last]).Cost) sim.Players[bay.Owner].Stock.Add(c.Resource, c.Amount);
                bay._production.RemoveAt(last);
                if (last == 0) bay.ProductionProgress = Fix64.Zero;
                return true;
            }

            case RepairCommand repair:
                foreach (var id in repair.UnitIds)
                {
                    var unit = FindUnit(id);
                    if (unit == null || unit.Owner != repair.Player || unit.Integrity >= Hundred) continue;
                    var pad = NearestRepairPad(repair.Player, unit.Tile);
                    if (pad == null) continue;
                    unit.DeployOnArrival = false;
                    if (pad.Tile == unit.Tile)
                    {
                        unit._path.Clear();
                        unit.Progress = Fix64.Zero;
                        continue;
                    }
                    var path = Pathfinder.Find(Map, unit.Tile, pad.Tile);
                    if (path == null) continue;
                    SetPath(unit, path);
                    StartPacking(unit);
                    unit.MiningStatus = MiningStatus.None;
                }
                return true;

            case PackUpCommand pack:
                foreach (var id in pack.UnitIds)
                {
                    var unit = FindUnit(id);
                    if (unit == null || unit.Owner != pack.Player) continue;
                    StartPacking(unit);
                    unit.DeployOnArrival = false;
                    unit.MiningStatus = MiningStatus.None;
                }
                return true;

            case SalvageCommand salvage:
            {
                var salvaged = FindStructure(salvage.StructureId);
                if (salvaged == null || salvaged.Owner != salvage.Player || salvaged.State != StructureState.Complete) return true;
                var def = _data.Building(salvaged.DefId);
                if (def.IsCore) return true;
                var bag = sim.Players[salvaged.Owner].Stock;
                foreach (var c in def.Cost) bag.Add(c.Resource, c.Amount * _data.SalvageRefund);
                for (var l = 0; l < salvaged.Level - 1 && l < def.Levels.Count; l++) // levels bought count too
                    foreach (var c in def.Levels[l].Cost) bag.Add(c.Resource, c.Amount * _data.SalvageRefund);
                if (salvaged.UpgradingLevel > 0) // an unfinished upgrade comes back in full
                    foreach (var c in def.Levels[salvaged.UpgradingLevel - 2].Cost) bag.Add(c.Resource, c.Amount);
                foreach (var queued in salvaged._production) // unbuilt orders come back in full
                    foreach (var c in _data.SurfaceUnit(queued).Cost) bag.Add(c.Resource, c.Amount);
                _structures.Remove(salvaged);
                _structureAt[salvaged.Tile] = 0;
                return true;
            }

            case PlaceBuildingCommand place:
                if (CheckPlacement(sim, place.Player, place.BuildingId, place.Tile) != PlacementProblem.None) return true;
                sim.Players[place.Player].Stock.TrySpend(_data.Building(place.BuildingId).Cost);
                PlaceStructure(place.Player, place.BuildingId, place.Tile, ((place.Facing % 6) + 6) % 6,
                    StructureState.Queued, BaseFor(place.Player, place.Tile));
                return true;

            case UpgradeCommand upgrade:
                if (UpgradeProblem(sim, upgrade.Player, upgrade.StructureId) != null) return true;
                var core = FindStructure(upgrade.StructureId)!;
                if (NextLevel(core) is { } level)
                {
                    sim.Players[core.Owner].Stock.TrySpend(level.Cost);
                    core.UpgradingLevel = core.Level + 1;
                    core.UpgradeTicks = 0;
                    return true;
                }
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
                    if (s.UpgradingLevel > 0)
                    {
                        foreach (var c in _data.Building(s.DefId).Levels[s.UpgradingLevel - 2].Cost) stock.Add(c.Resource, c.Amount);
                        s.UpgradingLevel = 0;
                        s.UpgradeTicks = 0;
                        return true;
                    }
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

    private static void SetPath(SurfaceUnit unit, List<int> path)
    {
        // Keep the effort already spent toward the next tile when that tile stays the same.
        var keep = unit._path.Count > 0 && path.Count > 0 && unit._path[0] == path[0];
        unit._path.Clear();
        unit._path.AddRange(path);
        if (!keep) unit.Progress = Fix64.Zero;
    }

    /// <summary>Salvage value of a finished building (its cost × salvage_refund).</summary>
    public IReadOnlyList<ResourceAmount> SalvageValue(string buildingId) =>
        _data.Building(buildingId).Cost.Select(c => new ResourceAmount(c.Resource, c.Amount * _data.SalvageRefund)).ToList();

    /// <summary>Salvage value of this building, counting the levels bought for it.</summary>
    public IReadOnlyList<ResourceAmount> SalvageValue(SurfaceStructure s)
    {
        var def = _data.Building(s.DefId);
        var bag = new ResourceBag(_data.Resources.Count, def.Cost);
        for (var l = 0; l < s.Level - 1 && l < def.Levels.Count; l++)
            foreach (var c in def.Levels[l].Cost) bag.Add(c.Resource, c.Amount);
        var list = new List<ResourceAmount>();
        for (var r = 0; r < bag.Count; r++)
            if (bag[r] > Fix64.Zero) list.Add(new ResourceAmount(r, bag[r] * _data.SalvageRefund));
        return list;
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
        LandPods(sim);
        Economy(sim);
        Maintenance(sim);
        Crawlers(sim);
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
            var def = _data.Building(s.DefId);
            if (def.RequiresDepleted && Map.IsDepleted(s.Tile) && !Map.DepositReopened[s.Tile])
            {
                Map.DepositAmount[s.Tile] = Map.DepositStart[s.Tile] * def.ReopenShare;
                Map.DepositReopened[s.Tile] = true;
            }
        }
    }

    /// <summary>Upgrading cores keep working at their current level until the upgrade completes.</summary>
    private void Upgrades()
    {
        foreach (var s in _structures)
        {
            if (s.UpgradingLevel > 0)
            {
                // A new level is a refit: it comes back in perfect condition.
                if (++s.UpgradeTicks < _data.Building(s.DefId).Levels[s.UpgradingLevel - 2].BuildSeconds * Simulation.TickRate) continue;
                s.Level = s.UpgradingLevel;
                s.UpgradingLevel = 0;
                s.UpgradeTicks = 0;
                s.Condition = Full;
                s.Broken = false;
                s.MaintenanceOrdered = false;
                continue;
            }
            if (s.UpgradingTo == null) continue;
            s.UpgradeTicks++;
            if (s.UpgradeTicks < _data.Building(s.UpgradingTo).BuildSeconds * Simulation.TickRate) continue;
            s.DefId = s.UpgradingTo;
            s.UpgradingTo = null;
            s.UpgradeTicks = 0;
        }
    }

    /// <summary>
    /// Finished buildings, in list order: (1) idle if their deposit is used up, storage is full (mines) or
    /// there's nothing to work on (converters), otherwise pay upkeep or idle; (2) work out each base's power
    /// budget from the buildings still running; (3) produce, with power users slowed by their base's efficiency.
    /// Output that takes storage space only goes in while there is room; mines dig only what fits.
    /// Converters spend their inputs first, so (with recipes that shrink) their output always fits.
    /// </summary>
    private void Economy(Simulation sim)
    {
        foreach (var s in _structures)
        {
            if (s.State != StructureState.Complete) continue;
            var def = _data.Building(s.DefId);
            var stock = sim.Players[s.Owner].Stock;

            if (s.Broken)
            {
                s.Status = StructureStatus.Broken;
                continue;
            }
            if (def.IsLab && _players[s.Owner].ActiveTech < 0)
            {
                s.Status = StructureStatus.NoInput;
                continue;
            }
            if (def.IsMine && (Map.DepositResource[s.Tile] < 0 || Map.DepositAmount[s.Tile] <= Fix64.Zero))
            {
                s.Status = StructureStatus.Depleted;
                continue;
            }
            if (def.IsMine && _data.IsStored(Map.DepositResource[s.Tile]) && FreeStorage(sim, s.Owner) <= Fix64.Zero)
            {
                s.Status = StructureStatus.StorageFull;
                continue;
            }
            if (def.IsConverter && !def.Recipes.Any(r => HasInput(stock, r)))
            {
                s.Status = StructureStatus.NoInput;
                continue;
            }
            if (def.IsFactory && s._production.Count == 0)
            {
                s.Status = StructureStatus.NoInput;
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
            if (def.PowerUse > Fix64.Zero && eff < Fix64.One) s.Status = StructureStatus.LowPower;
            var condition = ConditionFactor(s);
            var output = (def.PowerUse > Fix64.Zero ? eff : Fix64.One) * condition; // plain outputs (crew, the core's trickle)
            var speed = WorkRate(s, eff); // its work: extraction, cycles, assembly, research

            var stock = sim.Players[s.Owner].Stock;
            foreach (var o in def.OutputPerMinute) Store(sim, s.Owner, o.Resource, o.Amount * output / Simulation.TicksPerMinute);
            if (def.IsMine)
            {
                var take = Fix64.Min(def.ExtractPerMinute * speed / Simulation.TicksPerMinute, Map.DepositAmount[s.Tile]);
                Map.DepositAmount[s.Tile] -= Store(sim, s.Owner, Map.DepositResource[s.Tile], take);
            }
            if (def.IsConverter)
            {
                var runnable = def.Recipes.Where(r => HasInput(stock, r)).ToList();
                var share = def.CyclesPerMinute * speed / Simulation.TicksPerMinute / Fix64.FromInt(runnable.Count);
                var yield = YieldMultiplier(s);
                foreach (var r in runnable)
                {
                    var cycles = share;
                    foreach (var a in r.In) cycles = Fix64.Min(cycles, stock[a.Resource] / a.Amount);
                    if (cycles <= Fix64.Zero) continue;
                    foreach (var a in r.In) stock.Add(a.Resource, -a.Amount * cycles);
                    foreach (var a in r.Out) stock.Add(a.Resource, a.Amount * cycles * yield);
                }
            }
            if (def.IsFactory && s._production.Count > 0)
            {
                s.ProductionProgress += speed;
                var unitDef = _data.SurfaceUnit(s._production[0]);
                if (s.ProductionProgress >= Fix64.FromInt(unitDef.BuildSeconds * Simulation.TickRate))
                {
                    RollOut(sim, s, unitDef.Id);
                    s._production.RemoveAt(0);
                    s.ProductionProgress = Fix64.Zero;
                }
            }
            if (def.IsLab)
            {
                var p = _players[s.Owner];
                if (p.ActiveTech >= 0)
                {
                    var tech = _data.Techs[p.ActiveTech];
                    p.ResearchProgress[tech.Index] += def.ResearchPerMinute * speed / Simulation.TicksPerMinute;
                    if (p.ResearchProgress[tech.Index] >= tech.Cost)
                    {
                        p.ResearchProgress[tech.Index] = tech.Cost;
                        p.Known[tech.Index] = true;
                        p.ActiveTech = -1;
                    }
                }
            }

            // Wear while working; once below decline_below it may break down outright.
            if (def.Wears && _data.Maintenance != null)
            {
                s.Condition = Fix64.Max(Fix64.Zero, s.Condition - WearPerMinute(s) / Simulation.TicksPerMinute);
                var chance = BreakdownChancePerMinute(s);
                if (chance > Fix64.Zero && sim.Rng.NextFix() < chance / Simulation.TicksPerMinute)
                {
                    s.Broken = true;
                    s.Status = StructureStatus.Broken;
                }
            }
        }
    }

    /// <summary>Pods that have arrived drop their load into stock (space was kept for it at launch).</summary>
    private void LandPods(Simulation sim)
    {
        for (var i = 0; i < _pods.Count;)
        {
            var p = _pods[i];
            if (p.ArriveTick > sim.Tick)
            {
                i++;
                continue;
            }
            _pods.RemoveAt(i);
            sim.Players[p.Owner].Stock.Add(p.Resource, p.Amount);
        }
    }

    /// <summary>
    /// Mining crawlers: unfurl and pack up over their deploy time; when deployed, dig into the hopper and
    /// send each full load (or the last of a used-up deposit) to the nearest storage building as a cargo pod,
    /// waiting while storage has no room. When the deposit runs out they fold up.
    /// </summary>
    private void Crawlers(Simulation sim)
    {
        foreach (var unit in _units)
        {
            var def = _data.SurfaceUnit(unit.DefId);
            if (def.Repair != null && IsRepairing(unit)) RepairStep(sim, unit, def.Repair);
            else if (unit.RepairTicks > 0 || unit.RepairWaiting)
            {
                unit.RepairTicks = 0; // drove off the pad: the crew go back in
                unit.RepairWaiting = false;
            }
            switch (unit.Mode)
            {
                case UnitMode.Deploying:
                    if (++unit.ModeTicks < DeployTicks(unit)) break;
                    unit.Mode = UnitMode.Deployed;
                    unit.ModeTicks = 0;
                    unit.MiningStatus = MiningStatus.Mining;
                    break;
                case UnitMode.Packing:
                    if (++unit.ModeTicks < DeployTicks(unit)) break;
                    unit.Mode = UnitMode.Mobile;
                    unit.ModeTicks = 0;
                    break;
                case UnitMode.Deployed:
                    Dig(sim, unit, def);
                    break;
            }
        }
    }

    private void Dig(Simulation sim, SurfaceUnit unit, SurfaceUnitDef def)
    {
        var tile = unit.Tile;
        var depleted = Map.DepositResource[tile] < 0 || Map.DepositAmount[tile] <= Fix64.Zero;
        if (unit.Hopper > Fix64.Zero && (unit.Hopper >= def.Hopper || depleted || unit.HopperResource != Map.DepositResource[tile]))
            if (!Launch(sim, unit, def)) return;
        if (depleted)
        {
            StartPacking(unit);
            unit.MiningStatus = MiningStatus.Depleted;
            return;
        }
        if (def.Breakdown is { } rules && rules.ChancePerMinute > Fix64.Zero &&
            sim.Rng.NextFix() < rules.ChancePerMinute * Fix64.Max(Fix64.Zero, Fix64.One + UnitResearchBonus(unit.Owner, unit.DefId, Modifier.Breakdown)) / Simulation.TicksPerMinute)
        {
            Break(sim, unit, rules);
            return;
        }
        var take = Fix64.Min(def.ExtractPerMinute / Simulation.TicksPerMinute, Fix64.Min(Map.DepositAmount[tile], def.Hopper - unit.Hopper));
        Map.DepositAmount[tile] -= take;
        unit.Hopper += take;
        unit.HopperResource = Map.DepositResource[tile];
        unit.MiningStatus = MiningStatus.Mining;
    }

    /// <summary>Sends the hopper's load to the nearest storage building; false (and a status) if it can't go yet.</summary>
    private bool Launch(Simulation sim, SurfaceUnit unit, SurfaceUnitDef def)
    {
        var depot = NearestDepot(unit.Owner, unit.Tile);
        if (depot == null)
        {
            unit.MiningStatus = MiningStatus.NoDestination;
            return false;
        }
        if (_data.IsStored(unit.HopperResource) && FreeStorage(sim, unit.Owner) < unit.Hopper)
        {
            unit.MiningStatus = MiningStatus.StorageFull;
            return false;
        }
        var distance = Map.Grid.Distance(unit.Tile, depot.Tile);
        var flight = System.Math.Max(Simulation.TickRate, (Fix64.FromInt(distance * Simulation.TickRate) / def.PodSpeed).FloorToInt() + 1);
        _pods.Add(new CargoPod(_nextPodId++, unit.Owner, unit.HopperResource, unit.Hopper, unit.Tile, depot.Tile, sim.Tick, sim.Tick + flight));
        unit.Hopper = Fix64.Zero;
        unit.HopperResource = -1;
        unit.MiningStatus = MiningStatus.Mining;
        return true;
    }

    private void MoveUnits()
    {
        foreach (var unit in _units)
        {
            if (unit._path.Count == 0 || unit.Mode != UnitMode.Mobile) continue;
            unit.Progress += SpeedPerTick(unit);
            var next = unit._path[0];
            var cost = Fix64.FromInt(Map.Terrain(next).MoveCost);
            if (unit.Progress < cost) continue;
            unit.Progress -= cost;
            unit.Tile = next;
            unit._path.RemoveAt(0);
            Reveal(unit.Owner, unit.Tile, unit.SurveyRadius);
            if (unit._path.Count > 0) continue;
            unit.Progress = Fix64.Zero;
            if (!unit.DeployOnArrival) continue;
            unit.DeployOnArrival = false;
            if (DeployProblem(unit, unit.Tile) == null) StartDeploying(unit);
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
        h.Add(_pods.Count);
        foreach (var p in _pods) p.AddToHash(ref h);
    }
}
