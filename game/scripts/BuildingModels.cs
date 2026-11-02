using Godot;

namespace FinalFrontier;

/// <summary>
/// Placeholder 3D models for surface buildings, built from primitives until real art exists.
/// Every model is about one hex across, sits on y = 0, and has its "front" toward +Z so facing
/// (60° steps) is visible. Nodes named "Spin" turn while the building is working.
/// </summary>
public static class BuildingModels
{
    private static readonly Color Hull = new(0.86f, 0.88f, 0.92f);
    private static readonly Color Dark = new(0.32f, 0.34f, 0.4f);

    public static Node3D Build(string defId, Color owner) => defId switch
    {
        "colony_hub" => Hub(owner),
        "mine" => Mine(owner),
        "fusion_reactor" => Reactor(owner),
        "prefab_habitat" => Habitat(owner),
        _ => Generic(owner),
    };

    private static Node3D Hub(Color owner)
    {
        var n = new Node3D();
        Add(n, new CylinderMesh { TopRadius = 4.2f, BottomRadius = 4.6f, Height = 1.6f }, new Vector3(0, 0.8f, 0), Hull);
        Add(n, new SphereMesh { Radius = 3.2f, Height = 3.4f, IsHemisphere = true }, new Vector3(0, 1.6f, 0), Hull);
        Add(n, new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.25f, Height = 5f }, new Vector3(2.4f, 4f, -1.2f), Hull);
        Add(n, new BoxMesh { Size = new Vector3(1.8f, 1.6f, 1.4f) }, new Vector3(0, 0.8f, 4.4f), Dark); // entrance
        Add(n, new TorusMesh { InnerRadius = 4.5f, OuterRadius = 5f }, new Vector3(0, 1.65f, 0), owner, glow: true);
        n.AddChild(new OmniLight3D { LightColor = owner, LightEnergy = 1.5f, OmniRange = 18f, Position = new Vector3(0, 6, 0) });
        return n;
    }

    private static Node3D Mine(Color owner)
    {
        var n = new Node3D();
        Add(n, new BoxMesh { Size = new Vector3(6.5f, 0.8f, 6.5f) }, new Vector3(0, 0.4f, 0), Dark);
        // Derrick: four legs and a cap.
        foreach (var (x, z) in new[] { (-1.2f, -1.2f), (1.2f, -1.2f), (-1.2f, 1.2f), (1.2f, 1.2f) })
            Add(n, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.2f, Height = 6.5f }, new Vector3(x * 0.8f, 4f, z * 0.8f), Hull);
        Add(n, new BoxMesh { Size = new Vector3(2.4f, 0.6f, 2.4f) }, new Vector3(0, 7.2f, 0), owner, glow: true);
        var spin = new Node3D { Name = "Spin", Position = new Vector3(0, 2.4f, 0) };
        Add(spin, new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.1f, Height = 3f, RadialSegments = 6 }, Vector3.Zero, Hull);
        n.AddChild(spin);
        // Ore hopper and conveyor toward the front.
        Add(n, new BoxMesh { Size = new Vector3(2.2f, 1.8f, 2.2f) }, new Vector3(2.2f, 1.7f, -1.8f), Dark);
        Add(n, new BoxMesh { Size = new Vector3(1f, 0.4f, 3.6f) }, new Vector3(0, 1f, 2.6f), Hull);
        return n;
    }

    private static Node3D Reactor(Color owner)
    {
        var n = new Node3D();
        Add(n, new CylinderMesh { TopRadius = 3.6f, BottomRadius = 4f, Height = 1f }, new Vector3(0, 0.5f, 0), Dark);
        Add(n, new CylinderMesh { TopRadius = 1.8f, BottomRadius = 2.2f, Height = 4.5f }, new Vector3(0, 3.2f, 0), Hull);
        var spin = new Node3D { Name = "Spin", Position = new Vector3(0, 3.4f, 0) };
        Add(spin, new TorusMesh { InnerRadius = 2.4f, OuterRadius = 2.9f }, Vector3.Zero, new Color(0.35f, 0.85f, 1f), glow: true);
        n.AddChild(spin);
        // Two cooling towers at the back.
        Add(n, new CylinderMesh { TopRadius = 0.8f, BottomRadius = 1.2f, Height = 3.8f }, new Vector3(-2.6f, 2.4f, -2f), Hull);
        Add(n, new CylinderMesh { TopRadius = 0.8f, BottomRadius = 1.2f, Height = 3.8f }, new Vector3(2.6f, 2.4f, -2f), Hull);
        Add(n, new BoxMesh { Size = new Vector3(1.6f, 1.2f, 1f) }, new Vector3(0, 1.1f, 3.4f), owner, glow: true);
        n.AddChild(new OmniLight3D { LightColor = new Color(0.4f, 0.85f, 1f), LightEnergy = 1.2f, OmniRange = 14f, Position = new Vector3(0, 4, 0) });
        return n;
    }

    private static Node3D Habitat(Color owner)
    {
        var n = new Node3D();
        var domes = new[] { new Vector3(0, 0, -2f), new Vector3(-2.3f, 0, 1.6f), new Vector3(2.3f, 0, 1.6f) };
        foreach (var d in domes)
        {
            Add(n, new CylinderMesh { TopRadius = 1.9f, BottomRadius = 2f, Height = 0.5f }, d + new Vector3(0, 0.25f, 0), Dark);
            Add(n, new SphereMesh { Radius = 1.8f, Height = 2.2f, IsHemisphere = true }, d + new Vector3(0, 0.5f, 0), Hull);
        }
        // Connecting tubes and a lit commons at the front.
        Add(n, new BoxMesh { Size = new Vector3(4f, 0.6f, 0.6f) }, new Vector3(0, 0.6f, 1.6f), Hull);
        Add(n, new BoxMesh { Size = new Vector3(0.6f, 0.6f, 3.2f) }, new Vector3(0, 0.6f, 0), Hull);
        Add(n, new BoxMesh { Size = new Vector3(1.2f, 0.3f, 1.2f) }, new Vector3(0, 0.15f, 3.6f), owner, glow: true);
        return n;
    }

    private static Node3D Generic(Color owner)
    {
        var n = new Node3D();
        Add(n, new BoxMesh { Size = new Vector3(5f, 2.5f, 5f) }, new Vector3(0, 1.25f, 0), Hull);
        Add(n, new BoxMesh { Size = new Vector3(1.4f, 1f, 0.4f) }, new Vector3(0, 0.8f, 2.6f), owner, glow: true);
        return n;
    }

    private static void Add(Node3D parent, Mesh mesh, Vector3 position, Color colour, bool glow = false)
    {
        var material = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.45f, Metallic = 0.15f };
        if (glow) material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material });
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
