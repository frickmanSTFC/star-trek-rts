using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// A drop ship's rocket exhaust (render-only; owner 2026-11-09: "less impulse and such, more rocket"). Built once per
/// ship and driven every frame by <see cref="DropShipView"/> from the ship's pose and its throttle:
/// <list type="bullet">
/// <item>a flame under each of the four lift thrusters (<c>Thruster0..3</c>): an orange outer flame and a white-hot
/// core, each three crossed additive planes with soft vertex-colour edges (unshaded, no shadow, no fog), length and
/// width flickering with the throttle, squashed and spread where they reach the ground, longer as the air thins
/// with height; the aft thrusters 4/5 get small ones from the impulse value;</item>
/// <item>a flickering orange OmniLight between the ship and the ground, lighting the ground under it;</item>
/// <item>soft grey-white smoke/steam (world-space billboards) rolling out along the ground while the ship is low on a
/// strong burn, and a one-shot billow (<see cref="Billow"/>) for touchdown and lift-off.</item>
/// </list>
/// Nothing hangs under the ship model itself (the flames follow it from their own root), so the people blockers, the
/// model's materials and the light rig never see them. No allocation per frame.
/// </summary>
internal sealed class RocketPlume
{
    private const int LiftCount = 4, Count = 6;

    // Thruster panels in model space (build_dropship.py: pod grilles 0.9 × 1.9 under the pods, chin grilles 0.9 × 1.2
    // under the nose, aft exhausts 0.9 × 0.66 on the pods' tails), used when the model has no Thruster mesh.
    private static readonly Vector3[] FallbackAt =
    {
        new(-3.75f, 2.6f, 1.55f), new(3.75f, 2.6f, 1.55f), new(-0.95f, 0.76f, -3.1f), new(0.95f, 0.76f, -3.1f),
        new(-3.75f, 3.2f, 6.05f), new(3.75f, 3.2f, 6.05f),
    };
    private static readonly Vector2[] FallbackHalf =
    {
        new(0.45f, 0.95f), new(0.45f, 0.95f), new(0.45f, 0.6f), new(0.45f, 0.6f), new(0.45f, 0.33f), new(0.45f, 0.33f),
    };

    /// <summary>Lift flames point down the model's -Y; aft flames out of the tail (+Z): local -Y turned onto +Z.</summary>
    private static readonly Basis AftBasis = new(Vector3.Right, -Mathf.Pi / 2f);

    private static ArrayMesh? _outerMesh, _coreMesh;
    private static StandardMaterial3D? _smokeMaterial;

    private readonly Node3D _root;
    private readonly MeshInstance3D[] _outer = new MeshInstance3D[Count];
    private readonly MeshInstance3D[] _core = new MeshInstance3D[LiftCount];
    private readonly Vector3[] _at = new Vector3[Count];     // nozzle (model space)
    private readonly Vector2[] _half = new Vector2[Count];   // nozzle half-size across the flame (its local X, Z)
    private readonly float[] _phase = new float[Count];
    private readonly StandardMaterial3D _outerMat, _coreMat, _aftMat;
    private readonly OmniLight3D _light;
    private readonly CpuParticles3D _smoke, _billow;
    private bool _hidden;

    public RocketPlume(Node3D parent, Node3D model, Color haze, int seed)
    {
        _root = new Node3D { Name = "RocketPlume" };
        parent.AddChild(_root);
        _outerMat = FlameMaterial();
        _coreMat = FlameMaterial();
        _aftMat = FlameMaterial();
        var rng = (uint)(seed * 2654435761u + 977u);
        for (var k = 0; k < Count; k++)
        {
            Nozzle(model, k);
            rng = rng * 1664525u + 1013904223u;
            _phase[k] = (rng >> 8) / (float)(1 << 24) * Mathf.Tau;
            var lift = k < LiftCount;
            _outer[k] = Flame($"Flame{k}", OuterMesh, lift ? _outerMat : _aftMat);
            if (lift) _core[k] = Flame($"FlameCore{k}", CoreMesh, _coreMat);
        }

        _light = new OmniLight3D
        {
            Name = "PlumeLight",
            LightColor = new Color(1f, 0.6f, 0.28f),
            OmniRange = 20f,
            LightEnergy = 0f,
            ShadowEnabled = false,
            Visible = false,
        };
        _light.SetMeta(VehicleLights.NoBoostMeta, true); // set every frame here: the night boost leaves it alone
        parent.AddChild(_light);

        _smoke = Smoke(haze, burst: false);
        _billow = Smoke(haze, burst: true);
        parent.AddChild(_smoke);
        parent.AddChild(_billow);
    }

