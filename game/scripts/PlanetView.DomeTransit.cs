using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// A ground vehicle cycling through a dome's containment field (docs/missions-contract.md A; render-only). The sim stops
// it at the hex edge where the shell stands for domes.transit_seconds (PlanetSurface.DomeTransitOf: Fraction 0 -> 1 over
// the pause), then it drives through (LastDomeTransitOf: the after-glow). The view, per vehicle:
//   its own field   shaders/vehicle_field.gdshader on a unit sphere fitted to the model's box + 0.6, warm white-gold
//                   (ours; a rival's leans to its player colour), following the model.
//   f 0.00-0.06     ignition: a short flash at the roof emitters, a field hum starts low
//   f 0.02-0.34     the field climbs from the ground to the roof behind a bright front (rise)
//   f 0.14-0.42     it stutters as it locks (lock_flicker), a lock flash at f 0.37
//   f 0.30-0.42     contact: the bubble swells toward the shell to touch it (bulge), a crackle; on the shell
//                   (shaders/containment_field.gdshader contact slots) the shell sags in to meet it, a halo grows and
//                   rings start travelling out over the dome, fading with distance
//   f 0.32-0.95     arcs: three jagged camera-facing ribbons crackle between the bubble's seam and the shell (20 Hz),
//                   a flickering gold-blue light at the contact lights the vehicle and the ground
//   f 0.40-0.72     the colours exchange: the bubble's seam side and the part of it past the shell turn the dome's
//                   blue (the exact cut where the two surfaces meet is a crackling line), the shell round the contact
//                   warms toward the vehicle's gold
//   f 0.45-0.75     the aperture ("iris") opens in the shell round the vehicle, its rim bright with sparks and short
//                   streaks; f 0.85-1 it holds, the arcs thin out
//   after, entering the bubble melts into the dome: turns its colour (merge 0-0.6 s), a soft flash at 0.25 s, fades
//                   out by 1 s; the iris closes 0.5-1.1 s with a closing flash on its rim at 1 s; the contact fades
//                   by 1.6 s
//   after, leaving  the bubble stays round the vehicle as it drives off, its seam fading 0.4-1 s, the iris closing
//                   0.35-0.9 s (flash at 0.8 s); from 1.2 s it dissolves bottom-up behind a ragged edge, gone by 2.2 s
// Sounds (cheap, the exocomp kit): exocomp/beam_loop as the field hum (pitch and level rise with it), exocomp/lock at
// contact, exocomp/hop as the iris opens, exocomp/complete (high) as it merges, exocomp/hop (low) as it dissolves.
// Another player's transit only while ShowsLive. No allocation per frame (visuals made once per transit; the
// dictionaries' enumerators are structs; StringNames cached).
// Debug: Shift+F10 in a debug build fakes a transit on each selected vehicle (entering if it stands outside the nearest
// field, else leaving; best on a parked vehicle at a field's edge; without a field the bubble plays alone).
public partial class PlanetView
{
    private const int ShellContacts = 4;
    private const float FieldPad = 0.6f;
    private const float FakePauseSeconds = 2.5f;

    private static readonly Color FieldGold = new(1f, 0.84f, 0.52f);

    private static readonly StringName PRise = "rise", PLock = "lock_flicker", PFlash = "flash", PSeam = "seam",
        PSeamDir = "seam_dir", PBulge = "bulge", PMerge = "merge", PDissolve = "dissolve", PStrength = "strength",
        PShellOn = "shell_on", PShellCentre = "shell_centre", PShellScale = "shell_scale", PSeamColour = "seam_colour",
        PContactCount = "contact_count", PContactAt = "contact_at", PContactTo = "contact_to", PContactFx = "contact_fx",
        PContactTint = "contact_tint";

    /// <summary>What the transit is doing this frame, from the sim or the debug fake.</summary>
    private struct TransitNow
    {
        public int StructureId;
        public bool Entering;
        public float F;          // 0..1 over the pause
        public float After;      // seconds since it drove through, -1 while paused
        public float Pause;      // the pause, seconds
        public long Key;         // identity of this transit (its start)
    }

