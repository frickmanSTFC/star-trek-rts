using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Landing fields and drop ships on the ground (surfaces "landing_field", owner 2026-11-17: "the landers do not return
// to the ship, but land in an area near the outpost to fly back up after re-fueling").
//
// The field: every colony core gets one when it is placed (PlaceStructure → AssignField): up to `pads` hexes in one
// cluster `distance` hexes out, kept on the core (SurfaceStructure.Field, pad 0 first) for as long as the core stands.
// Nothing is built on a pad, crawlers don't unfurl there, no drop ship is ordered down onto one, and the lanes treat
// its middle like a building (BlockRadius), so vehicles drive round it and park toward the corners.
//
// The ships: a DropShip is made for everything that comes down in one: an orbit delivery (when it arrives), each ship of
// the first-landing convoy (at touchdown: one per vehicle that lands with the colony, at least ConvoyShips) and each ship
// of an outpost drop (when its core lands). From then on, one state at a time:
//   Unloading      until UntilTick (the view plays the ramp and the drive-off, or the module going down on its cables)
//   WaitingForPad  a landed ship whose owner's fields are all taken: it stays where it unloaded
//   Hopping        to its pad (reserved from now on), until UntilTick
//   WaitingForFuel on the pad: a delivery's ship pays the trip's fuel from the colony's stock; short of it, it waits
//   Refuelling     on the pad, until UntilTick
//   Climbing       back to the ship, until UntilTick (the pad is free again from lift-off); then it is gone
// A ship that never landed (it only lowered a module) and finds no free pad, and any ship whose owner has no field at
// all, climbs straight back without refuelling. The player's delivery drop ship counts as away the whole time.
// Everything here steps from the tick and the state alone (no dice), is hashed once the first ship exists, and changes
// only through the existing commands (ShuttleCommand, LandColonyCommand, OutpostDropCommand).

/// <summary>What brought a drop ship down.</summary>
public enum DropShipRole
{
    /// <summary>The ship's delivery drop ship (the orbit "shuttle"): it brought a vehicle down.</summary>
    Delivery,

    /// <summary>One of the first landing's ships (an outpost module and a vehicle, or a vehicle alone).</summary>
    Convoy,

    /// <summary>One of the three ships that set an outpost drop's modules down.</summary>
    OutpostDrop,
}

/// <summary>Where a drop ship on the surface is in its stay (see Surface/LandingField.cs).</summary>
public enum DropShipState
{
    Unloading,
    WaitingForPad,
    Hopping,
    WaitingForFuel,
    Refuelling,
    Climbing,
}

/// <summary>A drop ship from its arrival on the surface until it is back aboard the ship in orbit.</summary>
public sealed class DropShip
{
    public int Id { get; }
    public int Owner { get; }
    public DropShipRole Role { get; }

    /// <summary>The delivery that brought it (a delivery's ship, an outpost drop's ships); 0 = the first landing.</summary>
    public int DeliveryId { get; }

    /// <summary>Which ship of its convoy it is (0 for a delivery's ship).</summary>
    public int Slot { get; }

    /// <summary>The vehicle it carried (its id), 0 = none (it only brought an outpost module).</summary>
    public int UnitId { get; }

    /// <summary>Where it unloads: its vehicle's tile, or the new core's tile for a ship with a module only.</summary>
    public int Tile { get; }

    public DropShipState State { get; internal set; }

    /// <summary>The tick its current state began.</summary>
    public long StateTick { get; internal set; }

    /// <summary>The tick a timed state (Unloading, Hopping, Refuelling, Climbing) ends; 0 while it waits.</summary>
    public long UntilTick { get; internal set; }

    /// <summary>The colony core whose field it uses (0 = none).</summary>
    public int CoreId { get; internal set; }

    /// <summary>Its pad's tile (-1 = none): set when it starts its hop, kept while it climbs (the pad itself is free
    /// again from lift-off: <see cref="HoldsPad"/>).</summary>
    public int Pad { get; internal set; } = -1;

    /// <summary>It set down to unload (it carried a vehicle); false = it only hovered to lower a module.</summary>
    public bool Landed => Role == DropShipRole.Delivery || UnitId != 0;

    /// <summary>Its pad is taken by it: from the start of its hop until it lifts off.</summary>
    public bool HoldsPad => Pad >= 0 && State is DropShipState.Hopping or DropShipState.WaitingForFuel or DropShipState.Refuelling;

    /// <summary>Standing on its pad (after the hop, before lift-off).</summary>
    public bool OnPad => Pad >= 0 && State is DropShipState.WaitingForFuel or DropShipState.Refuelling;

    internal DropShip(int id, int owner, DropShipRole role, int deliveryId, int slot, int unitId, int tile, long now, long until)
    {
        Id = id;
        Owner = owner;
        Role = role;
        DeliveryId = deliveryId;
        Slot = slot;
        UnitId = unitId;
        Tile = tile;
        State = DropShipState.Unloading;
        StateTick = now;
        UntilTick = until;
    }

    internal void AddToHash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Owner);
        h.Add((int)Role);
        h.Add(DeliveryId);
        h.Add(Slot);
        h.Add(UnitId);
        h.Add(Tile);
        h.Add((int)State);
        h.Add(StateTick);
        h.Add(UntilTick);
        h.Add(CoreId);
        h.Add(Pad);
    }
}