    /// <summary>Where thruster k's panel is and how big, from the model's Thruster mesh (any depth) or the fallback.</summary>
    private void Nozzle(Node3D model, int k)
    {
        _at[k] = FallbackAt[k];
        _half[k] = FallbackHalf[k];
        if (model.FindChild($"Thruster{k}", true, false) is not MeshInstance3D { Mesh: { } mesh } mi) return;
        var box = mesh.GetAabb();
        if (box.Size.LengthSquared() < 1e-6f) return;
        var t = ModelSpace(mi, model);
        var centre = t * box.GetCenter();
        var size = (t.Basis * box.Size).Abs();
        if (k < LiftCount)
        {
            _at[k] = centre with { Y = centre.Y - size.Y / 2f - 0.02f };
            _half[k] = new Vector2(Mathf.Max(0.2f, size.X / 2f), Mathf.Max(0.2f, size.Z / 2f));
        }
        else
        {
            _at[k] = centre with { Z = centre.Z + size.Z / 2f + 0.02f };
            _half[k] = new Vector2(Mathf.Max(0.2f, size.X / 2f), Mathf.Max(0.15f, size.Y / 2f));
        }
    }

    private MeshInstance3D Flame(string name, Mesh mesh, Material material)
    {
        var mi = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _root.AddChild(mi);
        return mi;
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>
    /// Move and size everything. <paramref name="pose"/> = the ship model's transform (world), <paramref name="throttle"/>
    /// 0..1 for the lift thrusters, <paramref name="aft"/> 0..1 for the aft ones, <paramref name="groundY"/> = the ground
    /// under the ship, <paramref name="clock"/> = any running time in seconds (the flicker).
    /// </summary>
    public void Update(Transform3D pose, float throttle, float aft, float groundY, bool shown, float clock)
    {
        if (!shown)
        {
            Hide();
            return;
        }
        _hidden = false;
        _root.Transform = pose;
        var height = pose.Origin.Y - groundY;
        var thin = 1f + 0.6f * S(15f, 180f, height); // thinner air higher up: the plume lengthens and opens
        var down = -pose.Basis.Y.Normalized();
        var fall = Mathf.Min(-0.2f, down.Y);
        var lit = throttle > 0.02f;
        var t = clock;

        for (var k = 0; k < LiftCount; k++)
        {
            var p = _phase[k];
            var f = 1f + 0.1f * Mathf.Sin(29f * t + p) + 0.07f * Mathf.Sin(47f * t + 1.7f * p) + 0.05f * Mathf.Sin(83f * t + 2.3f * p);
            var w = 1f + 0.06f * Mathf.Sin(37f * t + 0.6f * p) + 0.05f * Mathf.Sin(71f * t + p);
            var outer = _outer[k];
            var core = _core[k];
            if (!lit)
            {
                if (outer.Visible) outer.Visible = false;
                if (core.Visible) core.Visible = false;
                continue;
            }
            var want = (1.4f + 7.6f * throttle) * thin * f;
            var nozzleY = (pose * _at[k]).Y;
            var clear = (nozzleY - groundY) / -fall;                 // along the flame to the ground
            var length = Mathf.Min(want, Mathf.Max(0.25f, clear + 0.35f));
            var splash = want > length ? Mathf.Min(1f, (want - length) / want * 1.5f) : 0f; // squashed: it spreads
            var width = (0.8f + 0.45f * throttle) * w * (1f + 0.9f * splash) * (0.75f + 0.25f * thin);
            var h = _half[k];
            outer.Transform = new Transform3D(Basis.FromScale(new Vector3(h.X * width * 1.1f, length, h.Y * width * 1.1f)), _at[k]);
            if (!outer.Visible) outer.Visible = true;
            var hot = throttle > 0.12f;
            if (hot)
                core.Transform = new Transform3D(Basis.FromScale(new Vector3(h.X * width * 0.55f, Mathf.Min(length, want * 0.42f), h.Y * width * 0.55f)), _at[k]);
            if (core.Visible != hot) core.Visible = hot;
        }
        for (var k = LiftCount; k < Count; k++)
        {
            var on = aft > 0.03f;
            var outer = _outer[k];
            if (outer.Visible != on) outer.Visible = on;
            if (!on) continue;
            var p = _phase[k];
            var f = 1f + 0.12f * Mathf.Sin(31f * t + p) + 0.06f * Mathf.Sin(59f * t + 2f * p);
            var h = _half[k];
            var width = 0.85f + 0.3f * aft;
            outer.Transform = new Transform3D(AftBasis * Basis.FromScale(new Vector3(h.X * width, (0.5f + 2.4f * aft) * f, h.Y * width)), _at[k]);
        }

        var flicker = 1f + 0.08f * Mathf.Sin(23f * t) + 0.06f * Mathf.Sin(41f * t + 1.3f);
        var b = (0.3f + 0.95f * throttle) * flicker;
        _outerMat.AlbedoColor = new Color(b, b, b);
        var c = (0.25f + 1.1f * throttle) * flicker;
        _coreMat.AlbedoColor = new Color(c, c, c);
        var a = 0.35f + 0.8f * aft;
        _aftMat.AlbedoColor = new Color(a, a, a);

        // The ground under the ship glows orange while the burn is low enough to reach it.
        var glow = lit ? (1.5f + 7f * throttle) * (1f - S(30f, 80f, height)) : 0f;
        var light = glow > 0.05f;
        if (_light.Visible != light) _light.Visible = light;
        if (light)
        {
            var o = pose.Origin;
            _light.Position = new Vector3(o.X, Mathf.Max(groundY + 1.2f, o.Y - Mathf.Min(6f, height * 0.6f)), o.Z);
            _light.OmniRange = Mathf.Clamp(14f + height * 0.9f, 16f, 50f);
            _light.LightEnergy = glow * (1f + 0.12f * Mathf.Sin(19f * t) + 0.1f * Mathf.Sin(33f * t + 0.7f));
        }

        // Smoke and steam rolling out along the ground while a strong burn is low over it.
        _smoke.Position = new Vector3(pose.Origin.X, groundY + 0.6f, pose.Origin.Z);
        var smoke = throttle > 0.25f && height < 40f;
        if (_smoke.Emitting != smoke) _smoke.Emitting = smoke;
    }

    /// <summary>A one-shot billow of smoke out along the ground at <paramref name="at"/> (touchdown, lift-off).</summary>
    public void Billow(Vector3 at)
    {
        _billow.Position = at;
        _billow.Restart();
    }

    private void Hide()
    {
        if (_hidden) return;
        _hidden = true;
        foreach (var f in _outer) f.Visible = false;
        foreach (var f in _core) f.Visible = false;
        _light.Visible = false;
        _smoke.Emitting = false;
    }

    public void Free()
    {
        _root.QueueFree();
        _light.QueueFree();
        _smoke.QueueFree();
        _billow.QueueFree();
    }

    // ---------------------------------------------------------------- parts

    private static StandardMaterial3D FlameMaterial() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        DisableReceiveShadows = true,
        DisableFog = true,
        AlbedoColor = Colors.White,
    };