    private sealed class TransitVisual
    {
        public required Node3D Root;            // follows the model: origin = the bubble's centre
        public required MeshInstance3D Bubble;
        public required ShaderMaterial Material;
        public required MeshInstance3D Arcs;
        public required ImmediateMesh ArcMesh;
        public required OmniLight3D Light;
        public AudioStreamPlayer3D? Hum;
        public Vector3 Centre, Semi;           // the model's box centre and the bubble's semi-axes (model space)
        public Color Tint;
        public long Key = long.MinValue;
        public Vector3 ShellPoint;
        public bool ShellFrozen;
        public float ArcClock, LastF = -1f, LastAfter = -1f;
        public bool Seen;
        public readonly Vector3[] ArcFrom = new Vector3[3], ArcTo = new Vector3[3];
        public readonly float[] ArcLit = new float[3];
    }

    private readonly Dictionary<int, TransitVisual> _transits = new();
    private readonly List<int> _transitsGone = new();
    private Node3D? _transitRoot;
    private static Shader? _vehicleFieldShader;
    private static SphereMesh? _bubbleMesh;
    private static StandardMaterial3D? _arcMaterial;
    private readonly RandomNumberGenerator _transitRng = new() { Seed = 7331 };

    private int _fakeUnit = -1;
    private float _fakeStart;
    private bool _fakeEntering;
    private int _fakeStructure = -1;

    /// <summary>Debug (render-only): play a transit on this vehicle where it stands, as if it were cycling through the
    /// nearest containment field. False if the vehicle isn't drawn.</summary>
    public bool DebugFakeTransit(int unitId)
    {
        if (!_rovers.TryGetValue(unitId, out var model)) return false;
        _fakeUnit = unitId;
        _fakeStart = _time;
        _fakeStructure = -1;
        _fakeEntering = true;
        var best = float.MaxValue;
        foreach (var (id, dome) in _domes)
        {
            if (dome.Field == null) continue;
            var d = new Vector2(model.Position.X - dome.Field.Position.X, model.Position.Z - dome.Field.Position.Z).Length();
            var edge = Mathf.Abs(d - dome.Field.Scale.X);
            if (edge >= best) continue;
            best = edge;
            _fakeStructure = id;
            _fakeEntering = d > dome.Field.Scale.X;
        }
        return true;
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (!OS.IsDebugBuild() || e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F10, ShiftPressed: true }) return;
        foreach (var id in SelectedUnits)
            if (DebugFakeTransit(id)) break; // one at a time is enough to look at
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Once a frame after UpdateRovers (the models are posed): every vehicle field and its shell contacts.</summary>
    private void UpdateDomeTransits(float alpha, float delta)
    {
        if (_transits.Count == 0 && _fakeUnit < 0 && !DomesWorld) return;
        foreach (var (_, d) in _domes)
        {
            d.ContactsBefore = d.Contacts;
            d.Contacts = 0;
        }
        foreach (var (_, v) in _transits) v.Seen = false;

        var now = _sim.Tick + alpha;
        foreach (var u in _surface.Units)
        {
            if (!TransitOf(u, now, out var t)) continue;
            if (!_rovers.TryGetValue(u.Id, out var model) || !model.Visible || !ShowsLive(u.Owner, u.Tile)) continue;
            if (!_transits.TryGetValue(u.Id, out var v)) _transits[u.Id] = v = NewTransitVisual(u, model);
            if (v.Key != t.Key) ResetTransit(v, t.Key);
            v.Seen = true;
            PoseTransit(u, model, v, t, delta);
        }
        if (_fakeUnit >= 0 && _time - _fakeStart > FakePauseSeconds + 2.6f) _fakeUnit = -1;

        _transitsGone.Clear();
        foreach (var (id, v) in _transits)
            if (!v.Seen) _transitsGone.Add(id);
        foreach (var id in _transitsGone)
        {
            var v = _transits[id];
            v.Root.QueueFree();
            v.Arcs.QueueFree();
            v.Light.QueueFree();
            _transits.Remove(id);
        }

        foreach (var (_, d) in _domes)
        {
            if (d.FieldMaterial == null || (d.Contacts == 0 && d.ContactsBefore == 0)) continue;
            for (var i = d.Contacts; i < ShellContacts; i++) d.ContactAt[i] = d.ContactTo[i] = d.ContactFx[i] = new Color(0, 0, 0, 0);
            d.FieldMaterial.SetShaderParameter(PContactCount, d.Contacts);
            d.FieldMaterial.SetShaderParameter(PContactAt, d.ContactAt);
            d.FieldMaterial.SetShaderParameter(PContactTo, d.ContactTo);
            d.FieldMaterial.SetShaderParameter(PContactFx, d.ContactFx);
        }
    }

