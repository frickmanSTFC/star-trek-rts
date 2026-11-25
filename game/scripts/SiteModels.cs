using System.Collections.Generic;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The space sites' 3D models (docs/space-jobs-contract.md "MODELS"; render-only): one glb,
/// <c>res://models/space/sites.glb</c> (blender/build_space_sites.py --game), whose root nodes are the models, each on its
/// own origin, bow -Z like the ships, metres-ish: derelict_unknown (Light0), derelict_earth, derelict_trader,
/// derelict_native, debris_piece_a..f (CargoPod, BlackBox with Beacon), asteroid_common / metal / ice / hollow, lifepod
/// (Beacon), comet_nucleus, station (Ring spins, Berth0..1), probe_alien. Materials Hull, Lamp, Beacon, Glow, Ice, Rock,
/// Metal. The glb is read once (kept as a template, out of the tree) and each model is a Duplicate of its node. A model
/// the file doesn't have (or no file / not imported yet) gets a primitive stand-in with the same node and part names, so
/// the view works the same either way (one warning per missing name). <see cref="Build"/> puts a whole site together:
/// a debris field is several pieces scattered round the middle, lifepods a ring of pods, an anomaly a set of glowing
/// shells (no model in the file); the pieces are placed from the site's own seed.
/// </summary>
public static class SiteModels
{
    public const string PathOfGlb = "res://models/space/sites.glb";

    private static Node? _template;
    private static bool _tried;
    private static readonly HashSet<string> Warned = new();

    /// <summary>The glb's root node name for a single model of a site kind and variant.</summary>
    public static string ModelName(SiteKind kind, string variant) => kind switch
    {
        SiteKind.Derelict => variant is "earth" or "trader" or "native" ? $"derelict_{variant}" : "derelict_unknown",
        SiteKind.KnownDerelict => variant is "trader" or "native" or "unknown" ? $"derelict_{variant}" : "derelict_earth",
        SiteKind.StrandedShip or SiteKind.HiddenShip => variant is "earth" or "native" ? $"derelict_{variant}" : "derelict_trader",
        SiteKind.Asteroid or SiteKind.RogueAsteroid => variant is "metal" or "ice" or "hollow" ? $"asteroid_{variant}" : "asteroid_common",
        SiteKind.Comet => "comet_nucleus",
        SiteKind.Lifepods => "lifepod",
        SiteKind.Station => "station",
        SiteKind.AlienProbe => "probe_alien",
        SiteKind.Debris => "debris_piece_a",
        _ => "",
    };

    /// <summary>The whole site as a scene node (not in the tree), centred on its origin, in model metres.</summary>
    public static Node3D Build(SiteKind kind, string variant, ulong seed)
    {
        var root = new Node3D { Name = "Site" };
        var rng = new RandomNumberGenerator { Seed = seed == 0 ? 1UL : seed };
        switch (kind)
        {
            case SiteKind.Debris:
            {
                // 9–12 pieces of the six kinds scattered through a loose cloud; the black box near the middle
                var count = 9 + rng.RandiRange(0, 3);
                string[] names = { "debris_piece_a", "debris_piece_b", "debris_piece_c", "debris_piece_d", "debris_piece_e", "debris_piece_f" };
                for (var i = 0; i < count; i++)
                {
                    var piece = Model(i == 0 ? names[5] : names[rng.RandiRange(0, 4)]);
                    var r = i == 0 ? 10f : Mathf.Sqrt(rng.Randf()) * 140f;
                    var a = rng.RandfRange(0f, Mathf.Tau);
                    piece.Position = new Vector3(Mathf.Sin(a) * r, rng.RandfRange(-28f, 28f), Mathf.Cos(a) * r);
                    piece.Basis = RandomBasis(rng);
                    piece.SetMeta(TumbleMeta, rng.RandfRange(0.05f, 0.25f));
                    root.AddChild(piece);
                }
                break;
            }
            case SiteKind.Lifepods:
            {
                var count = 6 + rng.RandiRange(0, 4);
                for (var i = 0; i < count; i++)
                {
                    var pod = Model("lifepod");
                    var a = i / (float)count * Mathf.Tau + rng.RandfRange(-0.2f, 0.2f);
                    var r = rng.RandfRange(25f, 70f);
                    pod.Position = new Vector3(Mathf.Sin(a) * r, rng.RandfRange(-12f, 12f), Mathf.Cos(a) * r);
                    pod.Basis = RandomBasis(rng);
                    pod.SetMeta(TumbleMeta, rng.RandfRange(0.1f, 0.4f));
                    root.AddChild(pod);
                }
                break;
            }
            case SiteKind.Anomaly:
                root.AddChild(Anomaly(variant));
                break;
            default:
            {
                var model = Model(ModelName(kind, variant));
                model.SetMeta(TumbleMeta, kind is SiteKind.Station or SiteKind.AlienProbe ? 0f : rng.RandfRange(0.02f, 0.06f));
                if (kind == SiteKind.Comet) model.AddChild(CometTail());
                root.AddChild(model);
                break;
            }
        }
        return root;
    }

