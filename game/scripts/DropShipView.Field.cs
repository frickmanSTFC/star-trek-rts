using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The drop ships' stay on the ground (owner 2026-11-17: "the landers do not return to the ship, but land in an area near
// the outpost to fly back up after re-fueling"). The sim decides (sim/Surface/LandingField.cs: a DropShip per ship, its
// state Unloading → [WaitingForPad] → Hopping → [WaitingForFuel] → Refuelling → Climbing); this draws it:
//   - Track: once a frame, what the ship's sim drop ship says: its pad (then the hop is planned from the sim's ticks),
//     its climb (from the sim's tick). A ship the sim has nothing for goes the old way (Ship.Legacy).
//   - FieldFlight: the ship's pose and engines from that plan: waiting where it unloaded (engines off), lighting up and
//     hopping to its pad (HopFlight: the convoy hop), standing on the pad, lighting up and climbing out (RocketClimb).
//     A module ship (it never landed) hops from its hover instead, and climbs from it when there is no pad.
//   - FieldEvents: billows, bursts, scorch marks and the lift-off / touchdown sounds at those moments.
//   - the fuel hose from the pad's bowser (LandingFieldView) to the ship's flank while it refuels or waits for fuel.
public partial class DropShipView
{
    /// <summary>The pads' frames and bowsers (set by PlanetView; null = ships stand on the tile centre, no hose).</summary>
    public LandingFieldView? Field { get; set; }

    private const float HopMinSeconds = 3f;      // a hop is never drawn shorter than this (it starts earlier instead)
    private const float HoseAfter = 1.2f;        // the hose goes on this long after the pad touchdown
    private const float HoseOffBefore = 1.5f;    // and comes off this long before the engines light
    private const int HoseSegments = 10;

    private static CylinderMesh? _hoseMesh;
    private static StandardMaterial3D? _hoseMaterial;

    private float ShipTime(Ship ship, long tick) => (tick - ship.ArriveTick) / (float)Simulation.TickRate;

    // ---------------------------------------------------------------- following the sim

    /// <summary>What the ship's sim drop ship is doing this frame; plans its hop and its climb when they begin.</summary>
    private void Track(Ship ship)
    {
        if (ship.Legacy) return;
        if (_sim.Data.LandingField == null)
        {
            ship.Legacy = true;
            return;
        }
        var sim = ship.Convoy != null ? ship.Convoy.Sim?.Invoke() : _surface.FindDropShip(ship.Owner, DropShipRole.Delivery, ship.DeliveryId, 0);
        if (sim == null)
        {
            ship.SimState = null;
            // Nothing came of it (an outpost site that was taken, a delivery the test panel removed, a ship first seen
            // after it had left): the old way back up. One that was there and has gone left between two frames.
            if (!ship.HadSim)
            {
                if (ship.Time > 0.4f) ship.Legacy = true;
            }
            else if (!ship.Climbing) BeginClimb(ship, _sim.Tick);
            return;
        }
        ship.HadSim = true;
        ship.SimState = sim.State;
        ship.SimUntil = sim.UntilTick;
        if (sim.State == DropShipState.Climbing)
        {
            if (!ship.Climbing) BeginClimb(ship, sim.StateTick);
        }
        else if (sim.Pad >= 0 && ship.PadTile != sim.Pad) SetPad(ship, sim);
    }

    /// <summary>The sim gave it a pad: where it will stand there, and the hop from where it is, in the sim's ticks (not
    /// before its ramp is shut; from its hover for a module ship).</summary>
    private void SetPad(Ship ship, DropShip sim)
    {
        var lands = ship.Convoy is not { FliesAway: true };
        ship.PadTile = sim.Pad;
        ship.PadPose = PadPoseFor(ship.Spec, sim.Pad);
        ship.HopAirborne = !lands;
        ship.HopFrom = lands ? ship.Landed : ship.Convoy!.Approach(ship.Time).Pose;
        ship.TakenOver = true;
        ship.Tile = sim.Pad; // fog: another player's ship shows while its pad is in sight
        if (sim.State == DropShipState.Hopping)
        {
            var start = ShipTime(ship, sim.StateTick);
            var end = ShipTime(ship, sim.UntilTick);
            if (lands) start = Mathf.Max(start, ship.LiftFrom - SpoolSeconds);
            if (end - start < HopMinSeconds) start = end - HopMinSeconds;
            ship.HopStart = start;
            ship.HopEnd = end;
            ship.HopSpool = lands ? Mathf.Min(SpoolSeconds, 0.25f * (end - start)) : 0f;
        }
        else
        {
            // First seen already on its pad (a jump in time): no hop to draw.
            ship.HopStart = ship.HopEnd = float.MinValue;
            ship.HopSpool = 0f;
            ship.HopLit = ship.HopLifted = ship.PadTouched = true;
        }
        // Cruise over whatever lies between: the highest ground on the way, and never lower than the convoy's hop.
        var from = ship.HopFrom.Origin;
        var to = ship.PadPose.Origin;
        var top = Mathf.Max(from.Y, to.Y) + 8f;
        for (var i = 1; i < 8; i++) top = Mathf.Max(top, _view.GroundAt(from.Lerp(to, i / 8f)) + 12f);
        ship.HopCruise = top;
    }

