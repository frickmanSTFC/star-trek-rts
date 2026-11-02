using Godot;

namespace FinalFrontier;

/// <summary>
/// 3D models for surface buildings, the colony pod and rovers, built from primitives until real art exists.
/// Every building is about one hex across, sits on y = 0, and has its "front" toward +Z so facing
/// (60° steps) is visible. Nodes named "Spin" turn while the building works; glowing parts use
/// emission so the scene's glow picks them up.
/// </summary>
public static class BuildingModels
{
    private static readonly Color HullColour = new(0.84f, 0.86f, 0.9f);
    private static readonly Color Trim = new(0.3f, 0.32f, 0.38f);
    private static readonly Color Window = new(0.55f, 0.8f, 1f);

    public static Node3D Build(string defId, Color owner) => defId switch
    {
        "colony_hub" => Hub(owner, 1),
        "colonial_administration" => Hub(owner, 2),
        "planetary_capital" => Hub(owner, 3),
        "mine" => Mine(owner),
        "fusion_reactor" => Reactor(owner),
        "prefab_habitat" => Habitat(owner),
        _ => Generic(owner),
    };

    // ---------------------------------------------------------------- materials

    private static StandardMaterial3D Metal(Color colour, float metallic = 0.55f, float roughness = 0.35f) =>
        new() { AlbedoColor = colour, Metallic = metallic, Roughness = roughness };