public sealed partial class PlanetSurface
{
    /// <summary>The ships that bring an outpost down: one per module of the Level 1 outpost (the view's three).</summary>
    public const int ConvoyShips = 3;

    /// <summary>What players are told about a pad tile (placement, drops, deliveries).</summary>
    public const string LandingFieldProblem = "that's a landing pad (drop ships refuel there)";

    /// <summary>The start of every "the drop ship is busy" refusal; where it is follows (<see cref="ShuttleWhere"/>).</summary>
    public const string ShuttleAwayProblem = "the drop ship is still away";

    /// <summary>Drop ships on the surface or climbing back, oldest first.</summary>
    public IReadOnlyList<DropShip> DropShips => _dropShips;

    private readonly List<DropShip> _dropShips = new();
    private int _nextDropShipId = 1;

    /// <summary>Per tile: the core whose field has a pad there (0 = none). Follows from the cores' Field lists (kept in
    /// step by AssignField / ForgetField; not hashed, the fuzz test checks they agree).</summary>
    private int[]? _padCore;

    private LandingFieldRules? FieldRules => _data.LandingField;

    // ---------------------------------------------------------------- queries

    /// <summary>The id of the colony core whose landing field has a pad on this tile (0 = not a pad).</summary>
    public int PadCoreAt(int tile) => _padCore != null && tile >= 0 && tile < _padCore.Length ? _padCore[tile] : 0;

    /// <summary>True for a tile of a landing field.</summary>
    public bool IsLandingPad(int tile) => PadCoreAt(tile) != 0;

    /// <summary>The drop ship that has this pad (hopping to it or standing on it), or null.</summary>
    public DropShip? ShipOnPad(int tile)
    {
        foreach (var s in _dropShips)
            if (s.HoldsPad && s.Pad == tile) return s;
        return null;
    }

    /// <summary>The drop ship that brought this unit down and hasn't left yet, or null.</summary>
    public DropShip? DropShipCarrying(int unitId)
    {
        if (unitId == 0) return null;
        foreach (var s in _dropShips)
            if (s.UnitId == unitId) return s;
        return null;
    }

    /// <summary>A drop ship by what brought it (the first landing: deliveryId 0), or null once it is back aboard.</summary>
    public DropShip? FindDropShip(int owner, DropShipRole role, int deliveryId, int slot)
    {
        foreach (var s in _dropShips)
            if (s.Owner == owner && s.Role == role && s.DeliveryId == deliveryId && s.Slot == slot) return s;
        return null;
    }

    /// <summary>The player's delivery drop ship while it is on the surface or climbing back (null: aboard, or still on
    /// its way down with a delivery).</summary>
    public DropShip? ShuttleShip(int player)
    {
        foreach (var s in _dropShips)
            if (s.Owner == player && s.Role == DropShipRole.Delivery) return s;
        return null;
    }