    /// <summary>The transit under way or just finished for this unit (or the debug fake), false if none.</summary>
    private bool TransitOf(SurfaceUnit u, float now, out TransitNow t)
    {
        t = default;
        if (u.Id == _fakeUnit)
        {
            var e = _time - _fakeStart;
            t = new TransitNow
            {
                StructureId = _fakeStructure, Entering = _fakeEntering, Pause = FakePauseSeconds, Key = -1 - (long)(_fakeStart * 1000f),
                F = Mathf.Clamp(e / FakePauseSeconds, 0f, 1f), After = e > FakePauseSeconds ? e - FakePauseSeconds : -1f,
            };
            return true;
        }
        if (_surface.DomeTransitOf(u) is { } d)
        {
            var span = Mathf.Max(1f, d.EndTick - d.StartTick);
            t = new TransitNow
            {
                StructureId = d.StructureId, Entering = d.Entering, Pause = span / Simulation.TickRate, Key = d.StartTick,
                F = Mathf.Clamp((now - d.StartTick) / span, 0f, 1f), After = -1f,
            };
            return true;
        }
        if (_surface.LastDomeTransitOf(u) is { } l)
        {
            var after = (now - l.EndTick) / Simulation.TickRate;
            if (after < 0f || after > (l.Entering ? 1.7f : 2.3f)) return false;
            t = new TransitNow
            {
                StructureId = l.StructureId, Entering = l.Entering, Pause = Mathf.Max(1f, l.EndTick - l.StartTick) / Simulation.TickRate,
                Key = l.StartTick, F = 1f, After = after,
            };
            return true;
        }
        return false;
    }

    private TransitVisual NewTransitVisual(SurfaceUnit u, Node3D model)
    {
        if (_transitRoot == null)
        {
            _transitRoot = new Node3D { Name = "DomeTransits" };
            AddChild(_transitRoot);
        }
        _vehicleFieldShader ??= GD.Load<Shader>("res://shaders/vehicle_field.gdshader");
        _bubbleMesh ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 };
        _arcMaterial ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        var (centre, half) = FieldBox(model);
        var semi = half + new Vector3(FieldPad, FieldPad, FieldPad);
        semi.Y = Mathf.Max(semi.Y, 1.2f);
        var tint = u.Owner == _localPlayer ? FieldGold.Lerp(PlanetDriver.ColorOf(u.Owner), 0.12f) : FieldGold.Lerp(PlanetDriver.ColorOf(u.Owner), 0.5f);
        var material = new ShaderMaterial { Shader = _vehicleFieldShader };
        material.SetShaderParameter("field_tint", tint);
        material.SetShaderParameter(PSeamColour, FieldColour);
        material.SetShaderParameter("ground_y", (-0.1f - centre.Y) / semi.Y);
        material.SetShaderParameter("hex_cells", 3.5f);
        material.SetShaderParameter("seed", (u.Id * 0.6180339f) % 1f * 100f);
        material.SetShaderParameter(PRise, 0f);

