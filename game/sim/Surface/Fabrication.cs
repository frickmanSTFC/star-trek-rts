using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Parts economy (docs/parts-contract.md; design doc "Early Era, Parts & Maintenance"; data surfaces/fabrication.json).
//
// Machine Shop (a building with a fabricator, id machine_shop): a queue of recipes, one order at a time per line, turning
// refined materials into parts, parts into spare kits and construction parts. Inputs are taken when an order starts (an
// order whose inputs are short is skipped and stays queued); progress runs at the building's WorkRate (power share x
// condition x speed bonuses) x fabricator.speed; the item goes into storage when done (the order waits while there is no
// room). The shop works like any building: status Working (and draws power) only while an order runs, NoInput otherwise,
// Paused / Broken stop it, and it wears while it works.
// A finished colony core has a small workshop: the four spare kits only, made straight from materials (the recipe tree
// flattened), one at a time, taking hub.time_factor x the summed shop time of the tree.
// Keep-in-stock (once a second, per player): each item's target (set by StockTargetCommand, else default_target for every
// kit the player's finished buildings and vehicles use) is kept by Auto orders at a Machine Shop of theirs (the shortest
// queue; the parts an auto kit order is short of are queued in front of it), else, for kits, at a core's workshop.
// Repairs (building maintenance, drone auto-repair, vehicle repair on the pad, pad tyres) take spare kits when the work
// starts (KitsNeeded); with too few in stock the repair waits (MaintenanceWaiting / RepairWaiting, RepairWaitReason) and
// the missing kits are queued at the front of a fabricator as Auto orders. A jury-rig (JuryRigCommand /
// JuryRigUnitsCommand) needs no kits but only patches up to jury_rig.max_condition, slower, and leaves the building or
// vehicle JuryRigged (more wear, more breakdowns) until a proper repair.

/// <summary>One order in a fabricator's queue. Progress 0..1 of the order (only started orders move).</summary>
public sealed record FabOrder(int Id, string RecipeId, bool Auto)
{
    public Fix64 Progress { get; internal set; }
    public bool Started { get; internal set; }
}

/// <summary>What a fabricator is doing now. StorageFull (added after the contract): an order is done but its item
/// doesn't fit in storage yet.</summary>
public enum FabStatus { None, Idle, Working, LowPower, NoInput, Broken, Paused, Building, StorageFull }

public sealed partial class PlanetSurface
{
    /// <summary>Most orders one fabricator's queue holds (commands and auto orders stop there).</summary>
    public const int MaxFabQueue = 50;

    /// <summary>Most orders one FabricateCommand may queue.</summary>
    public const int MaxFabCount = 50;

    private int _nextFabOrderId = 1;

    private FabricationRules? Fab => _data.Fabrication;

    /// <summary>Whether this kind of structure has a fabrication queue once finished (a Machine Shop, or a colony core when
    /// the data has a hub workshop).</summary>
    public bool HasFabricator(SurfaceStructure s)
    {
        if (Fab == null) return false;
        var def = _data.Building(s.DefId);
        return def.Fabricator != null || (def.IsCore && Fab.Hub.Recipes.Count > 0);
    }

    /// <summary>A finished building that has a fabrication queue: a Machine Shop, or a colony core (its workshop).</summary>
    public bool IsFabricator(SurfaceStructure s) => s.State == StructureState.Complete && HasFabricator(s);

    /// <summary>Is this structure a colony core's workshop (kits only, slower) rather than a Machine Shop?</summary>
    public bool IsHubWorkshop(SurfaceStructure s) => HasFabricator(s) && _data.Building(s.DefId).Fabricator == null;

    /// <summary>Recipe ids (= item ids) this fabricator can make, in menu order.</summary>
    public IReadOnlyList<string> RecipesAt(SurfaceStructure s)
    {
        if (!HasFabricator(s)) return System.Array.Empty<string>();
        if (IsHubWorkshop(s)) return Fab!.Hub.Recipes;
        var list = new List<string>();
        foreach (var r in Fab!.Recipes) list.Add(r.Id);
        return list;
    }

    private bool CanMake(SurfaceStructure s, string recipeId)
    {
        if (!HasFabricator(s) || Fab!.Recipe(recipeId) == null) return false;
        return !IsHubWorkshop(s) || Fab.Hub.Recipes.Contains(recipeId);
    }

    /// <summary>What one order of the recipe takes here (a core's workshop: flattened to materials).</summary>
    public IReadOnlyList<ResourceAmount> InputsAt(SurfaceStructure s, string recipeId)
    {
        if (!CanMake(s, recipeId)) return System.Array.Empty<ResourceAmount>();
        if (IsHubWorkshop(s)) return _data.Flattened(recipeId)?.In ?? System.Array.Empty<ResourceAmount>();
        return Fab!.Recipe(recipeId)!.In;
    }

