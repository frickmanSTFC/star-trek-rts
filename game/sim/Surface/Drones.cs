using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

/// <summary>What a scout drone is doing (docs/missions-contract.md B). None = not a drone.</summary>
public enum DroneState
{
    None,
    /// <summary>On its pad bay, charging (Battery rises to 1).</summary>
    Docked,
    /// <summary>Lifting off its bay (launch_seconds).</summary>
    Launching,
    /// <summary>Flying to its target.</summary>
    Flying,
    /// <summary>Circling its target tile (loiter_seconds), surveying.</summary>
    Loitering,
    /// <summary>Flying home (ordered, battery low, or out of comms coverage).</summary>
    Returning,
    /// <summary>Setting down on its bay (land_seconds).</summary>
    Landing,
    /// <summary>Battery ran out: came down (LostAtTick); the unit is removed a few seconds later.</summary>
    Lost,
}

/// <summary>A drone's state for the view and UI. Target = the tile it flies to (-1 none); Pad = its Drone Pad
/// structure id; Bay = its bay on the pad (0-based); Battery 0..1; Phase 0..1 within Launching / Landing / Loitering;
/// ReturnReason "", "ordered", "battery", "coverage", "done" (for a Lost drone: why it came down, "battery" or
/// "no pad").</summary>
public readonly record struct DroneInfo(DroneState State, int Target, int Pad, int Bay, Fix64 Battery, Fix64 Phase,
    long LostAtTick, string ReturnReason);

/// <summary>A drone unit's flight state (hashed with the unit; only drones have one).</summary>
internal sealed class DroneFlight
{
    public DroneState State = DroneState.Docked;
    public int Target = -1;
    public int Pad;
    public int Bay;
    public Fix64 Battery;
    public int PhaseTicks;
    public long LostAtTick = -1;
    public string ReturnReason = "";

    /// <summary>The bay it flies back to (kept, so it still knows where home was if the pad goes).</summary>
    public FixVec2 Home;

    public int Launches;
    public int TilesSurveyed;

    public void AddToHash(ref StateHasher h)
    {
        h.Add(0xD20E);
        h.Add((int)State);
        h.Add(Target);
        h.Add(Pad);
        h.Add(Bay);
        h.Add(Battery);
        h.Add(PhaseTicks);
        h.Add(LostAtTick);
        h.Add(ReturnReason);
        h.Add(Home);
        h.Add(Launches);
        h.Add(TilesSurveyed);
    }
}

// Scout drones and Drone Pads (docs/missions-contract.md B; data surfaces/units/scout_drone.json "drone", buildings
// drone_bays, the scout_drone resource + recipe in surfaces/fabrication.json, coverage in Comms.cs).
// A finished pad unpacks a packed drone from stock into each empty bay (a docked SurfaceUnit on the pad tile, at the
// bay's spot, battery 0) and charges it while the pad works. LaunchDroneCommand sends the pad's first docked drone whose
// battery covers the trip: it lifts off (launch_seconds), flies a straight line at speed tiles a second (no lanes,
// terrain, buildings or dome transits), surveying every tile it enters (survey_radius) and seeing round it like a rover,
// loiters loiter_seconds over the target, flies home, lands (land_seconds) and recharges. It turns home by itself when the
// battery only just covers the way back plus the reserve, or when its tile drops out of comms coverage. Battery 0 = Lost
// (removed lost_seconds later). Lanes, parking, rover wear, repairs and people blockers skip drones (unit.Drone != null).
public sealed partial class PlanetSurface
{
    private static readonly Fix64 BaySpacing = Fix64.Parse("6.4");
    private static readonly Fix64 BayRing = Fix64.FromInt(4);

    /// <summary>True for a unit that flies (data: "aerial": true). Aerial units ignore lanes, terrain and dome transits.</summary>
    public bool IsAerial(SurfaceUnit unit) => unit.Drone != null || _data.IsAerialUnit(unit.DefId);

