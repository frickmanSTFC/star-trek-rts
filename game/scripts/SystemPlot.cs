using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The helm's star-system plot (OrbitView's System display): a top-down chart of the map's bodies (maps.json, the
/// sim's MapDef), laid flat in the XZ plane at MapScale world units per map unit (map +y = world -Z, away from the camera).
/// A glowing star with a point light at the centre, one faint orbit ring per orbital radius (the current planet's in
/// peach), each body a sphere coloured by class and sized by size, moons at their own map positions beside their
/// parent, asteroid belts as a dotted ring of rocks, billboard labels. Render-only: rebuilt whenever the map or the
/// current body changes.
/// </summary>
public partial class SystemPlot : Node3D
{
    /// <summary>World units per map unit: the mvp_duel bodies (out to ~540) fit in a radius of ~160.</summary>
    public const float MapScale = 0.3f;

    public static readonly Color Cyan = new(0.373f, 0.949f, 0.902f); // #5ff2e6
    public static readonly Color Peach = new(0.949f, 0.651f, 0.353f); // #f2a65a

    private readonly List<SystemBody> _bodies = new();
    private readonly Dictionary<string, (Node3D Node, float Radius)> _nodes = new();
    private MeshInstance3D? _highlight;

    public IReadOnlyList<SystemBody> Bodies => _bodies;

    /// <summary>A map's body by name or id (any case), or null.</summary>
    public static BodyDef? FindBody(MapDef? map, string? nameOrId)
    {
        if (map == null || string.IsNullOrEmpty(nameOrId)) return null;
        foreach (var b in map.Bodies)
            if (string.Equals(b.Name, nameOrId, StringComparison.OrdinalIgnoreCase) || string.Equals(b.Id, nameOrId, StringComparison.OrdinalIgnoreCase))
                return b;
        return null;
    }

    /// <summary>Colour of a body by planet class id (render-only palette).</summary>
    public static Color ClassColour(string classId) => classId switch
    {
        "class_m" => new Color(0.247f, 0.498f, 0.816f),
        "class_l" => new Color(0.851f, 0.722f, 0.471f),
        "class_d" => new Color(0.769f, 0.604f, 0.478f),
        "class_h" => new Color(0.878f, 0.69f, 0.44f),
        "class_k" => new Color(0.72f, 0.39f, 0.24f),
        "class_n" => new Color(0.18f, 0.44f, 0.69f),
        "class_j" => new Color(0.784f, 0.635f, 0.42f),
        "class_t" => new Color(0.73f, 0.56f, 0.41f),
        "class_y" => new Color(0.75f, 0.29f, 0.19f),
        "ice_moon" => new Color(0.737f, 0.847f, 0.937f),
        "asteroid" => new Color(0.6f, 0.57f, 0.52f),
        "asteroid_rare" => new Color(0.72f, 0.63f, 0.85f),
        _ => new Color(0.67f, 0.67f, 0.67f),
    };

    private static bool IsRocks(string classId) => classId is "asteroid" or "asteroid_rare";

    /// <summary>Sphere radius of a body in the plot (world units) by its map size.</summary>
    public static float BodyRadius(int size) => 1.5f + size * 1.25f;

    private static Vector3 PlotPosition(BodyDef b) => new(b.Position.X.ToFloat() * MapScale, 0f, -b.Position.Y.ToFloat() * MapScale);

