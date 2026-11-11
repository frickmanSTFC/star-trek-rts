using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The Jack Skellington raid, view side (docs/jack-contract.md; sim Surface/Jack.cs; render-only). One giant Jack per raid:
/// rises out of a green mist, strides hex to hex (legs/arms swing, a footstep thud each stride), winds up and throws glowing
/// pumpkins that fly on a high arc and burst into orange and green fire, flings his arms wide to let a swarm of bats spiral
/// out over the base, throws his head back for a shriek, then bows and sinks back into the mist. Poses go through
/// <see cref="JackModel.Rig"/>, so the glb and the primitive stand-in animate the same.
/// </summary>
public partial class PlanetView
{
    private sealed class JackView
    {
        public JackModel.Rig Rig = null!;
        public CpuParticles3D Mist = null!;
        public OmniLight3D Glow = null!;
        public long LastStep = -1;
        public bool Rose, Sank;
        public float Yaw;
    }

    private sealed class Swarm
    {
        public readonly List<(Node3D Bat, Node3D? L, Node3D? R)> Bats = new();
    }

    private readonly Dictionary<JackRaid, JackView> _jackViews = new();
    private readonly Dictionary<JackAttack, Node3D> _jackPumpkins = new();
    private readonly Dictionary<JackAttack, Swarm> _jackSwarms = new();
    private readonly HashSet<JackAttack> _jackHeard = new();
    private readonly List<(Node3D Node, float Age, OmniLight3D Light)> _jackBursts = new();

    private const float JackSink = JackModel.Height * 1.15f;
    private const int BatCount = 10;

    private float JackNow(float alpha) => _sim.Tick - 1 + alpha; // positions at the start of the tick being drawn

    private Vector3 JackPos(JackRaid j, float now)
    {
        var a = TileCentre(j.FromTile);
        var b = TileCentre(j.Tile);
        var span = j.StepEndTick - j.StepStartTick;
        var f = span > 0 ? Mathf.Clamp((now - j.StepStartTick) / span, 0f, 1f) : 1f;
        var p = a.Lerp(b, Smooth(f));
        p.Y = GroundAt(p);
        return p;
    }

    private static float Smooth(float f) => f * f * (3 - 2 * f);

    private void UpdateJack(float alpha, float delta)
    {
        var now = JackNow(alpha);
        foreach (var j in _surface.Jacks)
        {
            var phase = j.PhaseAt((long)Mathf.Floor(now));
            _jackViews.TryGetValue(j, out var v);
            if (phase is JackPhase.Warning || j.Tile < 0 || phase == JackPhase.Gone)
            {
                if (v != null && phase == JackPhase.Gone) { v.Rig.Root.QueueFree(); _jackViews.Remove(j); }
                continue;
            }
            if (v == null) _jackViews[j] = v = NewJackView();
            PoseJack(j, v, phase, now);
        }
        UpdateJackAttacks(now);
        for (var i = _jackBursts.Count - 1; i >= 0; i--)
        {
            var (node, age, light) = _jackBursts[i];
            age += delta;
            light.LightEnergy = Mathf.Max(0f, 9f * (1f - age / 1.2f));
            if (age > 3f) { node.QueueFree(); _jackBursts.RemoveAt(i); continue; }
            _jackBursts[i] = (node, age, light);
        }
    }