    /// <summary>Meta on a node of the site: its own slow tumble (rad/s).</summary>
    public const string TumbleMeta = "site_tumble";

    private static Basis RandomBasis(RandomNumberGenerator rng)
    {
        var axis = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f));
        if (axis.LengthSquared() < 0.01f) axis = Vector3.Up;
        return new Basis(axis.Normalized(), rng.RandfRange(0f, Mathf.Tau));
    }

    /// <summary>One model by its glb root name: a copy from the file, else the stand-in.</summary>
    public static Node3D Model(string name)
    {
        var from = Template()?.FindChild(name, true, false) as Node3D;
        Node3D model;
        if (from != null)
        {
            model = (Node3D)from.Duplicate();
            model.Transform = Transform3D.Identity; // on its own origin
        }
        else
        {
            if (_template != null && Warned.Add(name)) GD.PushWarning($"SiteModels: {PathOfGlb} has no node \"{name}\"; using the stand-in");
            model = StandIn(name);
        }
        model.Name = name;
        return model;
    }

    private static Node? Template()
    {
        if (_tried) return _template;
        _tried = true;
        if (ResourceLoader.Exists(PathOfGlb))
        {
            var scene = GD.Load<PackedScene>(PathOfGlb);
            _template = scene?.Instantiate();
            if (_template == null) GD.PushWarning($"SiteModels: {PathOfGlb} did not load; using stand-ins");
        }
        else if (FileAccess.FileExists(PathOfGlb))
            GD.PushWarning($"SiteModels: {PathOfGlb} is not imported yet (open the project in Godot once); using stand-ins");
        return _template;
    }

    /// <summary>The axis-aligned box round every mesh of a node (not in the tree), in that node's frame.</summary>
    public static Aabb Bounds(Node3D root)
    {
        var box = new Aabb();
        var any = false;
        void Walk(Node node, Transform3D frame)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is not Node3D n) continue;
                var t = frame * n.Transform;
                if (n is MeshInstance3D { Mesh: { } mesh } && n.Name != "Tail") // a comet's tail doesn't size the site
                {
                    var b = t * mesh.GetAabb();
                    box = any ? box.Merge(b) : b;
                    any = true;
                }
                Walk(n, t);
            }
        }
        Walk(root, Transform3D.Identity);
        return any ? box : new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));
    }

    // ---------------------------------------------------------------- stand-ins (same names as the glb)

    private static readonly Dictionary<string, StandardMaterial3D> Materials = new();

    private static StandardMaterial3D Mat(string name)
    {
        if (Materials.TryGetValue(name, out var m)) return m;
        m = name switch
        {
            "Hull" => new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.42f, 0.44f), Metallic = 0.4f, Roughness = 0.7f },
            "Metal" => new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.53f, 0.5f), Metallic = 0.7f, Roughness = 0.45f },
            "Rock" => new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.33f, 0.3f), Roughness = 1f },
            "Ice" => new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.86f, 0.92f), Roughness = 0.35f },
            "Lamp" => new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.6f), EmissionEnabled = true, Emission = new Color(1f, 0.8f, 0.55f), EmissionEnergyMultiplier = 2f },
            "Beacon" => new StandardMaterial3D { AlbedoColor = new Color(1f, 0.3f, 0.2f), EmissionEnabled = true, Emission = new Color(1f, 0.25f, 0.15f), EmissionEnergyMultiplier = 4f },
            "Glow" => new StandardMaterial3D { AlbedoColor = new Color(0.66f, 0.62f, 1f), EmissionEnabled = true, Emission = new Color(0.6f, 0.55f, 1f), EmissionEnergyMultiplier = 3f },
            _ => new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.11f), Roughness = 1f }, // "Dark": holes, cavity
        };
        m.ResourceName = name;
        Materials[name] = m;
        return m;
    }

    private static MeshInstance3D Part(string name, Mesh mesh, string material, Vector3 at, Vector3? rotationDeg = null, Vector3? scale = null)
    {
        var mi = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = Mat(material), Position = at };
        if (rotationDeg is { } r) mi.RotationDegrees = r;
        if (scale is { } s) mi.Scale = s;
        return mi;
    }

    private static Mesh Box(float x, float y, float z) => new BoxMesh { Size = new Vector3(x, y, z) };
    private static Mesh Cyl(float r, float h, int seg = 16) => new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h, RadialSegments = seg, Rings = 1 };
    private static Mesh Ball(float r, int seg = 12) => new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = seg, Rings = seg / 2 };

    private static Node3D StandIn(string name)
    {
        var n = new Node3D { Name = name };
        n.SetMeta("site_stand_in", true);
        switch (name)
        {
            case "derelict_unknown":
                // ~180 m alien hull broken in the middle: two angular halves drifting apart, one faint aft light
                n.AddChild(Part("HullFore", Box(26, 14, 70), "Hull", new Vector3(0, 0, -50), new Vector3(0, 0, 8)));
                n.AddChild(Part("Prow", new PrismMesh { Size = new Vector3(26, 30, 14) }, "Hull", new Vector3(0, 0, -92), new Vector3(-90, 0, 8)));
                n.AddChild(Part("HullAft", Box(30, 16, 74), "Hull", new Vector3(3, -4, 44), new Vector3(6, 10, -12)));
                n.AddChild(Part("Fin", Box(4, 22, 30), "Hull", new Vector3(3, 12, 54), new Vector3(6, 10, -12)));
                n.AddChild(Part("Break", Box(24, 12, 6), "Dark", new Vector3(1, -2, -4), new Vector3(0, 4, 0)));
                n.AddChild(Part("Light0", Ball(1.6f, 8), "Lamp", new Vector3(4, 2, 82)));
                break;
            case "derelict_earth":
            case "derelict_trader":
            case "derelict_native":
            {
                var earth = name == "derelict_earth";
                n.AddChild(Part("Hull", Cyl(9, 110), "Hull", Vector3.Zero, new Vector3(90, 0, 0)));
                n.AddChild(Part("Bridge", Box(16, 10, 18), "Hull", new Vector3(0, 9, -44)));
                if (earth) n.AddChild(Part("Ring", new TorusMesh { InnerRadius = 20, OuterRadius = 26, Rings = 32, RingSegments = 8 }, "Metal", new Vector3(0, 0, 8), new Vector3(90, 0, 0)));
                else
                    for (var i = 0; i < 4; i++)
                        n.AddChild(Part($"Pod{i}", Box(14, 14, 18), name == "derelict_native" ? "Rock" : "Metal", new Vector3(i % 2 == 0 ? 14 : -14, 0, -20 + i / 2 * 26)));
                n.AddChild(Part("Engine", Cyl(11, 14), "Metal", new Vector3(0, 0, 60), new Vector3(90, 0, 0)));
                break;
            }
            case "debris_piece_a":
                n.AddChild(Part("Plate", Box(18, 1.2f, 11), "Hull", Vector3.Zero, new Vector3(0, 0, 12)));
                break;
            case "debris_piece_b":
                n.AddChild(Part("Plate", Box(9, 1, 16), "Hull", Vector3.Zero, new Vector3(20, 30, 0)));
                n.AddChild(Part("Rib", Box(1.4f, 3, 16), "Metal", new Vector3(3, 1.5f, 0), new Vector3(20, 30, 0)));
                break;
            case "debris_piece_c":
                n.AddChild(Part("Nacelle", Cyl(4, 26), "Metal", Vector3.Zero, new Vector3(90, 0, 0)));
                n.AddChild(Part("Cap", Ball(4.2f), "Hull", new Vector3(0, 0, -13)));
                break;
            case "debris_piece_d":
                n.AddChild(Part("Truss", Box(2, 2, 30), "Metal", Vector3.Zero, new Vector3(0, 40, 15)));
                n.AddChild(Part("Bit", Box(6, 4, 5), "Hull", new Vector3(4, 0, 9)));
                break;
            case "debris_piece_e":
                n.AddChild(Part("CargoPod", Cyl(4, 11, 12), "Metal", Vector3.Zero, new Vector3(90, 0, 0)));
                break;
            case "debris_piece_f":
            {
                var box = new Node3D { Name = "BlackBox" };
                box.AddChild(Part("Case", Box(2.4f, 1.6f, 3.2f), "Dark", Vector3.Zero));
                box.AddChild(Part("Beacon", Ball(0.5f, 8), "Beacon", new Vector3(0, 1.1f, 0)));
                n.AddChild(box);
                break;
            }
            case "asteroid_common":
            case "asteroid_metal":
            case "asteroid_ice":
            case "asteroid_hollow":
            {
                var mat = name == "asteroid_ice" ? "Ice" : name == "asteroid_metal" ? "Metal" : "Rock";
                n.AddChild(Part("Rock", Ball(70, 10), mat, Vector3.Zero, new Vector3(20, 30, 10), new Vector3(1.3f, 0.85f, 1f)));
                n.AddChild(Part("Lump", Ball(40, 8), mat, new Vector3(60, 20, -20), new Vector3(0, 40, 30)));
                if (name == "asteroid_hollow")
                    n.AddChild(Part("hollow", new TorusMesh { InnerRadius = 10, OuterRadius = 18, Rings = 24, RingSegments = 8 }, "Dark", new Vector3(0, 0, -62), new Vector3(90, 0, 0)));
                break;
            }
            case "comet_nucleus":
                n.AddChild(Part("Nucleus", Ball(45, 10), "Ice", Vector3.Zero, new Vector3(10, 20, 0), new Vector3(1.2f, 0.8f, 0.9f)));
                break;
            case "lifepod":
                n.AddChild(Part("Pod", new CapsuleMesh { Radius = 1.6f, Height = 5.5f }, "Hull", Vector3.Zero, new Vector3(90, 0, 0)));
                n.AddChild(Part("Beacon", Ball(0.45f, 8), "Beacon", new Vector3(0, 1.6f, -1f)));
                break;
            case "station":
                n.AddChild(Part("Hub", Cyl(12, 70), "Hull", Vector3.Zero, new Vector3(90, 0, 0)));
                var ring = new Node3D { Name = "Ring" };
                ring.AddChild(Part("RingBody", new TorusMesh { InnerRadius = 52, OuterRadius = 60, Rings = 48, RingSegments = 8 }, "Metal", Vector3.Zero, new Vector3(90, 0, 0)));
                for (var i = 0; i < 4; i++)
                    ring.AddChild(Part($"Spoke{i}", Box(2, 52, 2), "Metal", Vector3.Zero, new Vector3(0, 0, i * 45f)));
                n.AddChild(ring);
                n.AddChild(new Node3D { Name = "Berth0", Position = new Vector3(0, 14, -30) });
                n.AddChild(new Node3D { Name = "Berth1", Position = new Vector3(0, -14, -30) });
                n.AddChild(Part("Lamp0", Ball(1.4f, 8), "Lamp", new Vector3(12, 0, -34)));
                n.AddChild(Part("Beacon", Ball(1.6f, 8), "Beacon", new Vector3(0, 13, 34)));
                break;
            case "probe_alien":
                n.AddChild(Part("Body", Ball(9, 4), "Metal", Vector3.Zero, new Vector3(0, 45, 0), new Vector3(1f, 1.6f, 1f)));
                n.AddChild(Part("Glow", Ball(3.5f, 10), "Glow", Vector3.Zero));
                break;
            default:
                n.AddChild(Part("Body", Ball(20, 8), "Hull", Vector3.Zero));
                break;
        }
        return n;
    }

    /// <summary>An anomaly / ion storm: three nested additive shells (outer / middle / inner rings) and a bright core.</summary>
    private static Node3D Anomaly(string variant)
    {
        var n = new Node3D { Name = "anomaly" };
        var colour = variant.Contains("ion") ? new Color(0.62f, 0.58f, 1f) : new Color(0.75f, 0.55f, 1f);
        float[] radii = { 120f, 75f, 36f };
        for (var i = 0; i < radii.Length; i++)
        {
            var shell = new MeshInstance3D
            {
                Name = $"Ring{i}",
                Mesh = Ball(radii[i], 24),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = colour with { A = 0.05f + i * 0.05f },
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    NoDepthTest = false,
                },
            };
            shell.SetMeta(TumbleMeta, 0.05f + i * 0.04f);
            n.AddChild(shell);
        }
        n.AddChild(Part("Glow", Ball(9f, 12), "Glow", Vector3.Zero));
        return n;
    }

    /// <summary>A faint additive tail behind a comet's nucleus (away along +Z).</summary>
    private static Node3D CometTail() => new MeshInstance3D
    {
        Name = "Tail",
        Mesh = new CylinderMesh { TopRadius = 4f, BottomRadius = 40f, Height = 420f, RadialSegments = 16, Rings = 1, CapTop = false, CapBottom = false },
        Position = new Vector3(0, 0, 230f),
        RotationDegrees = new Vector3(-90f, 0, 0),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.85f, 1f, 0.12f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        },
    };
}