    /// <summary>The climb back to the ship begins at this tick: from its pad, from where it unloaded (no field), or from
    /// the air (a module ship with no pad).</summary>
    private void BeginClimb(Ship ship, long tick)
    {
        var lands = ship.Convoy is not { FliesAway: true };
        var at = ShipTime(ship, tick);
        ship.Climbing = true;
        ship.TakenOver = true;
        if (ship.PadTile >= 0 && at >= ship.HopEnd - 0.05f)
        {
            ship.ClimbFrom = ship.PadPose;
            ship.ClimbAirborne = false;
        }
        else if (ship.PadTile >= 0)
        {
            ship.ClimbFrom = HopPose(ship, at).Pose; // it never got there (its field went mid-hop): up from where it is
            ship.ClimbAirborne = true;
        }
        else if (lands)
        {
            ship.ClimbFrom = ship.Landed;
            ship.ClimbAirborne = false;
            at = Mathf.Max(at, ship.LiftFrom - SpoolSeconds); // not before the ramp is shut
        }
        else
        {
            ship.ClimbFrom = ship.Convoy!.Approach(ship.Time).Pose;
            ship.ClimbAirborne = true;
        }
        ship.ClimbAt = at;
        HideHose(ship);
    }

    /// <summary>Where a ship of this model stands on a pad: its four pads centred on the pad, nose away from the base,
    /// settled on the ground.</summary>
    private Transform3D PadPoseFor(DropShipSpec spec, int padTile)
    {
        var frame = Field?.Frame(padTile) ?? new Transform3D(Basis.Identity, _view.TileCentre(padTile));
        var pads = spec.PadSpots;
        var centre = (pads[0] + pads[1] + pads[2] + pads[3]) / 4f;
        var origin = frame.Origin - frame.Basis * new Vector3(centre.X, 0, centre.Z);
        return Settle(spec, new Transform3D(frame.Basis, origin with { Y = 0 }));
    }

    // ---------------------------------------------------------------- the flight

    /// <summary>
    /// A hop between two standing poses (the convoy's hop to its unloading spot, and every ship's hop to its pad), u = 0..1:
    /// pop up to the cruise height on a strong burn, lean into the hop and back to brake it, then drop and brake hard onto
    /// the spot (straight down for the last part). Legs: out the whole way from the ground (<paramref name="legsDown"/>),
    /// else coming out for the landing.
    /// </summary>
    internal static Flight HopFlight(Transform3D from, Transform3D to, float u, float cruise, bool legsDown)
    {
        u = Mathf.Clamp(u, 0f, 1f);
        var a = from.Origin;
        var b = to.Origin;
        var x = (u - 0.15f) / 0.6f;
        var across = a.Lerp(b, RocketPlume.S(0.15f, 0.75f, u));
        float y;
        if (u < 0.35f) y = Mathf.Lerp(a.Y, cruise, RocketPlume.S(0f, 0.35f, u));
        else if (u < 0.55f) y = cruise;
        else y = Mathf.Lerp(cruise, b.Y, RocketPlume.S(0.55f, 1f, u));
        var lean = x > 0f && x < 1f ? 12f * Mathf.Sin(Mathf.Tau * x) : 0f; // into the hop, then back against it
        var turn = new Quaternion(from.Basis.Orthonormalized()).Slerp(new Quaternion(to.Basis.Orthonormalized()), RocketPlume.S(0.1f, 0.75f, u));
        var basis = Lean(b - a, lean) * new Basis(turn);
        var throttle = 0.9f;
        throttle = Mathf.Lerp(throttle, 0.6f, RocketPlume.S(0.25f, 0.35f, u));   // cruising across
        throttle = Mathf.Lerp(throttle, 0.35f, RocketPlume.S(0.55f, 0.62f, u));  // dropping
        throttle = Mathf.Lerp(throttle, 0.95f, RocketPlume.S(0.72f, 0.78f, u));  // the braking burn
        throttle = Mathf.Lerp(throttle, 0.55f, RocketPlume.S(0.92f, 0.98f, u));  // settle
        return new Flight(new Transform3D(basis, across with { Y = y }), throttle, 0f, legsDown ? 1f : RocketPlume.S(0.6f, 0.92f, u));
    }