    /// <summary>Seconds one order takes here at full speed (before power/condition).</summary>
    public Fix64 SecondsAt(SurfaceStructure s, string recipeId)
    {
        if (!CanMake(s, recipeId)) return Fix64.Zero;
        if (IsHubWorkshop(s)) return (_data.Flattened(recipeId)?.Seconds ?? Fix64.Zero) * Fab!.Hub.TimeFactor;
        var speed = _data.Building(s.DefId).Fabricator!.Speed;
        return Fab!.Recipe(recipeId)!.Seconds / speed;
    }

    /// <summary>How many of its item one order makes.</summary>
    private int OutputOf(string recipeId) => System.Math.Max(1, Fab?.Recipe(recipeId)?.Output ?? 1);

    /// <summary>Orders that may run at once here (fabricator lines; a core's workshop: 1).</summary>
    private int LinesAt(SurfaceStructure s) => _data.Building(s.DefId).Fabricator?.Lines ?? 1;

    /// <summary>Why the player can't queue this recipe here (null = they can).</summary>
    public string? FabricateProblem(int player, int structureId, string recipeId)
    {
        if (!IsPlayer(player)) return "no such player";
        var s = FindStructure(structureId);
        if (s == null || s.Owner != player) return "not yours";
        if (Fab == null) return "no parts economy in this game";
        if (!HasFabricator(s)) return "it can't make anything";
        if (NotReadyProblem(s) is { } notReady) return notReady;
        if (Fab.Recipe(recipeId) == null) return "unknown recipe";
        if (!CanMake(s, recipeId)) return "the colony core's workshop only makes spare kits: parts need a Machine Shop";
        if (s._fabrication.Count >= MaxFabQueue) return "queue full";
        return null;
    }

    /// <summary>The fabricator's state right now (Working, NoInput when every order is short, ...).</summary>
    public FabStatus FabricatorStatus(SurfaceStructure s)
    {
        if (!HasFabricator(s)) return FabStatus.None;
        if (s.State != StructureState.Complete) return FabStatus.Building;
        if (s.Broken) return FabStatus.Broken;
        if (s.Paused) return FabStatus.Paused;
        if (s._fabrication.Count == 0) return FabStatus.Idle;
        var running = false;
        var waitingRoom = false;
        foreach (var o in s._fabrication)
        {
            if (!o.Started) continue;
            if (o.Progress >= Fix64.One) waitingRoom = true;
            else running = true;
        }
        if (running) return s.Status == StructureStatus.LowPower ? FabStatus.LowPower : FabStatus.Working;
        if (waitingRoom) return FabStatus.StorageFull;
        return FabStatus.NoInput;
    }

    /// <summary>Why this order can't start now ("needs 2 Mechanism (have 1)"), or null.</summary>
    public string? OrderProblem(SurfaceStructure s, FabOrder order)
    {
        if (order.Started) return order.Progress >= Fix64.One ? "done: waiting for room in storage" : null;
        if (!CanMake(s, order.RecipeId)) return "can't be made here";
        if (s.Owner < 0 || s.Owner >= _players.Count || _sim == null) return null;
        var stock = _sim.Players[s.Owner].Stock;
        foreach (var a in InputsAt(s, order.RecipeId))
            if (stock[a.Resource] < a.Amount)
                return $"needs {Count(a.Amount)} {_data.Resources[a.Resource].Name} (have {stock[a.Resource].FloorToInt()})";
        return null;
    }

    private static string Count(Fix64 amount) =>
        amount == Fix64.FromInt(amount.FloorToInt()) ? amount.FloorToInt().ToString() : amount.ToString();

    /// <summary>Work done per tick on an order here, as a share of SecondsAt (power share x condition x speed bonuses,
    /// and life support on a Domes world). 0 while broken, paused or unfinished.</summary>
    private Fix64 FabRate(SurfaceStructure s)
    {
        if (!IsFabricator(s) || s.Broken || s.Paused) return Fix64.Zero;
        var eff = _data.Building(s.DefId).PowerUse > Fix64.Zero ? PowerOf(s.BaseId).Efficiency : Fix64.One;
        return WorkRate(s, eff) * CrewShare(s, null);
    }

