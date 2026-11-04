using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>What the wheels or tracks throw up, by the ground under them.</summary>
public enum SprayKind { None, Dust, Dirt, Grit, Mud, Snow, Water }

/// <summary>
/// Wheel spray for a ground vehicle (render-only): a child of the vehicle model holding one emitter group per kind of
/// ground, created the first time the vehicle drives on it. Emitters sit at the rear wheels; particles live in world
/// space (LocalCoords off), so they hang in the air behind the vehicle as it drives on. Each kind mixes a soft haze
/// (transparent puffs that grow and fade) with hard bits (clods, stones, snow chunks, droplets that fall back down).
/// </summary>
public partial class SprayRig : Node3D
{
    private readonly Vector3[] _points;
    private readonly Dictionary<SprayKind, Node3D> _groups = new();
    private SprayKind _current = SprayKind.None;

    /// <param name="points">Emitter positions in the model's space, normally just behind each rear wheel at the ground.</param>
    public SprayRig(Vector3[] points)
    {
        Name = "SprayRig";
        _points = points;
    }

    // Parameterless constructor for Godot's script machinery.
    public SprayRig() : this(System.Array.Empty<Vector3>()) { }

    /// <summary>The ground a vehicle on this tile throws up. Wet ground is any plains or forest tile beside water.</summary>
    public static SprayKind KindFor(SurfaceMap map, int tile)
    {
        if (tile < 0) return SprayKind.None;
        var id = map.Terrain(tile).Id;
        if ((id == "plains" || id == "forest") && map.TouchesWater(tile)) return SprayKind.Water;
        return id switch
        {
            "desert" => SprayKind.Dust,
            "plains" => SprayKind.Dirt,
            "hills" => SprayKind.Grit,
            "forest" => SprayKind.Mud,
            "ice" => SprayKind.Snow,
            _ => SprayKind.None,
        };
    }

    /// <summary>Spray <paramref name="kind"/> while <paramref name="moving"/>; everything else stops emitting (puffs
    /// already in the air finish their life).</summary>
    public void Spray(SprayKind kind, bool moving)
    {
        var want = moving ? kind : SprayKind.None;
        if (want == _current) return;
        if (_current != SprayKind.None && _groups.TryGetValue(_current, out var old)) SetEmitting(old, false);
        _current = want;
        if (want == SprayKind.None) return;
        if (!_groups.TryGetValue(want, out var group))
        {
            group = Build(want);
            _groups[want] = group;
            AddChild(group);
        }
        SetEmitting(group, true);
    }

    private static void SetEmitting(Node3D group, bool on)
    {
        foreach (var child in group.GetChildren())
            if (child is CpuParticles3D p) p.Emitting = on;
    }

    // ---------------------------------------------------------------- looks

    /// <summary>One layer of a spray. Sizes are diameters in world units (a rover is ~5 long); Up is how steeply the bits
    /// leave the wheel (0 = straight back, 1 = 45° up).</summary>
    private sealed record Layer(bool Soft, Color Colour, int Amount, float Life, float Up, float Spread, float SpeedMin, float SpeedMax,
        float Gravity, float SizeMin, float SizeMax);

