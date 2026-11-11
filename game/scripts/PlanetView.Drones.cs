using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Scout drones in flight (docs/missions-contract.md B; render-only). Drones are SurfaceUnits (DefId scout_drone,
// PlanetSurface.IsAerial); UpdateRovers skips them and this draws them instead (BuildingModels.ScoutDrone ×DroneScale):
//   Docked     on its pad's bay (Bay0.. of the pad model), rotors still, skids down, nav lights dim (the pad shows
//              the charge: PlanetView.DronePad.cs)
//   Launching  rotors spin up over the first 15% of Phase, then it climbs straight up off the bay to cruise height,
//              skids tucking on the way
//   Flying /   at the sim's straight-line position, CruiseHeight above the ground under it and up to 40 ahead (it
//   Returning  climbs early for a ridge, sinks slowly after it: never through a mountain), yawing into its heading,
//              banking into turns, nose down and nacelles tipped forward with speed
//   Loitering  circles the target (LoiterRadius, ~0.6 rad/s), camera gimbal aimed at the tile's centre
//   Landing    slides over its bay, sinks onto it, skids down, rotors spin down once it sits
//   Lost       battery gone: wobbles, spirals down over 2.2 s, a spark burst and a smoke plume where it hits, tipped on
//              its side until the sim removes it
// Lights: red/green nav lights, the red beacon at 1 Hz, the white strobe double-flashing every 1.2 s while airborne.
// Sounds: the nearest DroneBuzzSlots airborne drones within DroneHearRange hum (exocomp/hover_loop pitched down, higher
// with rotor speed), pooled players, sticky slots, no allocation per frame. Rivals only while in sight (Shows).
// Rings (ours): a pulsing ring on each flying drone's target tile; ShowDroneTarget previews one while an order is armed.
public partial class PlanetView
{
    private const float DroneScale = 1.4f;
    private const float CruiseHeight = 14f;
    private const float LoiterRadius = 7f;
    private const float LostFallSeconds = 2.2f;
    private const int DroneBuzzSlots = 3;
    private const float DroneHearRange = 500f;

    private sealed class DroneVisual
    {
        public required Node3D Holder, Model;
        public readonly Node3D?[] Rotors = new Node3D?[4], Tilts = new Node3D?[4];
        public readonly Basis[] RotorRest = new Basis[4], TiltRest = new Basis[4];
        public Node3D? Camera, Skid;
        public Basis CameraRest;
        public Vector3 SkidScale = Vector3.One;
        public readonly List<(BaseMaterial3D Material, float Energy)> Beacon = new(), Strobe = new(), Nav = new(), Lens = new();
        public required MeshInstance3D Ring;
        public required Label3D Label;
        public Vector3 Pos;
        public bool Placed;
        public float Yaw, Bank, Pitch, Alt, RotorSpeed, RotorAngle, Tilt, Skids = 1f, LoiterAngle, LoiterBlend, Seed;
        public DroneState Last = DroneState.None;
        public Vector3 LostFrom;
        public float LostYaw;
        public bool Crashed;
        public CpuParticles3D? Smoke;
    }

    private readonly Dictionary<int, DroneVisual> _scoutDrones = new();
    private readonly HashSet<int> _scoutAlive = new();
    private readonly List<int> _scoutGone = new();
    private Node3D? _droneRoot;

    /// <summary>True for a unit drawn as a drone (not by UpdateRovers).</summary>
    private bool IsDroneUnit(SurfaceUnit u) => u.DefId == "scout_drone" || _surface.IsAerial(u);

    /// <summary>A drone's model holder (the feed's chase camera, the driver's screen positions), or null.</summary>
    private Node3D? DroneModel(int unitId) => _scoutDrones.TryGetValue(unitId, out var v) ? v.Holder : null;

    /// <summary>Where a drone is drawn now (in the air), or null if it isn't drawn.</summary>
    public Vector3? DronePosition(int unitId) => _scoutDrones.TryGetValue(unitId, out var v) && v.Placed ? v.Pos : null;

    /// <summary>Once a frame after UpdateRovers.</summary>
    private void UpdateScoutDrones(float alpha, float delta)
    {
        _scoutAlive.Clear();
        var now = _sim.Tick + alpha;
        foreach (var u in _surface.Units)
        {
            if (!IsDroneUnit(u)) continue;
            _scoutAlive.Add(u.Id);
            if (!_scoutDrones.TryGetValue(u.Id, out var v)) _scoutDrones[u.Id] = v = NewDroneVisual(u);
            var shown = Shows(u);
            v.Holder.Visible = shown;
            v.Label.Visible = shown && v.Label.Text.Length > 0;
            PoseDrone(u, v, alpha, now, delta);
        }
        if (_scoutDrones.Count != _scoutAlive.Count)
        {
            _scoutGone.Clear();
            foreach (var id in _scoutDrones.Keys)
                if (!_scoutAlive.Contains(id)) _scoutGone.Add(id);
            foreach (var id in _scoutGone)
            {
                _scoutDrones[id].Holder.QueueFree();
                _scoutDrones[id].Label.QueueFree();
                _scoutDrones.Remove(id);
            }
        }
        UpdateDroneTargets(delta);
        UpdateDroneBuzz(delta);
    }

    private DroneVisual NewDroneVisual(SurfaceUnit u)
    {
        if (_droneRoot == null)
        {
            _droneRoot = new Node3D { Name = "ScoutDrones" };
            AddChild(_droneRoot);
        }
        var colour = PlanetDriver.ColorOf(u.Owner);
        var model = BuildingModels.ScoutDrone(colour);
        RoverLook.Paint(model, u.Name, u.Number);
        model.Scale = Vector3.One * DroneScale;
        var holder = new Node3D { Name = $"Drone{u.Id}" };
        holder.AddChild(model);
        var ring = new MeshInstance3D
        {
            Name = "Selected",
            Mesh = new TorusMesh { InnerRadius = 2.3f, OuterRadius = 2.55f, Rings = 32 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.95f, 0.5f), EmissionEnabled = true, Emission = new Color(1f, 0.95f, 0.5f),
                EmissionEnergyMultiplier = 2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        holder.AddChild(ring);
        _droneRoot.AddChild(holder);
        var label = new Label3D
        {
            FontSize = 30, PixelSize = 0.08f, OutlineSize = 10, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true, Modulate = new Color(1f, 0.55f, 0.35f), Text = "", Visible = false,
        };
        _droneRoot.AddChild(label);

        var v = new DroneVisual { Holder = holder, Model = model, Ring = ring, Label = label, Seed = (u.Id * 0.6180339f) % 1f };
        for (var k = 0; k < 4; k++)
        {
            v.Rotors[k] = model.FindChild($"Rotor{k}", true, false) as Node3D;
            v.Tilts[k] = model.FindChild($"Tilt{k}", true, false) as Node3D;
            if (v.Rotors[k] is { } r) v.RotorRest[k] = r.Basis;
            if (v.Tilts[k] is { } t) v.TiltRest[k] = t.Basis;
        }
        v.Camera = model.FindChild("Camera", true, false) as Node3D;
        if (v.Camera != null) v.CameraRest = v.Camera.Basis;
        v.Skid = model.FindChild("Skid", true, false) as Node3D;
        if (v.Skid != null) v.SkidScale = v.Skid.Scale;
        Lamps(model, "Beacon", v.Beacon);
        Lamps(model, "Strobe", v.Strobe);
        Lamps(model, "NavL", v.Nav);
        Lamps(model, "NavR", v.Nav);
        Lamps(model, "Lens", v.Lens);
        return v;
    }

    /// <summary>The emissive materials of a named part (itself or the meshes under it).</summary>
    private static void Lamps(Node3D model, string part, List<(BaseMaterial3D, float)> into)
    {
        if (model.FindChild(part, true, false) is not Node3D node) return;
        void Walk(Node n)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null)
                for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                    if ((mi.GetSurfaceOverrideMaterial(i) ?? mi.MaterialOverride ?? mi.Mesh.SurfaceGetMaterial(i)) is BaseMaterial3D m)
                    {
                        m.EmissionEnabled = true;
                        into.Add((m, m.EmissionEnergyMultiplier > 0.05f ? m.EmissionEnergyMultiplier : 3f));
                    }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(node);
    }

    /// <summary>A drone's pad bay in world space (position and the pad's turn), or false when the pad isn't drawn
    /// (PlanetView.DronePad.cs keeps the finished pads' parts).</summary>
    private bool BayPose(int padId, int bay, out Vector3 at, out Basis facing)
    {
        at = Vector3.Zero;
        facing = Basis.Identity;
        if (!_pads.TryGetValue(padId, out var p) || !GodotObject.IsInstanceValid(p.Model)) return false;
        facing = new Basis(Vector3.Up, Mathf.DegToRad(-60f * p.Facing));
        if (bay >= 0 && bay < p.Bays.Length && p.Bays[bay] is { } node && node.IsInsideTree())
        {
            at = node.GlobalPosition;
            return true;
        }
        var local = BuildingModels.DronePadBays[Mathf.Clamp(bay, 0, BuildingModels.DronePadBays.Length - 1)];
        at = p.Model.Position + facing * local;
        return true;
    }

    /// <summary>Cruise altitude over a point: CruiseHeight above the highest ground under it and ahead.</summary>
    private float CruiseAt(Vector3 p, Vector3 ahead)
    {
        var top = GroundAt(p);
        if (ahead != Vector3.Zero)
            foreach (var d in DroneLookAhead) top = Mathf.Max(top, GroundAt(p + ahead * d));
        return top + CruiseHeight;
    }

    private static readonly float[] DroneLookAhead = { 10f, 22f, 40f };

    private void PoseDrone(SurfaceUnit u, DroneVisual v, float alpha, float now, float delta)
    {
        var info = _surface.DroneOf(u);
        var state = info.State;
        var phase = Mathf.Clamp(info.Phase.ToFloat(), 0f, 1f);
        var sim = UnitPos(u, alpha); // on the ground
        var hasBay = BayPose(info.Pad, info.Bay, out var bay, out var padFacing);
        if (!hasBay) bay = sim;
        var k = 1f - Mathf.Exp(-delta * 5f);
        var prev = v.Pos;
        var heading = (sim - prev) with { Y = 0f };
        var ahead = heading.LengthSquared() > 0.01f ? heading.Normalized() : Vector3.Zero;
        float targetRotor;
        float targetTilt = 0f;
        var skidsDown = false;
        Vector3 target;
        Vector3? aim = null;
        if (state != DroneState.Lost) v.Crashed = false;

        switch (state)
        {
            case DroneState.Docked:
                target = bay;
                v.Alt = 0f;
                targetRotor = 0f;
                skidsDown = true;
                v.Yaw = Mathf.LerpAngle(v.Yaw, YawOf(padFacing), Mathf.Min(1f, delta * 3f));
                break;
            case DroneState.Launching:
            {
                var climb = Mathf.SmoothStep(0.15f, 1f, phase);
                target = bay;
                v.Alt = climb * (CruiseAt(bay, Vector3.Zero) - bay.Y) + 0.0f;
                targetRotor = phase < 0.15f ? Mathf.Lerp(8f, 70f, phase / 0.15f) : 70f;
                skidsDown = phase < 0.5f;
                break;
            }
            case DroneState.Landing:
            {
                var over = Mathf.SmoothStep(0f, 0.4f, phase);
                var sink = 1f - Mathf.SmoothStep(0.25f, 0.92f, phase);
                var from = v.Placed ? v.Pos with { Y = bay.Y } : bay;
                target = from.Lerp(bay, over);
                var cruise = CruiseAt(bay, Vector3.Zero) - bay.Y;
                v.Alt = Mathf.Min(v.Alt, Mathf.Max(0f, cruise * sink));
                targetRotor = phase < 0.92f ? 58f : Mathf.Lerp(58f, 0f, (phase - 0.92f) / 0.08f);
                skidsDown = phase > 0.45f;
                break;
            }
            case DroneState.Loitering:
            {
                v.LoiterBlend = Mathf.MoveToward(v.LoiterBlend, 1f, delta / 1.5f);
                v.LoiterAngle += delta * 0.6f;
                var centre = info.Target >= 0 ? TileCentre(info.Target) : sim;
                var circle = new Vector3(Mathf.Cos(v.LoiterAngle), 0f, Mathf.Sin(v.LoiterAngle)) * LoiterRadius * v.LoiterBlend;
                target = new Vector3(sim.X, 0f, sim.Z) + circle;
                var tangent = new Vector3(-Mathf.Sin(v.LoiterAngle), 0f, Mathf.Cos(v.LoiterAngle));
                ahead = tangent;
                v.Alt = Mathf.MoveToward(v.Alt, CruiseAt(target, ahead) - GroundAt(target), delta * 10f);
                targetRotor = 52f;
                targetTilt = 18f;
                aim = centre;
                break;
            }
            case DroneState.Lost:
            {
                if (!v.Crashed && v.LostFrom == Vector3.Zero)
                {
                    v.LostFrom = v.Placed ? v.Pos : sim + Vector3.Up * CruiseHeight;
                    v.LostYaw = v.Yaw;
                }
                var t = info.LostAtTick >= 0 ? (now - info.LostAtTick) / Simulation.TickRate : LostFallSeconds;
                var f = Mathf.Clamp(t / LostFallSeconds, 0f, 1f);
                var ground = GroundAt(v.LostFrom);
                var spiral = (1f - f) * 3.5f;
                var a = v.Seed * Mathf.Tau + t * 5f;
                target = new Vector3(v.LostFrom.X + Mathf.Cos(a) * spiral, 0f, v.LostFrom.Z + Mathf.Sin(a) * spiral);
                v.Alt = Mathf.Lerp(v.LostFrom.Y - ground, 0.2f, f * f);
                v.Yaw = v.LostYaw + t * 4f * (1f - f);
                targetRotor = Mathf.Lerp(40f, 0f, f);
                if (f >= 1f && !v.Crashed)
                {
                    v.Crashed = true;
                    var hit = target with { Y = ground + 0.5f };
                    if (v.Holder.Visible) Blast(hit, 0);
                    v.Smoke ??= BuildingModels.Puffs("CrashSmoke", Vector3.Zero, new Color(0.16f, 0.15f, 0.14f), 22, 2.6f, 0.8f, 2.2f, 0.25f);
                    if (v.Smoke.GetParent() == null) v.Holder.AddChild(v.Smoke);
                    v.Smoke.Emitting = true;
                }
                break;
            }
            default: // Flying, Returning (and an aerial unit the stubs know nothing about)
            {
                v.LoiterBlend = Mathf.MoveToward(v.LoiterBlend, 0f, delta / 1.5f);
                target = sim;
                var want = CruiseAt(sim, ahead) - GroundAt(sim);
                var ground = GroundAt(sim);
                // climb quickly for a ridge, sink slowly after it (relative to the ground under it now)
                var absNow = v.Alt + GroundAt(v.Placed ? v.Pos : sim);
                var absWant = want + ground;
                absNow = absWant > absNow ? Mathf.MoveToward(absNow, absWant, delta * 18f) : Mathf.MoveToward(absNow, absWant, delta * 6f);
                v.Alt = Mathf.Max(absNow - ground, 2f);
                if (!v.Placed) v.Alt = want;
                targetRotor = 48f;
                var speed = v.Placed && delta > 0f ? ((sim - prev) with { Y = 0f }).Length() / delta : 0f;
                targetTilt = Mathf.Clamp(speed * 2.2f, 0f, 60f);
                break;
            }
        }
        if (state != DroneState.Lost) v.LostFrom = Vector3.Zero;

        // Position: the ground point (smoothed) + altitude.
        var groundPoint = target with { Y = 0f };
        if (!v.Placed || state is DroneState.Docked or DroneState.Launching) v.Pos = groundPoint;
        else v.Pos = (v.Pos with { Y = 0f }).Lerp(groundPoint, state == DroneState.Lost ? 1f : Mathf.Min(1f, k * 3f));
        var baseY = state is DroneState.Docked or DroneState.Launching or DroneState.Landing ? bay.Y : GroundAt(v.Pos);
        v.Pos = v.Pos with { Y = baseY + v.Alt };
        v.Placed = true;

        // Heading, bank and pitch from the drawn motion.
        var moved = (v.Pos - prev) with { Y = 0f };
        var spd = delta > 0f ? moved.Length() / delta : 0f;
        var yawRate = 0f;
        if (state is not (DroneState.Docked or DroneState.Lost) && moved.LengthSquared() > 0.0004f)
        {
            var dir = moved.Normalized();
            var want = Mathf.Atan2(-dir.X, -dir.Z);
            var before = v.Yaw;
            v.Yaw = Mathf.LerpAngle(v.Yaw, want, Mathf.Min(1f, delta * 3.5f));
            yawRate = delta > 0f ? Mathf.Wrap(v.Yaw - before, -Mathf.Pi, Mathf.Pi) / delta : 0f;
        }
        var airborne = v.Alt > 1.2f && state != DroneState.Docked;
        var wobble = state == DroneState.Lost && !v.Crashed ? Mathf.Sin(_time * 13f + v.Seed * 9f) * 0.35f : 0f;
        v.Bank = Mathf.Lerp(v.Bank, airborne ? Mathf.Clamp(yawRate * 0.35f, -0.45f, 0.45f) + wobble : 0f, Mathf.Min(1f, delta * 4f));
        v.Pitch = Mathf.Lerp(v.Pitch, airborne ? -Mathf.Clamp(spd * 0.012f, 0f, 0.2f) : 0f, Mathf.Min(1f, delta * 3f));
        var crashTip = v.Crashed ? new Basis(Vector3.Back, 0.9f) : Basis.Identity;
        v.Holder.Transform = new Transform3D(new Basis(Vector3.Up, v.Yaw) * new Basis(Vector3.Right, v.Pitch) * new Basis(Vector3.Back, v.Bank) * crashTip, v.Pos);
        if (v.Smoke != null && !v.Crashed && v.Smoke.Emitting) v.Smoke.Emitting = false;

        // Rotors, nacelles, skids, camera.
        v.RotorSpeed = Mathf.MoveToward(v.RotorSpeed, v.Crashed ? 0f : targetRotor, delta * 45f);
        v.RotorAngle = Mathf.PosMod(v.RotorAngle + v.RotorSpeed * delta, Mathf.Tau * 3f);
        v.Tilt = Mathf.MoveToward(v.Tilt, targetTilt, delta * 50f);
        for (var i = 0; i < 4; i++)
        {
            var spin = (i == 0 || i == 3) ? v.RotorAngle : -v.RotorAngle;
            if (v.Rotors[i] is { } r) r.Basis = v.RotorRest[i] * new Basis(Vector3.Up, spin + i * 0.7f);
            if (v.Tilts[i] is { } t) t.Basis = v.TiltRest[i] * new Basis(Vector3.Right, -Mathf.DegToRad(v.Tilt));
        }
        v.Skids = Mathf.MoveToward(v.Skids, skidsDown || !airborne ? 1f : 0.35f, delta * 1.5f);
        if (v.Skid != null) v.Skid.Scale = v.SkidScale with { Y = v.SkidScale.Y * v.Skids };
        if (v.Camera != null)
        {
            // aim the gimbal (its rest looks along local -Y) at the loiter target, else straight down
            var want = v.CameraRest;
            if (aim is { } point && v.Camera.GetParent() is Node3D parent && parent.IsInsideTree())
            {
                var d = parent.GlobalTransform.AffineInverse() * point - v.Camera.Position;
                if (d.LengthSquared() > 0.01f) want = new Basis(new Quaternion(Vector3.Down, d.Normalized()));
            }
            v.Camera.Basis = v.Camera.Basis.Orthonormalized().Slerp(want.Orthonormalized(), Mathf.Min(1f, delta * 3f));
        }

        // Lights.
        var lit = state != DroneState.Docked && !v.Crashed;
        var tick = _time + v.Seed * 1.2f;
        var beacon = lit && Mathf.PosMod(tick, 1f) < 0.12f ? 1f : (state == DroneState.Docked ? 0.04f : 0.08f);
        var s = Mathf.PosMod(tick, 1.2f);
        var strobe = lit && (s < 0.05f || (s > 0.16f && s < 0.21f)) ? 4f : 0.02f;
        foreach (var (m, e) in v.Beacon) m.EmissionEnergyMultiplier = e * beacon;
        foreach (var (m, e) in v.Strobe) m.EmissionEnergyMultiplier = e * strobe;
        foreach (var (m, e) in v.Nav) m.EmissionEnergyMultiplier = e * (lit ? 1f : 0.12f);
        foreach (var (m, e) in v.Lens) m.EmissionEnergyMultiplier = e * (v.Crashed ? 0f : 1f);

        // Selection ring, label.
        v.Ring.Visible = SelectedUnits.Contains(u.Id) && v.Holder.Visible;
        if (v.Ring.Visible) v.Ring.Position = new Vector3(0, 0.7f, 0);
        var text = state switch
        {
            DroneState.Lost => "Signal lost",
            DroneState.Returning when info.ReturnReason == "battery" => "Battery low: returning",
            DroneState.Returning when info.ReturnReason == "coverage" => "Out of comms range: returning",
            _ => "",
        };
        if (v.Label.Text != text) v.Label.Text = text;
        v.Label.Visible = v.Holder.Visible && text.Length > 0;
        if (v.Label.Visible) v.Label.Position = v.Pos + new Vector3(0, 6f, 0);
        v.Last = state;
    }

    private static float YawOf(Basis b)
    {
        var f = -b.Z;
        return Mathf.Atan2(-f.X, -f.Z);
    }

    // ---------------------------------------------------------------- target rings

    private readonly List<MeshInstance3D> _droneRings = new();
    private StandardMaterial3D? _droneRingMaterial;
    private MeshInstance3D? _droneTargetPreview;
    private StandardMaterial3D? _droneTargetMaterial;
    private int _droneTargetTile = -1;
    private bool _droneTargetOk;

    /// <summary>While a drone launch / move order is armed: a ring at the hovered target tile, green when the order
    /// can go there, red when it can't (-1 hides it). The driver calls it every frame while armed.</summary>
    public void ShowDroneTarget(int tile, bool ok)
    {
        _droneTargetTile = tile;
        _droneTargetOk = ok;
    }

    private void UpdateDroneTargets(float delta)
    {
        _droneRingMaterial ??= RingMaterial(new Color(0.45f, 0.9f, 1f, 0.6f));
        var used = 0;
        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 3f);
        foreach (var u in _surface.Units)
        {
            if (u.Owner != _localPlayer || !_scoutDrones.ContainsKey(u.Id)) continue;
            var info = _surface.DroneOf(u);
            if (info.Target < 0 || info.State is not (DroneState.Launching or DroneState.Flying or DroneState.Loitering)) continue;
            if (used == _droneRings.Count)
            {
                var ring = new MeshInstance3D
                {
                    Name = $"DroneTarget{used}",
                    Mesh = new TorusMesh { InnerRadius = LoiterRadius - 0.35f, OuterRadius = LoiterRadius + 0.35f, Rings = 48, RingSegments = 4 },
                    MaterialOverride = _droneRingMaterial,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                (_droneRoot ?? this).AddChild(ring);
                _droneRings.Add(ring);
            }
            var r = _droneRings[used++];
            var c = TileCentre(info.Target);
            r.Position = c with { Y = GroundAt(c) + 0.6f };
            var loiter = info.State == DroneState.Loitering;
            r.Scale = Vector3.One * (loiter ? 1f + 0.06f * pulse : 1f);
            r.Visible = true;
        }
        for (var i = used; i < _droneRings.Count; i++) _droneRings[i].Visible = false;
        _droneRingMaterial.AlbedoColor = new Color(0.45f, 0.9f, 1f, 0.35f + 0.35f * pulse);

        if (_droneTargetTile >= 0)
        {
            if (_droneTargetPreview == null)
            {
                _droneTargetMaterial = RingMaterial(new Color(0.4f, 1f, 0.5f, 0.7f));
                _droneTargetPreview = new MeshInstance3D
                {
                    Name = "DroneTargetPreview",
                    Mesh = new TorusMesh { InnerRadius = LoiterRadius - 0.45f, OuterRadius = LoiterRadius + 0.45f, Rings = 48, RingSegments = 4 },
                    MaterialOverride = _droneTargetMaterial,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(_droneTargetPreview);
            }
            var c = TileCentre(_droneTargetTile);
            _droneTargetPreview.Position = c with { Y = GroundAt(c) + 0.6f };
            _droneTargetPreview.Visible = true;
            _droneTargetMaterial!.AlbedoColor = (_droneTargetOk ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.35f, 0.3f)) with { A = 0.55f + 0.3f * pulse };
        }
        else if (_droneTargetPreview != null) _droneTargetPreview.Visible = false;
    }

    private static StandardMaterial3D RingMaterial(Color colour) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = colour,
        NoDepthTest = true,
        RenderPriority = 2,
    };

    // ---------------------------------------------------------------- rotor buzz

    private Node3D? _buzzRoot;
    private readonly AudioStreamPlayer3D?[] _buzz = new AudioStreamPlayer3D?[DroneBuzzSlots];
    private readonly int[] _buzzUnit = { -1, -1, -1 };
    private readonly float[] _buzzLevel = new float[DroneBuzzSlots];
    private readonly int[] _buzzNear = new int[DroneBuzzSlots];
    private readonly float[] _buzzDist = new float[DroneBuzzSlots];

    private void UpdateDroneBuzz(float delta)
    {
        if (_buzzRoot == null)
        {
            if (_scoutDrones.Count == 0) return;
            _buzzRoot = new Node3D { Name = "DroneBuzz" };
            AddChild(_buzzRoot);
            var stream = Sfx.Load("exocomp/hover_loop");
            for (var i = 0; i < DroneBuzzSlots; i++)
            {
                var p = Sfx.Player3D($"Buzz{i}", 100f, 900f);
                p.Stream = stream;
                _buzzRoot.AddChild(p);
                _buzz[i] = p;
            }
        }
        // The nearest airborne drones (a small insertion sort over fixed arrays).
        for (var i = 0; i < DroneBuzzSlots; i++)
        {
            _buzzNear[i] = -1;
            _buzzDist[i] = float.MaxValue;
        }
        if (Heard)
            foreach (var (id, v) in _scoutDrones)
            {
                if (!v.Holder.Visible || v.RotorSpeed < 5f) continue;
                var d = v.Pos.DistanceTo(_eye);
                if (d > DroneHearRange) continue;
                for (var i = 0; i < DroneBuzzSlots; i++)
                {
                    if (d >= _buzzDist[i]) continue;
                    for (var j = DroneBuzzSlots - 1; j > i; j--)
                    {
                        _buzzDist[j] = _buzzDist[j - 1];
                        _buzzNear[j] = _buzzNear[j - 1];
                    }
                    _buzzDist[i] = d;
                    _buzzNear[i] = id;
                    break;
                }
            }
        // Sticky slots: a slot keeps its drone while it is among the nearest; a free (faded) slot takes a new one.
        for (var i = 0; i < DroneBuzzSlots; i++)
        {
            var keep = _buzzUnit[i] >= 0 && System.Array.IndexOf(_buzzNear, _buzzUnit[i]) >= 0;
            if (!keep && _buzzLevel[i] <= 0.01f)
            {
                _buzzUnit[i] = -1;
                foreach (var id in _buzzNear)
                    if (id >= 0 && System.Array.IndexOf(_buzzUnit, id) < 0)
                    {
                        _buzzUnit[i] = id;
                        keep = true;
                        break;
                    }
            }
            _buzzLevel[i] = Mathf.MoveToward(_buzzLevel[i], keep ? 1f : 0f, delta / 0.3f);
            var p = _buzz[i];
            if (p == null || p.Stream == null) continue;
            if (_buzzUnit[i] >= 0 && _scoutDrones.TryGetValue(_buzzUnit[i], out var v) && _buzzLevel[i] > 0.01f)
            {
                p.Position = v.Pos;
                p.VolumeDb = Mathf.LinearToDb(_buzzLevel[i] * Mathf.Clamp(v.RotorSpeed / 50f, 0.2f, 1.2f)) - 4f;
                p.PitchScale = 0.55f + v.RotorSpeed / 160f + v.Seed * 0.06f;
                if (!p.Playing) p.Play();
            }
            else if (p.Playing) p.Stop();
        }
    }
}