    /// <summary>Seconds left on the whole queue at the current rate (at full speed while it is stopped).</summary>
    public Fix64 QueueSecondsLeft(SurfaceStructure s)
    {
        var rate = FabRate(s);
        if (rate <= Fix64.Zero) rate = Fix64.One;
        var total = Fix64.Zero;
        foreach (var o in s._fabrication) total += (Fix64.One - o.Progress) * SecondsAt(s, o.RecipeId) / rate;
        return total / Fix64.FromInt(LinesAt(s));
    }

    /// <summary>Every part, spare kit and construction part id, in display order (resources with tier part/spare/construction).</summary>
    public IReadOnlyList<string> PartItems
    {
        get
        {
            if (_partItems != null && _partItemsData == _data) return _partItems;
            var list = new List<string>();
            foreach (var r in _data.Resources)
                if (r.IsPartItem) list.Add(r.Id);
            _partItemsData = _data;
            return _partItems = list;
        }
    }

    private List<string>? _partItems;
    private GameData? _partItemsData;

    private int ResourceIndexOf(string id)
    {
        foreach (var r in _data.Resources)
            if (r.Id == id) return r.Index;
        return -1;
    }

    /// <summary>Does one of the player's finished buildings or vehicles use this kit for its repairs?</summary>
    private bool UsesKit(int player, string kit)
    {
        foreach (var s in _structures)
            if (s.Owner == player && s.State == StructureState.Complete && KitOf(s) == kit) return true;
        foreach (var u in _units)
            if (u.Owner == player && KitOf(u) == kit) return true;
        return false;
    }

    /// <summary>The player's keep-in-stock target for an item (0 = none).</summary>
    public int StockTarget(int player, string itemId)
    {
        if (!IsPlayer(player) || Fab == null) return 0;
        var r = ResourceIndexOf(itemId);
        if (r < 0 || !_data.Resources[r].IsPartItem) return 0;
        var set = _players[player]._stockTargets[r];
        if (set >= 0) return set;
        return _data.Resources[r].Tier == "spare" && UsesKit(player, itemId) ? Fab.DefaultTarget : 0;
    }

    /// <summary>How many of an item are queued (all the player's fabricators).</summary>
    public int Queued(int player, string itemId)
    {
        var n = 0;
        foreach (var s in _structures)
            if (s.Owner == player)
                foreach (var o in s._fabrication)
                    if (o.RecipeId == itemId) n += OutputOf(o.RecipeId);
        return n;
    }

    /// <summary>The spare kit a building or vehicle uses for repairs (resource id), or null.</summary>
    public string? KitOf(SurfaceStructure s) => _data.KitOfBuilding(s.DefId);
    public string? KitOf(SurfaceUnit u) => _data.KitOfUnit(u.DefId);

    private static int CeilDiv(Fix64 a, Fix64 b)
    {
        if (a <= Fix64.Zero || b <= Fix64.Zero) return 0;
        var q = a / b;
        var n = q.FloorToInt();
        return Fix64.FromInt(n) < q ? n + 1 : n;
    }

    /// <summary>Kits a full repair from here takes (building: ceil(missing condition / kit_points); vehicle: by damage and
    /// breakdown severity, plus pad tyres for rovers). 0 once the repair under way has taken its kits.</summary>
    public int KitsNeeded(SurfaceStructure s)
    {
        if (Fab == null || KitOf(s) == null || s.RepairPaid) return 0;
        var n = CeilDiv(Full - s.Condition, Fab.KitPoints);
        return s.Broken ? System.Math.Max(1, n) : n;
    }

    public int KitsNeeded(SurfaceUnit u)
    {
        if (Fab == null || KitOf(u) == null || u.RepairPaid) return 0;
        var n = CeilDiv(Hundred - u.Integrity, Fab.KitPoints);
        var def = _data.SurfaceUnit(u.DefId);
        if (u.Broken)
        {
            if (def.Breakdown is { } b && u.Breakdown >= 0 && u.Breakdown < b.Severities.Count && def.Damage == null)
                n += Fab.KitsForBreakdown(b.Severities[u.Breakdown].Id);
            n = System.Math.Max(1, n);
        }
        return n + PadTyreCount(u) * Fab.PadTyreKits;
    }

    private string KitName(string kit) => _data.Resource(kit).Name;

    /// <summary>Why the repair is stuck ("needs 2 Mechanical Spares (have 1)"), or null.</summary>
    public string? RepairWaitReason(SurfaceStructure s)
    {
        if (!s.MaintenanceWaiting || s.JuryRigOrdered || KitOf(s) is not { } kit) return null;
        return KitsShort(s.Owner, kit, KitsNeeded(s));
    }