    /// <summary>The drone's state (State None for any other unit).</summary>
    public DroneInfo DroneOf(SurfaceUnit unit)
    {
        if (unit.Drone is not { } d) return new(DroneState.None, -1, 0, 0, Fix64.One, Fix64.Zero, -1, "");
        var def = _data.DroneOf(unit.DefId);
        var phase = Fix64.Zero;
        if (def != null)
        {
            var total = d.State switch
            {
                DroneState.Launching => DroneTicks(def.LaunchSeconds),
                DroneState.Landing => DroneTicks(def.LandSeconds),
                DroneState.Loitering => DroneTicks(def.LoiterSeconds),
                _ => 0,
            };
            if (total > 0) phase = Fix64.Min(Fix64.One, Fix64.FromInt(d.PhaseTicks) / Fix64.FromInt(total));
        }
        return new DroneInfo(d.State, d.Target, d.Pad, d.Bay, d.Battery, phase, d.LostAtTick, d.ReturnReason);
    }

    /// <summary>Times this drone has been launched, and tiles it surveyed first (for the UI and missions).</summary>
    public (int Launches, int TilesSurveyed) DroneRecord(SurfaceUnit unit) =>
        unit.Drone is { } d ? (d.Launches, d.TilesSurveyed) : (0, 0);

    /// <summary>The drones belonging to this pad (docked or out), in bay order.</summary>
    public IReadOnlyList<SurfaceUnit> DronesOf(int padId)
    {
        var list = new List<SurfaceUnit>();
        foreach (var u in _units)
            if (u.Drone is { } d && d.Pad == padId) list.Add(u);
        list.Sort((a, b) => a.Drone!.Bay != b.Drone!.Bay ? a.Drone.Bay.CompareTo(b.Drone.Bay) : a.Id.CompareTo(b.Id));
        return list;
    }

    /// <summary>How many bays this structure has (0 = not a drone pad).</summary>
    public int DroneBays(SurfaceStructure s) => _data.DroneBaysOf(s.DefId);

    /// <summary>Where a drone sits on bay <paramref name="bay"/> of this pad (world units): two bays side by side across
    /// the pad (±3.2 in its frame), more on a ring of 4, turned with the building's facing.</summary>
    public FixVec2 DroneBayPos(SurfaceStructure pad, int bay)
    {
        var n = System.Math.Max(1, DroneBays(pad));
        FixVec2 local;
        if (n <= 2) local = new FixVec2(BaySpacing * (Fix64.FromInt(bay) - Fix64.FromInt(n - 1) / 2), Fix64.Zero);
        else
        {
            var deg = Fix64.FromInt(360) * Fix64.FromInt(bay) / Fix64.FromInt(n);
            local = new FixVec2(Climate.CosDeg(deg), Climate.SinDeg(deg)) * BayRing;
        }
        return CentreOf(pad.Tile) + Turn(local, pad.Facing);
    }

    private static int DroneTicks(Fix64 seconds) => System.Math.Max(0, (seconds * Simulation.TickRate).FloorToInt());

    private static Fix64 DrainPerTick(DroneDef def) => Fix64.One / (def.BatterySeconds * Simulation.TickRate);

    /// <summary>World units a drone flies in one tick (speed tiles a second; nothing slows it).</summary>
    private static Fix64 FlightStep(SurfaceUnit u) => u.SpeedPerTick * TileSize;

    private static int FlightTicks(FixVec2 from, FixVec2 to, Fix64 step)
    {
        var dist = (to - from).Length();
        if (dist == Fix64.Zero) return 0;
        if (step <= Fix64.Zero) return int.MaxValue / 4;
        return (dist / step).FloorToInt() + 1;
    }

    /// <summary>The drone's pad if it still stands and is its owner's, else null.</summary>
    private SurfaceStructure? PadOf(SurfaceUnit u, DroneFlight d)
    {
        var pad = FindStructure(d.Pad);
        return pad != null && pad.Owner == u.Owner && DroneBays(pad) > 0 ? pad : null;
    }