    /// <summary>The ship on its hop to its pad at t (seconds from ArriveTick): spooling up on the ground first (unless it
    /// left from a hover), then the hop.</summary>
    private static Flight HopPose(Ship ship, float t)
    {
        var flyFrom = ship.HopStart + ship.HopSpool;
        if (t < flyFrom)
        {
            var spool = ship.HopSpool > 0f ? 0.85f * RocketPlume.S(ship.HopStart, flyFrom, t) : 0f;
            return new Flight(ship.HopFrom, spool, 0f, 1f);
        }
        var u = (t - flyFrom) / Mathf.Max(0.1f, ship.HopEnd - flyFrom);
        return HopFlight(ship.HopFrom, ship.PadPose, u, ship.HopCruise, !ship.HopAirborne);
    }

    /// <summary>The ship's pose and engines while the sim has it (after its first touchdown, or after a module ship left
    /// its hover): waiting where it unloaded, hopping, on its pad, climbing out.</summary>
    private void FieldFlight(Ship ship, float t, out Transform3D pose, out float lift, out float impulse, out float legsOut)
    {
        impulse = 0f;
        legsOut = 1f;
        ship.OffGround = false;
        if (ship.Climbing)
        {
            var c = t - ship.ClimbAt;
            var from = ship.ClimbFrom;
            if (!ship.ClimbAirborne)
            {
                if (c < SpoolSeconds)
                {
                    // On the ground: still shut down (the ramp closing), then the engines light and spool up.
                    pose = from;
                    lift = c <= 0f ? 0f : 0.85f * RocketPlume.S(0f, SpoolSeconds, c);
                    ship.EnginesLit = lift > 0.02f;
                    return;
                }
                c -= SpoolSeconds;
            }
            c = Mathf.Max(0f, c);
            var pitch = ship.Spec.PitchOver;
            var forward = Flat(-from.Basis.Z);
            forward = forward.LengthSquared() > 1e-6f ? forward.Normalized() : Vector3.Forward;
            pose = new Transform3D(pitch ? from.Basis * new Basis(Vector3.Right, -Mathf.DegToRad(RocketClimb.Pitch(c))) : from.Basis,
                from.Origin + Vector3.Up * RocketClimb.Up(c) + (pitch ? forward * RocketClimb.Ahead(c) : Vector3.Zero));
            lift = ship.ClimbAirborne ? Mathf.Max(0.66f, RocketClimb.Throttle(c)) : RocketClimb.Throttle(c);
            impulse = pitch ? RocketClimb.Aft(c) : 0f;
            legsOut = ship.ClimbAirborne ? 0f : 1f - RocketPlume.S(1f, 3f, c);
            ship.OffGround = true;
            ship.EnginesLit = true;
            return;
        }
        if (ship.PadTile >= 0)
        {
            if (t < ship.HopEnd)
            {
                var hop = HopPose(ship, t);
                pose = hop.Pose;
                lift = hop.Lift;
                legsOut = hop.LegsOut;
                ship.OffGround = ship.HopAirborne || t >= ship.HopStart + ship.HopSpool;
                ship.EnginesLit = lift > 0.02f;
                // Before its own hop start a landed ship is still where it unloaded, engines winding down from its landing.
                if (!ship.OffGround && t < ship.HopStart) lift = LandedIdle(t);
                return;
            }
            // On the pad: the engines cut, it stands there while it refuels.
            pose = ship.PadPose;
            lift = 0.45f * (1f - RocketPlume.S(ship.HopEnd, ship.HopEnd + 0.7f, t));
            ship.EnginesLit = false;
            return;
        }
        // No pad yet: where it unloaded, engines off (they cut after its touchdown).
        pose = ship.Landed;
        lift = LandedIdle(t);
        ship.EnginesLit = false;
    }

    /// <summary>The engines cutting after the first touchdown (as on the old timeline).</summary>
    private static float LandedIdle(float t) => 0.45f * (1f - RocketPlume.S(TouchdownAt, TouchdownAt + 0.7f, t));

    /// <summary>True the first time t passes a moment; <paramref name="fresh"/> = it only just did (a ship first seen
    /// part-way through doesn't replay old bursts and sounds).</summary>
    private static bool Once(ref bool done, float t, float at, out bool fresh)
    {
        fresh = false;
        if (done || t < at) return false;
        done = true;
        fresh = t < at + 0.75f;
        return true;
    }