    public string? RepairWaitReason(SurfaceUnit u)
    {
        if (!u.RepairWaiting || u.JuryRigOrdered || KitOf(u) is not { } kit) return null;
        return KitsShort(u.Owner, kit, KitsNeeded(u));
    }

    private string KitsShort(int player, string kit, int need)
    {
        var have = _sim != null && IsPlayer(player) ? _sim.Players[player].Stock[_data.Resource(kit).Index].FloorToInt() : 0;
        return $"needs {need} {KitName(kit)} (have {have})";
    }

    /// <summary>Seconds until the fabricators should have the kits this repair waits for (null = nothing queued).</summary>
    public Fix64? KitEtaSeconds(int player, string kitId)
    {
        Fix64? best = null;
        foreach (var s in _structures)
        {
            if (s.Owner != player || !IsFabricator(s)) continue;
            var rate = FabRate(s);
            if (rate <= Fix64.Zero) rate = Fix64.One;
            var lines = Fix64.FromInt(LinesAt(s));
            var ahead = Fix64.Zero;
            foreach (var o in s._fabrication)
            {
                ahead += (Fix64.One - o.Progress) * SecondsAt(s, o.RecipeId) / rate;
                if (o.RecipeId != kitId) continue;
                var eta = ahead / lines;
                if (best == null || eta < best.Value) best = eta;
                break;
            }
        }
        return best;
    }

    /// <summary>Why the building can't be jury-rigged now (null = it can).</summary>
    public string? JuryRigProblem(int player, int structureId)
    {
        var s = FindStructure(structureId);
        if (s == null || s.Owner != player) return "not yours";
        if (!_data.Building(s.DefId).Wears || _data.Maintenance == null) return "doesn't need maintenance";
        if (Fab == null) return "repairs need no spare kits here";
        if (NotReadyProblem(s) is { } notReady) return notReady;
        if (s.JuryRigOrdered) return "a jury-rig is already ordered (again to cancel)";
        if (s.RepairPaid) return "already being repaired with spare kits";
        var cap = Fab.JuryRig.MaxCondition;
        if (s.Condition >= cap && !s.Broken) return $"above {cap.FloorToInt()}%: a jury-rig only patches it up to {cap.FloorToInt()}%";
        return null;
    }

    /// <summary>Why the vehicle can't be jury-rigged now (null = it can).</summary>
    public string? JuryRigProblem(SurfaceUnit u)
    {
        if (Fab == null) return "repairs need no spare kits here";
        if (_data.SurfaceUnit(u.DefId).Repair == null) return "it can't be repaired";
        if (!NeedsRepair(u)) return "doesn't need a repair";
        if (!IsRepairing(u)) return "park it on a repair pad first (R)";
        if (u.JuryRigOrdered) return "already being jury-rigged";
        if (u.RepairPaid) return "already being repaired with spare kits";
        var cap = Fab.JuryRig.MaxCondition;
        if (u.Integrity >= cap && !u.Broken && FlatCount(u) == 0) return $"above {cap.FloorToInt()}%: a jury-rig only patches it up to {cap.FloorToInt()}%";
        return null;
    }

    /// <summary>The game this surface belongs to (read-only stock lookups for the view helpers); set by Simulation.LoadSurface.</summary>
    internal Simulation? Game { get; set; }

    private Simulation? _sim => Game;

    // ---------------------------------------------------------------- repairs with kits

    /// <summary>What a repair step did.</summary>
    private enum RepairResult { Waiting, Working, Done }

    /// <summary>Takes the kits a repair needs from the owner's stock when it starts; false (nothing taken) if they're short.</summary>
    private static bool TakeKits(Simulation sim, int owner, int kitIndex, int need)
    {
        if (need <= 0 || kitIndex < 0) return true;
        var stock = sim.Players[owner].Stock;
        if (stock[kitIndex] < Fix64.FromInt(need)) return false;
        stock.Add(kitIndex, -Fix64.FromInt(need));
        return true;
    }