        var root = new Node3D { Name = $"Transit{u.Id}" };
        var bubble = new MeshInstance3D
        {
            Name = "VehicleField",
            Mesh = _bubbleMesh,
            MaterialOverride = material,
            Scale = semi,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(bubble);
        _transitRoot.AddChild(root);
        var arcMesh = new ImmediateMesh();
        var arcs = new MeshInstance3D { Name = $"TransitArcs{u.Id}", Mesh = arcMesh, MaterialOverride = _arcMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _transitRoot.AddChild(arcs);
        var light = new OmniLight3D { Name = $"TransitLight{u.Id}", LightColor = FieldGold, LightEnergy = 0f, OmniRange = 12f, ShadowEnabled = false, Visible = false };
        light.SetMeta(VehicleLights.NoBoostMeta, true);
        _transitRoot.AddChild(light);
        var v = new TransitVisual { Root = root, Bubble = bubble, Material = material, Arcs = arcs, ArcMesh = arcMesh, Light = light, Centre = centre, Semi = semi, Tint = tint };
        if (Heard) v.Hum = Sfx.Loop(root, "exocomp/beam_loop");
        return v;
    }

    private static void ResetTransit(TransitVisual v, long key)
    {
        v.Key = key;
        v.ShellFrozen = false;
        v.LastF = -1f;
        v.LastAfter = -1f;
        v.ArcClock = 0f;
    }

    private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(a, b, x);
    private static float Bell(float x, float c, float w) => Mathf.Exp(-((x - c) / w) * ((x - c) / w));

    private void PoseTransit(SurfaceUnit u, Node3D model, TransitVisual v, TransitNow t, float delta)
    {
        // Follow the model (a direct child of PlanetView, like our root): the bubble's centre at the box centre.
        var frame = new Transform3D(model.Basis.Orthonormalized(), model.Position);
        v.Root.Transform = frame * new Transform3D(Basis.Identity, v.Centre);
        var b = v.Root.Position;

        // The timeline (see the header).
        var f = t.F;
        var a = t.After;
        var after = a >= 0f;
        float rise, lockFlicker, flash, contact, age, seam, bulgeK, open, exchange, arcs, merge = 0f, dissolve = 0f, strength = 1f, closing = 0f;
        if (!after)
        {
            rise = Smooth(0.02f, 0.34f, f);
            lockFlicker = Smooth(0.14f, 0.24f, f) * (1f - Smooth(0.33f, 0.42f, f));
            flash = Bell(f, 0.37f, 0.035f) * 0.9f + Bell(f, 0.03f, 0.02f) * 0.5f;
            contact = Smooth(0.30f, 0.42f, f);
            age = Mathf.Max(0f, (f - 0.30f) * t.Pause);
            seam = Smooth(0.30f, 0.45f, f);
            bulgeK = Smooth(0.30f, 0.5f, f) * (1f - 0.45f * Smooth(0.55f, 0.8f, f));
            open = Smooth(0.45f, 0.75f, f);
            exchange = Smooth(0.40f, 0.72f, f);
            arcs = Smooth(0.32f, 0.38f, f) * (1f - 0.6f * Smooth(0.7f, 0.95f, f));
        }
        else
        {
            rise = 1f;
            lockFlicker = 0f;
            age = 0.7f * t.Pause + a;
            contact = 1f - Smooth(1.0f, 1.6f, a);
            bulgeK = (1f - Smooth(0f, 0.4f, a)) * 0.55f;
            arcs = (1f - Smooth(0f, 0.5f, a)) * 0.4f;
            exchange = contact;
            if (t.Entering)
            {
                seam = 1f;
                open = 1f - Smooth(0.5f, 1.1f, a);
                closing = Bell(a, 1.0f, 0.12f);
                merge = Smooth(0.05f, 0.6f, a);
                strength = 1f - Smooth(0.35f, 1.0f, a);
                flash = Bell(a, 0.25f, 0.1f) * 0.7f;
            }
            else
            {
                seam = 1f - Smooth(0.4f, 1.0f, a);
                open = 1f - Smooth(0.35f, 0.9f, a);
                closing = Bell(a, 0.8f, 0.12f);
                dissolve = Smooth(1.2f, 2.2f, a);
                flash = 0f;
            }
        }

        // The big shell: where its wall stands at the bubble's height, toward the vehicle.
        var shell = t.StructureId >= 0 && _domes.TryGetValue(t.StructureId, out var dome) && dome.Field != null && dome.FieldMaterial != null ? dome : null;
        var basis = v.Root.Basis;
        Vector3 shellPoint;
        if (shell != null)
        {
            if (!v.ShellFrozen)
            {
                var c = shell.Field!.Position;
                var s = shell.Field.Scale;
                var flat = new Vector3(b.X - c.X, 0f, b.Z - c.Z);
                var dir = flat.LengthSquared() > 1e-4f ? flat.Normalized() : -basis.Z;
                var h = b.Y - c.Y;
                var rh = h > 0f ? s.X * Mathf.Sqrt(Mathf.Max(0f, 1f - h * h / (s.Y * s.Y))) : s.X;
                v.ShellPoint = new Vector3(c.X, b.Y, c.Z) + dir * rh;
                if (after) v.ShellFrozen = true; // it drives on: the contact stays where it was
            }
            shellPoint = v.ShellPoint;
        }
        else shellPoint = b + (-basis.Z) * (v.Semi.Z * 1.3f); // no field drawn: a virtual wall ahead

        // Seam direction and how far the bubble must swell to touch the wall (unit space).
        var toWall = shellPoint - b;
        var local = basis.Inverse() * toWall;
        var dist = local.Length();
        var n = dist > 1e-3f ? local / dist : Vector3.Forward;
        var radiusAlong = 1f / new Vector3(n.X / v.Semi.X, n.Y / v.Semi.Y, n.Z / v.Semi.Z).Length();
        var reach = Mathf.Clamp((dist - radiusAlong) / radiusAlong + 0.08f, 0.08f, 0.9f);
        var seamUnit = new Vector3(n.X / v.Semi.X, n.Y / v.Semi.Y, n.Z / v.Semi.Z).Normalized();

        var m = v.Material;
        m.SetShaderParameter(PRise, rise);
        m.SetShaderParameter(PLock, lockFlicker);
        m.SetShaderParameter(PFlash, flash);
        m.SetShaderParameter(PSeam, seam);
        m.SetShaderParameter(PSeamDir, seamUnit);
        m.SetShaderParameter(PBulge, reach * bulgeK);
        m.SetShaderParameter(PMerge, merge);
        m.SetShaderParameter(PDissolve, dissolve);
        m.SetShaderParameter(PStrength, strength);
        m.SetShaderParameter(PShellOn, shell != null ? 1f : 0f);
        if (shell != null)
        {
            m.SetShaderParameter(PShellCentre, shell.Field!.Position);
            m.SetShaderParameter(PShellScale, shell.Field.Scale);
        }

        // The shell's side of it: one contact slot.
        var aperture = 1.25f * Mathf.Max(v.Semi.X, v.Semi.Y) + 0.3f;
        if (shell != null && contact > 0.001f && shell.Contacts < ShellContacts)
        {
            var c = shell.Field!.Position;
            var s = shell.Field.Scale;
            var at = (shellPoint - c) / s;
            var to = (b - c) / s;
            var k = shell.Contacts++;
            shell.ContactAt[k] = new Color(at.X, at.Y, at.Z, contact);
            shell.ContactTo[k] = new Color(to.X, to.Y, to.Z, open);
            shell.ContactFx[k] = new Color(age, closing, exchange, aperture);
            shell.FieldMaterial!.SetShaderParameter(PContactTint, v.Tint);
        }

        // Arcs, light and sounds.
        DrawTransitArcs(v, b, shellPoint, shell, aperture * Mathf.Max(open, 0.35f), arcs, delta);
        var flicker = arcs > 0.01f ? 0.55f + 0.45f * _transitRng.Randf() : 0f;
        var energy = (contact * 0.9f + arcs * flicker * 2.2f + flash * 3f + closing * 1.5f) * strength;
        v.Light.Visible = energy > 0.02f;
        if (v.Light.Visible)
        {
            v.Light.Position = shellPoint.Lerp(b, 0.35f);
            v.Light.LightEnergy = energy;
            v.Light.LightColor = FieldGold.Lerp(FieldColour, 0.35f + 0.4f * exchange);
        }
        TransitSounds(v, t, rise, seam, contact, shellPoint);
        v.LastF = f;
        v.LastAfter = a;
    }

    /// <summary>Three jagged, camera-facing ribbons crackling between the bubble's seam and the shell round the
    /// aperture, regenerated 20 times a second while they show.</summary>
    private void DrawTransitArcs(TransitVisual v, Vector3 b, Vector3 wall, DomeVisual? shell, float rimRadius, float level, float delta)
    {
        v.ArcClock -= delta;
        if (level <= 0.01f)
        {
            if (v.Arcs.Visible) { v.ArcMesh.ClearSurfaces(); v.Arcs.Visible = false; }
            return;
        }
        if (v.ArcClock > 0f) return;
        v.ArcClock = 0.05f;
        v.Arcs.Visible = true;

        // The shell's tangent plane at the contact (its outward normal: the ellipsoid gradient).
        var normal = (b - wall).Normalized();
        if (shell?.Field is { } field)
        {
            var q = (wall - field.Position) / field.Scale;
            normal = new Vector3(q.X / field.Scale.X, q.Y / field.Scale.Y, q.Z / field.Scale.Z).Normalized();
        }
        var tanA = Mathf.Abs(normal.Y) > 0.98f ? Vector3.Right : Vector3.Up.Cross(normal).Normalized();
        var tanB = normal.Cross(tanA);
        var toWall = wall - b;
        var lit = 0;
        for (var i = 0; i < 3; i++)
        {
            v.ArcLit[i] = _transitRng.Randf() < 0.35f + 0.5f * level ? level * (0.6f + 0.4f * _transitRng.Randf()) : 0f;
            if (v.ArcLit[i] <= 0f) continue;
            lit++;
            var ang = _transitRng.Randf() * Mathf.Tau;
            var around = tanA * Mathf.Cos(ang) + tanB * Mathf.Sin(ang);
            v.ArcFrom[i] = b + toWall * 0.75f + around * rimRadius * (0.35f + 0.3f * _transitRng.Randf());
            v.ArcTo[i] = wall + around * rimRadius * (0.95f + 0.6f * _transitRng.Randf()) + normal * (_transitRng.Randf() - 0.5f) * 0.6f;
        }
        v.ArcMesh.ClearSurfaces();
        if (lit == 0) return;
        v.ArcMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        const int segments = 7;
        for (var i = 0; i < 3; i++)
        {
            if (v.ArcLit[i] <= 0f) continue;
            var from = v.ArcFrom[i];
            var to = v.ArcTo[i];
            var along = to - from;
            var length = along.Length();
            if (length < 0.05f) continue;
            var dir = along / length;
            var side = dir.Cross((_eye - from).Normalized());
            if (side.LengthSquared() < 1e-4f) side = dir.Cross(Vector3.Up);
            side = side.Normalized();
            var up = dir.Cross(side);
            var prev = from;
            var core = new Color(0.9f, 0.95f, 1f, v.ArcLit[i]);
            var halo = new Color(0.62f, 0.8f, 1f, v.ArcLit[i] * 0.35f);
            for (var s = 1; s <= segments; s++)
            {
                var k = s / (float)segments;
                var jitter = s == segments ? Vector3.Zero
                    : (side * (_transitRng.Randf() - 0.5f) + up * (_transitRng.Randf() - 0.5f)) * length * 0.32f * Mathf.Sin(k * Mathf.Pi);
                var next = from + along * k + jitter;
                Ribbon(v.ArcMesh, prev, next, side, 0.07f, core);
                Ribbon(v.ArcMesh, prev, next, side, 0.22f, halo);
                prev = next;
            }
        }
        v.ArcMesh.SurfaceEnd();
    }

    private static void Ribbon(ImmediateMesh mesh, Vector3 a, Vector3 b, Vector3 side, float half, Color colour)
    {
        var s = side * half;
        mesh.SurfaceSetColor(colour);
        mesh.SurfaceAddVertex(a - s);
        mesh.SurfaceAddVertex(a + s);
        mesh.SurfaceAddVertex(b + s);
        mesh.SurfaceAddVertex(a - s);
        mesh.SurfaceAddVertex(b + s);
        mesh.SurfaceAddVertex(b - s);
    }

    private void TransitSounds(TransitVisual v, TransitNow t, float rise, float seam, float contact, Vector3 at)
    {
        if (v.Hum != null)
        {
            var level = rise * (t.After < 0f ? 1f : 1f - Smooth(0.6f, t.Entering ? 1.4f : 2.2f, t.After));
            if (level > 0.01f && Heard)
            {
                v.Hum.VolumeDb = Mathf.LinearToDb(level) - 12f + 6f * contact;
                v.Hum.PitchScale = 0.55f + 0.5f * rise + 0.15f * seam;
                if (!v.Hum.Playing) v.Hum.Play();
            }
            else if (v.Hum.Playing) v.Hum.Stop();
        }
        if (!Heard) return;
        bool Crossed(float last, float now, float mark) => last < mark && now >= mark;
        if (t.After < 0f)
        {
            if (Crossed(v.LastF, t.F, 0.31f)) Sfx.At("exocomp/lock", at, -4f, 0.75f);
            if (Crossed(v.LastF, t.F, 0.47f)) Sfx.At("exocomp/hop", at, -6f, 1.3f);
        }
        else if (t.Entering && Crossed(v.LastAfter, t.After, 0.1f)) Sfx.At("exocomp/complete", at, -6f, 1.25f);
        else if (!t.Entering && Crossed(v.LastAfter, t.After, 1.2f)) Sfx.At("exocomp/hop", v.Root.Position, -8f, 0.7f);
    }

    private static readonly StringName FieldBoxMeta = "field_box";

    /// <summary>The model's box in its own space (centre, half size) from its visible meshes; selection rings, light
    /// cones, effects and labels left out. Cached on the model.</summary>
    private static (Vector3 Centre, Vector3 Half) FieldBox(Node3D model)
    {
        if (model.HasMeta(FieldBoxMeta))
        {
            var cached = model.GetMeta(FieldBoxMeta).AsAabb();
            return (cached.GetCenter(), cached.Size * 0.5f);
        }
        var have = false;
        var box = new Aabb();
        void Walk(Node node, Transform3D toModel)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is not Node3D n || !n.Visible) continue;
                var name = n.Name.ToString();
                if (name.StartsWith("Selected") || name.StartsWith("LightCone") || name.StartsWith("RepairPad")
                    || n is GpuParticles3D or CpuParticles3D or Label3D or SprayRig) continue;
                var t = toModel * n.Transform;
                if (n is MeshInstance3D mi && mi.Mesh != null)
                {
                    var b = t * mi.Mesh.GetAabb();
                    if (b.Size.LengthSquared() > 0.0001f)
                    {
                        box = have ? box.Merge(b) : b;
                        have = true;
                    }
                }
                Walk(n, t);
            }
        }
        Walk(model, Transform3D.Identity);
        if (!have) return (new Vector3(0, 1.2f, 0), new Vector3(1.4f, 1.2f, 2.2f));
        model.SetMeta(FieldBoxMeta, box);
        return (box.GetCenter(), box.Size * 0.5f);
    }
}