    private FixVec2 HomeOf(SurfaceUnit u, DroneFlight d) => PadOf(u, d) is { } pad ? DroneBayPos(pad, d.Bay) : d.Home;

    /// <summary>Why this drone can't fly from <paramref name="from"/> to the tile and get home with its reserve left
    /// (<paramref name="before"/> = ticks it still spends first, e.g. lifting off), or null. Loitering isn't counted: on a
    /// long trip the battery cuts it short.</summary>
    private string? TripProblem(SurfaceUnit u, DroneFlight d, DroneDef def, FixVec2 from, int tile, int before)
    {
        if (!OnMap(tile)) return "off the map";
        if (!CommsCovered(u.Owner, tile)) return "out of comms range: put a relay satellite over it";
        var target = CentreOf(tile);
        if (!CommsCoveredLine(u.Owner, from, target)) return "the way there leaves comms coverage";
        var step = FlightStep(u);
        var ticks = (long)before + FlightTicks(from, target, step) + FlightTicks(target, HomeOf(u, d), step) + DroneTicks(def.LandSeconds);
        var need = DrainPerTick(def) * Fix64.FromInt((int)System.Math.Min(ticks, int.MaxValue / 4)) + def.Reserve;
        return need > d.Battery ? "too far: the battery can't get there and back" : null;
    }

    /// <summary>Enough charge to take off at all (reserve + the launch margin).</summary>
    private static bool Charged(DroneFlight d, DroneDef def) => d.Battery >= def.Reserve + DroneDef.LaunchMargin;

    /// <summary>The docked drone LaunchDroneCommand would send (null with the reason when there is none).</summary>
    private SurfaceUnit? LaunchCandidate(int player, int padId, int tile, out string? problem)
    {
        problem = null;
        if (!IsPlayer(player)) { problem = "no such player"; return null; }
        var pad = FindStructure(padId);
        if (pad == null || DroneBays(pad) == 0) { problem = "no such drone pad"; return null; }
        if (pad.Owner != player) { problem = "not your drone pad"; return null; }
        if (pad.State != StructureState.Complete) { problem = "the pad isn't finished"; return null; }
        if (!OnMap(tile)) { problem = "off the map"; return null; }
        if (!CommsCovered(player, tile)) { problem = "out of comms range: put a relay satellite over it"; return null; }
        string? tripProblem = null;
        var docked = 0;
        var charged = 0;
        foreach (var u in DronesOf(padId))
        {
            if (u.Drone is not { State: DroneState.Docked } d || _data.DroneOf(u.DefId) is not { } def) continue;
            docked++;
            if (!Charged(d, def)) continue;
            charged++;
            var trip = TripProblem(u, d, def, DroneBayPos(pad, d.Bay), tile, DroneTicks(def.LaunchSeconds));
            if (trip == null) return u;
            tripProblem ??= trip;
        }
        problem = docked == 0 ? "no drone on the pad (make scout drones at a Machine Shop)"
            : charged == 0 ? "no drone charged enough yet" : tripProblem;
        return null;
    }

    /// <summary>Why LaunchDroneCommand(player, padId, tile) would be refused now, or null.</summary>
    public string? LaunchProblem(Simulation sim, int player, int padId, int tile)
    {
        LaunchCandidate(player, padId, tile, out var problem);
        return problem;
    }

    /// <summary>Why MoveUnitsCommand can't send this drone to the tile now (retarget a flying one, or launch a docked
    /// one), or null.</summary>
    public string? DroneMoveProblem(int player, SurfaceUnit unit, int tile)
    {
        if (unit.Drone is not { } d || _data.DroneOf(unit.DefId) is not { } def) return "not a drone";
        if (unit.Owner != player) return "not yours";
        switch (d.State)
        {
            case DroneState.Docked:
            {
                if (PadOf(unit, d) is not { } pad || pad.State != StructureState.Complete) return "its pad isn't working";
                if (!Charged(d, def)) return "not charged enough yet";
                return TripProblem(unit, d, def, DroneBayPos(pad, d.Bay), tile, DroneTicks(def.LaunchSeconds));
            }
            case DroneState.Launching:
                return TripProblem(unit, d, def, unit.Pos, tile, System.Math.Max(0, DroneTicks(def.LaunchSeconds) - d.PhaseTicks));
            case DroneState.Flying or DroneState.Loitering or DroneState.Returning:
                return TripProblem(unit, d, def, unit.Pos, tile, 0);
            case DroneState.Landing:
                return "landing";
            default:
                return "lost";
        }
    }