    /// <summary>The moments of the field plan: engines lighting on the ground (a billow, the lift-off sound), leaving the
    /// ground (a burst of dust, a scorch mark), touching down on the pad (a burst, a scorch mark, the touchdown sound).</summary>
    private void FieldEvents(Ship ship, float t, Vector3 under)
    {
        var seen = ship.Shown;
        if (ship.PadTile >= 0 && !ship.Climbing)
        {
            if (!ship.HopAirborne && Once(ref ship.HopLit, t, ship.HopStart + 0.25f * ship.HopSpool, out var lit) && lit && seen) LightUp(ship, under);
            if (!ship.HopAirborne && Once(ref ship.HopLifted, t, ship.HopStart + ship.HopSpool, out var up) && up && seen) LiftOff(ship, under);
            if (Once(ref ship.PadTouched, t, ship.HopEnd, out var down) && down && seen)
            {
                Burst(ship, under);
                Scorched?.Invoke(under, false);
                if (_heard) Sfx.At("landing/touchdown", ToGlobal(under), 0f, Pitch(ship, 0.06f));
            }
        }
        if (ship.Climbing && !ship.ClimbAirborne)
        {
            if (Once(ref ship.ClimbLit, t, ship.ClimbAt + 0.25f, out var lit) && lit && seen) LightUp(ship, under);
            if (Once(ref ship.ClimbLifted, t, ship.ClimbAt + SpoolSeconds, out var up) && up && seen) LiftOff(ship, under);
        }
    }

    private void LightUp(Ship ship, Vector3 under)
    {
        ship.Plume.Billow(under);
        if (_heard) Sfx.At("landing/liftoff", ToGlobal(under));
    }

    private void LiftOff(Ship ship, Vector3 under)
    {
        Burst(ship, under);
        Scorched?.Invoke(under, true);
    }

    // ---------------------------------------------------------------- the fuel hose

    /// <summary>The hose from the pad's bowser to the ship's flank: on a little after it has set down while the sim has
    /// it refuelling or waiting for fuel, off shortly before its engines light.</summary>
    private void UpdateHose(Ship ship, float t)
    {
        var fuelling = ship.SimState is DropShipState.Refuelling or DropShipState.WaitingForFuel;
        var leaving = ship.SimState == DropShipState.Refuelling && ship.SimUntil - _sim.Tick < (long)(HoseOffBefore * Simulation.TickRate);
        var want = Field != null && ship.Shown && ship.FieldPlan && ship.PadTile >= 0 && !ship.Climbing && fuelling && !leaving && t >= ship.HopEnd + HoseAfter;
        if (!want)
        {
            HideHose(ship);
            return;
        }
        if (ship.HoseOut) return;
        ship.HoseOut = true;
        ship.Hose ??= NewHose();
        // From the bowser's reel to the flank nearest it, sagging onto the ground in between.
        var port = Field!.HosePort(ship.PadTile);
        var local = ship.PadPose.AffineInverse() * port;
        var flank = ship.PadPose * new Vector3(Mathf.Sign(local.X) * 2.2f, 1.5f, Mathf.Clamp(local.Z, -2f, 3f));
        var sag = Mathf.Min(1.4f, 0.3f * port.DistanceTo(flank));
        var last = port;
        for (var i = 0; i < HoseSegments; i++)
        {
            var s = (i + 1f) / HoseSegments;
            var p = port.Lerp(flank, s) + Vector3.Down * (sag * 4f * s * (1f - s));
            p.Y = Mathf.Max(p.Y, _view.GroundAt(p) + 0.08f);
            Segment(ship.Hose[i], last, p);
            last = p;
        }
    }

    private static void HideHose(Ship ship)
    {
        if (!ship.HoseOut || ship.Hose == null) return;
        ship.HoseOut = false;
        foreach (var segment in ship.Hose) segment.Visible = false;
    }

    private static void FreeHose(Ship ship)
    {
        if (ship.Hose == null) return;
        foreach (var segment in ship.Hose) segment.QueueFree();
        ship.Hose = null;
        ship.HoseOut = false;
    }

    private MeshInstance3D[] NewHose()
    {
        _hoseMesh ??= new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 1f, RadialSegments = 6, Rings = 1 };
        _hoseMaterial ??= new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.07f, 0.08f), Roughness = 0.8f };
        var hose = new MeshInstance3D[HoseSegments];
        for (var i = 0; i < HoseSegments; i++)
        {
            hose[i] = new MeshInstance3D { Name = "FuelHose", Mesh = _hoseMesh, MaterialOverride = _hoseMaterial, Visible = false };
            AddChild(hose[i]);
        }
        return hose;
    }

    /// <summary>One straight piece of hose between two points.</summary>
    private static void Segment(MeshInstance3D piece, Vector3 a, Vector3 b)
    {
        var v = b - a;
        var length = v.Length();
        piece.Visible = length > 0.02f;
        if (!piece.Visible) return;
        var y = v / length;
        var x = Mathf.Abs(y.Dot(Vector3.Forward)) > 0.95f ? y.Cross(Vector3.Right).Normalized() : y.Cross(Vector3.Forward).Normalized();
        var z = x.Cross(y).Normalized();
        piece.Transform = new Transform3D(new Basis(x, y * length, z), (a + b) / 2f);
    }
}