    /// <summary>Whether the player's delivery drop ship is away at this tick (on its way down, on the surface, or
    /// climbing back).</summary>
    public bool ShuttleAway(int player, long tick)
    {
        if (!IsPlayer(player)) return false;
        if (FieldRules == null) return _players[player].ShuttleBackTick > tick; // old data: down and straight back up
        foreach (var d in _deliveries)
            if (d.Owner == player && d.Kind == DeliveryKind.Shuttle) return true;
        return ShuttleShip(player) != null;
    }

    /// <summary>Where the player's delivery drop ship is while it is away ("refuelling at the landing field · 0:32"), or
    /// null when it is aboard.</summary>
    public string? ShuttleWhere(int player, long tick)
    {
        if (!IsPlayer(player)) return null;
        foreach (var d in _deliveries)
            if (d.Owner == player && d.Kind == DeliveryKind.Shuttle) return $"on its way down · {Clock(d.ArriveTick - tick)}";
        if (ShuttleShip(player) is { } ship) return DropShipStatus(ship, tick);
        return _players[player].ShuttleBackTick > tick ? $"back in {Clock(_players[player].ShuttleBackTick - tick)}" : null;
    }

    /// <summary>What a drop ship is doing, for the HUD ("refuelling at the landing field · 0:32").</summary>
    public string DropShipStatus(DropShip ship, long tick)
    {
        var left = Clock(ship.UntilTick - tick);
        switch (ship.State)
        {
            case DropShipState.Unloading: return ship.Landed ? "unloading" : "setting its module down";
            case DropShipState.WaitingForPad: return "waiting for a free pad at the landing field";
            case DropShipState.Hopping: return $"hopping to the landing field · {left}";
            case DropShipState.WaitingForFuel: return $"on its pad, waiting for fuel ({FuelText(ShuttleFuel)})";
            case DropShipState.Refuelling: return $"refuelling at the landing field · {left}";
            default: return $"climbing back to the ship · {left}";
        }
    }

    private string FuelText(IReadOnlyList<ResourceAmount> fuel)
    {
        var text = "";
        foreach (var f in fuel)
            text += (text.Length > 0 ? ", " : "") + $"{(f.Amount + Fix64.One - Fix64.FromRaw(1)).FloorToInt()} {_data.Resources[f.Resource].Name.ToLowerInvariant()}";
        return text.Length > 0 ? "needs " + text : "none needed";
    }