    private static ArrayMesh OuterMesh => _outerMesh ??= FlameMesh(
        new[] { 0f, 0.12f, 0.35f, 0.65f, 1f }, new[] { 0.75f, 1f, 0.9f, 0.55f, 0.1f },
        new[] { 0f, 0.15f, 0.45f, 0.75f, 1f },
        new[] { new Color(1f, 0.78f, 0.45f), new Color(1f, 0.55f, 0.18f), new Color(0.75f, 0.25f, 0.06f), new Color(0.3f, 0.08f, 0.02f), new Color(0f, 0f, 0f) });

    private static ArrayMesh CoreMesh => _coreMesh ??= FlameMesh(
        new[] { 0f, 0.2f, 0.6f, 1f }, new[] { 0.85f, 0.75f, 0.4f, 0.08f },
        new[] { 0f, 0.3f, 0.7f, 1f },
        new[] { new Color(1f, 1f, 0.95f), new Color(1f, 0.9f, 0.62f), new Color(0.6f, 0.35f, 0.1f), new Color(0f, 0f, 0f) });

    /// <summary>A unit flame: from the nozzle at the origin down to -1 along Y, ±1 across at its widest, made of three
    /// planes crossed at 60° about Y; the colour fades to black (= nothing, additive) toward the edges and the tip.</summary>
    private static ArrayMesh FlameMesh(float[] widthAt, float[] width, float[] colourAt, Color[] colour)
    {
        const int rows = 10;
        float[] across = { -1f, -0.5f, 0f, 0.5f, 1f };
        float[] edge = { 0f, 0.6f, 1f, 0.6f, 0f };
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Color>();
        var indices = new List<int>();
        for (var plane = 0; plane < 3; plane++)
        {
            var a = plane * Mathf.Pi / 3f;
            var side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var normal = side.Cross(Vector3.Up);
            var start = verts.Count;
            for (var j = 0; j <= rows; j++)
            {
                var y = j / (float)rows;
                var w = Profile(widthAt, width, y);
                var c = Ramp(colourAt, colour, y);
                for (var i = 0; i < across.Length; i++)
                {
                    verts.Add(side * across[i] * w + Vector3.Down * y);
                    normals.Add(normal);
                    colours.Add(new Color(c.R * edge[i], c.G * edge[i], c.B * edge[i]));
                }
            }
            var n = across.Length;
            for (var j = 0; j < rows; j++)
                for (var i = 0; i < n - 1; i++)
                {
                    int q = start + j * n + i, r = q + 1, s = q + n, u = s + 1;
                    indices.Add(q); indices.Add(s); indices.Add(r);
                    indices.Add(r); indices.Add(s); indices.Add(u);
                }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static float Profile(float[] at, float[] value, float x)
    {
        for (var i = 1; i < at.Length; i++)
            if (x <= at[i]) return Mathf.Lerp(value[i - 1], value[i], (x - at[i - 1]) / (at[i] - at[i - 1]));
        return value[^1];
    }

    private static Color Ramp(float[] at, Color[] value, float x)
    {
        for (var i = 1; i < at.Length; i++)
            if (x <= at[i]) return value[i - 1].Lerp(value[i], (x - at[i - 1]) / (at[i] - at[i - 1]));
        return value[^1];
    }

    private static StandardMaterial3D SmokeMaterial => _smokeMaterial ??= new StandardMaterial3D
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = SoftDot(),
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        BillboardKeepScale = true,
        Roughness = 1f,
        DisableReceiveShadows = true,
    };

    private static Texture2D SoftDot() => new GradientTexture2D
    {
        Gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.45f, 1f },
            Colors = new[] { new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0.45f), new Color(1f, 1f, 1f, 0f) },
        },
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
        Width = 64,
        Height = 64,
    };

    /// <summary>Soft grey-white smoke/steam (a little of the ground's haze colour in it) rolling out along the ground
    /// and rising slowly; world-space, so it stays where it was made. The burst version is one billow at once.</summary>
    private static CpuParticles3D Smoke(Color haze, bool burst)
    {
        var tint = new Color(0.86f, 0.85f, 0.83f).Lerp(haze with { A = 1f }, 0.3f);
        var ramp = new Gradient
        {
            Offsets = new[] { 0f, 0.12f, 0.6f, 1f },
            Colors = new[] { tint with { A = 0f }, tint with { A = burst ? 0.42f : 0.3f }, tint with { A = burst ? 0.24f : 0.18f }, tint with { A = 0f } },
        };
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, 0.35f));
        grow.AddPoint(new Vector2(1f, 1f));
        return new CpuParticles3D
        {
            Name = burst ? "RocketBillow" : "RocketSmoke",
            Amount = burst ? 44 : 40,
            Lifetime = burst ? 5.5f : 4.2f,
            OneShot = burst,
            Explosiveness = burst ? 0.85f : 0f,
            Randomness = 0.4f,
            Emitting = false,
            LocalCoords = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = burst ? 5f : 3.5f,
            EmissionRingInnerRadius = burst ? 1.5f : 1f,
            EmissionRingHeight = 0.6f,
            Direction = new Vector3(1f, 0.12f, 0f),
            Spread = 180f,
            Flatness = 0.8f,
            InitialVelocityMin = burst ? 7f : 4f,
            InitialVelocityMax = burst ? 15f : 9f,
            RadialAccelMin = 0.5f,
            RadialAccelMax = burst ? 2f : 3f,
            Gravity = new Vector3(0, 0.7f, 0),
            DampingMin = burst ? 2f : 1.2f,
            DampingMax = burst ? 3.2f : 2.2f,
            AngleMin = 0f,
            AngleMax = 360f,
            AngularVelocityMin = -14f,
            AngularVelocityMax = 14f,
            ScaleAmountMin = burst ? 2.6f : 1.8f,
            ScaleAmountMax = burst ? 4.4f : 3.2f,
            ScaleAmountCurve = grow,
            ColorRamp = ramp,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new QuadMesh { Size = new Vector2(2f, 2f), Material = SmokeMaterial },
        };
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>0 at <paramref name="a"/>, 1 at <paramref name="b"/>, smooth between (either order; equal ends = a step).</summary>
    internal static float S(float a, float b, float x)
    {
        var d = b - a;
        if (Mathf.Abs(d) < 1e-6f) return x >= b ? 1f : 0f;
        var u = Mathf.Clamp((x - a) / d, 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    /// <summary>A node's transform relative to an ancestor, from the local transforms (works before it's in the tree).</summary>
    private static Transform3D ModelSpace(Node3D node, Node3D ancestor)
    {
        var t = node.Transform;
        for (var p = node.GetParent(); p != null && p != ancestor; p = p.GetParent())
            if (p is Node3D p3) t = p3.Transform * t;
        return t;
    }
}

/// <summary>
/// A rocket lander's way down (render-only), in τ = seconds before it arrives (0 = touchdown, or the convoy's hover):
/// a near-ballistic fall on the engines' idle, a hard deceleration burn at full throttle (the suicide burn), throttle
/// back into a brief hover a few units up with a sideways correction, then a gentle settle. The descent speed is
/// piecewise linear in τ (0.5 at contact, 3 by the end of the settle, 0.25 at the hover, v0 when the burn lights,
/// 0.55·v0 at the start), v0 chosen so it covers the whole height in the time given. The approach distance is
/// flown at constant speed and braked over the end of the fall (leaned back 24° on a quarter throttle) and the start
/// of the burn, all of it gone by the time it is 60 up, so the last stretch is straight down. For the full 7 s from
/// 260 up: falls at up to ~74 u/s, starts braking the approach ~180 up, lights the full burn at ~88 up (~32 u/s² of
/// braking for 2.3 s), upright by ~45, hovers ~2.9 up, settles 1.7 s later.
/// </summary>
internal readonly struct RocketDescent
{
    private const float FullSeconds = 7f;            // the phases below are for a descent this long (shorter: scaled)
    private const float SettleSeconds = 0.8f, HoverSeconds = 0.9f, BurnSeconds = 2.3f;
    private const float VContact = 0.5f, VSettle = 3f, VHover = 0.25f, EntryShare = 0.55f;
    private const float VerticalBelow = 60f;
    private const float CorrectionDistance = 1.6f;

    public readonly float Duration, Height, Reach;
    private readonly float _t1, _t2, _t3;            // τ at the end of the settle, the hover, the burn's start
    private readonly float _v0, _h1, _h2, _h3;
    private readonly float _tVertical, _tBrake, _u0;  // the approach: braked from _tBrake to _tVertical at _u0

    public RocketDescent(float duration, float height, float reach)
    {
        Duration = Mathf.Max(0.5f, duration);
        Height = height;
        Reach = reach;
        var k = Mathf.Min(1f, Duration / FullSeconds);
        _t1 = SettleSeconds * k;
        _t2 = _t1 + HoverSeconds * k;
        _t3 = _t2 + BurnSeconds * k;
        var fall = Duration - _t3;
        _h1 = _t1 * (VContact + VSettle) / 2f;
        _h2 = _h1 + (_t2 - _t1) * (VSettle + VHover) / 2f;
        var rest = height - _h2 - (_t3 - _t2) * VHover / 2f;
        _v0 = Mathf.Max(1f, rest / ((_t3 - _t2) / 2f + fall * (1f + EntryShare) / 2f));
        _h3 = _h2 + (_t3 - _t2) * (VHover + _v0) / 2f;
        _tVertical = 0f;
        _tBrake = 0f;
        _u0 = 0f;
        _tVertical = TauAt(Mathf.Min(VerticalBelow, 0.7f * _h3));
        _tBrake = _t3 + 0.45f * fall;
        _u0 = reach / Mathf.Max(0.1f, (_tBrake - _tVertical) / 2f + (Duration - _tBrake));
    }

    /// <summary>Height above the end point.</summary>
    public float HeightAt(float tau)
    {
        if (tau <= 0f) return 0f;
        if (tau <= _t1) return tau * (VContact + Mathf.Lerp(VContact, VSettle, tau / _t1)) / 2f;
        if (tau <= _t2) return _h1 + (tau - _t1) * (VSettle + Mathf.Lerp(VSettle, VHover, (tau - _t1) / (_t2 - _t1))) / 2f;
        if (tau <= _t3) return _h2 + (tau - _t2) * (VHover + Mathf.Lerp(VHover, _v0, (tau - _t2) / (_t3 - _t2))) / 2f;
        var fall = Mathf.Max(1e-4f, Duration - _t3);
        var x = Mathf.Min(tau, Duration) - _t3;
        var h = _h3 + x * (_v0 + Mathf.Lerp(_v0, _v0 * EntryShare, x / fall)) / 2f;
        return tau > Duration ? h + (tau - Duration) * _v0 * EntryShare : h;
    }

    /// <summary>Horizontal distance still to fly toward the end point (0 below ~60 up).</summary>
    public float ReachAt(float tau)
    {
        if (tau <= _tVertical) return 0f;
        var brake = Mathf.Max(0.05f, _tBrake - _tVertical);
        if (tau <= _tBrake)
        {
            var x = tau - _tVertical;
            return _u0 * x * x / (2f * brake);
        }
        return _u0 * brake / 2f + _u0 * (Mathf.Min(tau, Duration) - _tBrake);
    }

    /// <summary>The hover correction: how far to one side it still is (it comes out of the burn a little off and
    /// slides over while hovering), and its sideways lean in degrees toward where it is going.</summary>
    public float SideAt(float tau, out float leanDeg)
    {
        var from = _t2 + 0.2f;
        var to = _t1 * 0.5f;
        var p = 1f - RocketPlume.S(to, from, tau); // 0 → 1 as the correction happens
        leanDeg = tau < from && tau > to ? 3f * Mathf.Sin(Mathf.Tau * p) : 0f;
        return CorrectionDistance * (1f - p);
    }

    /// <summary>Lift throttle 0..1: idle on the fall, a quarter while it leans back to brake the approach, ignition,
    /// full burn, back to a hover, a settle.</summary>
    public float Throttle(float tau)
    {
        if (tau <= 0f) return 0.45f;
        var ignite = Mathf.Min(0.25f, 0.15f * (_t3 - _t2));
        var entry = 0.25f * RocketPlume.S(_tBrake, _tBrake - 0.4f, tau);
        var burn = 0.06f + Mathf.Max(entry, 0.94f * RocketPlume.S(_t3, _t3 - ignite, tau));
        var hover = Mathf.Lerp(0.45f, 0.62f, Mathf.Clamp(tau / Mathf.Max(0.1f, _t2), 0f, 1f));
        return Mathf.Lerp(burn, hover, RocketPlume.S(_t2 + 0.35f, _t2 - 0.1f, tau));
    }

    /// <summary>Nose-up lean in degrees (belly into the braking): a little on the fall, most while it brakes the
    /// approach, upright by the last ~45 units.</summary>
    public float LeanAt(float tau) =>
        24f * RocketPlume.S(_tVertical - 0.5f, _tVertical + 0.4f, tau) * (1f - 0.6f * RocketPlume.S(_tBrake, Duration, tau));

    /// <summary>1 while still turning in (heading, attitude) → 0 once it is coming straight down.</summary>
    public float TurnAt(float tau) => RocketPlume.S(_tVertical - 0.3f, _tBrake, tau);

    /// <summary>τ at which it is <paramref name="h"/> above the end point (bisection; set-up only).</summary>
    public float TauAt(float h)
    {
        float lo = 0f, hi = Duration;
        for (var i = 0; i < 28; i++)
        {
            var mid = (lo + hi) / 2f;
            if (HeightAt(mid) < h) lo = mid;
            else hi = mid;
        }
        return (lo + hi) / 2f;
    }
}

/// <summary>A rocket take-off (render-only), c = seconds since it left the ground (or the hover): straight up on a
/// growing burn, accelerating, then it pitches over toward its nose and away.</summary>
internal static class RocketClimb
{
    /// <summary>Height gained.</summary>
    public static float Up(float c) => c <= 0f ? 0f : 1.5f * c * c + 1.2f * c * c * c;

    /// <summary>Distance flown forward (along the nose) once it pitches over.</summary>
    public static float Ahead(float c)
    {
        var x = Mathf.Max(0f, c - 2.2f);
        return 2.5f * x * x * x;
    }

    /// <summary>Nose-down pitch in degrees.</summary>
    public static float Pitch(float c) => 32f * RocketPlume.S(2.2f, 5f, c);

    public static float Throttle(float c) => 0.85f + 0.15f * RocketPlume.S(0f, 1f, c);

    public static float Aft(float c) => 0.6f * RocketPlume.S(2.5f, 4.5f, c);
}