    /// <param name="current">Name or id of the body we are orbiting (peach ring and label), or null.</param>
    public void Build(MapDef? map, GameData data, string? current)
    {
        _bodies.Clear();
        _nodes.Clear();
        if (map == null) return;
        var currentBody = FindBody(map, current);

        // The star: an emissive ball with two additive halos (the orbit glow picks it up), and the light for the plot.
        var starColour = new Color(1f, 0.78f, 0.4f);
        var starRadius = Mathf.Max(6f, map.Star.Radius.ToFloat() * MapScale);
        AddChild(new MeshInstance3D { Mesh = Sphere(starRadius, 32), MaterialOverride = Glow(starColour, 4f) });
        foreach (var (k, a) in new[] { (1.6f, 0.22f), (2.6f, 0.08f) })
            AddChild(new MeshInstance3D
            {
                Mesh = Sphere(starRadius * k, 24),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = starColour with { A = a },
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            });
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.92f, 0.8f), LightEnergy = 3f, OmniRange = 600f, OmniAttenuation = 0.4f, ShadowEnabled = false });
        AddChild(Label($"{map.Star.Name.ToUpperInvariant()} · {map.Star.Class}-CLASS STAR", new Vector3(0, starRadius + 3f, 0), starColour, 26));

        // One ring per orbital radius (the map is point-symmetric, so pairs share a ring); belts get rocks instead.
        var rings = new List<(float Radius, bool Current)>();
        var belts = new List<float>();
        foreach (var b in map.Bodies)
        {
            if (b.Parent != null) continue;
            var r = PlotPosition(b).Length();
            if (b.ClassId == "asteroid")
            {
                if (!belts.Exists(x => Mathf.Abs(x - r) < 5f)) belts.Add(r);
                continue;
            }
            if (IsRocks(b.ClassId)) continue;
            var i = rings.FindIndex(x => Mathf.Abs(x.Radius - r) < 1f);
            var isCurrent = b == currentBody;
            if (i < 0) rings.Add((r, isCurrent));
            else if (isCurrent) rings[i] = (rings[i].Radius, true);
        }
        foreach (var (r, cur) in rings)
            AddChild(OrbitLines.Instance(OrbitLines.Circle(r, 160), cur ? Peach with { A = 0.85f } : Cyan with { A = 0.28f }));
        var rng = new RandomNumberGenerator { Seed = 4711 };
        foreach (var r in belts) AddChild(Rocks(rng, 200, p => new Vector3(Mathf.Sin(p) * r, 0f, Mathf.Cos(p) * r), 3f, ClassColour("asteroid")));

        foreach (var b in map.Bodies)
        {
            var pos = PlotPosition(b);
            var isCurrent = b == currentBody;
            var colour = ClassColour(b.ClassId);
            var radius = BodyRadius(b.Size);
            var node = new Node3D { Name = b.Id, Position = pos };
            AddChild(node);
            if (IsRocks(b.ClassId))
            {
                radius = 4f + b.Size * 2f;
                node.AddChild(Rocks(rng, 26, _ => Vector3.Zero, radius * 0.8f, colour));
            }
            else
                node.AddChild(new MeshInstance3D
                {
                    Mesh = Sphere(radius, 24),
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f, EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = 0.18f },
                });
            if (isCurrent)
                node.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = radius + 2.4f, OuterRadius = radius + 3.1f, Rings = 48, RingSegments = 6 }, MaterialOverride = Glow(Peach, 1.6f) });

            var moons = 0;
            foreach (var m in map.Bodies)
                if (m.Parent == b.Id) moons++;
            var name = b.Name.ToUpperInvariant();
            var text = isCurrent ? $"{name} · IN ORBIT" : moons > 0 ? $"{name} · {moons} MOON{(moons > 1 ? "S" : "")}" : name;
            var textColour = isCurrent ? Peach : IsRocks(b.ClassId) ? Cyan : new Color(0.95f, 0.9f, 0.85f);
            if (b.Parent == null) node.AddChild(Label(text, new Vector3(0, radius + 3f, 0), textColour, 24));
            else
            {
                var moonLabel = Label(text, Vector3.Zero, textColour with { A = 0.8f }, 18);
                moonLabel.HorizontalAlignment = HorizontalAlignment.Left;
                moonLabel.VerticalAlignment = VerticalAlignment.Center;
                moonLabel.Offset = new Vector2(radius / moonLabel.PixelSize + 10f, 0f);
                node.AddChild(moonLabel);
            }

            _nodes[b.Id] = (node, radius);
            var className = b.ClassId;
            foreach (var pc in data.PlanetClasses)
                if (pc.Id == b.ClassId) className = pc.Name;
            _bodies.Add(new SystemBody(b.Id, b.Name, className, b.Size, new Vector2(b.Position.X.ToFloat(), b.Position.Y.ToFloat()), b.Parent, isCurrent));
        }

        _highlight = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 1f, OuterRadius = 1.08f, Rings = 64, RingSegments = 4 }, MaterialOverride = Glow(Cyan, 2f), Visible = false };
        AddChild(_highlight);
    }

    /// <summary>Ring one body in cyan (the helm's selection); null clears it.</summary>
    public void Highlight(string? id)
    {
        if (_highlight == null) return;
        if (id == null || !_nodes.TryGetValue(id, out var n))
        {
            _highlight.Visible = false;
            return;
        }
        _highlight.Visible = true;
        _highlight.Position = n.Node.Position;
        _highlight.Scale = Vector3.One * (n.Radius + 5f); // the torus is built at radius 1
    }

    /// <summary>Where a body is (global) and its drawn radius; false for an unknown id or name.</summary>
    public bool TryLocate(string nameOrId, out Vector3 global, out float radius)
    {
        foreach (var b in _bodies)
            if (string.Equals(b.Id, nameOrId, StringComparison.OrdinalIgnoreCase) || string.Equals(b.Name, nameOrId, StringComparison.OrdinalIgnoreCase))
            {
                var n = _nodes[b.Id];
                global = n.Node.GlobalPosition;
                radius = n.Radius;
                return true;
            }
        global = Vector3.Zero;
        radius = 0f;
        return false;
    }

    private static SphereMesh Sphere(float radius, int segments) => new() { Radius = radius, Height = radius * 2f, RadialSegments = segments, Rings = segments / 2 };

    private static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
    };

    private static Label3D Label(string text, Vector3 at, Color colour, int fontSize) => new()
    {
        Text = text,
        Position = at,
        FontSize = fontSize,
        PixelSize = 0.3f,
        Modulate = colour,
        OutlineSize = 8,
        OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        VerticalAlignment = VerticalAlignment.Bottom,
        RenderPriority = 21,
        OutlineRenderPriority = 20,
    };

    /// <summary>Low-poly rocks: count of them at place(angle), jittered by spread (flatter vertically).</summary>
    private static MultiMeshInstance3D Rocks(RandomNumberGenerator rng, int count, Func<float, Vector3> place, float spread, Color colour)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new SphereMesh { Radius = 0.7f, Height = 1.2f, RadialSegments = 5, Rings = 3 },
            InstanceCount = count,
        };
        for (var i = 0; i < count; i++)
        {
            var angle = (i + rng.RandfRange(-0.3f, 0.3f)) / count * Mathf.Tau;
            var jitter = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(-1f, 1f)) * spread;
            var axis = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f));
            if (axis.LengthSquared() < 0.01f) axis = Vector3.Up;
            var basis = new Basis(axis.Normalized(), rng.RandfRange(0f, Mathf.Tau)).Scaled(Vector3.One * rng.RandfRange(0.5f, 1.5f));
            mm.SetInstanceTransform(i, new Transform3D(basis, place(angle) + jitter));
            mm.SetInstanceColor(i, colour * rng.RandfRange(0.75f, 1.15f));
        }
        return new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