    /// <summary>One tick of a building repair (crew or drones): a jury-rig up to max_condition with no kits, or a proper
    /// repair that takes its kits first (waiting while they're short), then restores condition to 100.</summary>
    private RepairResult RepairTick(Simulation sim, SurfaceStructure s, MaintenanceRules rules)
    {
        var rate = rules.RepairPerSecond * Gravity.CrewWork / Simulation.TickRate * SickShare(s.BaseId); // a sick crew (Excavation.cs)
        if (s.JuryRigOrdered && Fab is { } fab)
        {
            s.MaintenanceWaiting = false;
            var cap = fab.JuryRig.MaxCondition;
            if (s.Condition < cap) s.Condition = Fix64.Min(cap, s.Condition + rate * fab.JuryRig.Speed);
            if (s.Condition < cap) return RepairResult.Working;
            s.Broken = false;
            s.JuryRigged = true;
            s.JuryRigOrdered = false;
            s.MaintenanceOrdered = false;
            return RepairResult.Done;
        }
        if (!s.RepairPaid)
        {
            var kit = KitOf(s);
            var need = KitsNeeded(s);
            if (kit != null && !TakeKits(sim, s.Owner, _data.Resource(kit).Index, need))
            {
                s.MaintenanceWaiting = true;
                return RepairResult.Waiting;
            }
            s.RepairPaid = true;
        }
        var heal = Fix64.Min(rate, Full - s.Condition);
        var cost = new List<ResourceAmount>(); // the old per-point metal (empty since the parts economy)
        foreach (var c in rules.CostPerPoint) cost.Add(new ResourceAmount(c.Resource, c.Amount * heal));
        if (!sim.Players[s.Owner].Stock.TrySpend(cost))
        {
            s.MaintenanceWaiting = true;
            return RepairResult.Waiting;
        }
        s.MaintenanceWaiting = false;
        s.Condition += heal;
        return Fixed(s) ? RepairResult.Done : RepairResult.Working;
    }

    /// <summary>On a repair pad, after the crew have walked out: a jury-rig (no kits, up to max_condition, flats patched)
    /// or a proper repair that takes its kits first (waiting while short), fits pad tyres, then welds integrity to 100.</summary>
    private void RepairWork(Simulation sim, SurfaceUnit unit, RepairRules repair)
    {
        var rate = repair.PerSecond * Gravity.CrewWork / Simulation.TickRate * SickShare(RepairPadAt(unit.Owner, unit.Tile)?.Id ?? 0); // a sick crew (Excavation.cs)
        if (unit.JuryRigOrdered && Fab is { } fab)
        {
            unit.RepairWaiting = false;
            unit.FlatTyres = 0; // patched, not restocked
            unit.TyreChangeTicks = 0;
            var cap = fab.JuryRig.MaxCondition;
            if (unit.Integrity < cap) unit.Integrity = Fix64.Min(cap, unit.Integrity + rate * fab.JuryRig.Speed);
            if (unit.Integrity < cap) return;
            unit.Broken = false;
            unit.JuryRigged = true;
            unit.JuryRigOrdered = false;
            unit.RepairTicks = 0;
            return;
        }
        if (!unit.RepairPaid)
        {
            var kit = KitOf(unit);
            if (kit != null && !TakeKits(sim, unit.Owner, _data.Resource(kit).Index, KitsNeeded(unit)))
            {
                unit.RepairWaiting = true;
                return;
            }
            unit.RepairPaid = true;
        }
        if (!PadTyres(sim, unit)) // rovers: new tyres on every flat and the spares restocked first
        {
            unit.RepairWaiting = true;
            return;
        }
        var heal = Fix64.Min(rate, Hundred - unit.Integrity);
        var cost = new List<ResourceAmount>();
        foreach (var c in repair.CostPerPoint) cost.Add(new ResourceAmount(c.Resource, c.Amount * heal));
        if (!sim.Players[unit.Owner].Stock.TrySpend(cost))
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
        unit.RepairPaid = false;
        unit.JuryRigged = false;
    }

    /// <summary>Wear x jury_rig.wear_multiplier while jury-rigged.</summary>
    private Fix64 JuryWear(bool juryRigged) => juryRigged && Fab is { } f ? f.JuryRig.WearMultiplier : Fix64.One;

    /// <summary>Breakdown (and rover hazard / flat) chances x jury_rig.breakdown_multiplier while jury-rigged.</summary>
    private Fix64 JuryBreakdown(bool juryRigged) => juryRigged && Fab is { } f ? f.JuryRig.BreakdownMultiplier : Fix64.One;

    // ---------------------------------------------------------------- running the queues

    /// <summary>Whether a Machine Shop has something to do this tick: an order running, or one whose inputs are in stock
    /// (and a line free for it). Otherwise its status: NoInput, or StorageFull when only finished orders wait for room.</summary>
    private StructureStatus? ShopIdle(Simulation sim, SurfaceStructure s)
    {
        var stock = sim.Players[s.Owner].Stock;
        var started = 0;
        var waitingRoom = false;
        foreach (var o in s._fabrication)
        {
            if (!o.Started) continue;
            started++;
            if (o.Progress < Fix64.One) return null;
            waitingRoom = true;
            if (Fits(sim, s.Owner, o.RecipeId)) return null;
        }
        if (started < LinesAt(s))
            foreach (var o in s._fabrication)
                if (!o.Started && HasInputs(stock, InputsAt(s, o.RecipeId))) return null;
        return waitingRoom ? StructureStatus.StorageFull : StructureStatus.NoInput;
    }