    private static void Launch(DroneFlight d, int tile, FixVec2 home)
    {
        d.State = DroneState.Launching;
        d.PhaseTicks = 0;
        d.Target = tile;
        d.Home = home;
        d.ReturnReason = "";
        d.Launches++;
    }

    private bool ApplyLaunchDrone(Simulation sim, LaunchDroneCommand command)
    {
        var drone = LaunchCandidate(command.Player, command.PadId, command.Tile, out var problem);
        if (drone == null) return sim.Refuse(command, problem ?? "can't launch");
        var d = drone.Drone!;
        Launch(d, command.Tile, DroneBayPos(FindStructure(command.PadId)!, d.Bay));
        Achieve(command.Player, "drone_launched");
        return true;
    }

    /// <summary>The drones among a MoveUnitsCommand's units: each flies to the tile itself (no group spots); docked ones
    /// launch. Returns how many went; problem = the first drone's reason when one couldn't.</summary>
    private int MoveDrones(MoveUnitsCommand move, out string? problem)
    {
        problem = null;
        var sent = 0;
        var seen = new List<int>();
        foreach (var id in move.UnitIds)
        {
            if (seen.Contains(id)) continue;
            seen.Add(id);
            var u = FindUnit(id);
            if (u?.Drone is not { } d || u.Owner != move.Player) continue;
            if (DroneMoveProblem(move.Player, u, move.Tile) is { } p)
            {
                problem ??= p;
                continue;
            }
            sent++;
            if (d.State == DroneState.Docked)
            {
                Launch(d, move.Tile, DroneBayPos(PadOf(u, d)!, d.Bay));
                Achieve(u.Owner, "drone_launched");
                continue;
            }
            d.Target = move.Tile;
            d.ReturnReason = "";
            if (d.State != DroneState.Launching)
            {
                d.State = DroneState.Flying;
                d.PhaseTicks = 0;
            }
        }
        return sent;
    }

    private bool ApplyRecallDrones(Simulation sim, RecallDroneCommand command)
    {
        var recalled = 0;
        var seen = new List<int>();
        foreach (var id in command.UnitIds)
        {
            if (seen.Contains(id)) continue;
            seen.Add(id);
            var u = FindUnit(id);
            if (u?.Drone is not { } d || u.Owner != command.Player) continue;
            if (d.State is not (DroneState.Launching or DroneState.Flying or DroneState.Loitering)) continue;
            Return(d, "ordered");
            recalled++;
        }
        return recalled > 0 || sim.Refuse(command, "none of these drones are out");
    }

    private static void Return(DroneFlight d, string reason)
    {
        d.State = DroneState.Returning;
        d.ReturnReason = reason;
        d.PhaseTicks = 0;
    }

    private void Lose(Simulation sim, SurfaceUnit u, DroneFlight d, string reason)
    {
        d.State = DroneState.Lost;
        d.LostAtTick = sim.Tick;
        d.ReturnReason = reason;
        d.PhaseTicks = 0;
        Achieve(u.Owner, "drone_lost", reason);
    }

    // ---------------------------------------------------------------- ticking

    /// <summary>Each tick after the ground vehicles have moved: pads unpack drones into empty bays, then every drone
    /// charges, flies, loiters, lands or is lost.</summary>
    private void DronesStep(Simulation sim)
    {
        if (_data.Drones.Count == 0) return;
        LoadPads(sim);
        List<SurfaceUnit>? gone = null;
        foreach (var u in _units)
            if (u.Drone is { } d && StepDrone(sim, u, d)) (gone ??= new List<SurfaceUnit>()).Add(u);
        if (gone != null)
            foreach (var u in gone) _units.Remove(u);
    }

