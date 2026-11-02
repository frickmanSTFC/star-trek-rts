using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Draws the star system: star, planets and moons, asteroid fields, orbit rings, name labels,
/// colony markers and a starfield. Render-only; reads the sim, never changes it.
/// </summary>
public partial class SystemView : Node3D
{
    private sealed class BodyVisual
    {
        public required Body Body;
        public required float Radius;
        public required Label3D Label;
        public required MeshInstance3D ColonyRing;
        public required StandardMaterial3D ColonyMaterial;
    }

    private readonly List<BodyVisual> _bodies = new();
    private Simulation _sim = null!;
    private Color[] _playerColors = null!;

    /// <summary>Placeholder look per planet class until real art exists.</summary>
    private static readonly Dictionary<string, Color> ClassColors = new()
    {
        ["class_m"] = new(0.25f, 0.55f, 0.9f),
        ["class_l"] = new(0.5f, 0.6f, 0.35f),
        ["class_k"] = new(0.7f, 0.45f, 0.3f),
        ["class_h"] = new(0.85f, 0.7f, 0.45f),
        ["class_d"] = new(0.55f, 0.55f, 0.55f),
        ["class_j"] = new(0.85f, 0.6f, 0.4f),
        ["class_t"] = new(0.6f, 0.75f, 0.85f),
        ["class_n"] = new(0.85f, 0.8f, 0.5f),
        ["class_y"] = new(0.7f, 0.2f, 0.15f),
        ["ice_moon"] = new(0.8f, 0.92f, 1f),
        ["asteroid"] = new(0.5f, 0.45f, 0.4f),
        ["asteroid_rare"] = new(0.6f, 0.45f, 0.75f),
    };

    /// <summary>On-screen radius for a body (map units). Asteroid fields return their scatter radius.</summary>
    public static float VisualRadius(Body body) => IsField(body) ? 14f + body.Size * 14f : 3f + body.Size * 5f;

    private static bool IsField(Body body) => body.ClassId.StartsWith("asteroid");

    public void Build(Simulation sim, Color[] playerColors)
    {
        _sim = sim;
        _playerColors = playerColors;
        BuildStar();
        BuildStarfield();
        foreach (var body in sim.Bodies) BuildBody(body);
    }

    /// <summary>Refresh colony markers and labels (cheap; called every frame).</summary>
    public void Refresh()
    {
        foreach (var v in _bodies)
        {
            var colony = v.Body.ColonyId == 0 ? null : _sim.FindColony(v.Body.ColonyId);
            v.ColonyRing.Visible = colony != null;
            if (colony == null)
            {
                v.Label.Modulate = new Color(0.8f, 0.85f, 0.9f);
                continue;
            }
            var color = _playerColors[colony.Owner % _playerColors.Length];
            v.ColonyMaterial.AlbedoColor = color;
            v.Label.Modulate = color;
        }
    }

    /// <summary>The body under a point on the system plane, if any.</summary>
    public Body? BodyAt(Vector3 point)
    {
        Body? best = null;
        var bestDist = float.MaxValue;
        foreach (var v in _bodies)
        {
            var d = SimDriver.ToWorld(v.Body.Position).DistanceTo(point);
            if (d < v.Radius + 8f && d < bestDist)
            {
                best = v.Body;
                bestDist = d;
            }
        }
        return best;
    }

    private void BuildStar()
    {
        var map = _sim.Map!;
        var r = map.Star.Radius.ToFloat();
        var color = new Color(1f, 0.85f, 0.55f);
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = r, Height = r * 2 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = color,
            },
        });
        AddChild(new OmniLight3D { LightColor = color, LightEnergy = 3f, OmniRange = 3000f, OmniAttenuation = 0.4f });
        AddChild(MakeLabel(map.Star.Name, new Vector3(0, r + 12, 0), color));
    }

    private void BuildBody(Body body)
    {
        var pos = SimDriver.ToWorld(body.Position);
        var radius = VisualRadius(body);
        var color = ClassColors.TryGetValue(body.ClassId, out var c) ? c : new Color(0.6f, 0.6f, 0.6f);

        if (IsField(body)) BuildAsteroidField(body, pos, radius, color);
        else
        {
            var planet = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = radius, Height = radius * 2 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f },
                Position = pos,
            };
            AddChild(planet);
        }

        // Orbit ring around the star, or around the parent for moons.
        var centre = body.ParentIndex >= 0 ? SimDriver.ToWorld(_sim.Bodies[body.ParentIndex].Position) : Vector3.Zero;
        AddChild(MakeCircle(centre, centre.DistanceTo(pos), new Color(0.35f, 0.45f, 0.6f, body.ParentIndex >= 0 ? 0.25f : 0.35f)));

        var colonyMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Colors.White,
        };
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = radius + 4f, OuterRadius = radius + 6f, Rings = 48 },
            MaterialOverride = colonyMaterial,
            Position = pos,
            Visible = false,
        };
        AddChild(ring);

        var label = MakeLabel(body.Name, pos + new Vector3(0, radius + 8f, -radius - 4f), Colors.White);
        AddChild(label);
        _bodies.Add(new BodyVisual { Body = body, Radius = radius, Label = label, ColonyRing = ring, ColonyMaterial = colonyMaterial });
    }

    private void BuildAsteroidField(Body body, Vector3 centre, float radius, Color color)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(body.Index + 1) * 7919UL }; // render-only randomness
        var count = 25 + body.Size * 20;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 6, Rings = 3 },
        };
        mm.InstanceCount = count;
        for (var i = 0; i < count; i++)
        {
            var angle = rng.RandfRange(0, Mathf.Tau);
            var dist = Mathf.Sqrt(rng.Randf()) * radius;
            var size = rng.RandfRange(0.8f, 2.6f);
            var basis = new Basis(new Vector3(rng.Randf(), rng.Randf(), rng.Randf()).Normalized(), rng.RandfRange(0, Mathf.Tau))
                .Scaled(new Vector3(size, size * rng.RandfRange(0.6f, 1f), size));
            var offset = new Vector3(Mathf.Cos(angle) * dist, rng.RandfRange(-2f, 2f), Mathf.Sin(angle) * dist);
            mm.SetInstanceTransform(i, new Transform3D(basis, centre + offset));
        }
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f },
        });
    }

    private void BuildStarfield()
    {
        var rng = new RandomNumberGenerator { Seed = 1701UL };
        const int count = 900;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 4, Rings = 2 },
        };
        mm.InstanceCount = count;
        for (var i = 0; i < count; i++)
        {
            var s = rng.RandfRange(1.5f, 4f);
            var p = new Vector3(rng.RandfRange(-4000, 4000), rng.RandfRange(-1500, -600), rng.RandfRange(-4000, 4000));
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s)), p));
        }
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.75f, 0.8f, 1f),
            },
        });
    }

    private static MeshInstance3D MakeCircle(Vector3 centre, float radius, Color color)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = color,
        };
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip, material);
        const int segments = 160;
        for (var i = 0; i <= segments; i++)
        {
            var a = Mathf.Tau * i / segments;
            mesh.SurfaceAddVertex(centre + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D { Mesh = mesh };
    }

    private static Label3D MakeLabel(string text, Vector3 position, Color color) => new()
    {
        Text = text,
        Position = position,
        Modulate = color,
        FontSize = 40,
        PixelSize = 0.35f,
        OutlineSize = 10,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
    };
}