    private static bool HasInputs(ResourceBag stock, IReadOnlyList<ResourceAmount> inputs)
    {
        foreach (var a in inputs)
            if (stock[a.Resource] < a.Amount) return false;
        return true;
    }

    private bool Fits(Simulation sim, int owner, string itemId)
    {
        var r = _data.Resource(itemId).Index;
        return !_data.IsStored(r) || FreeStorage(sim, owner) >= _data.VolumeOf(r) * Fix64.FromInt(OutputOf(itemId));
    }

    /// <summary>One tick of a fabricator at this rate: started orders move (a done one goes into storage when it fits),
    /// then free lines start the first queued orders whose inputs are in stock (taking them).</summary>
    private void FabStep(Simulation sim, SurfaceStructure s, Fix64 rate)
    {
        var stock = sim.Players[s.Owner].Stock;
        for (var i = 0; i < s._fabrication.Count;)
        {
            var o = s._fabrication[i];
            if (!o.Started)
            {
                i++;
                continue;
            }
            if (o.Progress < Fix64.One)
            {
                var seconds = SecondsAt(s, o.RecipeId);
                o.Progress = seconds <= Fix64.Zero ? Fix64.One : Fix64.Min(Fix64.One, o.Progress + rate / (seconds * Simulation.TickRate));
            }
            if (o.Progress >= Fix64.One && Fits(sim, s.Owner, o.RecipeId))
            {
                stock.Add(_data.Resource(o.RecipeId).Index, Fix64.FromInt(OutputOf(o.RecipeId)));
                s._fabrication.RemoveAt(i);
                s.LastFabricated = o.RecipeId;
                s.FabricatedAtTick = sim.Tick;
                continue;
            }
            i++;
        }
        var running = 0;
        foreach (var o in s._fabrication)
            if (o.Started) running++;
        for (var i = 0; i < s._fabrication.Count && running < LinesAt(s); i++)
        {
            var o = s._fabrication[i];
            if (o.Started) continue;
            var inputs = InputsAt(s, o.RecipeId);
            if (!stock.TrySpend(inputs)) continue; // short: skipped, stays queued
            o.Started = true;
            running++;
        }
    }

    /// <summary>Colony cores' workshops, each tick after the economy (Machine Shops run in Economy like other buildings).</summary>
    private void HubWorkshops(Simulation sim)
    {
        if (Fab == null) return;
        foreach (var s in _structures)
        {
            if (s._fabrication.Count == 0 || !IsFabricator(s) || !IsHubWorkshop(s) || s.Broken) continue;
            FabStep(sim, s, FabRate(s));
        }
    }

    /// <summary>A started order's inputs back to the owner (cancel, salvage); refunds ignore the storage cap.</summary>
    private void Refund(Simulation sim, SurfaceStructure s, FabOrder o)
    {
        if (!o.Started) return;
        foreach (var a in InputsAt(s, o.RecipeId)) sim.Players[s.Owner].Stock.Add(a.Resource, a.Amount);
    }

    /// <summary>Salvage: every started order's inputs come back.</summary>
    private void RefundFabrication(Simulation sim, SurfaceStructure s)
    {
        foreach (var o in s._fabrication) Refund(sim, s, o);
        s._fabrication.Clear();
    }

    // ---------------------------------------------------------------- keep-in-stock and kit requests

    /// <summary>Where the player's auto orders for this item go: their Machine Shop with the shortest queue (not broken or
    /// paused, with room), else for a hub recipe their core workshop with the shortest queue; null if none can.</summary>
    private SurfaceStructure? AutoTarget(int player, string itemId)
    {
        SurfaceStructure? best = null;
        foreach (var s in _structures)
        {
            if (s.Owner != player || !IsFabricator(s) || IsHubWorkshop(s) || s.Broken || s.Paused || s._fabrication.Count >= MaxFabQueue) continue;
            if (best == null || s._fabrication.Count < best._fabrication.Count) best = s;
        }
        if (best != null || !Fab!.Hub.Recipes.Contains(itemId)) return best;
        foreach (var s in _structures)
        {
            if (s.Owner != player || !IsFabricator(s) || !IsHubWorkshop(s) || s._fabrication.Count >= MaxFabQueue) continue;
            if (best == null || s._fabrication.Count < best._fabrication.Count) best = s;
        }
        return best;
    }

