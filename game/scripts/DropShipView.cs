using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The drop ship that flies orbit "shuttle" deliveries down (render-only; the sim only knows the delivery's tile,
/// LaunchTick and ArriveTick, and puts the unit on the ground at ArriveTick with <see cref="SurfaceUnit.DeliveryId"/>
/// set). For every shuttle delivery, own or another player's, one ship (models/vehicles/dropship.glb, Halyard 07 from
/// blender/build_dropship.py) plays, in seconds from ArriveTick:
/// <list type="bullet">
/// <item>-9 → -2: comes down like a rocket lander (<see cref="RocketDescent"/>): from 260 up and 140 back a
/// near-ballistic fall on idle engines, leaning back on a quarter throttle to brake the approach, a full-throttle
/// deceleration burn from ~88 up, the approach gone by 60 up (so the last stretch is straight down), throttle back
/// into a brief hover ~3 up with a sideways correction, a gentle settle, touchdown (legs out -5.8 → -3.6; rocket flames, a ground glow and smoke from
/// <see cref="RocketPlume"/>; fine dust blasted out along the ground below 35 up, a dust burst + smoke billow on
/// touchdown; the engines cut over 0.7 s).</item>
/// <item>0 → 1.6: the ramp opens (the sim's unit appears in the closed bay at 0); from 1.3 the unit drives down the
/// ramp and on to its tile centre (<see cref="Unloading"/> places it), ~3–4 s.</item>
/// <item>then the ramp closes (1.6 s), the engines light on the ground (1.1 s spool to 85%, a smoke billow) and the
/// ship climbs straight up, accelerating, its plume growing, pitches over toward its nose from 2.2 s
/// (<see cref="RocketClimb"/>) and is out of sight and freed after 7 s.</item>
/// </list>
/// The ship lands beside the unit's tile centre with its ramp toward it, turned to whichever of 12 headings keeps
/// its footprint off buildings, water and mountains and on the flattest ground, then tilted to the ground under its
/// pads. Another player's ship only shows while its tile is in sight (fog); your own always shows.
/// <para>Sounds (docs/sound-contract.md; <see cref="Sound"/>): each ship carries a landing/burn_loop player
/// (<see cref="Sfx.Loop"/>) whose volume (linear-to-dB of the throttle) and pitch (0.85–1.15) follow the engines;
/// burn_ignite / engine_cut on the throttle crossing ~0.15 in flight, touchdown, ramp_open, vehicle_roll as the unit
/// starts down the ramp, ramp_close, liftoff as the engines light on the ground. Only while the ship is shown and the
/// surface is (not in orbit); the player goes with the model.</para>
/// <para>The first landing's three ships (<see cref="LandingView"/>) are convoy ships: LandingView flies them
/// (<see cref="ConvoyShip.Approach"/>: down from orbit with an outpost module slung underneath, set it down, hop over
/// to their landing spot) and names the unit each one carries; from touchdown on they unload and leave like any other.
/// A convoy ship with <see cref="ConvoyShip.FliesAway"/> never lands: LandingView flies it the whole time.</para>
/// </summary>
public partial class DropShipView : Node3D
{
    // ---------------------------------------------------------------- timeline (seconds from ArriveTick)

    private const float DescentSeconds = 9f;   // starts this long before arrival (sooner if the flight is shorter)
    private const float TouchdownAt = -2f;
    private const float LegsFrom = -5.8f, LegsTo = -3.6f; // out during the burn, down by the hover
    private const float RampSeconds = 1.6f;    // opens from ArriveTick, closes after the unit is out
    private const float DriveFrom = 1.3f;      // the unit starts down the ramp
    private const float DriveSpeed = 3.4f;     // world units a second
    private const float CloseDelay = 0.4f;
    private const float SpoolSeconds = 1.1f;
    private const float ClimbSeconds = 7f;
    private const float ReleaseSeconds = 0.6f; // the unit blends from the end of the ramp path to where the sim has it

    private const float StartHeight = 260f, ApproachDistance = 140f;

    // ---------------------------------------------------------------- the model (Godot space: front -Z, up +Y)

    private const float RampOpenDegrees = 109.6f; // build_dropship.py RAMP_OPEN: the toe rests on the ground
    private const float RampLength = 3.32f;
    private static readonly Vector3 HingeDefault = new(0, 1.25f, 6f);
    private static readonly Vector3 BayStart = new(0, 1.25f, -0.5f); // rear wheels of the unit parked in the bay
    private const float ToeReach = 9.15f; // ship origin to the open ramp's toe along +Z

    private static readonly Vector3[] PadSpots = { new(-4.25f, 0, -2.4f), new(4.25f, 0, -2.4f), new(-4.25f, 0, 5.5f), new(4.25f, 0, 5.5f) };

    /// <summary>Points under the landed ship (and its open ramp) that must be clear, flat ground.</summary>
    private static readonly Vector3[] Footprint =
    {
        new(0, 0, -6.6f), new(-2.3f, 0, -4.5f), new(2.3f, 0, -4.5f), new(-2.3f, 0, 0), new(2.3f, 0, 0), new(0, 0, 0),
        new(-2.3f, 0, 5.8f), new(2.3f, 0, 5.8f), new(-3.75f, 0, -5.9f), new(3.75f, 0, -5.9f), new(-3.75f, 0, 3.9f),
        new(3.75f, 0, 3.9f), PadSpots[0], PadSpots[1], PadSpots[2], PadSpots[3], new(0, 0, 9.1f), new(-1.6f, 0, 8.6f), new(1.6f, 0, 8.6f),
    };

    /// <summary>Each player's ship has its own name and hull number (render-only).</summary>
    private static readonly (string Name, string Number)[] Hulls =
        { ("HALYARD", "07"), ("BOWLINE", "12"), ("LANYARD", "23"), ("CAPSTAN", "31"), ("GANTLINE", "46"), ("TOPMAST", "58") };

    private const float WashBelow = 35f; // fine dust blasted out along the ground below this height on a burn

    private static readonly Color LiftColour = new(1f, 0.74f, 0.46f), ImpulseColour = new(1f, 0.32f, 0.08f); // hot orange-white rocket throats

    private readonly PlanetView _view = null!;
    private readonly Simulation _sim = null!;
    private readonly PlanetSurface _surface = null!;
    private readonly int _localPlayer;
    private readonly Dictionary<int, Ship> _ships = new();   // by delivery id
    private readonly Dictionary<int, Ship> _byUnit = new();  // by the id of the unit it carries
    private readonly List<int> _done = new();
    private float _clock;                                    // running seconds (flame flicker)
    private bool _heard;                                     // the surface is on screen (not in orbit): ships may sound

    /// <summary>Where a convoy ship is and how its engines and legs look at a moment (LandingView's flight): Lift = the
    /// lift thrusters' throttle 0..1 (drives the rocket plume), Impulse = the aft thrusters' 0..1, Cant 0..1 tips the lift
    /// flames outward (a module slung underneath).</summary>
    public readonly record struct Flight(Transform3D Pose, float Lift, float Impulse, float LegsOut, float Cant = 0f);

    /// <summary>A ship LandingView flies (see the class summary). Times are seconds from <see cref="ArriveTick"/>.</summary>
    public sealed class ConvoyShip
    {
        public required int Key;              // negative, so it never meets a delivery id
        public required int Owner;
        public required long ArriveTick;      // touchdown is 2 s before it (TouchdownAt), the ramp opens at it
        public required float ShowFrom;       // first shown (seconds from ArriveTick, negative)
        public required Transform3D Landed;   // from ConvoyLanding (unused when FliesAway)
        public required bool Crawler;         // what it carries, for the ramp drive (wheelbase)
        public required string Name, Number;
        public required System.Func<float, Flight> Approach; // used before touchdown (the whole time when FliesAway)
        public required System.Func<int> Resolve;            // the carried unit's id: 0 = not down yet, -1 = none
        public bool FliesAway;
        public float GoneAt;                  // FliesAway only: freed after this
    }

    private sealed class Ship
    {
        public required int DeliveryId;
        public required int Owner;
        public required long LaunchTick, ArriveTick;
        public required Node3D Model;
        public required Transform3D Landed;
        public required int Tile;
        public required float Wheelbase;
        public Node3D? Ramp;
        public Basis RampRest;
        public Vector3 Hinge = HingeDefault;
        public readonly Node3D?[] Legs = new Node3D?[4], Feet = new Node3D?[4];
        public readonly Basis[] LegRest = new Basis[4], FootRest = new Basis[4];
        public readonly BaseMaterial3D?[] Thrusters = new BaseMaterial3D?[6];
        public BaseMaterial3D? Nacelle, Bay;
        public RoverLook.Lamps? Lamps;
        public VehicleLights.Rig? Lights; // landing lights + belly flood (VehicleLights, budgeted by PlanetView.Headlights)
        public OmniLight3D BayLamp = null!;
        public CpuParticles3D Wash = null!, Burst = null!;
        public RocketPlume Plume = null!;
        public RocketDescent Descent;   // deliveries: the way down from DescentFrom to TouchdownAt
        public float Side;              // which way its hover correction slides (±1)
        public int UnitId;          // 0 = not down yet, -1 = gave up looking
        public Vector3[]? Path;     // the unit's wheel path: bay, hinge, toe, ground, end (world)
        public float PathLength, DriveSeconds;
        public bool TouchedDown, LiftedOff, Lit, Shown;
        public AudioStreamPlayer3D? Burn;   // landing/burn_loop, driven by the throttle (Sound)
        public bool Burning, RampOpened, RampClosed, Rolled;
        public float Time;          // this frame, seconds from ArriveTick
        public ConvoyShip? Convoy;  // flown by LandingView until touchdown

        public bool Flown => Convoy != null && (Convoy.FliesAway || Time < TouchdownAt);

        public float DescentFrom => Mathf.Min(TouchdownAt - 1f, Mathf.Max(-DescentSeconds, -(ArriveTick - LaunchTick) / (float)Simulation.TickRate));
        public float UnloadEnd => DriveFrom + DriveSeconds;
        public float CloseFrom => Mathf.Max(RampSeconds, UnloadEnd + CloseDelay);
        public float LiftFrom => CloseFrom + RampSeconds + SpoolSeconds;
        public float Gone => Convoy is { FliesAway: true } c ? c.GoneAt : LiftFrom + ClimbSeconds;
    }

    /// <summary>Where a unit coming off a drop ship is drawn: Origin on the ground under it; Basis null = leave its
    /// facing to the normal code (while it blends back to its sim position).</summary>
    public readonly record struct Carried(Vector3 Origin, Basis? Basis);

    public DropShipView(PlanetView view, Simulation sim, int localPlayer)
    {
        _view = view;
        _sim = sim;
        _surface = sim.Surface!;
        _localPlayer = localPlayer;
    }

    // Parameterless constructor for Godot's script machinery; such an instance draws nothing.
    public DropShipView() { }

    // ---------------------------------------------------------------- per frame

    /// <summary>Start a ship for every new shuttle delivery, move every ship along its timeline, free finished ones.</summary>
    public void Update(float alpha, float delta)
    {
        if (_surface == null) return;
        _clock = (_clock + delta) % 600f; // the flicker doesn't mind the wrap; keeps the sines precise
        _heard = IsVisibleInTree();
        foreach (var d in _surface.Deliveries)
            if (d.Kind == DeliveryKind.Shuttle && !_ships.ContainsKey(d.Id))
                _ships[d.Id] = Create(d);

        var done = _done;
        done.Clear();
        foreach (var ship in _ships.Values)
        {
            ship.Time = (_sim.Tick - ship.ArriveTick + alpha) / Simulation.TickRate;
            if (ship.Time < 0 && ship.Convoy == null && !InFlight(ship.DeliveryId))
            {
                done.Add(ship.DeliveryId); // the delivery vanished before landing (test panel): nothing comes down
                continue;
            }
            if (ship.UnitId == 0 && (ship.Time >= 0 || ship.Convoy != null)) FindUnit(ship);
            if (ship.Time >= ship.Gone)
            {
                done.Add(ship.DeliveryId);
                continue;
            }
            Animate(ship, delta);
        }
        foreach (var id in done)
        {
            var ship = _ships[id];
            ship.Burn?.Stop(); // freed with the model
            ship.Model.QueueFree();
            ship.Wash.QueueFree();
            ship.Burst.QueueFree();
            ship.Plume.Free();
            if (ship.Lights != null) _view.RemoveLightRig(ship.Lights);
            if (ship.UnitId > 0) _byUnit.Remove(ship.UnitId);
            _ships.Remove(id);
        }
    }

    /// <summary>Drop ships standing on (or just above) the ground and shown: people walk round them
    /// (PlanetView.People.cs).</summary>
    public void GroundedModels(List<Node3D> into)
    {
        foreach (var ship in _ships.Values)
        {
            if (!ship.Shown || !ship.Model.Visible) continue;
            var p = ship.Model.Position;
            if (p.Y - _view.GroundAt(p) < 3f) into.Add(ship.Model);
        }
    }

    /// <summary>
    /// While a unit is coming off its drop ship: where to draw it instead of <paramref name="spot"/> (its normal place on
    /// its tile). It waits in the bay until the ramp is down, drives down the ramp and on to its tile centre, then blends
    /// into wherever the sim has it by then (it may already have orders). False once it is clear of the ship, or while
    /// the ship is hidden by fog.
    /// </summary>
    public bool Unloading(SurfaceUnit u, Vector3 spot, float alpha, out Carried pose, out bool driving)
    {
        pose = default;
        driving = false;
        if (!_byUnit.TryGetValue(u.Id, out var ship) || !ship.Shown) return false;
        var t = ship.Time;
        ship.Path ??= PathFor(ship, spot);
        var travel = ship.PathLength - ship.Wheelbase;
        if (t < ship.UnloadEnd)
        {
            var k = t < DriveFrom || ship.DriveSeconds <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, (t - DriveFrom) / ship.DriveSeconds);
            driving = t >= DriveFrom;
            pose = PoseAt(ship, ship.Wheelbase + travel * k);
            if (ship.Flown)
            {
                // Still flying (a convoy ship hopping to its spot): the unit rides in the bay wherever the ship is.
                var rel = ship.Model.Transform * ship.Landed.AffineInverse();
                pose = new Carried(rel * pose.Origin, rel.Basis * (pose.Basis ?? Basis.Identity));
            }
            return true;
        }
        if (t < ship.UnloadEnd + ReleaseSeconds)
        {
            var end = PoseAt(ship, ship.PathLength).Origin;
            var k = Mathf.SmoothStep(0f, 1f, (t - ship.UnloadEnd) / ReleaseSeconds);
            pose = new Carried(end.Lerp(spot, k), null);
            driving = u.Path.Count > 0;
            return true;
        }
        return false;
    }

    private bool InFlight(int deliveryId)
    {
        foreach (var d in _surface.Deliveries)
            if (d.Id == deliveryId) return true;
        return false;
    }

    private void FindUnit(Ship ship)
    {
        if (ship.Convoy != null)
        {
            var id = ship.Convoy.Resolve();
            if (id > 0 && _surface.FindUnit(id) is { } carried)
            {
                ship.UnitId = id;
                _byUnit[id] = ship;
                ship.Path = PathFor(ship, _view.UnitPos(carried)); // its parking spot on the tile (sim lanes)
            }
            else if (id < 0 || ship.Time >= DriveFrom) ship.UnitId = -1;
            return;
        }
        foreach (var u in _surface.Units)
            if (u.DeliveryId == ship.DeliveryId)
            {
                ship.UnitId = u.Id;
                _byUnit[u.Id] = ship;
                ship.Path = PathFor(ship, _view.UnitPos(u)); // its parking spot; fixes DriveSeconds, so the rest of the timeline
                return;
            }
        if (ship.Time >= DriveFrom) ship.UnitId = -1; // nothing came down: just close up and leave
    }

    // ---------------------------------------------------------------- the unit's way out

    /// <summary>The unit's wheel path in world space: rear wheels at the front of the bay, along the deck to the hinge,
    /// down the open ramp to its toe, a step onto the ground, then on until its centre is on <paramref name="spot"/>.</summary>
    private Vector3[] PathFor(Ship ship, Vector3 spot)
    {
        var l = ship.Landed;
        var toeLocal = ship.Hinge + new Basis(Vector3.Right, Mathf.DegToRad(RampOpenDegrees)) * new Vector3(0, RampLength, 0);
        var bay = l * BayStart;
        var hinge = l * ship.Hinge;
        var toe = l * toeLocal;
        var outward = Flat(l.Basis.Z).Normalized();
        var step = toe + outward * 1.2f;
        step.Y = _view.GroundAt(step);
        var toSpot = Flat(spot - step);
        var dir = toSpot.Length() > 0.5f ? toSpot.Normalized() : outward;
        var end = spot + dir * (ship.Wheelbase / 2f);
        end.Y = _view.GroundAt(end);
        var path = new[] { bay, hinge, toe, step, end };
        ship.PathLength = 0f;
        for (var i = 1; i < path.Length; i++) ship.PathLength += path[i].DistanceTo(path[i - 1]);
        ship.DriveSeconds = Mathf.Clamp((ship.PathLength - ship.Wheelbase) / DriveSpeed, 2f, 7f);
        return path;
    }

    /// <summary>The unit with its front wheels <paramref name="along"/> the path and its rear wheels a wheelbase behind.</summary>
    private static Carried PoseAt(Ship ship, float along)
    {
        var front = PointAt(ship.Path!, along);
        var rear = PointAt(ship.Path!, along - ship.Wheelbase);
        var forward = front - rear;
        if (forward.LengthSquared() < 1e-4f) forward = ship.Landed.Basis.Z;
        return new Carried((front + rear) / 2f, Basis.LookingAt(forward.Normalized(), ship.Landed.Basis.Y.Normalized()));
    }

    private static Vector3 PointAt(Vector3[] path, float along)
    {
        if (along <= 0f) return path[0];
        for (var i = 1; i < path.Length; i++)
        {
            var leg = path[i].DistanceTo(path[i - 1]);
            if (along <= leg && leg > 1e-5f) return path[i - 1].Lerp(path[i], along / leg);
            along -= leg;
        }
        return path[^1];
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    // ---------------------------------------------------------------- the ship's flight

    private void Animate(Ship ship, float delta)
    {
        var t = ship.Time;
        var from = ship.Convoy?.ShowFrom ?? ship.DescentFrom;
        ship.Shown = t >= from && (ship.Owner == _localPlayer || (ship.Tile >= 0 && _view.Sees(ship.Tile)));
        ship.Model.Visible = ship.Shown;
        var l = ship.Landed;
        var forward = -l.Basis.Z.Normalized();

        // Where it is: coming down, on the ground, or climbing out.
        float lift, impulse, legsOut, cant = 0f;
        Transform3D pose;
        if (ship.Flown)
        {
            var f = ship.Convoy!.Approach(t);
            pose = f.Pose;
            lift = f.Lift;
            impulse = f.Impulse;
            legsOut = f.LegsOut;
            cant = f.Cant;
        }
        else if (t < TouchdownAt)
        {
            // A rocket lander: fall, suicide burn, hover-correct, settle (RocketDescent). It comes in over its tail.
            var d = ship.Descent;
            var tau = TouchdownAt - t;
            var back = Flat(l.Basis.Z).Normalized();
            var side = Flat(l.Basis.X).Normalized() * ship.Side;
            var slide = d.SideAt(tau, out var roll);
            var turn = d.TurnAt(tau);
            var origin = l.Origin + Vector3.Up * d.HeightAt(tau) + back * d.ReachAt(tau) + side * slide;
            var basis = Lean(back, d.LeanAt(tau)) * Lean(-side, roll) * l.Basis * new Basis(Vector3.Up, Mathf.DegToRad(35f) * turn * turn);
            pose = new Transform3D(basis, origin);
            lift = d.Throttle(tau);
            impulse = 0f;
            legsOut = RocketPlume.S(LegsFrom, LegsTo, t);
        }
        else if (t < ship.LiftFrom)
        {
            // The engines cut after touchdown; before take-off they light on the ground and spool up.
            var spoolFrom = ship.LiftFrom - SpoolSeconds;
            var cut = 0.45f * (1f - RocketPlume.S(TouchdownAt, TouchdownAt + 0.7f, t));
            var spool = t >= spoolFrom ? 0.85f * RocketPlume.S(spoolFrom, ship.LiftFrom, t) : 0f;
            pose = l;
            lift = Mathf.Max(cut, spool);
            impulse = 0f;
            legsOut = 1f;
        }
        else
        {
            // Straight up, accelerating, then it pitches over toward its nose and away (RocketClimb).
            var c = t - ship.LiftFrom;
            pose = new Transform3D(l.Basis * new Basis(Vector3.Right, -Mathf.DegToRad(RocketClimb.Pitch(c))),
                l.Origin + Vector3.Up * RocketClimb.Up(c) + Flat(forward).Normalized() * RocketClimb.Ahead(c));
            lift = RocketClimb.Throttle(c);
            impulse = RocketClimb.Aft(c);
            legsOut = 1f - RocketPlume.S(1f, 3f, c);
        }
        ship.Model.Transform = pose;

        // Legs fold under the pods for flight: front ones aft (-90°), rear ones forward (+90°); feet stay level.
        for (var k = 0; k < 4; k++)
        {
            var fold = Mathf.DegToRad(k < 2 ? -90f : 90f) * (1f - legsOut);
            if (ship.Legs[k] is { } leg) leg.Basis = ship.LegRest[k] * new Basis(Vector3.Right, fold);
            if (ship.Feet[k] is { } foot) foot.Basis = ship.FootRest[k] * new Basis(Vector3.Right, -fold);
        }

        // Ramp: opens at ArriveTick, closes once the unit is out.
        var open = ship.Convoy is { FliesAway: true } ? 0f : Mathf.SmoothStep(0f, 1f, t / RampSeconds) * (1f - Mathf.SmoothStep(0f, 1f, (t - ship.CloseFrom) / RampSeconds));
        if (ship.Ramp != null) ship.Ramp.Basis = ship.RampRest * new Basis(Vector3.Right, Mathf.DegToRad(RampOpenDegrees) * open);

        // Glow: the lift thrusters' throats hot orange-white by throttle, the aft exhausts small, the bay lit while the
        // ramp is down.
        for (var k = 0; k < 6; k++)
            if (ship.Thrusters[k] is { } m) m.EmissionEnergyMultiplier = k < 4 ? 0.3f + 14f * lift : 0.3f + 5f * impulse;
        if (ship.Nacelle != null) ship.Nacelle.EmissionEnergyMultiplier = 1.2f + 2f * Mathf.Max(lift, impulse);
        if (ship.Bay != null) ship.Bay.EmissionEnergyMultiplier = 0.4f + 4.5f * open;
        ship.BayLamp.LightEnergy = 1.6f * open;
        ship.Lamps?.Update(true, false, false, delta); // the beacon flashes the whole time
        if (ship.Lights is { } lights)
        {
            // Landing lights while it comes in, touches down and climbs out; the belly flood while the ramp is down.
            var landing = ship.Convoy is { FliesAway: true } || t < TouchdownAt + 1.5f || (t > ship.LiftFrom - SpoolSeconds && t < ship.LiftFrom + 3f);
            for (var k = 0; k < lights.Demand.Length; k++)
                lights.Demand[k] = lights.RoleOf(k) switch
                {
                    VehicleLights.Role.Landing => landing ? 1f : 0f,
                    VehicleLights.Role.Belly => open,
                    _ => lights.Demand[k],
                };
            lights.Hidden = !ship.Shown;
        }

        // Exhaust: flames, ground glow and smoke (RocketPlume); fine dust blasted out while the burn is low, a burst of
        // dust and a billow of smoke as it touches down and as it lifts off, a billow as the engines light on the ground.
        var ground = _view.GroundAt(pose.Origin);
        var under = pose.Origin with { Y = ground + 0.3f };
        var height = pose.Origin.Y - ground;
        ship.Plume.Update(pose, lift, impulse, ground, ship.Shown, _clock, cant);
        ship.Wash.Position = under;
        var wash = ship.Shown && lift > 0.2f && height < WashBelow;
        if (ship.Wash.Emitting != wash) ship.Wash.Emitting = wash;
        var lands = ship.Convoy is not { FliesAway: true };
        if (!ship.TouchedDown && t >= TouchdownAt && lands)
        {
            ship.TouchedDown = true;
            if (ship.Shown) Burst(ship, under);
            if (ship.Shown && _heard && t < TouchdownAt + 0.5f) Sfx.At("landing/touchdown", ToGlobal(under), 0f, Pitch(ship, 0.06f));
        }
        if (!ship.Lit && t >= ship.LiftFrom - SpoolSeconds + 0.25f && lands)
        {
            ship.Lit = true;
            if (ship.Shown) ship.Plume.Billow(under);
            if (ship.Shown && _heard && t < ship.LiftFrom - SpoolSeconds + 0.75f) Sfx.At("landing/liftoff", ToGlobal(under));
        }
        if (!ship.LiftedOff && t >= ship.LiftFrom && lands)
        {
            ship.LiftedOff = true;
            if (ship.Shown) Burst(ship, under);
        }
        Sound(ship, t, lift, impulse, lands);
    }

    // ---------------------------------------------------------------- sounds

    /// <summary>The engine roar and the ship's one-shots this frame (no allocation: the paths are constants).</summary>
    private void Sound(Ship ship, float t, float lift, float impulse, bool lands)
    {
        var heard = ship.Shown && _heard;
        var throttle = Mathf.Clamp(Mathf.Max(lift, 0.7f * impulse), 0f, 1f);
        if (ship.Burn is { } burn)
        {
            if (heard && throttle > 0.02f)
            {
                burn.VolumeDb = Mathf.LinearToDb(Mathf.Max(throttle, 0.03f));
                burn.PitchScale = 0.85f + 0.3f * throttle;
                if (!burn.Playing)
                {
                    // Start somewhere along the loop, different per ship, so three ships don't phase.
                    var length = (float)(burn.Stream?.GetLength() ?? 0.0);
                    burn.Play(length > 0.5f ? length * ((ship.DeliveryId & 7) / 8f) : 0f);
                }
            }
            else if (burn.Playing) burn.Stop();
        }

        // Ignition / cut-off edges in flight (with some hysteresis). On the ground the engines lighting for take-off are
        // the lift-off sound instead (played as they light, above).
        var onGround = lands && t >= TouchdownAt && t < ship.LiftFrom;
        if (!ship.Burning && throttle > 0.18f)
        {
            ship.Burning = true;
            if (heard && !onGround) Sfx.At("landing/burn_ignite", ship.Model.GlobalPosition, 0f, Pitch(ship, 0.05f));
        }
        else if (ship.Burning && throttle < 0.12f)
        {
            ship.Burning = false;
            if (heard) Sfx.At("landing/engine_cut", ship.Model.GlobalPosition, 0f, Pitch(ship, 0.05f));
        }

        if (!lands) return;
        // Ramp down, the unit rolling off, ramp up (each once; not replayed when first seen part-way through).
        if (!ship.RampOpened && t >= 0f)
        {
            ship.RampOpened = true;
            if (heard && t < 0.5f) Sfx.At("landing/ramp_open", ship.Model.ToGlobal(ship.Hinge));
        }
        if (!ship.Rolled && ship.UnitId > 0 && t >= DriveFrom)
        {
            ship.Rolled = true;
            if (heard && t < DriveFrom + 0.5f) Sfx.At("landing/vehicle_roll", ship.Model.ToGlobal(ship.Hinge), 0f, Pitch(ship, 0.08f));
        }
        if (!ship.RampClosed && t >= ship.CloseFrom)
        {
            ship.RampClosed = true;
            if (heard && t < ship.CloseFrom + 0.5f) Sfx.At("landing/ramp_close", ship.Model.ToGlobal(ship.Hinge));
        }
    }

    /// <summary>A small fixed pitch offset per ship (± spread), so a convoy doesn't sound like one sample three times.</summary>
    private static float Pitch(Ship ship, float spread) => 1f + spread * ((((ship.DeliveryId % 5) + 5) % 5) - 2) / 2f;

    private static void Burst(Ship ship, Vector3 at)
    {
        ship.Burst.Position = at;
        ship.Burst.Restart();
        ship.Plume.Billow(at);
    }

    /// <summary>A world-space tilt that leans "up" <paramref name="degrees"/> toward the horizontal direction
    /// <paramref name="toward"/> (a rocket leans its thrust the way it wants to accelerate).</summary>
    internal static Basis Lean(Vector3 toward, float degrees)
    {
        var axis = Vector3.Up.Cross(toward with { Y = 0 });
        if (Mathf.Abs(degrees) < 1e-3f || axis.LengthSquared() < 1e-8f) return Basis.Identity;
        return new Basis(axis.Normalized(), Mathf.DegToRad(degrees));
    }

    // ---------------------------------------------------------------- building a ship

    private Ship Create(Delivery d)
    {
        var def = _sim.Data.SurfaceUnit(d.Payload);
        var hull = Hulls[d.Owner % Hulls.Length];
        return Create(d.Id, d.Owner, d.LaunchTick, d.ArriveTick, LandingFor(d.Tile, d.Id, def.CanMine), def.CanMine, hull.Name, hull.Number);
    }

    /// <summary>Start a convoy ship (LandingView). Its key must be negative; a key already flying is ignored.</summary>
    public void AddConvoyShip(ConvoyShip c)
    {
        if (_ships.ContainsKey(c.Key)) return;
        var ship = Create(c.Key, c.Owner, c.ArriveTick, c.ArriveTick, c.Landed, c.Crawler, c.Name, c.Number);
        ship.Convoy = c;
        if (c.FliesAway) ship.Tile = _view.TileAt(c.Approach(0f).Pose.Origin);
        _ships[c.Key] = ship;
    }

    /// <summary>Where a convoy ship will stand to unload the unit the sim puts on <paramref name="unitTile"/>: as for a
    /// delivery's ship, but also clear of the outpost (no footprint point within <paramref name="hubClear"/> of
    /// <paramref name="hubTile"/>'s centre) and 9 away from the other convoy ships' spots.</summary>
    public Transform3D ConvoyLanding(int unitTile, int seed, bool crawler, int hubTile, float hubClear, IReadOnlyList<Vector3> others) =>
        LandingFor(unitTile, seed, crawler, hubTile, hubClear, others);

    private Ship Create(int key, int owner, long launchTick, long arriveTick, Transform3D landed, bool crawler, string name, string number)
    {
        var colour = PlanetDriver.ColorOf(owner);
        var model = BuildingModels.DropShip(colour) ?? Primitive(colour);
        var ours = owner == _localPlayer;
        if (ours)
        {
            var down = 0; // our ships already flying: a convoy's second and third ship count on (01, 02, 03)
            foreach (var other in _ships.Values) if (other.Owner == owner) down++;
            if (Cosmetics.DropshipHull(down) is { } custom) (name, number) = custom; // achievement name
        }
        RoverLook.Paint(model, name, number);
        if (ours) CosmeticLook.Dress(model, colour); // achievement paint / finish (ours only)
        model.Visible = false;
        AddChild(model);

        var ship = new Ship
        {
            DeliveryId = key,
            Owner = owner,
            LaunchTick = launchTick,
            ArriveTick = arriveTick,
            Model = model,
            Landed = landed,
            Tile = _view.TileAt(landed.Origin),
            Wheelbase = crawler ? 4.2f : 3f,
        };
        if (model.FindChild("Ramp", true, false) is Node3D ramp)
        {
            ship.Ramp = ramp;
            ship.RampRest = ramp.Basis;
            ship.Hinge = ModelSpace(ramp, model).Origin;
        }
        for (var k = 0; k < 4; k++)
        {
            if (model.FindChild($"Leg{k}", true, false) is Node3D leg)
            {
                ship.Legs[k] = leg;
                ship.LegRest[k] = leg.Basis;
            }
            if (model.FindChild($"Foot{k}", true, false) is Node3D foot)
            {
                ship.Feet[k] = foot;
                ship.FootRest[k] = foot.Basis;
            }
        }
        for (var k = 0; k < 6; k++)
            if (model.FindChild($"Thruster{k}", true, false) is MeshInstance3D mi)
            {
                ship.Thrusters[k] = OwnGlow(mi, k < 4 ? LiftColour : ImpulseColour);
                ship.Thrusters[k]?.SetMeta(VehicleLights.NoBoostMeta, true); // driven by the throttle every frame
            }
        BaseMaterial3D? nacelle = null;
        for (var k = 0; k < 2; k++)
            if (model.FindChild($"NacelleGlow{k}", true, false) is MeshInstance3D mi)
            {
                if (nacelle == null) nacelle = OwnGlow(mi, null);
                else mi.SetSurfaceOverrideMaterial(0, nacelle);
            }
        ship.Nacelle = nacelle;
        if (model.FindChild("BayLight", true, false) is MeshInstance3D bay) ship.Bay = OwnGlow(bay, null);
        ship.Lamps = RoverLook.Lamps.Attach(model, key & 0xffff); // convoy keys are negative: the flash phase needs a time >= 0
        ship.Lights = VehicleLights.Rig.Attach(model, VehicleLights.Kind.DropShip, key & 0xffff);
        _view.AddLightRig(ship.Lights);

        ship.BayLamp = new OmniLight3D { LightColor = new Color(1f, 0.93f, 0.8f), OmniRange = 9f, LightEnergy = 0f, ShadowEnabled = false, Position = new Vector3(0, 3.8f, 2.6f) };
        model.AddChild(ship.BayLamp);

        var haze = SprayRig.HazeColour(SprayRig.KindFor(_surface.Map, ship.Tile));
        haze.A = Mathf.Max(haze.A, 0.45f);
        ship.Wash = Downwash(haze, burst: false);
        ship.Burst = Downwash(haze, burst: true);
        AddChild(ship.Wash);
        AddChild(ship.Burst);
        ship.Plume = new RocketPlume(this, model, haze, key);
        ship.Descent = new RocketDescent(TouchdownAt - ship.DescentFrom, StartHeight, ApproachDistance);
        ship.Side = (key & 1) == 0 ? 1f : -1f;
        ship.Burn = Sfx.Loop(model, "landing/burn_loop"); // null while the file is missing: silent

        // Seen for the first time part-way through (e.g. after a test-panel speed-up): don't replay old bursts.
        var t = (_sim.Tick - arriveTick) / (float)Simulation.TickRate;
        ship.TouchedDown = t > TouchdownAt + 0.5f;
        return ship;
    }

    /// <summary>
    /// Where the ship stands: beside the tile centre the unit will be put on (the delivery tile, or the first free
    /// neighbour the sim moves it to when a building is there), far enough out that the unit rolls off the ramp's toe
    /// onto open ground and stops on the centre. Of 12 headings, the one whose footprint stays off buildings, water,
    /// mountains and the map edge on the flattest ground wins; then the ship is tilted to the ground under its pads.
    /// </summary>
    private Transform3D LandingFor(int tile, int seed, bool crawler, int keepOff = -1, float keepOffRadius = 0f, IReadOnlyList<Vector3>? others = null)
    {
        var map = _surface.Map;
        if (_surface.StructureAt(tile) != null || !map.Terrain(tile).Passable)
            foreach (var n in map.Grid.Neighbours(tile))
                if (map.Terrain(n).Passable && _surface.StructureAt(n) == null)
                {
                    tile = n;
                    break;
                }
        var spot = _view.TileCentre(tile);
        var reach = ToeReach + (crawler ? 4.2f : 3.4f);
        var bestScore = float.MaxValue;
        var best = Transform3D.Identity;
        for (var i = 0; i < 12; i++)
        {
            var k = ((i + seed * 5) % 12 + 12) % 12; // a different first choice per delivery, so repeat drops don't all line up
            var a = Mathf.DegToRad(30f * k);
            var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            var basis = Basis.LookingAt(dir, Vector3.Up); // nose away from the spot, ramp (+Z) toward it
            var origin = spot with { Y = 0 } + dir * reach;
            var score = FootprintScore(origin, basis, keepOff, keepOffRadius, others);
            if (score < bestScore - 0.01f)
            {
                bestScore = score;
                best = new Transform3D(basis, origin);
            }
        }
        return Settle(best);
    }

    private float FootprintScore(Vector3 origin, Basis basis, int keepOff, float keepOffRadius, IReadOnlyList<Vector3>? others)
    {
        var map = _surface.Map;
        float lo = float.MaxValue, hi = float.MinValue, penalty = 0f;
        foreach (var p in Footprint)
        {
            var w = origin + basis * p;
            var t = _view.TileAt(w);
            if (t < 0)
            {
                penalty += 1000f;
                continue;
            }
            if (!map.Terrain(t).Passable || map.IsWater(t)) penalty += 200f;
            if (_surface.StructureAt(t) != null && Flat(w - _view.TileCentre(t)).Length() < 11f) penalty += 100f;
            if (keepOff >= 0 && Flat(w - _view.TileCentre(keepOff)).Length() < keepOffRadius) penalty += 100f;
            if (others != null)
                foreach (var o in others)
                    if (Flat(w - o).Length() < 9f) penalty += 100f;
            var h = _view.GroundAt(w);
            lo = Mathf.Min(lo, h);
            hi = Mathf.Max(hi, h);
        }
        return penalty + (hi > lo ? (hi - lo) * 3f : 0f);
    }

    /// <summary>Lift the ship onto the ground under its four pads, tilted to match (a few degrees at most).</summary>
    private Transform3D Settle(Transform3D flat)
    {
        var h = new float[4];
        for (var k = 0; k < 4; k++) h[k] = _view.GroundAt(flat * PadSpots[k]);
        var rise = Mathf.Clamp((h[2] + h[3] - h[0] - h[1]) / 2f, -1.4f, 1.4f); // rear pads minus front, over 7.9
        var roll = Mathf.Clamp((h[1] + h[3] - h[0] - h[2]) / 2f, -1.5f, 1.5f); // right minus left, over 8.5
        var z = (flat.Basis.Z.Normalized() * 7.9f + Vector3.Up * rise).Normalized();
        var x = (flat.Basis.X.Normalized() * 8.5f + Vector3.Up * roll).Normalized();
        var y = z.Cross(x).Normalized();
        x = y.Cross(z).Normalized();
        // The pads' centre is 1.55 aft of the origin: take the plane's height at the origin itself.
        var origin = flat.Origin with { Y = (h[0] + h[1] + h[2] + h[3]) / 4f - rise * 1.55f / 7.9f };
        return new Transform3D(new Basis(x, y, z), origin);
    }

    /// <summary>A node's transform relative to an ancestor, from the local transforms (works before it's in the tree).</summary>
    private static Transform3D ModelSpace(Node3D node, Node3D ancestor)
    {
        var t = node.Transform;
        for (var p = node.GetParent(); p != null && p != ancestor; p = p.GetParent())
            if (p is Node3D p3) t = p3.Transform * t;
        return t;
    }

    /// <summary>This ship's own copy of a glowing part's material (so ships glow independently); colour null keeps the
    /// glb's emission colour.</summary>
    private static BaseMaterial3D? OwnGlow(MeshInstance3D mi, Color? colour)
    {
        var source = mi.MaterialOverride as BaseMaterial3D ?? (mi.Mesh != null && mi.Mesh.GetSurfaceCount() > 0 ? mi.GetActiveMaterial(0) as BaseMaterial3D : null);
        if (source == null) return null;
        var copy = (BaseMaterial3D)source.Duplicate();
        copy.EmissionEnabled = true;
        if (colour is { } c) copy.Emission = c;
        if (mi.MaterialOverride != null) mi.MaterialOverride = copy;
        else mi.SetSurfaceOverrideMaterial(0, copy);
        return copy;
    }

    // ---------------------------------------------------------------- dust

    private static StandardMaterial3D? _puff;

    private static StandardMaterial3D Puff => _puff ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 1f,
    };

    /// <summary>Fine grains of the ground's dust (SprayRig's haze colour) blasted out low along the ground from under the
    /// ship's burn (owner 2026-11-09: "the dust particles need to be much smaller"): tiny 0.1-radius specks at a fifth
    /// of the old puffs' size, many of them, fast outward, kept flat and low; world-space, so they stay behind as it
    /// climbs. The burst version throws a ring of them out at once. The soft cloud is RocketPlume's smoke.</summary>
    internal static CpuParticles3D Downwash(Color haze, bool burst)
    {
        var grain = haze with { A = Mathf.Max(haze.A, 0.6f) };
        var ramp = new Gradient { Offsets = new[] { 0f, 0.08f, 0.55f, 1f }, Colors = new[] { grain with { A = 0f }, grain, grain with { A = grain.A * 0.6f }, grain with { A = 0f } } };
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, 0.5f));
        grow.AddPoint(new Vector2(1f, 1f));
        return new CpuParticles3D
        {
            Name = burst ? "DropShipBurst" : "DropShipWash",
            Amount = burst ? 300 : 220,
            Lifetime = burst ? 1.9f : 1.4f,
            OneShot = burst,
            Explosiveness = burst ? 0.92f : 0f,
            Randomness = 0.3f,
            Emitting = false,
            LocalCoords = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = burst ? 4f : 4f,
            EmissionRingInnerRadius = burst ? 1f : 1.5f,
            EmissionRingHeight = 0.2f,
            Direction = new Vector3(1f, 0.06f, 0f),
            Spread = 180f,
            Flatness = 0.95f,
            InitialVelocityMin = burst ? 14f : 7f,
            InitialVelocityMax = burst ? 28f : 14f,
            RadialAccelMin = burst ? 0f : 8f,
            RadialAccelMax = burst ? 3f : 16f,
            Gravity = new Vector3(0, -0.6f, 0),
            DampingMin = burst ? 6f : 3f,
            DampingMax = burst ? 9f : 5f,
            ScaleAmountMin = 1.4f,
            ScaleAmountMax = 3.4f,
            ScaleAmountCurve = grow,
            ColorRamp = ramp,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new SphereMesh { Radius = 0.1f, Height = 0.2f, RadialSegments = 4, Rings = 2, Material = Puff },
        };
    }

    // ---------------------------------------------------------------- stand-in

    /// <summary>A primitive drop ship with the same named parts, for when dropship.glb isn't there or not imported yet.</summary>
    private static Node3D Primitive(Color owner)
    {
        var n = new Node3D { Name = "dropship" };
        var hull = new StandardMaterial3D { AlbedoColor = new Color(0.84f, 0.86f, 0.9f), Metallic = 0.3f, Roughness = 0.45f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.16f, 0.18f), Metallic = 0.5f, Roughness = 0.5f };
        var stripe = new StandardMaterial3D { AlbedoColor = owner, EmissionEnabled = true, Emission = owner, EmissionEnergyMultiplier = 0.35f };
        void Box(Node3D parent, string name, Vector3 size, Vector3 at, Material m) =>
            parent.AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, Position = at, MaterialOverride = m });
        Box(n, "Hull", new Vector3(4.6f, 4.1f, 12.6f), new Vector3(0, 2.9f, 0), hull);
        Box(n, "Stripe", new Vector3(4.64f, 0.16f, 12f), new Vector3(0, 1.8f, 0), stripe);
        for (var s = -1; s <= 1; s += 2)
        {
            Box(n, $"Pod{s}", new Vector3(1.3f, 1.3f, 9.9f), new Vector3(s * 3.75f, 3.35f, 1.4f), hull);
            Box(n, $"Pylon{s}", new Vector3(1.6f, 0.3f, 3.6f), new Vector3(s * 2.9f, 3.45f, 1.5f), hull);
        }
        var ramp = new Node3D { Name = "Ramp", Position = HingeDefault };
        Box(ramp, "RampSlab", new Vector3(3.5f, RampLength, 0.26f), new Vector3(0, RampLength / 2f, 0.13f), hull);
        n.AddChild(ramp);
        for (var k = 0; k < 4; k++)
        {
            var s = k % 2 == 0 ? -1 : 1;
            var leg = new Node3D { Name = $"Leg{k}", Position = new Vector3(s * 3.75f, 2.55f, k < 2 ? -2.4f : 5.5f) };
            leg.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.1f, Height = 2.4f }, Position = new Vector3(s * 0.25f, -1.2f, 0), MaterialOverride = dark });
            var foot = new Node3D { Name = $"Foot{k}", Position = new Vector3(s * 0.5f, -2.37f, 0) };
            foot.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.1f }, Position = new Vector3(0, -0.13f, 0), MaterialOverride = dark });
            leg.AddChild(foot);
            n.AddChild(leg);
        }
        var spots = new[] { new Vector3(-3.75f, 2.63f, 1.55f), new Vector3(3.75f, 2.63f, 1.55f), new Vector3(-0.95f, 0.78f, -3.1f), new Vector3(0.95f, 0.78f, -3.1f) };
        for (var k = 0; k < 6; k++)
        {
            var glow = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.2f, 0.22f), EmissionEnabled = true, Emission = k < 4 ? LiftColour : ImpulseColour };
            var at = k < 4 ? spots[k] : new Vector3(k == 4 ? -3.75f : 3.75f, 3.2f, 6.4f);
            Box(n, $"Thruster{k}", k < 4 ? new Vector3(0.9f, 0.04f, 1.5f) : new Vector3(0.9f, 0.66f, 0.04f), at, glow);
        }
        var amber = new StandardMaterial3D { ResourceName = "Amber", AlbedoColor = new Color(1f, 0.55f, 0.12f), EmissionEnabled = true, Emission = new Color(1f, 0.5f, 0.1f) };
        n.AddChild(new MeshInstance3D { Name = "Beacon", Mesh = new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.1f, Height = 0.16f }, Position = new Vector3(0, 5.45f, 0.4f), MaterialOverride = amber });
        return n;
    }
}