    /// <summary>Ticks as m:ss, rounded up to the second (0:00 for none left).</summary>
    public static string Clock(long ticks)
    {
        var s = System.Math.Max(0, (ticks + Simulation.TickRate - 1) / Simulation.TickRate);
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>How long a drop ship refuels on its pad on this surface, in ticks (refuel_seconds × the lift-cost curve,
    /// × the player's turnaround bonus).</summary>
    public long RefuelTicks(int player) =>
        FieldRules is { } f ? Quicker(GravityProfile.Ticks(FieldTicks(f.RefuelSeconds), Gravity.LiftCost), player, 0) : 0;

    /// <summary>How long a drop ship takes to climb back to the ship, in ticks (the flight down, × the player's
    /// turnaround bonus).</summary>
    public long ClimbTicks(int player) => Quicker(DescentTicks(_data.Orbit?.ShuttleSeconds ?? 0), player, 1);

    /// <summary>The hop from where a ship unloaded to a pad this many hexes away, in ticks.</summary>
    public long HopTicks(int hexes) =>
        FieldRules is { } f ? System.Math.Max(1, FieldTicks(Fix64.Min(f.HopMaxSeconds, f.HopSeconds + f.HopSecondsPerTile * hexes))) : 0;

    /// <summary>How long after its delivery lands the player's delivery drop ship is back aboard when nothing holds it
    /// up (unloading, a hop of <paramref name="hexes"/>, refuelling, the climb), in ticks.</summary>
    public long GroundStayTicks(int player, int hexes = 0) =>
        FieldRules is { } f ? FieldTicks(f.UnloadSeconds) + HopTicks(hexes) + RefuelTicks(player) + ClimbTicks(player) : ClimbTicks(player);

    private static long FieldTicks(Fix64 seconds) => System.Math.Max(0, (seconds * Simulation.TickRate).FloorToInt());

    /// <summary>A duration × the player's drop-ship turnaround bonus (an Iconian gateway fragment; Excavation.cs).</summary>
    private long Quicker(long ticks, int player, long atLeast)
    {
        if (_shuttleFactor.Count == 0 || ticks <= 0) return ticks;
        return System.Math.Max(atLeast, (Fix64.FromInt((int)System.Math.Min(int.MaxValue, ticks)) * ShuttleTurnaround(player)).FloorToInt());
    }

    // ---------------------------------------------------------------- the field

    /// <summary>Ground a pad can be marked out on.</summary>
    private bool PadGround(int tile)
    {
        var terrain = Map.Terrain(tile);
        if (!terrain.Passable || !terrain.Buildable) return false;
        if (_structureAt[tile] != 0 || PadCoreAt(tile) != 0) return false;
        if (Map.DepositResource[tile] >= 0 || Map.Toxic[tile] || Map.NearNatives(tile) || RuinBlocks(tile)) return false;
        if (PodLandingNear(tile)) return false;
        foreach (var d in _deliveries)
            if (d.Kind == DeliveryKind.Outpost && Map.Grid.Distance(d.Tile, tile) <= 1) return false;
        return true;
    }

    /// <summary>Marks out a new core's landing field: of every cluster grown from a tile `distance` out over its
    /// neighbours at distance or distance + 1, the one with the most pads, then the most on landing terrain, then the
    /// flattest, then the lowest first tile.</summary>
    private void AssignField(SurfaceStructure core)
    {
        if (FieldRules is not { Pads: > 0 } rules) return;
        var g = Map.Grid;
        var open = new List<int>();
        foreach (var t in g.Within(core.Tile, rules.Distance + 1))
            if (g.Distance(core.Tile, t) >= rules.Distance && PadGround(t)) open.Add(t);
        int[]? best = null;
        var bestLanding = -1;
        var bestSpread = Fix64.MaxValue;
        var cluster = new List<int>();
        foreach (var anchor in open)
        {
            if (g.Distance(core.Tile, anchor) != rules.Distance) continue;
            cluster.Clear();
            cluster.Add(anchor);
            for (var i = 0; i < cluster.Count && cluster.Count < rules.Pads; i++)
                foreach (var n in g.Neighbours(cluster[i]))
                {
                    if (cluster.Count >= rules.Pads) break;
                    if (open.Contains(n) && !cluster.Contains(n)) cluster.Add(n);
                }
            var landing = 0;
            Fix64 lo = Fix64.MaxValue, hi = Fix64.MinValue;
            foreach (var t in cluster)
            {
                if (_data.Landing.Terrain.Contains(Map.Terrain(t).Id)) landing++;
                lo = Fix64.Min(lo, Map.Elevation[t]);
                hi = Fix64.Max(hi, Map.Elevation[t]);
            }
            var spread = hi - lo;
            var better = best == null || cluster.Count > best.Length
                || (cluster.Count == best.Length && (landing > bestLanding || (landing == bestLanding && spread < bestSpread)));
            if (!better) continue;
            best = cluster.ToArray();
            bestLanding = landing;
            bestSpread = spread;
        }
        if (best == null) return;
        core._field = best;
        _padCore ??= new int[g.Count];
        foreach (var t in best)
        {
            _padCore[t] = core.Id;
            MakeRoom(t, null, rules.FootprintRadius + Lanes.Clearance + Fix64.Half); // parked vehicles drive off the pad
        }
    }

    /// <summary>A structure is going: if it is a core, its field goes with it (ships on its pads carry on from there).</summary>
    private void ForgetField(SurfaceStructure s)
    {
        if (_padCore == null || s._field.Length == 0) return;
        foreach (var t in s._field)
            if (_padCore[t] == s.Id) _padCore[t] = 0;
    }

    /// <summary>A pad a ship could take now: nothing built on it, no rig unfolded on it, no other ship has it.</summary>
    private bool PadFree(int tile)
    {
        if (_structureAt[tile] != 0 || ShipOnPad(tile) != null) return false;
        foreach (var u in _units)
            if (u.Tile == tile && u.Mode != UnitMode.Mobile) return false;
        return true;
    }

    /// <summary>The free pad for a ship at <paramref name="from"/>: of its owner's cores with a field, nearest first
    /// (then oldest), the first free pad. <paramref name="anyField"/> = the owner has a pad at all (taken or not).</summary>
    private bool FindPad(int owner, int from, out int coreId, out int pad, out bool anyField)
    {
        coreId = 0;
        pad = -1;
        anyField = false;
        var bestDist = int.MaxValue;
        foreach (var core in _structures)
        {
            if (core.Owner != owner || core._field.Length == 0) continue;
            var free = -1;
            foreach (var t in core._field)
            {
                if (_structureAt[t] == 0) anyField = true;
                if (free < 0 && PadFree(t)) free = t;
            }
            if (free < 0) continue;
            var d = Map.Grid.Distance(core.Tile, from);
            if (d >= bestDist) continue;
            bestDist = d;
            coreId = core.Id;
            pad = free;
        }
        return pad >= 0;
    }

    // ---------------------------------------------------------------- the ships

    private void AddDropShip(int owner, DropShipRole role, int deliveryId, int slot, int unitId, int tile, long now, long until)
    {
        if (FieldRules == null) return; // old data: the view flies it straight back up, as before
        _dropShips.Add(new DropShip(_nextDropShipId++, owner, role, deliveryId, slot, unitId, tile, now, System.Math.Max(now, until)));
    }

    /// <summary>The first landing's ships (Touchdown): one per vehicle that landed, in landing order, and at least
    /// <see cref="ConvoyShips"/> (the outpost's modules).</summary>
    private void AddLandingConvoy(Simulation sim, int player, long touchTick, int hubTile, List<SurfaceUnit> landed)
    {
        if (FieldRules is not { } f) return;
        var n = System.Math.Max(ConvoyShips, landed.Count);
        for (var i = 0; i < n; i++)
        {
            var unit = i < landed.Count ? landed[i] : null;
            var ready = (unit != null ? f.ConvoyUnloadSeconds : f.ModuleSeconds) + f.StaggerSeconds * i;
            AddDropShip(player, DropShipRole.Convoy, 0, i, unit?.Id ?? 0, unit?.Tile ?? hubTile, sim.Tick, touchTick + FieldTicks(ready));
        }
    }

    /// <summary>An outpost drop's three ships, once its core is on the ground.</summary>
    private void AddOutpostConvoy(Simulation sim, Delivery d)
    {
        if (FieldRules is not { } f) return;
        for (var i = 0; i < ConvoyShips; i++)
            AddDropShip(d.Owner, DropShipRole.OutpostDrop, d.Id, i, 0, d.Tile, sim.Tick, d.ArriveTick + FieldTicks(f.ModuleSeconds + f.StaggerSeconds * i));
    }

    private void DropShipsStep(Simulation sim)
    {
        if (_dropShips.Count == 0) return;
        for (var i = 0; i < _dropShips.Count;)
        {
            var ship = _dropShips[i];
            if (!StepDropShip(sim, ship))
            {
                i++;
                continue;
            }
            _dropShips.RemoveAt(i); // back aboard
            if (ship.Role == DropShipRole.Delivery && IsPlayer(ship.Owner)) _players[ship.Owner].ShuttleBackTick = sim.Tick;
        }
        // The delivery drop ship's "back by" estimate follows where it has got to (it only grows while it waits).
        foreach (var ship in _dropShips)
            if (ship.Role == DropShipRole.Delivery && IsPlayer(ship.Owner))
                _players[ship.Owner].ShuttleBackTick = sim.Tick + System.Math.Max(1, RemainingTicks(ship, sim.Tick));
    }

    /// <summary>Moves a ship on through its states; true = it is back aboard.</summary>
    private bool StepDropShip(Simulation sim, DropShip ship)
    {
        var now = sim.Tick;
        switch (ship.State)
        {
            case DropShipState.Unloading:
                if (now >= ship.UntilTick) LeaveForField(sim, ship);
                break;
            case DropShipState.WaitingForPad:
                LeaveForField(sim, ship);
                break;
            case DropShipState.Hopping:
                if (now >= ship.UntilTick) Refuel(sim, ship);
                break;
            case DropShipState.WaitingForFuel:
                Refuel(sim, ship);
                break;
            case DropShipState.Refuelling:
                if (now >= ship.UntilTick) Climb(sim, ship);
                break;
            case DropShipState.Climbing:
                return now >= ship.UntilTick;
        }
        return false;
    }

    private static void Enter(DropShip ship, DropShipState state, long now, long ticks)
    {
        if (ship.State != state) ship.StateTick = now;
        ship.State = state;
        ship.UntilTick = ticks > 0 ? now + ticks : 0;
    }

    /// <summary>Unloaded: hop to a free pad; with every pad taken a landed ship waits where it is; with no field, or
    /// still hovering over a module, it climbs straight back.</summary>
    private void LeaveForField(Simulation sim, DropShip ship)
    {
        if (FindPad(ship.Owner, ship.Tile, out var core, out var pad, out var anyField))
        {
            ship.CoreId = core;
            ship.Pad = pad;
            Enter(ship, DropShipState.Hopping, sim.Tick, HopTicks(Map.Grid.Distance(ship.Tile, pad)));
            return;
        }
        if (anyField && ship.Landed)
        {
            Enter(ship, DropShipState.WaitingForPad, sim.Tick, 0);
            return;
        }
        // No field to go to: back up on what is left in its tanks (a delivery's trip fuel is still paid if the colony has it).
        if (ship.Role == DropShipRole.Delivery && sim.Players[ship.Owner].Stock.CanAfford(ShuttleFuel)) sim.Players[ship.Owner].Stock.TrySpend(ShuttleFuel);
        Climb(sim, ship);
    }

    /// <summary>On the pad: a delivery's ship takes the trip's fuel from the colony's stock (and waits while it is
    /// short); convoy and outpost-drop ships brought their own.</summary>
    private void Refuel(Simulation sim, DropShip ship)
    {
        if (ship.Role == DropShipRole.Delivery && ShuttleFuel.Count > 0)
        {
            var stock = sim.Players[ship.Owner].Stock;
            if (!stock.CanAfford(ShuttleFuel))
            {
                Enter(ship, DropShipState.WaitingForFuel, sim.Tick, 0);
                return;
            }
            stock.TrySpend(ShuttleFuel);
        }
        var ticks = RefuelTicks(ship.Owner);
        if (ticks <= 0) Climb(sim, ship);
        else Enter(ship, DropShipState.Refuelling, sim.Tick, ticks);
    }

    private void Climb(Simulation sim, DropShip ship) =>
        Enter(ship, DropShipState.Climbing, sim.Tick, System.Math.Max(1, ClimbTicks(ship.Owner)));

    /// <summary>Ticks until the ship is back aboard if nothing (more) holds it up.</summary>
    private long RemainingTicks(DropShip ship, long now)
    {
        var left = System.Math.Max(0, ship.UntilTick - now);
        var refuel = RefuelTicks(ship.Owner);
        var climb = System.Math.Max(1, ClimbTicks(ship.Owner));
        return ship.State switch
        {
            DropShipState.Unloading => left + HopTicks(0) + refuel + climb,
            DropShipState.WaitingForPad => HopTicks(0) + refuel + climb,
            DropShipState.Hopping => left + refuel + climb,
            DropShipState.WaitingForFuel => refuel + climb,
            DropShipState.Refuelling => left + climb,
            _ => left,
        };
    }

    private void AddLandingFieldToHash(ref StateHasher h)
    {
        if (_nextDropShipId == 1) return; // no ship yet: games before their first landing hash as they always did
        h.Add("drop_ships");
        h.Add(_nextDropShipId);
        h.Add(_dropShips.Count);
        foreach (var s in _dropShips) s.AddToHash(ref h);
    }
}