    /// <summary>Queues up to count Auto orders of an item (front = after the orders already running).</summary>
    private void Request(int player, string itemId, int count, bool front)
    {
        for (var n = 0; n < count; n++)
        {
            var s = AutoTarget(player, itemId);
            if (s == null) return;
            var order = new FabOrder(_nextFabOrderId++, itemId, true);
            if (front)
            {
                var at = 0;
                while (at < s._fabrication.Count && (s._fabrication[at].Started || (s._fabrication[at].Auto && _frontIds.Contains(s._fabrication[at].Id)))) at++;
                s._fabrication.Insert(at, order);
                _frontIds.Add(order.Id);
            }
            else s._fabrication.Add(order);
        }
    }

    /// <summary>Orders put at the front during this pass (so several front requests keep their order); cleared each pass.</summary>
    private readonly List<int> _frontIds = new();

    /// <summary>Kits the player's waiting repairs need, by kit resource index (jury-rigs need none).</summary>
    private int[] KitDemand(int player)
    {
        var demand = new int[_data.Resources.Count];
        foreach (var s in _structures)
            if (s.Owner == player && s.MaintenanceWaiting && !s.JuryRigOrdered && KitOf(s) is { } kit)
                demand[_data.Resource(kit).Index] += KitsNeeded(s);
        foreach (var u in _units)
            if (u.Owner == player && u.RepairWaiting && !u.JuryRigOrdered && KitOf(u) is { } kit)
                demand[_data.Resource(kit).Index] += KitsNeeded(u);
        return demand;
    }

    /// <summary>
    /// Once a second, per landed player: kits waiting repairs are short of go to the front of a fabricator; then each
    /// item's keep-in-stock target (on top of what waiting repairs need) is topped up at the back; then the parts the
    /// Machine Shops' auto orders are short of are queued in front of them.
    /// </summary>
    private void AutoOrders(Simulation sim)
    {
        if (Fab == null || sim.Tick % Simulation.TickRate != 0) return;
        for (var p = 0; p < _players.Count; p++)
        {
            if (_players[p].Phase != LandingPhase.Landed) continue;
            var stock = sim.Players[p].Stock;
            _frontIds.Clear();
            var demand = KitDemand(p);
            foreach (var id in PartItems)
            {
                var r = _data.Resource(id).Index;
                if (demand[r] <= 0) continue;
                var short_ = demand[r] - stock[r].FloorToInt() - Queued(p, id);
                if (short_ > 0) Request(p, id, short_, front: true);
            }
            foreach (var id in PartItems)
            {
                var target = StockTarget(p, id);
                if (target <= 0) continue;
                var r = _data.Resource(id).Index;
                var have = stock[r].FloorToInt() + Queued(p, id) - demand[r];
                if (have < target) Request(p, id, target - have, front: false);
            }
            QueueMissingParts(sim, p);
            _frontIds.Clear();
        }
    }

    /// <summary>For the auto orders at the player's Machine Shops, in queue order: the parts (items with a recipe) each is
    /// short of, counting stock + queued, are queued just in front of it.</summary>
    private void QueueMissingParts(Simulation sim, int player)
    {
        var stock = sim.Players[player].Stock;
        var supply = new Fix64[_data.Resources.Count];
        var counted = new bool[_data.Resources.Count];
        var done = new List<int>();
        foreach (var s in _structures)
        {
            if (s.Owner != player || !IsFabricator(s) || IsHubWorkshop(s)) continue;
            for (var i = 0; i < s._fabrication.Count; i++)
            {
                var o = s._fabrication[i];
                if (o.Started || !o.Auto || done.Contains(o.Id)) continue;
                done.Add(o.Id);
                var inserted = 0;
                foreach (var a in Fab!.Recipe(o.RecipeId)!.In)
                {
                    var part = _data.Resources[a.Resource].Id;
                    if (Fab.Recipe(part) == null) continue; // a material, not a part
                    if (!counted[a.Resource])
                    {
                        supply[a.Resource] = stock[a.Resource] + Fix64.FromInt(Queued(player, part));
                        counted[a.Resource] = true;
                    }
                    if (supply[a.Resource] >= a.Amount)
                    {
                        supply[a.Resource] -= a.Amount;
                        continue;
                    }
                    var orders = CeilDiv(a.Amount - supply[a.Resource], Fix64.FromInt(OutputOf(part)));
                    for (var n = 0; n < orders && s._fabrication.Count < MaxFabQueue; n++)
                    {
                        s._fabrication.Insert(i + inserted, new FabOrder(_nextFabOrderId++, part, true));
                        inserted++;
                        supply[a.Resource] += Fix64.FromInt(OutputOf(part));
                    }
                    supply[a.Resource] = Fix64.Max(Fix64.Zero, supply[a.Resource] - a.Amount);
                }
                if (inserted > 0) i--; // look at the new part orders next (their own inputs are materials, or more parts)
            }
        }
    }