    private static StandardMaterial3D Glow(Color colour, float energy = 2.4f) => new()
    {
        AlbedoColor = colour,
        EmissionEnabled = true,
        Emission = colour,
        EmissionEnergyMultiplier = energy,
        Roughness = 0.3f,
    };

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Vector3 position, Material material, Vector3? rotationDegrees = null)
    {
        var m = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material };
        if (rotationDegrees is { } r) m.RotationDegrees = r;
        parent.AddChild(m);
        return m;
    }

    // ---------------------------------------------------------------- buildings

    /// <summary>Colony core: a domed hub that grows a ring of modules and a taller tower with each level.</summary>
    private static Node3D Hub(Color owner, int level)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var trim = Metal(Trim, 0.4f, 0.5f);
        Add(n, new CylinderMesh { TopRadius = 4.3f, BottomRadius = 4.7f, Height = 1.2f, RadialSegments = 24 }, new Vector3(0, 0.6f, 0), trim);
        Add(n, new CylinderMesh { TopRadius = 3.9f, BottomRadius = 4.2f, Height = 1f, RadialSegments = 24 }, new Vector3(0, 1.7f, 0), hull);
        Add(n, new TorusMesh { InnerRadius = 3.95f, OuterRadius = 4.25f, Rings = 32 }, new Vector3(0, 1.9f, 0), Glow(Window, 1.6f));
        Add(n, new SphereMesh { Radius = 3.1f, Height = 3.6f, IsHemisphere = true, RadialSegments = 24 }, new Vector3(0, 2.2f, 0), hull);
        // Tower and beacon, taller per level.
        var tower = 3.5f + level * 1.6f;
        Add(n, new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.6f, Height = tower }, new Vector3(0, 2.2f + tower / 2f, 0), hull);
        Add(n, new SphereMesh { Radius = 0.45f, Height = 0.9f }, new Vector3(0, 2.4f + tower, 0), Glow(owner, 3f));
        // Entrance at the front.
        Add(n, new BoxMesh { Size = new Vector3(2f, 1.8f, 1.6f) }, new Vector3(0, 0.9f, 4.5f), hull);
        Add(n, new BoxMesh { Size = new Vector3(1.2f, 1.1f, 0.1f) }, new Vector3(0, 0.9f, 5.32f), Glow(Window, 1.2f));
        // Extra modules from level 2 up.
        for (var i = 0; i < (level - 1) * 3; i++)
        {
            var a = Mathf.DegToRad(60f + i * (360f / ((level - 1) * 3)));
            var p = new Vector3(Mathf.Sin(a) * 4.6f, 0, Mathf.Cos(a) * 4.6f);
            if (p.Z > 3.5f) continue;
            Add(n, new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.2f, Height = 1.6f, RadialSegments = 12 }, p + new Vector3(0, 0.8f, 0), hull);
            Add(n, new CylinderMesh { TopRadius = 1.12f, BottomRadius = 1.12f, Height = 0.15f, RadialSegments = 12 }, p + new Vector3(0, 1.3f, 0), Glow(owner, 1.8f));
        }
        n.AddChild(new OmniLight3D { LightColor = owner, LightEnergy = 1.4f, OmniRange = 18f, Position = new Vector3(0, 6, 0) });
        return n;
    }

    private static Node3D Mine(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var dark = Metal(Trim, 0.3f, 0.6f);
        Add(n, new BoxMesh { Size = new Vector3(6.6f, 0.7f, 6.6f) }, new Vector3(0, 0.35f, 0), dark);
        // Derrick: four angled legs meeting under a cap.
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var leg = Add(n, new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.18f, Height = 7f, RadialSegments = 6 }, new Vector3(x * 1.1f, 4.1f, z * 1.1f), hull);
            leg.RotationDegrees = new Vector3(z * 8f, 0, -x * 8f);
        }
        Add(n, new BoxMesh { Size = new Vector3(1.6f, 0.7f, 1.6f) }, new Vector3(0, 7.6f, 0), Glow(owner, 2f));
        var spin = new Node3D { Name = "Spin", Position = new Vector3(0, 2.6f, 0) };
        Add(spin, new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.1f, Height = 3.2f, RadialSegments = 6 }, Vector3.Zero, Metal(new Color(0.7f, 0.62f, 0.45f), 0.8f, 0.25f));
        n.AddChild(spin);
        // Ore hopper, conveyor toward the front, and a work light.
        Add(n, new CylinderMesh { TopRadius = 1.4f, BottomRadius = 0.7f, Height = 2.2f, RadialSegments = 8 }, new Vector3(2.3f, 1.8f, -1.9f), hull);
        Add(n, new BoxMesh { Size = new Vector3(1f, 0.35f, 3.6f) }, new Vector3(0, 0.95f, 2.6f), dark);
        Add(n, new BoxMesh { Size = new Vector3(0.9f, 0.08f, 3.4f) }, new Vector3(0, 1.15f, 2.6f), Glow(new Color(1f, 0.75f, 0.4f), 1.2f));
        n.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.82f, 0.6f), LightEnergy = 0.9f, OmniRange = 12f, Position = new Vector3(0, 5, 2) });
        return n;
    }

    private static Node3D Reactor(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        Add(n, new CylinderMesh { TopRadius = 3.6f, BottomRadius = 4f, Height = 0.9f, RadialSegments = 20 }, new Vector3(0, 0.45f, 0), Metal(Trim, 0.4f, 0.5f));
        Add(n, new CylinderMesh { TopRadius = 1.7f, BottomRadius = 2.2f, Height = 4.8f, RadialSegments = 20 }, new Vector3(0, 3.3f, 0), hull);
        Add(n, new SphereMesh { Radius = 1.7f, Height = 1.6f, IsHemisphere = true }, new Vector3(0, 5.7f, 0), hull);
        var core = new Color(0.35f, 0.85f, 1f);
        var spin = new Node3D { Name = "Spin", Position = new Vector3(0, 3.4f, 0) };
        Add(spin, new TorusMesh { InnerRadius = 2.35f, OuterRadius = 2.75f, Rings = 32 }, Vector3.Zero, Glow(core, 3f));
        Add(spin, new BoxMesh { Size = new Vector3(5.6f, 0.25f, 0.25f) }, Vector3.Zero, hull);
        n.AddChild(spin);
        // Two cooling towers at the back.
        foreach (var x in new[] { -2.7f, 2.7f })
        {
            Add(n, new CylinderMesh { TopRadius = 0.85f, BottomRadius = 1.25f, Height = 3.8f, RadialSegments = 14 }, new Vector3(x, 2.3f, -2.1f), hull);
            Add(n, new CylinderMesh { TopRadius = 0.86f, BottomRadius = 0.86f, Height = 0.2f, RadialSegments = 14 }, new Vector3(x, 4.1f, -2.1f), Glow(owner, 1.6f));
        }
        Add(n, new BoxMesh { Size = new Vector3(1.6f, 1.2f, 1f) }, new Vector3(0, 1.1f, 3.4f), hull);
        Add(n, new BoxMesh { Size = new Vector3(1f, 0.6f, 0.1f) }, new Vector3(0, 1.2f, 3.92f), Glow(Window, 1.2f));
        n.AddChild(new OmniLight3D { LightColor = core, LightEnergy = 1.6f, OmniRange = 16f, Position = new Vector3(0, 4, 0) });
        return n;
    }

    private static Node3D Habitat(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(HullColour, 0.35f, 0.4f);
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.75f, 0.9f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.6f,
            Roughness = 0.08f,
            EmissionEnabled = true,
            Emission = new Color(1f, 0.85f, 0.6f),
            EmissionEnergyMultiplier = 0.35f,
        };
        var domes = new[] { new Vector3(0, 0, -2f), new Vector3(-2.3f, 0, 1.6f), new Vector3(2.3f, 0, 1.6f) };
        foreach (var d in domes)
        {
            Add(n, new CylinderMesh { TopRadius = 1.9f, BottomRadius = 2.05f, Height = 0.6f, RadialSegments = 16 }, d + new Vector3(0, 0.3f, 0), hull);
            Add(n, new SphereMesh { Radius = 1.8f, Height = 2.4f, IsHemisphere = true, RadialSegments = 16 }, d + new Vector3(0, 0.6f, 0), glass);
            Add(n, new CylinderMesh { TopRadius = 1.92f, BottomRadius = 1.92f, Height = 0.1f, RadialSegments = 16 }, d + new Vector3(0, 0.62f, 0), Glow(owner, 1.4f));
        }
        // Connecting tubes and a lit commons at the front.
        Add(n, new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 4f }, new Vector3(0, 0.6f, 1.6f), hull, new Vector3(0, 0, 90));
        Add(n, new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 3.2f }, new Vector3(0, 0.6f, 0), hull, new Vector3(90, 0, 0));
        Add(n, new BoxMesh { Size = new Vector3(1.4f, 0.25f, 1.4f) }, new Vector3(0, 0.13f, 3.6f), Glow(Window, 1f));
        return n;
    }

    private static Node3D Generic(Color owner)
    {
        var n = new Node3D();
        Add(n, new BoxMesh { Size = new Vector3(5f, 2.5f, 5f) }, new Vector3(0, 1.25f, 0), Metal(HullColour));
        Add(n, new BoxMesh { Size = new Vector3(1.4f, 1f, 0.2f) }, new Vector3(0, 0.8f, 2.6f), Glow(owner, 1.6f));
        return n;
    }

    // ---------------------------------------------------------------- vehicles

    /// <summary>Colony pod: capsule hull, nose, fins, landing legs, glowing band and engine flame ("Flame").</summary>
    public static Node3D Pod()
    {
        var n = new Node3D();
        var hull = Metal(new Color(0.9f, 0.91f, 0.94f), 0.6f, 0.3f);
        Add(n, new CylinderMesh { TopRadius = 2.1f, BottomRadius = 2.6f, Height = 4.5f, RadialSegments = 20 }, new Vector3(0, 3.6f, 0), hull);
        Add(n, new CylinderMesh { TopRadius = 0.5f, BottomRadius = 2.1f, Height = 2.6f, RadialSegments = 20 }, new Vector3(0, 7.15f, 0), hull);
        Add(n, new TorusMesh { InnerRadius = 2.3f, OuterRadius = 2.6f, Rings = 32 }, new Vector3(0, 2.4f, 0), Glow(new Color(0.45f, 0.75f, 1f), 3f));
        Add(n, new CylinderMesh { TopRadius = 1.4f, BottomRadius = 1.9f, Height = 1f, RadialSegments = 16 }, new Vector3(0, 0.9f, 0), Metal(new Color(0.25f, 0.26f, 0.3f)));
        for (var i = 0; i < 4; i++)
        {
            var a = Mathf.DegToRad(45f + i * 90f);
            var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            // Tilt each leg outward around the horizontal axis across it (set directly: the pod isn't in the scene yet).
            var leg = Add(n, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 3.2f }, dir * 3.1f + new Vector3(0, 1.4f, 0), hull);
            leg.Basis = new Basis(new Vector3(dir.Z, 0, -dir.X), Mathf.DegToRad(-28f));
            Add(n, new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.55f, Height = 0.2f }, dir * 3.7f + new Vector3(0, 0.1f, 0), hull);
            Add(n, new BoxMesh { Size = new Vector3(0.15f, 2.2f, 1.2f) }, dir * 2.4f + new Vector3(0, 5.2f, 0), hull, new Vector3(0, -45f - i * 90f, 0));
        }
        var flame = new Node3D { Name = "Flame" };
        Add(flame, new CylinderMesh { TopRadius = 1.5f, BottomRadius = 0f, Height = 7f, RadialSegments = 14 }, new Vector3(0, -3.2f, 0), Glow(new Color(1f, 0.55f, 0.2f), 4f));
        Add(flame, new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0f, Height = 4f, RadialSegments = 10 }, new Vector3(0, -1.8f, 0), Glow(new Color(1f, 0.92f, 0.7f), 5f));
        n.AddChild(flame);
        n.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 5f, OmniRange = 45f, Position = new Vector3(0, -3, 0) });
        return n;
    }

    /// <summary>Survey rover: six wheels, cabin with a lit window, spinning sensor dish ("Dish"), selection ring ("Selected").</summary>
    public static Node3D Rover(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(HullColour, 0.45f, 0.4f);
        var tyre = Metal(new Color(0.12f, 0.12f, 0.14f), 0.1f, 0.9f);
        Add(n, new BoxMesh { Size = new Vector3(2.4f, 0.8f, 3.8f) }, new Vector3(0, 1f, 0), hull);
        Add(n, new BoxMesh { Size = new Vector3(2f, 0.9f, 1.4f) }, new Vector3(0, 1.85f, -0.9f), hull);
        Add(n, new BoxMesh { Size = new Vector3(1.7f, 0.45f, 0.08f) }, new Vector3(0, 1.95f, -1.62f), Glow(Window, 1.5f));
        Add(n, new BoxMesh { Size = new Vector3(2.42f, 0.18f, 3.82f) }, new Vector3(0, 1.2f, 0), Glow(owner, 1.6f));
        foreach (var z in new[] { -1.3f, 0f, 1.3f })
            foreach (var x in new[] { -1.35f, 1.35f })
                Add(n, new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.4f, RadialSegments = 12 }, new Vector3(x, 0.5f, z), tyre, new Vector3(0, 0, 90));
        var dish = new Node3D { Name = "Dish", Position = new Vector3(0, 2.3f, 0.9f) };
        Add(dish, new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 1f }, new Vector3(0, 0.4f, 0), hull);
        Add(dish, new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.1f, Height = 0.35f, RadialSegments = 12 }, new Vector3(0, 1f, 0), hull, new Vector3(25, 0, 0));
        n.AddChild(dish);
        var ring = new MeshInstance3D
        {
            Name = "Selected",
            Mesh = new TorusMesh { InnerRadius = 2.6f, OuterRadius = 2.9f, Rings = 32 },
            Position = new Vector3(0, 0.15f, 0),
            MaterialOverride = Glow(new Color(1f, 0.95f, 0.5f), 2f),
            Visible = false,
        };
        n.AddChild(ring);
        n.AddChild(new SpotLight3D { LightColor = new Color(1f, 0.95f, 0.85f), LightEnergy = 2f, SpotRange = 16f, SpotAngle = 30f, Position = new Vector3(0, 1.6f, -2f), RotationDegrees = new Vector3(-15, 0, 0) });
        return n;
    }

    /// <summary>Replace every material in a model (ghosts and construction holograms) and switch off its lights.</summary>
    public static void Override(Node node, Material material)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh) mesh.MaterialOverride = material;
            if (child is Light3D light) light.Visible = false;
            Override(child, material);
        }
    }
}