    private JackView NewJackView()
    {
        var rig = JackModel.Build();
        AddChild(rig.Root);
        var mistMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
            AlbedoTexture = new GradientTexture2D
            {
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
                Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 1f } },
            },
        };
        var fade = new Gradient
        {
            Colors = new[] { new Color(0.45f, 1f, 0.5f, 0f), new Color(0.45f, 1f, 0.55f, 0.55f), new Color(0.3f, 0.8f, 0.45f, 0f) },
            Offsets = new[] { 0f, 0.25f, 1f },
        };
        var mist = new CpuParticles3D
        {
            Name = "JackMist", Amount = 70, Lifetime = 3.2f, Emitting = false, LocalCoords = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 9f,
            Direction = Vector3.Up, Spread = 35f, InitialVelocityMin = 0.6f, InitialVelocityMax = 2.2f, Gravity = new Vector3(0, 0.3f, 0),
            ScaleAmountMin = 4f, ScaleAmountMax = 8f, ColorRamp = fade,
            Mesh = new QuadMesh { Size = new Vector2(1.6f, 1.6f), Material = mistMat },
        };
        mist.SetMeta(VehicleLights.NoBoostMeta, true);
        AddChild(mist);
        var glow = new OmniLight3D { Name = "JackGlow", LightColor = new Color(0.45f, 1f, 0.45f), LightEnergy = 0f, OmniRange = 42f };
        glow.SetMeta(VehicleLights.NoBoostMeta, true);
        rig.Root.AddChild(glow);
        glow.Position = new Vector3(0, 9f, -3f);
        return new JackView { Rig = rig, Mist = mist, Glow = glow };
    }

    private void PoseJack(JackRaid j, JackView v, JackPhase phase, float now)
    {
        var pos = JackPos(j, now);
        var root = v.Rig.Root;
        root.Visible = j.Player == _localPlayer || Sees(j.Tile);

        // Facing: toward his target, turning smoothly.
        var to = TileCentre(j.FacingTile >= 0 ? j.FacingTile : j.Tile) - pos;
        if (to.LengthSquared() > 1f)
        {
            var want = Mathf.Atan2(-to.X, -to.Z);
            v.Yaw = Mathf.LerpAngle(v.Yaw, want, 0.08f);
        }

        var p = new JackModel.Pose { ArmLOut = 9f, ArmROut = 9f, ElbowL = 12f, ElbowR = 12f, HeadPitch = -10f };
        var t = now / Simulation.TickRate;
        // Idle sway.
        p.HeadYaw = 8f * Mathf.Sin(t * 0.9f);
        p.ArmL = 4f * Mathf.Sin(t * 1.3f);
        p.ArmR = 4f * Mathf.Sin(t * 1.3f + 1f);

        var y = 0f;
        var moving = now < j.StepEndTick && j.FromTile != j.Tile;
        if (moving)
        {
            var span = Mathf.Max(1, j.StepEndTick - j.StepStartTick);
            var f = Mathf.Clamp((now - j.StepStartTick) / span, 0f, 1f);
            var phi = f * Mathf.Tau * 2f; // two strides a hex
            var s = Mathf.Sin(phi);
            p.LegL = 30f * s;
            p.LegR = -30f * s;
            p.KneeL = 45f * Mathf.Max(0f, Mathf.Sin(phi - 1.1f));
            p.KneeR = 45f * Mathf.Max(0f, Mathf.Sin(phi - 1.1f + Mathf.Pi));
            p.FootL = -0.4f * p.LegL;
            p.FootR = -0.4f * p.LegR;
            p.ArmL = -24f * s;
            p.ArmR = 24f * s;
            p.ElbowL = 18f + 10f * Mathf.Max(0f, -s);
            p.ElbowR = 18f + 10f * Mathf.Max(0f, s);
            p.TorsoTwist = 7f * s;
            p.HipsRoll = 3f * s;
            p.Bob = -0.45f * Mathf.Abs(Mathf.Cos(phi));
            if (j.StepStartTick != v.LastStep)
            {
                v.LastStep = j.StepStartTick;
                if (root.Visible) Sfx.At("jack/step", pos, 2f, 0.9f + 0.2f * (j.StepStartTick % 3) / 2f);
            }
        }

        // The latest attack's pose (throw, bats, shriek) for its first second or so.
        JackAttack? latest = null;
        foreach (var a in _surface.JackAttacks)
            if (a.Player == j.Player && a.Tick <= now) latest = a;
        if (latest != null && phase == JackPhase.Rampage)
        {
            var f = (now - latest.Tick) / JackRules.ThrowTicks;
            if (latest.Kind == "pumpkin" && f < 1.6f) ThrowPose(ref p, f);
            else if (latest.Kind == "bats" && f < 1.6f) WideArms(ref p, Mathf.Sin(Mathf.Pi * Mathf.Clamp(f / 1.6f, 0, 1)));
            else if (latest.Kind == "shriek" && f < 2f) ShriekPose(ref p, Mathf.Sin(Mathf.Pi * Mathf.Clamp(f / 2f, 0, 1)), t);
        }

        switch (phase)
        {
            case JackPhase.Rising:
            {
                var f = Mathf.Clamp((now - j.ArriveTick) / (float)(j.RampageTick - j.ArriveTick), 0f, 1f);
                y = -JackSink * (1f - Smooth(f));
                WideArms(ref p, Smooth(f));
                if (!v.Rose)
                {
                    v.Rose = true;
                    if (root.Visible) Sfx.At("jack/rise", pos, 4f);
                }
                break;
            }
            case JackPhase.Leaving:
            {
                var total = (float)(j.GoneTick - j.LeaveTick);
                var f = Mathf.Clamp((now - j.LeaveTick) / total, 0f, 1f);
                var bow = Smooth(Mathf.Clamp(f / 0.25f, 0f, 1f)) * (f < 0.5f ? 1f : 1f - Smooth(Mathf.Clamp((f - 0.5f) / 0.2f, 0f, 1f)) * 0.6f);
                p.TorsoPitch = -55f * bow;
                p.HeadPitch = -10f - 15f * bow;
                p.ArmR = 75f * bow;
                p.ArmROut = 9f - 20f * bow;
                p.ElbowR = 12f + 70f * bow;
                p.ArmL = -55f * bow;
                p.ArmLOut = 9f + 35f * bow;
                p.LegR = 12f * bow;
                p.KneeL = 10f * bow;
                p.Hips = -8f * bow;
                if (f > 0.5f)
                {
                    y = -JackSink * Smooth((f - 0.5f) / 0.5f);
                    if (!v.Sank)
                    {
                        v.Sank = true;
                        if (root.Visible) Sfx.At("jack/rise", pos, 2f, 0.8f);
                    }
                }
                break;
            }
        }

        v.Rig.Apply(p);
        root.Position = pos + new Vector3(0, y, 0);
        root.Basis = new Basis(Vector3.Up, v.Yaw);
        var misty = phase == JackPhase.Rising || phase == JackPhase.Leaving;
        v.Mist.Position = new Vector3(pos.X, pos.Y + 1f, pos.Z);
        v.Mist.Emitting = misty && root.Visible;
        v.Glow.LightEnergy = misty ? 4f : 1.4f;
    }

    /// <summary>Wind up over the shoulder, sweep over the top and let go at f 0.62, follow through, recover.</summary>
    private static void ThrowPose(ref JackModel.Pose p, float f)
    {
        float arm, elbow, twist, pitch;
        if (f < 0.45f)
        {
            var u = Smooth(f / 0.45f);
            arm = -150f * u; elbow = 12f + 60f * u; twist = 25f * u; pitch = 8f * u;
        }
        else if (f < 0.7f)
        {
            var u = Smooth((f - 0.45f) / 0.25f);
            arm = -150f - 100f * u; elbow = 72f - 62f * u; twist = 25f - 45f * u; pitch = 8f - 23f * u;
        }
        else
        {
            var u = Smooth(Mathf.Clamp((f - 0.7f) / 0.9f, 0f, 1f));
            arm = 110f * (1f - u); elbow = 10f + 2f * u; twist = -20f * (1f - u); pitch = -15f * (1f - u);
        }
        p.ArmR = arm;
        p.ElbowR = elbow;
        p.ArmROut = 14f;
        p.TorsoTwist = twist;
        p.TorsoPitch = pitch;
        p.ArmL = -25f * Mathf.Clamp(1.6f - f, 0f, 1f);
        p.ArmLOut = 30f;
        p.PumpkinInHand = f < 0.62f;
        p.HeadPitch = -16f;
    }

    private static void WideArms(ref JackModel.Pose p, float w)
    {
        p.ArmLOut = Mathf.Lerp(p.ArmLOut, 105f, w);
        p.ArmROut = Mathf.Lerp(p.ArmROut, 105f, w);
        p.ArmL = Mathf.Lerp(p.ArmL, -12f, w);
        p.ArmR = Mathf.Lerp(p.ArmR, -12f, w);
        p.ElbowL = Mathf.Lerp(p.ElbowL, 25f, w);
        p.ElbowR = Mathf.Lerp(p.ElbowR, 25f, w);
        p.HeadPitch = Mathf.Lerp(p.HeadPitch, 6f, w);
        p.TorsoPitch = Mathf.Lerp(p.TorsoPitch, 5f, w);
    }

    private static void ShriekPose(ref JackModel.Pose p, float w, float t)
    {
        p.ArmLOut = Mathf.Lerp(p.ArmLOut, 140f, w);
        p.ArmROut = Mathf.Lerp(p.ArmROut, 140f, w);
        p.ElbowL = Mathf.Lerp(p.ElbowL, 40f, w);
        p.ElbowR = Mathf.Lerp(p.ElbowR, 40f, w);
        p.HeadPitch = Mathf.Lerp(p.HeadPitch, 22f, w);
        p.HeadRoll = 9f * w * Mathf.Sin(t * 14f);
        p.TorsoPitch = Mathf.Lerp(p.TorsoPitch, 10f, w);
    }

    private void UpdateJackAttacks(float now)
    {
        foreach (var a in _surface.JackAttacks)
        {
            var age = (now - a.Tick) / Simulation.TickRate;
            if (age < 0f || age > 6f) continue;
            var raid = _surface.JackOf(a.Player);
            var seen = a.Player == _localPlayer || Sees(a.Tile) || Sees(a.FromTile);
            var heard = _jackHeard.Contains(a);
            switch (a.Kind)
            {
                case "pumpkin":
                    PumpkinFlight(a, raid, now, seen);
                    break;
                case "bats":
                    BatSwarm(a, raid, now, age, seen);
                    if (!heard && seen) Sfx.At("jack/bats", TileCentre(a.FromTile) + Vector3.Up * 8f, 3f);
                    break;
                case "shriek":
                    if (!heard && seen) Sfx.At("jack/shriek", TileCentre(a.FromTile) + Vector3.Up * 14f, 4f);
                    break;
            }
            _jackHeard.Add(a);
        }
        foreach (var a in new List<JackAttack>(_jackSwarms.Keys))
            if ((now - a.Tick) / Simulation.TickRate > 3.4f)
            {
                foreach (var b in _jackSwarms[a].Bats) b.Bat.QueueFree();
                _jackSwarms.Remove(a);
            }
    }

    private void PumpkinFlight(JackAttack a, JackRaid? raid, float now, bool seen)
    {
        var release = a.Tick + JackRules.ThrowTicks * 0.62f;
        if (now < release) return;
        if (now >= a.LandTick)
        {
            if (_jackPumpkins.Remove(a, out var done))
            {
                done.QueueFree();
                if (seen) Burst(TileCentre(a.Tile));
            }
            return;
        }
        if (!_jackPumpkins.TryGetValue(a, out var pk))
        {
            pk = JackModel.Pumpkin();
            pk.Scale = Vector3.One * 1.4f;
            AddChild(pk);
            _jackPumpkins[a] = pk;
            if (seen) Sfx.At("jack/throw", TileCentre(a.FromTile) + Vector3.Up * 14f, 2f);
        }
        pk.Visible = seen;
        // From over his shoulder where the throw lets go (where he stands now, so it leaves his hand as he walks).
        var from = (raid != null && _jackViews.ContainsKey(raid) ? JackPos(raid, now) : TileCentre(a.FromTile)) + Vector3.Up * 15f;
        var to = TileCentre(a.Tile) + Vector3.Up * 1.5f;
        var u = Mathf.Clamp((now - release) / Mathf.Max(1f, a.LandTick - release), 0f, 1f);
        var at = from.Lerp(to, u) + Vector3.Up * (22f * Mathf.Sin(Mathf.Pi * u));
        pk.Position = at;
        pk.Rotation = new Vector3(u * 9f, u * 5f, 0);
    }

    /// <summary>The pumpkin bursting: an orange fireball, green ghost-fire licking up, embers, a flash.</summary>
    private void Burst(Vector3 at)
    {
        var n = new Node3D { Name = "JackBurst", Position = at };
        AddChild(n);
        n.AddChild(Fire(new Color(1f, 0.55f, 0.1f), 50, 0.9f, 7f, 14f, 1.4f));
        n.AddChild(Fire(new Color(0.4f, 1f, 0.35f), 40, 2.2f, 2f, 6f, 1.1f));
        n.AddChild(Fire(new Color(1f, 0.85f, 0.4f), 30, 1.6f, 9f, 18f, 0.35f));
        var light = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.2f), LightEnergy = 9f, OmniRange = 38f, Position = Vector3.Up * 4f };
        light.SetMeta(VehicleLights.NoBoostMeta, true);
        n.AddChild(light);
        _jackBursts.Add((n, 0f, light));
        Sfx.At("jack/burst", at, 4f);
    }

    private static CpuParticles3D Fire(Color colour, int amount, float life, float vMin, float vMax, float size)
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = new GradientTexture2D
            {
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
                Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 1f } },
            },
        };
        var p = new CpuParticles3D
        {
            Amount = amount, Lifetime = life, OneShot = true, Explosiveness = 0.85f, Emitting = true, LocalCoords = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 1.5f,
            Direction = Vector3.Up, Spread = 80f, InitialVelocityMin = vMin, InitialVelocityMax = vMax,
            Gravity = new Vector3(0, 2f, 0), DampingMin = 4f, DampingMax = 8f,
            ScaleAmountMin = 2f * size, ScaleAmountMax = 4f * size,
            ColorRamp = new Gradient { Colors = new[] { colour, new Color(colour, 0f) }, Offsets = new[] { 0f, 1f } },
            Mesh = new QuadMesh { Size = new Vector2(1.5f, 1.5f), Material = mat },
        };
        p.SetMeta(VehicleLights.NoBoostMeta, true);
        return p;
    }

    private void BatSwarm(JackAttack a, JackRaid? raid, float now, float age, bool seen)
    {
        if (age > 3.4f) return;
        if (!_jackSwarms.TryGetValue(a, out var swarm))
        {
            swarm = new Swarm();
            for (var i = 0; i < BatCount; i++)
            {
                var bat = JackModel.Bat(out var l, out var r);
                bat.Scale = Vector3.One * 2.2f;
                AddChild(bat);
                swarm.Bats.Add((bat, l, r));
            }
            _jackSwarms[a] = swarm;
        }
        var centre = TileCentre(a.FromTile);
        if (raid != null && _jackViews.ContainsKey(raid)) centre = JackPos(raid, now);
        var t = Mathf.Max(0f, age - 0.15f);
        for (var i = 0; i < swarm.Bats.Count; i++)
        {
            var (bat, l, r) = swarm.Bats[i];
            bat.Visible = seen && age > 0.15f;
            var a0 = i * Mathf.Tau / swarm.Bats.Count + (a.Tick % 7);
            var out1 = Mathf.Min(1f, t / 1.0f);
            var radius = 4f + 46f * Smooth(out1) + 3f * Mathf.Sin(t * 3f + i);
            var ang = a0 + t * 2.4f * (i % 2 == 0 ? 1f : -1f);
            var h = 10f - 5f * out1 + 10f * Mathf.Max(0f, t - 1.4f) + 1.5f * Mathf.Sin(t * 6f + i * 1.7f);
            var at = centre + new Vector3(Mathf.Cos(ang) * radius, h, Mathf.Sin(ang) * radius);
            var heading = new Vector3(-Mathf.Sin(ang), 0, Mathf.Cos(ang)) * (i % 2 == 0 ? 1f : -1f);
            bat.Position = new Vector3(at.X, Mathf.Max(at.Y, GroundAt(at) + 2f), at.Z);
            bat.Basis = new Basis(Vector3.Up, Mathf.Atan2(-heading.X, -heading.Z)) * new Basis(Vector3.Back, 0.25f * Mathf.Sin(t * 5f + i));
            var fade = 1f - Mathf.Clamp((t - 2.6f) / 0.6f, 0f, 1f);
            bat.Scale = Vector3.One * 2.2f * Mathf.Max(0.01f, fade);
            var flap = Mathf.DegToRad(45f * Mathf.Sin(t * 28f + i * 1.3f) + 5f);
            if (l != null) l.Basis = new Basis(Vector3.Back, -flap);
            if (r != null) r.Basis = new Basis(Vector3.Back, flap);
        }
    }
}