    // ---------------------------------------------------------------- commands

    /// <summary>Handles the five parts-economy commands.</summary>
    private bool ApplyFabrication(Simulation sim, Command command)
    {
        switch (command)
        {
            case FabricateCommand c:
            {
                if (c.Count < 1 || c.Count > MaxFabCount) return sim.Refuse(c, $"count must be 1..{MaxFabCount}");
                if (FabricateProblem(c.Player, c.StructureId, c.RecipeId) is { } problem) return sim.Refuse(c, problem);
                var s = FindStructure(c.StructureId)!;
                for (var n = 0; n < c.Count && s._fabrication.Count < MaxFabQueue; n++)
                    s._fabrication.Add(new FabOrder(_nextFabOrderId++, c.RecipeId, false));
                return true;
            }

            case CancelFabricateCommand c:
            {
                var s = FindStructure(c.StructureId);
                if (s == null || s.Owner != c.Player) return sim.Refuse(c, "not yours");
                var index = s._fabrication.FindIndex(o => o.Id == c.OrderId);
                if (index < 0) return sim.Refuse(c, "no such order");
                Refund(sim, s, s._fabrication[index]);
                s._fabrication.RemoveAt(index);
                return true;
            }

            case StockTargetCommand c:
            {
                if (!IsPlayer(c.Player)) return sim.Refuse(c, "no such player");
                if (Fab == null) return sim.Refuse(c, "no parts economy in this game");
                var r = ResourceIndexOf(c.ItemId);
                if (r < 0 || !_data.Resources[r].IsPartItem || Fab.Recipe(c.ItemId) == null) return sim.Refuse(c, "not a part, spare kit or construction part");
                if (c.Target < 0 || c.Target > GameData.MaxStockTarget) return sim.Refuse(c, $"target must be 0..{GameData.MaxStockTarget}");
                _players[c.Player]._stockTargets[r] = c.Target;
                // Fewer wanted: unstarted auto orders of it beyond the target (and what waiting repairs need) go, newest first.
                var keep = c.Target + KitDemand(c.Player)[r];
                var have = sim.Players[c.Player].Stock[r].FloorToInt() + Queued(c.Player, c.ItemId);
                for (var i = _structures.Count - 1; i >= 0 && have > keep; i--)
                {
                    var s = _structures[i];
                    if (s.Owner != c.Player) continue;
                    for (var k = s._fabrication.Count - 1; k >= 0 && have > keep; k--)
                    {
                        var o = s._fabrication[k];
                        if (o.Started || !o.Auto || o.RecipeId != c.ItemId) continue;
                        s._fabrication.RemoveAt(k);
                        have -= OutputOf(o.RecipeId);
                    }
                }
                return true;
            }

            case JuryRigCommand c:
            {
                var s = FindStructure(c.StructureId);
                if (s == null || s.Owner != c.Player) return sim.Refuse(c, "not yours");
                if (s.JuryRigOrdered)
                {
                    // Cancel, unless the crew (or drones) are already on it.
                    var core = CrewBase(s);
                    if (s.AutoRepairing || (core != null && core.CrewJob == s.Id && core.CrewPhase == CrewPhase.Working))
                        return sim.Refuse(c, "the crew are already on it");
                    s.JuryRigOrdered = false;
                    s.MaintenanceOrdered = false;
                    return true;
                }
                if (JuryRigProblem(c.Player, c.StructureId) is { } problem) return sim.Refuse(c, problem);
                s.JuryRigOrdered = true;
                s.MaintenanceWaiting = false;
                if (!s.AutoRepairing && !s.MaintenanceOrdered)
                {
                    s.MaintenanceOrdered = true;
                    s.MaintenanceOrderTick = sim.Tick;
                }
                return true;
            }

            case JuryRigUnitsCommand c:
            {
                var rigged = 0;
                foreach (var id in System.Linq.Enumerable.Distinct(c.UnitIds))
                {
                    var u = FindUnit(id);
                    if (u == null || u.Owner != c.Player || JuryRigProblem(u) != null) continue;
                    u.JuryRigOrdered = true;
                    u.RepairWaiting = false;
                    rigged++;
                }
                return rigged > 0 || sim.Refuse(c, "none of these vehicles can be jury-rigged");
            }

            default:
                return false;
        }
    }

    private void AddFabricationToHash(ref StateHasher h) => h.Add(_nextFabOrderId);
}