    private static readonly Dictionary<SprayKind, Layer[]> Looks = new()
    {
        [SprayKind.Dust] = new[]
        {
            new Layer(true, new Color(0.8f, 0.66f, 0.45f, 0.55f), 26, 2.4f, 0.35f, 35f, 2f, 4.5f, 0.25f, 0.7f, 1.3f),
            new Layer(false, new Color(0.62f, 0.5f, 0.34f), 10, 0.6f, 0.8f, 25f, 3f, 5f, -9.8f, 0.06f, 0.12f),
        },
        [SprayKind.Dirt] = new[]
        {
            new Layer(true, new Color(0.56f, 0.48f, 0.37f, 0.4f), 14, 1.5f, 0.35f, 35f, 1.5f, 3.5f, 0.15f, 0.6f, 1.1f),
            new Layer(false, new Color(0.33f, 0.25f, 0.17f), 14, 0.7f, 0.9f, 25f, 3f, 5.5f, -9.8f, 0.08f, 0.18f),
        },
        [SprayKind.Grit] = new[]
        {
            new Layer(true, new Color(0.62f, 0.6f, 0.56f, 0.45f), 18, 1.7f, 0.35f, 35f, 1.5f, 4f, 0.2f, 0.6f, 1.2f),
            new Layer(false, new Color(0.42f, 0.4f, 0.37f), 10, 0.6f, 0.8f, 30f, 3f, 5f, -9.8f, 0.08f, 0.16f),
        },
        [SprayKind.Mud] = new[]
        {
            new Layer(false, new Color(0.2f, 0.14f, 0.09f), 24, 0.8f, 1.1f, 25f, 3.5f, 6f, -9.8f, 0.08f, 0.2f),
            new Layer(true, new Color(0.32f, 0.26f, 0.2f, 0.25f), 6, 1f, 0.3f, 30f, 1f, 2f, 0f, 0.5f, 0.8f),
        },
        [SprayKind.Snow] = new[]
        {
            new Layer(true, new Color(0.93f, 0.95f, 1f, 0.6f), 30, 1.8f, 0.5f, 40f, 2.5f, 5f, -0.6f, 0.5f, 1.1f),
            new Layer(false, new Color(0.9f, 0.93f, 0.97f), 12, 0.7f, 0.9f, 25f, 3f, 5f, -9.8f, 0.08f, 0.16f),
        },
        [SprayKind.Water] = new[]
        {
            new Layer(false, new Color(0.72f, 0.82f, 0.92f), 28, 0.6f, 1.3f, 30f, 4f, 7f, -9.8f, 0.05f, 0.1f),
            new Layer(true, new Color(0.86f, 0.9f, 0.95f, 0.35f), 12, 0.9f, 0.6f, 40f, 2f, 3.5f, -1f, 0.4f, 0.8f),
        },
    };

    private static StandardMaterial3D? _softMaterial, _hardMaterial;

    private static StandardMaterial3D SoftMaterial => _softMaterial ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 1f,
    };

    private static StandardMaterial3D HardMaterial => _hardMaterial ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.9f,
    };

    private Node3D Build(SprayKind kind)
    {
        var group = new Node3D { Name = kind.ToString() };
        var i = 0;
        foreach (var layer in Looks[kind])
            foreach (var at in _points)
                group.AddChild(Emitter($"{kind}{i++}", layer, at));
        return group;
    }

    private static CpuParticles3D Emitter(string name, Layer l, Vector3 at)
    {
        var c = l.Colour;
        // Soft puffs grow and fade out; hard bits keep their colour and shrink away at the end of their arc.
        var ramp = l.Soft
            ? new Gradient { Offsets = new[] { 0f, 0.15f, 1f }, Colors = new[] { c with { A = 0f }, c, c with { A = 0f } } }
            : new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { c, c } };
        var scale = new Curve();
        if (l.Soft)
        {
            scale.AddPoint(new Vector2(0f, 0.35f));
            scale.AddPoint(new Vector2(1f, 1f));
        }
        else
        {
            scale.AddPoint(new Vector2(0f, 1f));
            scale.AddPoint(new Vector2(0.75f, 0.9f));
            scale.AddPoint(new Vector2(1f, 0f));
        }
        return new CpuParticles3D
        {
            Name = name,
            Position = at,
            Amount = l.Amount,
            Lifetime = l.Life,
            Emitting = false,
            LocalCoords = false, // trails stay where they were thrown up
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.25f, 0.05f, 0.15f),
            Direction = new Vector3(0, l.Up, 1f), // +Z is the back of a vehicle model
            Spread = l.Spread,
            InitialVelocityMin = l.SpeedMin,
            InitialVelocityMax = l.SpeedMax,
            Gravity = new Vector3(0, l.Gravity, 0),
            DampingMin = l.Soft ? 1.5f : 0.2f,
            DampingMax = l.Soft ? 2.5f : 0.5f,
            ScaleAmountMin = l.SizeMin,
            ScaleAmountMax = l.SizeMax,
            ScaleAmountCurve = scale,
            ColorRamp = ramp,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new SphereMesh
            {
                Radius = 0.5f,
                Height = 1f,
                RadialSegments = l.Soft ? 8 : 5,
                Rings = l.Soft ? 4 : 3,
                Material = l.Soft ? SoftMaterial : HardMaterial,
            },
        };
    }
}