    /// <summary>Every finished pad takes a packed drone (the first drone type in stock) from stock into each empty bay.</summary>
    private void LoadPads(Simulation sim)
    {
        List<SurfaceStructure>? pads = null;
        foreach (var s in _structures)
            if (s.State == StructureState.Complete && DroneBays(s) > 0) (pads ??= new List<SurfaceStructure>()).Add(s);
        if (pads == null) return;
        foreach (var pad in pads)
        {
            var bays = DroneBays(pad);
            for (var bay = 0; bay < bays; bay++)
            {
                if (BayHeld(pad.Id, bay, null)) continue;
                var stock = sim.Players[pad.Owner].Stock;
                DroneDef? def = null;
                foreach (var dd in _data.Drones)
                    if (_data.HasResource(dd.UnitId) && stock[_data.Resource(dd.UnitId).Index] >= Fix64.One)
                    {
                        def = dd;
                        break;
                    }
                if (def == null) break; // nothing in stock for this pad's owner
                stock.Add(_data.Resource(def.UnitId).Index, -Fix64.One);
                var unitDef = _data.SurfaceUnit(def.UnitId);
                var u = NewUnit(pad.Owner, unitDef, pad.Tile, sim.Tick, pad.Tile);
                var home = DroneBayPos(pad, bay);
                u.Pos = u.PrevPos = home;
                u.Drone = new DroneFlight { Pad = pad.Id, Bay = bay, Home = home, Battery = Fix64.Zero };
            }
        }
    }

    /// <summary>A drone (other than <paramref name="except"/>) holds this bay: docked there or out from it.</summary>
    private bool BayHeld(int padId, int bay, SurfaceUnit? except)
    {
        foreach (var u in _units)
            if (u != except && u.Drone is { } d && d.Pad == padId && d.Bay == bay) return true;
        return false;
    }

    /// <summary>Another finished pad of the owner's with a free bay, nearest the drone (lowest id on a tie); the drone
    /// is assigned to it. False if there is none.</summary>
    private bool FindOtherPad(SurfaceUnit u, DroneFlight d)
    {
        SurfaceStructure? best = null;
        var bestBay = -1;
        var bestDist = Fix64.MaxValue;
        foreach (var s in _structures)
        {
            if (s.Owner != u.Owner || s.State != StructureState.Complete || s.Id == d.Pad) continue;
            var bays = DroneBays(s);
            for (var bay = 0; bay < bays; bay++)
            {
                if (BayHeld(s.Id, bay, u)) continue;
                var dist = (CentreOf(s.Tile) - u.Pos).Length();
                if (dist < bestDist)
                {
                    best = s;
                    bestBay = bay;
                    bestDist = dist;
                }
                break;
            }
        }
        if (best == null) return false;
        d.Pad = best.Id;
        d.Bay = bestBay;
        d.Home = DroneBayPos(best, bestBay);
        return true;
    }

