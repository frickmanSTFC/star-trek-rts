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
        "deep_core_mine" => DeepCoreMine(owner),
        "storage_container" => StorageContainer(owner),
        "refinery" => Refinery(owner),
        "foundry" => Foundry(owner),
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
        // Storage vault at the back: an armoured block with lit bands.
        Add(n, new BoxMesh { Size = new Vector3(3.4f, 2.2f, 2.2f) }, new Vector3(0, 1.1f, -4.9f), trim);
        Add(n, new BoxMesh { Size = new Vector3(3.5f, 0.18f, 2.3f) }, new Vector3(0, 1.5f, -4.9f), Glow(owner, 1.4f));
        Add(n, new BoxMesh { Size = new Vector3(1.4f, 1.3f, 0.1f) }, new Vector3(0, 0.75f, -6.02f), Metal(new Color(0.55f, 0.5f, 0.32f), 0.7f, 0.35f));
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

    /// <summary>Deep-core mine: a heavier derrick over a lit shaft ring, with a big drill ("Spin") and pressure tanks.</summary>
    private static Node3D DeepCoreMine(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var dark = Metal(Trim, 0.3f, 0.6f);
        Add(n, new CylinderMesh { TopRadius = 3.6f, BottomRadius = 4.1f, Height = 0.8f, RadialSegments = 8 }, new Vector3(0, 0.4f, 0), dark);
        Add(n, new TorusMesh { InnerRadius = 1.5f, OuterRadius = 1.9f, Rings = 24 }, new Vector3(0, 0.85f, 0), Glow(new Color(1f, 0.5f, 0.2f), 2.6f));
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var leg = Add(n, new BoxMesh { Size = new Vector3(0.35f, 10f, 0.35f) }, new Vector3(x * 1.6f, 5.4f, z * 1.6f), hull);
            leg.RotationDegrees = new Vector3(z * 7f, 0, -x * 7f);
        }
        Add(n, new BoxMesh { Size = new Vector3(2.4f, 1f, 2.4f) }, new Vector3(0, 10.4f, 0), hull);
        Add(n, new BoxMesh { Size = new Vector3(2.5f, 0.2f, 2.5f) }, new Vector3(0, 10.9f, 0), Glow(owner, 2f));
        var spin = new Node3D { Name = "Spin", Position = new Vector3(0, 4f, 0) };
        Add(spin, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 6f, RadialSegments = 8 }, new Vector3(0, 1.5f, 0), Metal(new Color(0.6f, 0.6f, 0.65f), 0.85f, 0.2f));
        Add(spin, new CylinderMesh { TopRadius = 1.1f, BottomRadius = 0.15f, Height = 3f, RadialSegments = 6 }, new Vector3(0, -2.4f, 0), Metal(new Color(0.75f, 0.62f, 0.4f), 0.8f, 0.25f));
        n.AddChild(spin);
        foreach (var x in new[] { -2.9f, 2.9f })
            Add(n, new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.8f, Height = 3f, RadialSegments = 12 }, new Vector3(x, 2.3f, 2.2f), hull);
        n.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 1.4f, OmniRange = 14f, Position = new Vector3(0, 2, 0) });
        return n;
    }

    /// <summary>Storage container yard: stacked, ribbed cargo containers in a few colours and a small gantry crane.</summary>
    private static Node3D StorageContainer(Color owner)
    {
        var n = new Node3D();
        Add(n, new BoxMesh { Size = new Vector3(7.4f, 0.4f, 7f) }, new Vector3(0, 0.2f, 0), Metal(Trim, 0.3f, 0.7f));
        var colours = new[] { new Color(0.72f, 0.36f, 0.22f), new Color(0.3f, 0.45f, 0.62f), new Color(0.78f, 0.7f, 0.42f), new Color(0.42f, 0.55f, 0.4f) };
        var slots = new[] { (-1.8f, -1.8f, 0), (1.8f, -1.8f, 0), (-1.8f, 1.2f, 0), (1.8f, 1.2f, 0), (-1.8f, -1.8f, 1), (1.8f, 1.2f, 1), (-1.8f, 1.2f, 1) };
        for (var i = 0; i < slots.Length; i++)
        {
            var (x, z, level) = slots[i];
            var box = new Node3D { Position = new Vector3(x, 0.4f + 1.35f + level * 2.7f, z), RotationDegrees = new Vector3(0, i % 2 * 2f - 1f, 0) };
            var paint = Metal(colours[i % colours.Length], 0.45f, 0.55f);
            Add(box, new BoxMesh { Size = new Vector3(3.2f, 2.6f, 2.6f) }, Vector3.Zero, paint);
            for (var r = -1; r <= 1; r++)
                Add(box, new BoxMesh { Size = new Vector3(0.12f, 2.4f, 2.7f) }, new Vector3(r * 1.1f, 0, 0), Metal(colours[i % colours.Length].Darkened(0.25f), 0.45f, 0.55f));
            n.AddChild(box);
        }
        // Gantry crane over the yard.
        var hull = Metal(HullColour);
        foreach (var x in new[] { -3.5f, 3.5f })
            Add(n, new BoxMesh { Size = new Vector3(0.3f, 7.4f, 0.3f) }, new Vector3(x, 3.9f, 3.1f), hull);
        Add(n, new BoxMesh { Size = new Vector3(7.4f, 0.45f, 0.45f) }, new Vector3(0, 7.6f, 3.1f), hull);
        Add(n, new BoxMesh { Size = new Vector3(1f, 0.6f, 0.9f) }, new Vector3(0.8f, 7.1f, 3.1f), Glow(owner, 1.6f));
        Add(n, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 2.6f }, new Vector3(0.8f, 5.6f, 3.1f), Metal(Trim));
        return n;
    }

    /// <summary>Ore refinery: a squat smelter with a glowing melt slot, two stacks, an ore hopper and a spinning cooling fan ("Spin").</summary>
    private static Node3D Refinery(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var dark = Metal(Trim, 0.35f, 0.55f);
        var molten = new Color(1f, 0.48f, 0.15f);
        Add(n, new BoxMesh { Size = new Vector3(7f, 0.6f, 6.6f) }, new Vector3(0, 0.3f, 0), dark);
        Add(n, new CylinderMesh { TopRadius = 2.1f, BottomRadius = 2.6f, Height = 4.2f, RadialSegments = 16 }, new Vector3(-0.6f, 2.7f, 0), hull);
        Add(n, new BoxMesh { Size = new Vector3(2.4f, 0.5f, 0.2f) }, new Vector3(-0.6f, 1.6f, 2.5f), Glow(molten, 3.2f));
        foreach (var (x, z) in new[] { (-1.7f, -1.4f), (0.4f, -1.6f) })
        {
            Add(n, new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.6f, Height = 6.5f, RadialSegments = 10 }, new Vector3(x, 5.2f, z), hull);
            Add(n, new CylinderMesh { TopRadius = 0.47f, BottomRadius = 0.47f, Height = 0.25f, RadialSegments = 10 }, new Vector3(x, 8.4f, z), Glow(molten, 2f));
        }
        // Ore hopper on the right, chute into the smelter.
        Add(n, new CylinderMesh { TopRadius = 1.3f, BottomRadius = 0.5f, Height = 2f, RadialSegments = 8 }, new Vector3(2.4f, 3.4f, 1f), hull);
        Add(n, new BoxMesh { Size = new Vector3(2f, 0.3f, 0.7f) }, new Vector3(1.4f, 2.4f, 1f), dark, new Vector3(0, 0, 25));
        Add(n, new BoxMesh { Size = new Vector3(0.3f, 2.4f, 0.3f) }, new Vector3(2.4f, 1.2f, 1f), dark);
        // Cooling fan housing at the back right.
        Add(n, new CylinderMesh { TopRadius = 1.2f, BottomRadius = 1.2f, Height = 0.6f, RadialSegments = 16 }, new Vector3(2.3f, 1f, -2f), dark);
        var spin = new Node3D { Name = "Spin", Position = new Vector3(2.3f, 1.35f, -2f) };
        Add(spin, new BoxMesh { Size = new Vector3(2.1f, 0.08f, 0.35f) }, Vector3.Zero, hull);
        Add(spin, new BoxMesh { Size = new Vector3(0.35f, 0.08f, 2.1f) }, Vector3.Zero, hull);
        n.AddChild(spin);
        Add(n, new BoxMesh { Size = new Vector3(7.02f, 0.16f, 6.62f) }, new Vector3(0, 0.62f, 0), Glow(owner, 1.2f));
        n.AddChild(new OmniLight3D { LightColor = molten, LightEnergy = 1.5f, OmniRange = 12f, Position = new Vector3(-0.6f, 1.8f, 3.5f) });
        return n;
    }

    /// <summary>Alloy foundry: a long casting hall with a glowing crucible under an arch, ingot stacks out front and a stack ("Spin" = crucible stirrer).</summary>
    private static Node3D Foundry(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var dark = Metal(Trim, 0.35f, 0.55f);
        var hot = new Color(1f, 0.62f, 0.2f);
        Add(n, new BoxMesh { Size = new Vector3(7.2f, 0.5f, 7f) }, new Vector3(0, 0.25f, 0), dark);
        Add(n, new BoxMesh { Size = new Vector3(6.2f, 3.4f, 3.2f) }, new Vector3(0, 2.2f, -1.6f), hull);
        Add(n, new BoxMesh { Size = new Vector3(6.3f, 0.2f, 3.3f) }, new Vector3(0, 3.95f, -1.6f), Glow(owner, 1.5f));
        Add(n, new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.7f, Height = 5f, RadialSegments = 10 }, new Vector3(2.2f, 5.5f, -2.2f), hull);
        // Arch and crucible in front of the hall.
        foreach (var x in new[] { -1.6f, 1.6f })
            Add(n, new BoxMesh { Size = new Vector3(0.35f, 3.6f, 0.35f) }, new Vector3(x, 2.3f, 1.2f), hull);
        Add(n, new BoxMesh { Size = new Vector3(3.6f, 0.4f, 0.5f) }, new Vector3(0, 4.1f, 1.2f), hull);
        Add(n, new CylinderMesh { TopRadius = 1.1f, BottomRadius = 0.8f, Height = 1.2f, RadialSegments = 14 }, new Vector3(0, 1.1f, 1.2f), dark);
        Add(n, new CylinderMesh { TopRadius = 0.95f, BottomRadius = 0.95f, Height = 0.1f, RadialSegments = 14 }, new Vector3(0, 1.72f, 1.2f), Glow(hot, 3.5f));
        var spin = new Node3D { Name = "Spin", Position = new Vector3(0, 2.6f, 1.2f) };
        Add(spin, new BoxMesh { Size = new Vector3(1.4f, 0.12f, 0.12f) }, Vector3.Zero, dark);
        Add(spin, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 1.4f }, new Vector3(0.6f, -0.6f, 0), dark);
        n.AddChild(spin);
        // Parasteel ingots stacked by the door.
        var ingot = Metal(new Color(0.62f, 0.66f, 0.74f), 0.85f, 0.25f);
        for (var i = 0; i < 6; i++)
            Add(n, new BoxMesh { Size = new Vector3(1.1f, 0.35f, 0.45f) }, new Vector3(-2.6f + (i % 3) * 0.05f, 0.7f + (i / 2) * 0.36f, 2.2f + (i % 2) * 0.5f), ingot);
        n.AddChild(new OmniLight3D { LightColor = hot, LightEnergy = 1.6f, OmniRange = 12f, Position = new Vector3(0, 2.5f, 2f) });
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

    /// <summary>
    /// Mining crawler, folded for driving. Parts the view animates as it unfurls: "Leg0".."Leg3" (stabilizer
    /// pivots; rotate about local Z from FoldedLeg to DeployedLeg), "Boom" (arm pivot; rotate about X up to
    /// DeployedBoom), "Drill" under the boom tip (keep it upright, scale Y to reach the ground, spin "Bit"),
    /// "Fill" (ore in the hopper; scale Y 0..1), "Dust" (particles while digging), "WorkLight", "Selected".
    /// Front is -Z, like the rover.
    /// </summary>
    public static Node3D Crawler(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(new Color(0.86f, 0.74f, 0.36f), 0.45f, 0.45f); // hazard yellow work vehicle
        var trim = Metal(Trim, 0.4f, 0.5f);
        var tread = Metal(new Color(0.1f, 0.1f, 0.12f), 0.15f, 0.9f);
        var steel = Metal(new Color(0.62f, 0.64f, 0.7f), 0.8f, 0.3f);
        var body = new Node3D { Name = "Body" };
        n.AddChild(body);
        // Tracks with road wheels.
        foreach (var x in new[] { -1.75f, 1.75f })
        {
            Add(body, new BoxMesh { Size = new Vector3(0.9f, 1.1f, 5.6f) }, new Vector3(x, 0.6f, 0), tread);
            for (var k = 0; k < 4; k++)
                Add(body, new CylinderMesh { TopRadius = 0.38f, BottomRadius = 0.38f, Height = 0.95f, RadialSegments = 10 }, new Vector3(x, 0.55f, -2f + k * 1.33f), trim, new Vector3(0, 0, 90));
        }
        Add(body, new BoxMesh { Size = new Vector3(2.7f, 1f, 5.4f) }, new Vector3(0, 1.45f, 0), hull);
        Add(body, new BoxMesh { Size = new Vector3(2.72f, 0.16f, 5.42f) }, new Vector3(0, 1.75f, 0), Glow(owner, 1.6f));
        // Cab at the front.
        Add(body, new BoxMesh { Size = new Vector3(2.2f, 1.2f, 1.5f) }, new Vector3(0, 2.5f, -1.8f), hull);
        Add(body, new BoxMesh { Size = new Vector3(1.9f, 0.55f, 0.08f) }, new Vector3(0, 2.65f, -2.56f), Glow(Window, 1.5f));
        // Hopper at the back, with the ore load inside.
        Add(body, new BoxMesh { Size = new Vector3(2.4f, 0.15f, 2f) }, new Vector3(0, 2.05f, 1.6f), trim);
        foreach (var (x, z, sx, sz) in new[] { (-1.15f, 1.6f, 0.12f, 2f), (1.15f, 1.6f, 0.12f, 2f), (0f, 0.62f, 2.4f, 0.12f), (0f, 2.58f, 2.4f, 0.12f) })
            Add(body, new BoxMesh { Size = new Vector3(sx, 1.2f, sz) }, new Vector3(x, 2.7f, z), hull);
        var fill = new Node3D { Name = "Fill", Position = new Vector3(0, 2.12f, 1.6f), Scale = new Vector3(1, 0.01f, 1) };
        Add(fill, new BoxMesh { Size = new Vector3(2.2f, 1.1f, 1.8f) }, new Vector3(0, 0.55f, 0), Metal(new Color(0.48f, 0.42f, 0.36f), 0.2f, 0.85f));
        body.AddChild(fill);
        // Beacon.
        Add(body, new SphereMesh { Radius = 0.22f, Height = 0.44f }, new Vector3(0.8f, 3.25f, -1.8f), Glow(new Color(1f, 0.6f, 0.15f), 3f));

        // Stabilizer legs: pivots at the corners, the leg reaching outward along local +X.
        var corners = new[] { (1.3f, -1.9f), (1.3f, 1.9f), (-1.3f, -1.9f), (-1.3f, 1.9f) };
        for (var i = 0; i < corners.Length; i++)
        {
            var (x, z) = corners[i];
            var pivot = new Node3D { Name = $"Leg{i}", Position = new Vector3(x, 1.5f, z), RotationDegrees = new Vector3(0, x < 0 ? 180 : 0, FoldedLeg) };
            Add(pivot, new BoxMesh { Size = new Vector3(2.3f, 0.28f, 0.34f) }, new Vector3(1.15f, 0, 0), steel);
            Add(pivot, new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.42f, Height = 0.2f }, new Vector3(2.3f, -0.1f, 0), trim, new Vector3(0, 0, 0));
            n.AddChild(pivot);
        }

        // Drill boom: pivots at the back of the roof and reaches forward over the cab.
        var boom = new Node3D { Name = "Boom", Position = new Vector3(0, BoomPivotY, 1.1f) };
        Add(boom, new BoxMesh { Size = new Vector3(0.55f, 0.5f, BoomLength) }, new Vector3(0, 0, -BoomLength / 2f), steel);
        Add(boom, new BoxMesh { Size = new Vector3(0.6f, 0.08f, BoomLength - 0.4f) }, new Vector3(0, 0.29f, -BoomLength / 2f), Glow(owner, 1.2f));
        var drill = new Node3D { Name = "Drill", Position = new Vector3(0, 0, -BoomLength) };
        // The drill string hangs from the tip; its length is 1 unit so scaling Y sets how far down it reaches.
        var strand = new Node3D { Name = "String" };
        Add(strand, new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 1f, RadialSegments = 8 }, new Vector3(0, -0.5f, 0), steel);
        drill.AddChild(strand);
        var bit = new Node3D { Name = "Bit", Position = new Vector3(0, -0.6f, 0) };
        Add(bit, new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.05f, Height = 1.3f, RadialSegments = 6 }, new Vector3(0, -0.65f, 0), Metal(new Color(0.75f, 0.62f, 0.4f), 0.85f, 0.25f));
        Add(bit, new BoxMesh { Size = new Vector3(1.3f, 0.12f, 0.12f) }, new Vector3(0, -0.2f, 0), steel);
        drill.AddChild(bit);
        Add(drill, new BoxMesh { Size = new Vector3(0.8f, 0.5f, 0.8f) }, Vector3.Zero, trim);
        boom.AddChild(drill);
        n.AddChild(boom);

        // Dust kicked up where the drill meets the ground (off until digging).
        var dust = new CpuParticles3D
        {
            Name = "Dust",
            Position = new Vector3(0, 0.3f, -BoomReach),
            Amount = 26,
            Lifetime = 1.6f,
            Emitting = false,
            LocalCoords = false,
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 4f,
            Gravity = new Vector3(0, -1.2f, 0),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.3f,
            ColorRamp = new Gradient
            {
                Offsets = new[] { 0f, 0.25f, 1f },
                Colors = new[] { new Color(0.75f, 0.66f, 0.52f, 0f), new Color(0.72f, 0.64f, 0.5f, 0.55f), new Color(0.6f, 0.55f, 0.45f, 0f) },
            },
            Mesh = new SphereMesh
            {
                Radius = 0.5f,
                Height = 1f,
                RadialSegments = 6,
                Rings = 3,
                Material = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            },
        };
        n.AddChild(dust);
        n.AddChild(new OmniLight3D { Name = "WorkLight", LightColor = new Color(1f, 0.85f, 0.6f), LightEnergy = 1.6f, OmniRange = 10f, Position = new Vector3(0, 3.5f, -BoomReach), Visible = false });
        n.AddChild(new SpotLight3D { LightColor = new Color(1f, 0.95f, 0.85f), LightEnergy = 1.6f, SpotRange = 14f, SpotAngle = 30f, Position = new Vector3(0, 2.4f, -2.6f), RotationDegrees = new Vector3(-15, 0, 0) });
        n.AddChild(new MeshInstance3D
        {
            Name = "Selected",
            Mesh = new TorusMesh { InnerRadius = 3.6f, OuterRadius = 3.9f, Rings = 32 },
            Position = new Vector3(0, 0.15f, 0),
            MaterialOverride = Glow(new Color(1f, 0.95f, 0.5f), 2f),
            Visible = false,
        });
        return n;
    }

    /// <summary>Leg pivot angles (degrees about local Z): folded up against the hull, and braced out on the ground.</summary>
    public const float FoldedLeg = 78f;
    public const float DeployedLeg = -38f;

    /// <summary>Boom raise when deployed (degrees about X; positive lifts the front end).</summary>
    public const float DeployedBoom = 32f;

    private const float BoomLength = 5.4f;

    /// <summary>The boom rests just above the cab and hopper when folded.</summary>
    private const float BoomPivotY = 3.45f;

    /// <summary>Where the drill tip ends up in front of the crawler when deployed (horizontal distance from its centre).</summary>
    public static readonly float BoomReach = BoomLength * Mathf.Cos(Mathf.DegToRad(DeployedBoom)) - 1.1f;

    /// <summary>Height of the boom tip above the ground when deployed (the drill string must cover it).</summary>
    public static readonly float BoomTipHeight = BoomPivotY + BoomLength * Mathf.Sin(Mathf.DegToRad(DeployedBoom));

    /// <summary>Cargo pod: a small capsule with a lit band and an engine glow ("Flame") at the back (+Z).</summary>
    public static Node3D CargoPod(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(new Color(0.88f, 0.89f, 0.92f), 0.6f, 0.3f);
        Add(n, new CapsuleMesh { Radius = 0.55f, Height = 2.2f, RadialSegments = 12, Rings = 4 }, Vector3.Zero, hull, new Vector3(90, 0, 0));
        Add(n, new CylinderMesh { TopRadius = 0.58f, BottomRadius = 0.58f, Height = 0.25f, RadialSegments = 12 }, Vector3.Zero, Glow(owner, 2.4f), new Vector3(90, 0, 0));
        var flame = new Node3D { Name = "Flame", Position = new Vector3(0, 0, 1.2f) };
        Add(flame, new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0f, Height = 1.6f, RadialSegments = 8 }, new Vector3(0, 0, 0.8f), Glow(new Color(0.55f, 0.85f, 1f), 4f), new Vector3(-90, 0, 0));
        n.AddChild(flame);
        n.AddChild(new OmniLight3D { LightColor = new Color(0.55f, 0.85f, 1f), LightEnergy = 1.2f, OmniRange = 8f });
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