    /// <summary>One drone's tick; true = remove the unit (lost and gone, or back in stock).</summary>
    private bool StepDrone(Simulation sim, SurfaceUnit u, DroneFlight d)
    {
        if (_data.DroneOf(u.DefId) is not { } def) return false;
        var pad = PadOf(u, d);
        switch (d.State)
        {
            case DroneState.Docked:
            {
                if (pad == null) // the pad was salvaged or removed: the drone goes back in stock
                {
                    if (_data.HasResource(u.DefId)) sim.Players[u.Owner].Stock.Add(_data.Resource(u.DefId).Index, Fix64.One);
                    return true;
                }
                u.Pos = DroneBayPos(pad, d.Bay);
                var rate = Fix64.One / (def.RechargeSeconds * Simulation.TickRate);
                if (pad.Status == StructureStatus.LowPower) rate *= PowerOf(pad.BaseId).Efficiency;
                else if (pad.Status != StructureStatus.Working) rate = Fix64.Zero;
                d.Battery = Fix64.Min(Fix64.One, d.Battery + rate);
                return false;
            }
            case DroneState.Lost:
                return sim.Tick - d.LostAtTick >= DroneTicks(def.LostSeconds);
            case DroneState.None:
                return false;
        }

        var drain = DrainPerTick(def);
        d.Battery = Fix64.Max(Fix64.Zero, d.Battery - drain);
        if (d.Battery <= Fix64.Zero && d.State != DroneState.Landing)
        {
            Lose(sim, u, d, "battery");
            return false;
        }
        switch (d.State)
        {
            case DroneState.Launching:
                if (++d.PhaseTicks >= DroneTicks(def.LaunchSeconds))
                {
                    d.State = DroneState.Flying;
                    d.PhaseTicks = 0;
                }
                break;
            case DroneState.Flying:
                if (MustTurnHome(u, d, def)) break;
                if (Fly(u, d, CentreOf(d.Target)))
                {
                    d.State = DroneState.Loitering;
                    d.PhaseTicks = 0;
                }
                break;
            case DroneState.Loitering:
                if (MustTurnHome(u, d, def)) break;
                if (++d.PhaseTicks >= DroneTicks(def.LoiterSeconds)) Return(d, "done");
                break;
            case DroneState.Returning:
                if (pad != null) d.Home = DroneBayPos(pad, d.Bay);
                if (!Fly(u, d, d.Home)) break;
                if (pad == null && !FindOtherPad(u, d))
                {
                    Lose(sim, u, d, "no pad"); // home to nothing
                    break;
                }
                if (PadOf(u, d) is { } landOn && (DroneBayPos(landOn, d.Bay) - u.Pos).Length() > Fix64.Zero) break; // fly on to the other pad
                d.State = DroneState.Landing;
                d.PhaseTicks = 0;
                break;
            case DroneState.Landing:
                if (pad == null)
                {
                    if (FindOtherPad(u, d)) Return(d, d.ReturnReason);
                    else Lose(sim, u, d, "no pad");
                    break;
                }
                if (++d.PhaseTicks >= DroneTicks(def.LandSeconds))
                {
                    d.State = DroneState.Docked;
                    d.PhaseTicks = 0;
                    d.Target = -1;
                    u.Pos = DroneBayPos(pad, d.Bay);
                }
                break;
        }
        return false;
    }

    /// <summary>Out of coverage, or only just enough battery left for the way home and the landing plus the reserve:
    /// it turns home (true).</summary>
    private bool MustTurnHome(SurfaceUnit u, DroneFlight d, DroneDef def)
    {
        if (!CommsCovered(u.Owner, u.Tile))
        {
            Return(d, "coverage");
            return true;
        }
        var need = DrainPerTick(def) * Fix64.FromInt(FlightTicks(u.Pos, HomeOf(u, d), FlightStep(u)) + DroneTicks(def.LandSeconds)) + def.Reserve;
        if (d.Battery > need) return false;
        Return(d, "battery");
        return true;
    }

    /// <summary>Flies one tick straight toward the point; true once there. Every tile it enters is surveyed round it
    /// (survey_radius, less in bad weather) and seen like a rover.</summary>
    private bool Fly(SurfaceUnit u, DroneFlight d, FixVec2 to)
    {
        var v = to - u.Pos;
        var dist = v.Length();
        var step = FlightStep(u);
        var there = dist <= step;
        u.Pos = there ? to : u.Pos + v * (step / dist);
        var tile = TileAtPos(u.Pos);
        if (tile != u.Tile)
        {
            u.Tile = tile;
            var radius = SurveyRadiusOf(u);
            var surveyed = _players[u.Owner].Surveyed;
            foreach (var t in Map.Grid.Within(tile, radius))
                if (!surveyed[t])
                {
                    Survey(u.Owner, t);
                    d.TilesSurveyed++;
                }
            MarkSightAt(u, radius); // Visibility.cs
        }
        return there;
    }
}
