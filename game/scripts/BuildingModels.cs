using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// 3D models for surface buildings, the colony pod and rovers, built from primitives until real art exists.
/// Every building is about one hex across, sits on y = 0, and has its "front" toward +Z so facing
/// (60° steps) is visible. Nodes named "Spin" turn while the building works; glowing parts use
/// emission so the scene's glow picks them up.
/// </summary>
public static partial class BuildingModels
{
    private static readonly Color HullColour = new(0.84f, 0.86f, 0.9f);
    private static readonly Color Trim = new(0.3f, 0.32f, 0.38f);
    private static readonly Color Window = new(0.55f, 0.8f, 1f);

    /// <summary>The canonical look of a building (placement ghost): no per-instance variety.</summary>
    public static Node3D Build(string defId, Color owner) => BuildModel(defId, owner, Look.Plain);

    /// <summary>A building with its own render-only look (BuildingModels.Variety.cs): <paramref name="seed"/> picks the
    /// tint, accent, weathering, proportions, mirrored sub-parts and add-ons; <paramref name="serial"/> is the number
    /// painted on it ("STORAGE 03"), 0 = none.</summary>
    public static Node3D Build(string defId, Color owner, int seed, int serial = 0)
    {
        var look = new Look(defId, seed);
        var n = BuildModel(defId, owner, look);
        Dress(n, defId, look, serial);
        return n;
    }

    private static Node3D BuildModel(string defId, Color owner, Look v) => defId switch
    {
        "colony_hub" => OutpostL1(owner),
        "colonial_administration" => Hub(owner, 2),
        "planetary_capital" => Hub(owner, 3),
        "mine" => Mine(owner, v),
        "deep_core_mine" => DeepCoreMine(owner, v),
        "storage_container" => StorageContainer(owner, v),
        "refinery" => Refinery(owner, v),
        "foundry" => Foundry(owner, v),
        "vehicle_bay" => VehicleBay(owner, v),
        "science_lab" => ResearchLab(owner, v),
        "fusion_reactor" => Reactor(owner, v),
        "prefab_habitat" => Habitat(owner, v),
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
        RepairPad(n, owner);
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

    /// <summary>Mine: a derrick (height ±10% per instance) over a drill ("Spin"), the ore hopper on one side or the
    /// other, a conveyor to the front.</summary>
    private static Node3D Mine(Color owner, Look v)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var dark = Metal(Trim, 0.3f, 0.6f);
        Add(n, new BoxMesh { Size = new Vector3(6.6f, 0.7f, 6.6f) }, new Vector3(0, 0.35f, 0), dark);
        // Derrick: four angled legs meeting under a cap.
        var h = v.Height;
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var leg = Add(n, new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.18f, Height = 7f * h, RadialSegments = 6 }, new Vector3(x * 1.1f, 0.6f + 3.5f * h, z * 1.1f), hull);
            leg.RotationDegrees = new Vector3(z * 8f, 0, -x * 8f);
        }
        Add(n, new BoxMesh { Size = new Vector3(1.6f, 0.7f, 1.6f) }, new Vector3(0, 0.6f + 7f * h, 0), Glow(owner, 2f));
        var spin = new Node3D { Name = "Spin", Position = new Vector3(0, 2.6f, 0) };
        Add(spin, new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.1f, Height = 3.2f, RadialSegments = 6 }, Vector3.Zero, Metal(new Color(0.7f, 0.62f, 0.45f), 0.8f, 0.25f));
        n.AddChild(spin);
        // Ore hopper (in the accent colour, on either side), conveyor toward the front, and a work light.
        Add(n, new CylinderMesh { TopRadius = 1.4f, BottomRadius = 0.7f, Height = 2.2f, RadialSegments = 8 }, new Vector3(2.3f * v.Side, 1.8f, -1.9f), Metal(v.Accent, 0.45f, 0.45f));
        Add(n, new BoxMesh { Size = new Vector3(1f, 0.35f, 3.6f) }, new Vector3(0, 0.95f, 2.6f), dark);
        Add(n, new BoxMesh { Size = new Vector3(0.9f, 0.08f, 3.4f) }, new Vector3(0, 1.15f, 2.6f), Glow(new Color(1f, 0.75f, 0.4f), 1.2f));
        n.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.82f, 0.6f), LightEnergy = 0.9f, OmniRange = 12f, Position = new Vector3(0, 5, 2) });
        return n;
    }

    /// <summary>Fusion reactor: core drum with a turning glow ring ("Spin"), two cooling towers (height ±10% per
    /// instance) and a control block in the accent colour, offset to one side.</summary>
    private static Node3D Reactor(Color owner, Look v)
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
        foreach (var (x, th) in new[] { (-2.7f, v.Height), (2.7f, v.Height2) })
        {
            var tower = 3.8f * th;
            Add(n, new CylinderMesh { TopRadius = 0.85f, BottomRadius = 1.25f, Height = tower, RadialSegments = 14 }, new Vector3(x, 0.4f + tower / 2f, -2.1f), hull);
            Add(n, new CylinderMesh { TopRadius = 0.86f, BottomRadius = 0.86f, Height = 0.2f, RadialSegments = 14 }, new Vector3(x, 0.2f + tower, -2.1f), Glow(owner, 1.6f));
        }
        var bx = v.IsPlain ? 0f : 0.6f * v.Side;
        Add(n, new BoxMesh { Size = new Vector3(1.6f, 1.2f, 1f) }, new Vector3(bx, 1.1f, 3.4f), Metal(v.Accent, 0.4f, 0.45f));
        Add(n, new BoxMesh { Size = new Vector3(1f, 0.6f, 0.1f) }, new Vector3(bx, 1.2f, 3.92f), Glow(Window, 1.2f));
        n.AddChild(new OmniLight3D { LightColor = core, LightEnergy = 1.6f, OmniRange = 16f, Position = new Vector3(0, 4, 0) });
        return n;
    }

    /// <summary>Prefab habitat: three glass domes (each ±8% per instance, warm or cool interior light) joined by
    /// tubes in the accent colour, and a lit commons at the front.</summary>
    private static Node3D Habitat(Color owner, Look v)
    {
        var n = new Node3D();
        var hull = Metal(HullColour, 0.35f, 0.4f);
        var warmth = v.IsPlain ? 0.5f : v.Next();
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.75f, 0.9f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.6f,
            Roughness = 0.08f,
            EmissionEnabled = true,
            Emission = new Color(1f, 0.85f, 0.6f).Lerp(new Color(0.75f, 0.9f, 1f), warmth),
            EmissionEnergyMultiplier = 0.35f,
        };
        var domes = new[] { new Vector3(0, 0, -2f), new Vector3(-2.3f, 0, 1.6f), new Vector3(2.3f, 0, 1.6f) };
        foreach (var d in domes)
        {
            var s = v.IsPlain ? 1f : v.Range(0.92f, 1.06f);
            Add(n, new CylinderMesh { TopRadius = 1.9f, BottomRadius = 2.05f, Height = 0.6f, RadialSegments = 16 }, d + new Vector3(0, 0.3f, 0), hull);
            Add(n, new SphereMesh { Radius = 1.8f, Height = 2.4f * s, IsHemisphere = true, RadialSegments = 16 }, d + new Vector3(0, 0.6f, 0), glass);
            Add(n, new CylinderMesh { TopRadius = 1.92f, BottomRadius = 1.92f, Height = 0.1f, RadialSegments = 16 }, d + new Vector3(0, 0.62f, 0), Glow(owner, 1.4f));
        }
        // Connecting tubes and a lit commons at the front.
        var tube = Metal(v.Accent, 0.4f, 0.45f);
        Add(n, new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 4f }, new Vector3(0, 0.6f, 1.6f), tube, new Vector3(0, 0, 90));
        Add(n, new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 3.2f }, new Vector3(0, 0.6f, 0), tube, new Vector3(90, 0, 0));
        Add(n, new BoxMesh { Size = new Vector3(1.4f, 0.25f, 1.4f) }, new Vector3(0, 0.13f, 3.6f), Glow(Window, 1f));
        return n;
    }

    /// <summary>Where the repair pad sits beside a colony core, in the core's own frame (front = +Z).</summary>
    public static readonly Vector3 RepairPadOffset = new(9.4f, 0, 2f);

    /// <summary>Which way a crawler parks on the pad (yaw in the core's frame, degrees): nose toward the front.</summary>
    public const float RepairPadYaw = 0f;

    /// <summary>Repair pad beside the hub: a hazard-striped slab, a lamp mast, a tool rack and a small jib crane.</summary>
    private static void RepairPad(Node3D hub, Color owner)
    {
        var pad = new Node3D { Name = "RepairPad", Position = RepairPadOffset };
        var slab = Metal(new Color(0.36f, 0.37f, 0.4f), 0.3f, 0.75f);
        var stripe = Metal(new Color(0.9f, 0.72f, 0.18f), 0.2f, 0.6f);
        var dark = Metal(new Color(0.12f, 0.12f, 0.14f), 0.2f, 0.7f);
        Add(pad, new BoxMesh { Size = new Vector3(7.4f, 0.22f, 8.6f) }, new Vector3(0, 0.11f, 0), slab);
        // Hazard border: alternating yellow and dark blocks along the edges.
        for (var i = 0; i < 9; i++)
        {
            var m = i % 2 == 0 ? stripe : dark;
            var z = -4f + i * 1f;
            Add(pad, new BoxMesh { Size = new Vector3(0.4f, 0.06f, 1f) }, new Vector3(-3.5f, 0.25f, z), m);
            Add(pad, new BoxMesh { Size = new Vector3(0.4f, 0.06f, 1f) }, new Vector3(3.5f, 0.25f, z), m);
        }
        for (var i = 0; i < 7; i++)
        {
            var m = i % 2 == 0 ? dark : stripe;
            var x = -3f + i * 1f;
            Add(pad, new BoxMesh { Size = new Vector3(1f, 0.06f, 0.4f) }, new Vector3(x, 0.25f, -4.1f), m);
            Add(pad, new BoxMesh { Size = new Vector3(1f, 0.06f, 0.4f) }, new Vector3(x, 0.25f, 4.1f), m);
        }
        // A painted wrench mark in the middle, lit in the owner's colour.
        Add(pad, new BoxMesh { Size = new Vector3(0.5f, 0.04f, 3.2f) }, new Vector3(0, 0.24f, 0), Glow(owner, 0.9f), new Vector3(0, 45, 0));
        Add(pad, new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.9f, Height = 0.04f, RadialSegments = 12 }, new Vector3(0.95f, 0.24f, -0.95f), Glow(owner, 0.9f));
        // Lamp mast at the back corner.
        var hull = Metal(HullColour);
        Add(pad, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.18f, Height = 6.5f, RadialSegments = 8 }, new Vector3(3.2f, 3.25f, -3.8f), hull);
        Add(pad, new BoxMesh { Size = new Vector3(1.2f, 0.3f, 0.6f) }, new Vector3(2.8f, 6.5f, -3.6f), Glow(new Color(1f, 0.95f, 0.85f), 2.5f));
        pad.AddChild(new SpotLight3D { LightColor = new Color(1f, 0.95f, 0.85f), LightEnergy = 3f, SpotRange = 14f, SpotAngle = 50f, Position = new Vector3(2.6f, 6.3f, -3.4f), RotationDegrees = new Vector3(-70, 30, 0) });
        // Tool rack and gas bottles on the hub side.
        Add(pad, new BoxMesh { Size = new Vector3(0.5f, 1.6f, 2.2f) }, new Vector3(-3.4f, 1f, -2.5f), Metal(new Color(0.7f, 0.22f, 0.2f), 0.4f, 0.5f));
        foreach (var z in new[] { 0.8f, 1.5f })
            Add(pad, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 1.4f, RadialSegments = 8 }, new Vector3(-3.4f, 0.9f, z), Metal(new Color(0.3f, 0.55f, 0.35f), 0.5f, 0.4f));
        // Small jib crane.
        Add(pad, new BoxMesh { Size = new Vector3(0.3f, 5f, 0.3f) }, new Vector3(-3.2f, 2.6f, 3.6f), hull);
        Add(pad, new BoxMesh { Size = new Vector3(3.6f, 0.3f, 0.3f) }, new Vector3(-1.5f, 5f, 3.6f), hull);
        Add(pad, new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 1.8f }, new Vector3(0.1f, 4.1f, 3.6f), dark);
        Add(pad, new BoxMesh { Size = new Vector3(0.4f, 0.3f, 0.4f) }, new Vector3(0.1f, 3.1f, 3.6f), stripe);
        hub.AddChild(pad);
    }

    /// <summary>
    /// One repair crew member, about 2 units tall, front = -Z: boots, coverall in the owner's colour, helmet with
    /// a lit visor, and a welding torch ("Torch", tip glow "Flame") held forward. "Sparks" spray from the torch tip.
    /// </summary>
    public static Node3D Crew(Color owner)
    {
        var n = new Node3D();
        var suit = Metal(owner.Darkened(0.15f), 0.1f, 0.75f);
        var boots = Metal(new Color(0.15f, 0.14f, 0.13f), 0.1f, 0.8f);
        foreach (var x in new[] { -0.16f, 0.16f })
        {
            var leg = new Node3D { Name = x < 0 ? "LegL" : "LegR", Position = new Vector3(x, 0.95f, 0) };
            Add(leg, new CapsuleMesh { Radius = 0.13f, Height = 0.95f }, new Vector3(0, -0.45f, 0), suit);
            Add(leg, new BoxMesh { Size = new Vector3(0.24f, 0.16f, 0.36f) }, new Vector3(0, -0.88f, -0.05f), boots);
            n.AddChild(leg);
        }
        Add(n, new CapsuleMesh { Radius = 0.27f, Height = 0.95f }, new Vector3(0, 1.32f, 0), suit);
        Add(n, new BoxMesh { Size = new Vector3(0.5f, 0.08f, 0.34f) }, new Vector3(0, 1.1f, 0), Metal(new Color(0.85f, 0.7f, 0.2f), 0.3f, 0.5f)); // tool belt
        Add(n, new SphereMesh { Radius = 0.21f, Height = 0.42f }, new Vector3(0, 1.98f, 0), Metal(new Color(0.92f, 0.92f, 0.9f), 0.2f, 0.4f));
        Add(n, new BoxMesh { Size = new Vector3(0.3f, 0.12f, 0.06f) }, new Vector3(0, 1.98f, -0.19f), Glow(Window, 1.4f));
        var arm = new Node3D { Name = "Arm", Position = new Vector3(0.3f, 1.62f, 0) };
        Add(arm, new CapsuleMesh { Radius = 0.09f, Height = 0.7f }, new Vector3(0, -0.1f, -0.25f), suit, new Vector3(-70, 0, 0));
        var torch = new Node3D { Name = "Torch", Position = new Vector3(0, -0.2f, -0.62f) };
        Add(torch, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 0.45f, RadialSegments = 6 }, Vector3.Zero, Metal(Trim), new Vector3(-80, 0, 0));
        var flame = Add(torch, new SphereMesh { Radius = 0.07f, Height = 0.14f }, new Vector3(0, 0, -0.25f), Glow(new Color(0.65f, 0.85f, 1f), 6f));
        flame.Name = "Flame";
        torch.AddChild(new CpuParticles3D
        {
            Name = "Sparks",
            Position = new Vector3(0, 0, -0.28f),
            Amount = 30,
            Lifetime = 0.55f,
            Emitting = false,
            LocalCoords = false,
            Direction = new Vector3(0, 0.6f, 1f),
            Spread = 55f,
            InitialVelocityMin = 2f,
            InitialVelocityMax = 5f,
            Gravity = new Vector3(0, -9f, 0),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1f,
            Mesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 4, Rings = 2, Material = Glow(new Color(1f, 0.85f, 0.45f), 5f) },
        });
        arm.AddChild(torch);
        n.AddChild(arm);
        return n;
    }

    /// <summary>
    /// Research Lab: a glass-roofed lab block beside a sensor mast with a dish, and a glowing containment ring
    /// that turns while it works ("Spin").
    /// </summary>
    private static Node3D ResearchLab(Color owner, Look v)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var dark = Metal(Trim, 0.35f, 0.55f);
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.5f, 0.75f, 0.95f, 0.5f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.6f,
            Roughness = 0.08f,
            EmissionEnabled = true,
            Emission = new Color(0.6f, 0.85f, 1f),
            EmissionEnergyMultiplier = 0.5f,
        };
        Add(n, new BoxMesh { Size = new Vector3(7.2f, 0.4f, 7f) }, new Vector3(0, 0.2f, 0), dark);
        Add(n, new BoxMesh { Size = new Vector3(5.2f, 2.6f, 4.2f) }, new Vector3(-0.8f, 1.7f, 0.6f), hull);
        Add(n, new BoxMesh { Size = new Vector3(5.3f, 0.16f, 4.3f) }, new Vector3(-0.8f, 2.6f, 0.6f), Glow(owner, 1.6f));
        Add(n, new SphereMesh { Radius = 2f, Height = 2.2f, IsHemisphere = true, RadialSegments = 16 }, new Vector3(-0.8f, 3f, 0.6f), glass);
        // Windows along the front.
        for (var i = 0; i < 3; i++)
            Add(n, new BoxMesh { Size = new Vector3(1f, 0.6f, 0.08f) }, new Vector3(-2.4f + i * 1.6f, 1.8f, 2.74f), Glow(Window, 1.3f));
        // Sensor mast and dish at the back right (height ±10% per instance, the dish tilted a little differently).
        var mh = 6.5f * v.Height;
        var tilt = v.IsPlain ? 35f : v.Range(25f, 50f);
        Add(n, new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.3f, Height = mh, RadialSegments = 8 }, new Vector3(2.6f, 0.35f + mh / 2f, -2f), hull);
        Add(n, new CylinderMesh { TopRadius = 1.4f, BottomRadius = 0.2f, Height = 0.6f, RadialSegments = 14 }, new Vector3(2.6f, 0.3f + mh, -2f), hull, new Vector3(tilt, 0, 0));
        Add(n, new SphereMesh { Radius = 0.2f, Height = 0.4f }, new Vector3(2.6f, 0.9f + mh, -1.6f), Glow(new Color(1f, 0.6f, 0.15f), 3f));
        // Containment ring on a plinth in the accent colour: the "experiment".
        Add(n, new CylinderMesh { TopRadius = 0.9f, BottomRadius = 1.1f, Height = 0.8f, RadialSegments = 12 }, new Vector3(2.4f, 0.8f, 1.8f), Metal(v.Accent, 0.4f, 0.45f));
        var spin = new Node3D { Name = "Spin", Position = new Vector3(2.4f, 2.1f, 1.8f) };
        Add(spin, new TorusMesh { InnerRadius = 0.75f, OuterRadius = 0.95f, Rings = 24 }, Vector3.Zero, Glow(new Color(0.55f, 0.9f, 1f), 3f), new Vector3(70, 0, 0));
        Add(spin, new SphereMesh { Radius = 0.3f, Height = 0.6f }, Vector3.Zero, Glow(new Color(0.85f, 0.95f, 1f), 4f));
        n.AddChild(spin);
        n.AddChild(new OmniLight3D { LightColor = new Color(0.55f, 0.85f, 1f), LightEnergy = 1.3f, OmniRange = 10f, Position = new Vector3(2.4f, 2.5f, 2.2f) });
        return n;
    }

    /// <summary>Maintenance drone: a small hovering pod with a glowing ring, a work light and a spark spray ("Sparks").</summary>
    public static Node3D Drone(Color owner)
    {
        var n = new Node3D();
        var hull = Metal(new Color(0.85f, 0.86f, 0.9f), 0.6f, 0.3f);
        Add(n, new SphereMesh { Radius = 0.45f, Height = 0.7f, RadialSegments = 10, Rings = 5 }, Vector3.Zero, hull);
        Add(n, new TorusMesh { InnerRadius = 0.5f, OuterRadius = 0.65f, Rings = 16 }, Vector3.Zero, Glow(owner, 2.2f));
        Add(n, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.5f }, new Vector3(0, -0.45f, 0), Metal(Trim));
        n.AddChild(new OmniLight3D { LightColor = new Color(0.65f, 0.85f, 1f), LightEnergy = 1.2f, OmniRange = 5f, Position = new Vector3(0, -0.6f, 0) });
        n.AddChild(new CpuParticles3D
        {
            Name = "Sparks",
            Position = new Vector3(0, -0.7f, 0),
            Amount = 16,
            Lifetime = 0.5f,
            Emitting = true,
            LocalCoords = false,
            Direction = Vector3.Down,
            Spread = 50f,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 4f,
            Gravity = new Vector3(0, -9f, 0),
            Mesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 4, Rings = 2, Material = Glow(new Color(1f, 0.85f, 0.45f), 5f) },
        });
        return n;
    }

    /// <summary>Height of the Vehicle Bay's door opening (the "Door" panel rolls up into the lintel).</summary>
    public const float BayDoorHeight = 4f;

    /// <summary>Where vehicles come out of the Vehicle Bay, in its own frame (just outside the door).</summary>
    public static readonly Vector3 BayDoorOffset = new(0, 0, 3.4f);

    /// <summary>
    /// Vehicle Bay: a ribbed assembly hall with a wide roll-up door at the front ("Door": scale Y 1 = shut, ~0.08 = open),
    /// a lit assembly bay inside ("WorkGlow"), a striped apron, and a control tower with a turning beacon ("Spin").
    /// </summary>
    private static Node3D VehicleBay(Color owner, Look v)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var dark = Metal(Trim, 0.35f, 0.55f);
        var stripe = Metal(new Color(0.9f, 0.72f, 0.18f), 0.2f, 0.6f);
        Add(n, new BoxMesh { Size = new Vector3(8.2f, 0.4f, 8.4f) }, new Vector3(0, 0.2f, 0.4f), dark);
        // Hall: back wall, side walls, roof, leaving the front open for the door.
        Add(n, new BoxMesh { Size = new Vector3(7.4f, 5f, 0.4f) }, new Vector3(0, 2.9f, -3f), hull);
        foreach (var x in new[] { -3.5f, 3.5f })
            Add(n, new BoxMesh { Size = new Vector3(0.4f, 5f, 5.2f) }, new Vector3(x, 2.9f, -0.6f), hull);
        Add(n, new BoxMesh { Size = new Vector3(7.8f, 0.5f, 5.8f) }, new Vector3(0, 5.6f, -0.6f), hull);
        for (var i = 0; i < 4; i++)
            Add(n, new BoxMesh { Size = new Vector3(7.9f, 0.25f, 0.35f) }, new Vector3(0, 5.95f, -2.8f + i * 1.45f), dark);
        // Front frame and lintel around a 5-wide door opening.
        foreach (var x in new[] { -2.95f, 2.95f })
            Add(n, new BoxMesh { Size = new Vector3(1.5f, 5f, 0.5f) }, new Vector3(x, 2.9f, 2f), hull);
        Add(n, new BoxMesh { Size = new Vector3(7.4f, 1f, 0.6f) }, new Vector3(0, 4.9f, 2f), hull);
        Add(n, new BoxMesh { Size = new Vector3(7.42f, 0.18f, 0.62f) }, new Vector3(0, 4.55f, 2f), Glow(owner, 1.8f));
        // Roll-up door: pivot at the top of the opening, panel hanging down.
        var door = new Node3D { Name = "Door", Position = new Vector3(0, 0.4f + BayDoorHeight, 2.05f) };
        Add(door, new BoxMesh { Size = new Vector3(4.6f, BayDoorHeight, 0.15f) }, new Vector3(0, -BayDoorHeight / 2f, 0), Metal(new Color(0.62f, 0.64f, 0.68f), 0.6f, 0.4f));
        for (var i = 0; i < 6; i++)
            Add(door, new BoxMesh { Size = new Vector3(4.62f, 0.06f, 0.18f) }, new Vector3(0, -0.4f - i * 0.62f, 0), dark);
        n.AddChild(door);
        // Inside: an assembly cradle under lights (seen when the door is up).
        Add(n, new BoxMesh { Size = new Vector3(4.2f, 0.3f, 3.6f) }, new Vector3(0, 0.55f, -0.8f), dark);
        var glow = new Node3D { Name = "WorkGlow", Visible = false };
        Add(glow, new BoxMesh { Size = new Vector3(5.6f, 0.12f, 0.6f) }, new Vector3(0, 5.2f, -0.8f), Glow(new Color(0.75f, 0.9f, 1f), 3f));
        glow.AddChild(new OmniLight3D { LightColor = new Color(0.7f, 0.85f, 1f), LightEnergy = 2.2f, OmniRange = 9f, Position = new Vector3(0, 3.5f, -0.6f) });
        n.AddChild(glow);
        // Apron with guide stripes leading out of the door.
        for (var i = 0; i < 4; i++)
        {
            Add(n, new BoxMesh { Size = new Vector3(0.3f, 0.04f, 0.9f) }, new Vector3(-2.1f, 0.42f, 2.8f + i * 1.15f), stripe);
            Add(n, new BoxMesh { Size = new Vector3(0.3f, 0.04f, 0.9f) }, new Vector3(2.1f, 0.42f, 2.8f + i * 1.15f), stripe);
        }
        // Control tower and beacon at one back corner (either side per instance, height ±10%, accent band).
        var tx = 3.6f * v.Side;
        var th = 7.2f * v.Height;
        Add(n, new BoxMesh { Size = new Vector3(1.6f, th, 1.6f) }, new Vector3(tx, th / 2f, -3.4f), hull);
        Add(n, new BoxMesh { Size = new Vector3(1.64f, 0.5f, 1.64f) }, new Vector3(tx, th * 0.45f, -3.4f), Metal(v.Accent, 0.4f, 0.45f));
        Add(n, new BoxMesh { Size = new Vector3(1.7f, 0.6f, 1.7f) }, new Vector3(tx, th - 0.8f, -3.4f), Glow(Window, 1.4f));
        var spin = new Node3D { Name = "Spin", Position = new Vector3(tx, th + 0.3f, -3.4f) };
        Add(spin, new BoxMesh { Size = new Vector3(1.2f, 0.25f, 0.3f) }, new Vector3(0.4f, 0, 0), Glow(new Color(1f, 0.6f, 0.15f), 3.5f));
        Add(spin, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.2f, Height = 0.4f }, Vector3.Zero, dark);
        n.AddChild(spin);
        return n;
    }

    /// <summary>Deep-core mine: a heavier derrick over a lit shaft ring, with a big drill ("Spin") and pressure tanks.</summary>
    private static Node3D DeepCoreMine(Color owner, Look v)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var dark = Metal(Trim, 0.3f, 0.6f);
        Add(n, new CylinderMesh { TopRadius = 3.6f, BottomRadius = 4.1f, Height = 0.8f, RadialSegments = 8 }, new Vector3(0, 0.4f, 0), dark);
        Add(n, new TorusMesh { InnerRadius = 1.5f, OuterRadius = 1.9f, Rings = 24 }, new Vector3(0, 0.85f, 0), Glow(new Color(1f, 0.5f, 0.2f), 2.6f));
        var h = v.Height3;
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var leg = Add(n, new BoxMesh { Size = new Vector3(0.35f, 10f * h, 0.35f) }, new Vector3(x * 1.6f, 0.4f + 5f * h, z * 1.6f), hull);
            leg.RotationDegrees = new Vector3(z * 7f, 0, -x * 7f);
        }
        Add(n, new BoxMesh { Size = new Vector3(2.4f, 1f, 2.4f) }, new Vector3(0, 0.4f + 10f * h, 0), hull);
        Add(n, new BoxMesh { Size = new Vector3(2.5f, 0.2f, 2.5f) }, new Vector3(0, 0.9f + 10f * h, 0), Glow(owner, 2f));
        var spin = new Node3D { Name = "Spin", Position = new Vector3(0, 4f, 0) };
        Add(spin, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 6f, RadialSegments = 8 }, new Vector3(0, 1.5f, 0), Metal(new Color(0.6f, 0.6f, 0.65f), 0.85f, 0.2f));
        Add(spin, new CylinderMesh { TopRadius = 1.1f, BottomRadius = 0.15f, Height = 3f, RadialSegments = 6 }, new Vector3(0, -2.4f, 0), Metal(new Color(0.75f, 0.62f, 0.4f), 0.8f, 0.25f));
        n.AddChild(spin);
        // Pressure tanks in the accent colour, one taller than the other.
        foreach (var (x, k) in new[] { (-2.9f, v.Height), (2.9f, v.Height2) })
            Add(n, new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.8f, Height = 3f * k, RadialSegments = 12 }, new Vector3(x, 0.8f + 1.5f * k, 2.2f), Metal(v.Accent, 0.45f, 0.4f));
        n.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 1.4f, OmniRange = 14f, Position = new Vector3(0, 2, 0) });
        return n;
    }

    /// <summary>Storage container yard: stacked, ribbed cargo containers in a few colours and a small gantry crane.</summary>
    /// <summary>Storage container yard: stacked, ribbed cargo containers and a gantry crane. Per instance: which
    /// slots carry a second container, the container colours (one in the accent colour) and their slight skew, and
    /// where the crane's trolley stands.</summary>
    private static Node3D StorageContainer(Color owner, Look v)
    {
        var n = new Node3D();
        Add(n, new BoxMesh { Size = new Vector3(7.4f, 0.4f, 7f) }, new Vector3(0, 0.2f, 0), Metal(Trim, 0.3f, 0.7f));
        var colours = new[] { new Color(0.72f, 0.36f, 0.22f), new Color(0.3f, 0.45f, 0.62f), new Color(0.78f, 0.7f, 0.42f), new Color(0.42f, 0.55f, 0.4f), new Color(0.62f, 0.62f, 0.64f), new Color(0.55f, 0.24f, 0.2f) };
        var ground = new[] { (-1.8f, -1.8f), (1.8f, -1.8f), (-1.8f, 1.2f), (1.8f, 1.2f) };
        // the upper level: the canonical three, or one of a few other layouts (always two or three stacked, so it reads
        // as the same yard)
        var layouts = new[] { new[] { 0, 3, 2 }, new[] { 1, 2 }, new[] { 0, 1, 3 }, new[] { 2, 3 }, new[] { 0, 1 } };
        var upper = layouts[v.Int(layouts.Length)];
        var slots = new List<(float X, float Z, int Level)>();
        foreach (var (x, z) in ground) slots.Add((x, z, 0));
        foreach (var k in upper) slots.Add((ground[k].Item1, ground[k].Item2, 1));
        var offset = v.Int(colours.Length);
        var accentSlot = v.IsPlain ? -1 : v.Int(slots.Count);
        for (var i = 0; i < slots.Count; i++)
        {
            var (x, z, level) = slots[i];
            var skew = v.IsPlain ? i % 2 * 2f - 1f : v.Range(-3f, 3f);
            var box = new Node3D { Position = new Vector3(x, 0.4f + 1.35f + level * 2.7f, z), RotationDegrees = new Vector3(0, skew, 0) };
            var colour = i == accentSlot ? v.Accent : colours[(i + offset) % colours.Length];
            var paint = Metal(colour, 0.45f, 0.55f);
            Add(box, new BoxMesh { Size = new Vector3(3.2f, 2.6f, 2.6f) }, Vector3.Zero, paint);
            for (var r = -1; r <= 1; r++)
                Add(box, new BoxMesh { Size = new Vector3(0.12f, 2.4f, 2.7f) }, new Vector3(r * 1.1f, 0, 0), Metal(colour.Darkened(0.25f), 0.45f, 0.55f));
            n.AddChild(box);
        }
        // Gantry crane over the yard.
        var hull = Metal(HullColour);
        foreach (var x in new[] { -3.5f, 3.5f })
            Add(n, new BoxMesh { Size = new Vector3(0.3f, 7.4f, 0.3f) }, new Vector3(x, 3.9f, 3.1f), hull);
        Add(n, new BoxMesh { Size = new Vector3(7.4f, 0.45f, 0.45f) }, new Vector3(0, 7.6f, 3.1f), hull);
        var trolley = v.IsPlain ? 0.8f : v.Range(-2.4f, 2.4f);
        Add(n, new BoxMesh { Size = new Vector3(1f, 0.6f, 0.9f) }, new Vector3(trolley, 7.1f, 3.1f), Glow(owner, 1.6f));
        Add(n, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 2.6f }, new Vector3(trolley, 5.6f, 3.1f), Metal(Trim));
        return n;
    }

    /// <summary>Alloy foundry: a long casting hall with a glowing crucible under an arch, ingot stacks out front and a stack ("Spin" = crucible stirrer).</summary>
    private static Node3D Foundry(Color owner, Look v)
    {
        var n = new Node3D();
        var hull = Metal(HullColour);
        var dark = Metal(Trim, 0.35f, 0.55f);
        var hot = new Color(1f, 0.62f, 0.2f);
        Add(n, new BoxMesh { Size = new Vector3(7.2f, 0.5f, 7f) }, new Vector3(0, 0.25f, 0), dark);
        Add(n, new BoxMesh { Size = new Vector3(6.2f, 3.4f, 3.2f) }, new Vector3(0, 2.2f, -1.6f), hull);
        Add(n, new BoxMesh { Size = new Vector3(6.3f, 0.2f, 3.3f) }, new Vector3(0, 3.95f, -1.6f), Glow(owner, 1.5f));
        // the stack: on either side, height ±10%
        var sh = 5f * v.Height;
        Add(n, new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.7f, Height = sh, RadialSegments = 10 }, new Vector3(2.2f * v.Side, 3f + sh / 2f, -2.2f), hull);
        // Arch and crucible in front of the hall.
        var arch = Metal(v.Accent, 0.4f, 0.45f);
        foreach (var x in new[] { -1.6f, 1.6f })
            Add(n, new BoxMesh { Size = new Vector3(0.35f, 3.6f, 0.35f) }, new Vector3(x, 2.3f, 1.2f), arch);
        Add(n, new BoxMesh { Size = new Vector3(3.6f, 0.4f, 0.5f) }, new Vector3(0, 4.1f, 1.2f), arch);
        Add(n, new CylinderMesh { TopRadius = 1.1f, BottomRadius = 0.8f, Height = 1.2f, RadialSegments = 14 }, new Vector3(0, 1.1f, 1.2f), dark);
        Add(n, new CylinderMesh { TopRadius = 0.95f, BottomRadius = 0.95f, Height = 0.1f, RadialSegments = 14 }, new Vector3(0, 1.72f, 1.2f), Glow(hot, 3.5f));
        var spin = new Node3D { Name = "Spin", Position = new Vector3(0, 2.6f, 1.2f) };
        Add(spin, new BoxMesh { Size = new Vector3(1.4f, 0.12f, 0.12f) }, Vector3.Zero, dark);
        Add(spin, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 1.4f }, new Vector3(0.6f, -0.6f, 0), dark);
        n.AddChild(spin);
        // Parasteel ingots stacked by the door (how many, and on which side, varies).
        var ingot = Metal(new Color(0.62f, 0.66f, 0.74f), 0.85f, 0.25f);
        var bars = v.IsPlain ? 6 : 3 + v.Int(6);
        for (var i = 0; i < bars; i++)
            Add(n, new BoxMesh { Size = new Vector3(1.1f, 0.35f, 0.45f) }, new Vector3(-2.6f * v.Side + (i % 3) * 0.05f, 0.7f + (i / 2) * 0.36f, 2.2f + (i % 2) * 0.5f), ingot);
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
    /// <summary>
    /// A vehicle model made in Blender (blender/build_vehicles.py → models/vehicles/&lt;id&gt;.glb), wrapped in a plain
    /// Node3D, with the "Owner" material recoloured and the named animated parts (e.g. "Dish") moved up to be direct
    /// children so the view finds them like on the code-built models. Null when the file isn't there or hasn't been
    /// imported yet, so callers fall back to the primitive model.
    /// </summary>
    private static Node3D? Imported(string id, Color owner, params string[] parts) =>
        ImportedFile($"res://models/vehicles/{id}.glb", id, owner, parts);

    /// <summary><see cref="Imported"/> for any glb path (buildings live in models/buildings/).</summary>
    private static Node3D? ImportedFile(string path, string id, Color owner, params string[] parts)
    {
        if (!ResourceLoader.Exists(path)) return null;
        if (GD.Load<PackedScene>(path)?.Instantiate() is not Node3D model) return null;
        var n = new Node3D { Name = id };
        n.AddChild(model);
        Recolour(model, owner);
        foreach (var part in parts)
        {
            if (model.FindChild(part, true, false) is not Node3D node) continue;
            var t = RelativeTo(node, n);
            node.GetParent().RemoveChild(node);
            node.Transform = t;
            n.AddChild(node);
        }
        return n;
    }

    /// <summary>The node's transform relative to an ancestor, from the local transforms (works before it's in the tree).</summary>
    private static Transform3D RelativeTo(Node3D node, Node3D ancestor)
    {
        var t = node.Transform;
        for (var p = node.GetParent(); p != null && p != ancestor; p = p.GetParent())
            if (p is Node3D p3) t = p3.Transform * t;
        return t;
    }

    private static void Recolour(Node node, Color owner)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if (mi.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D m && m.ResourceName == "Owner")
                {
                    var copy = (BaseMaterial3D)m.Duplicate();
                    copy.AlbedoColor = owner;
                    copy.EmissionEnabled = true;
                    copy.Emission = owner;
                    copy.EmissionEnergyMultiplier = 0.35f;
                    mi.SetSurfaceOverrideMaterial(i, copy);
                }
        foreach (var child in node.GetChildren()) Recolour(child, owner);
    }

    /// <summary>The drop ship (models/vehicles/dropship.glb from blender/build_dropship.py --game; parts Ramp, Leg0-3,
    /// Foot0-3, Thruster0-5, NacelleGlow0/1, BayLight, Beacon and the decal markers stay in the glb's hierarchy), or
    /// null if the file is missing or not imported yet (DropShipView then builds a primitive stand-in).</summary>
    public static Node3D? DropShip(Color owner) => Imported("dropship", owner);

    /// <summary>A survey rover: the unit's own model (survey_rover = Mk I, survey_rover_mk2, survey_rover_mk3), falling
    /// back to the Mk I file and then to primitives. Its blank decal markers (NameL/NameR/NameRear/NumberL/NumberR) are
    /// left in place for <see cref="RoverLook.Paint"/>.</summary>
    public static Node3D Rover(Color owner, string unitId = "survey_rover")
    {
        if ((Imported(unitId, owner, "Dish", "Drone") ?? Imported("survey_rover", owner, "Dish")) is { } made)
        {
            var mk3 = unitId.EndsWith("mk3");
            made.AddChild(new MeshInstance3D
            {
                Name = "Selected",
                Mesh = new TorusMesh { InnerRadius = mk3 ? 3.4f : 3.0f, OuterRadius = mk3 ? 3.7f : 3.3f, Rings = 32 },
                Position = new Vector3(0, 0.15f, 0),
                MaterialOverride = Glow(new Color(1f, 0.95f, 0.5f), 2f),
                Visible = false,
            });
            made.AddChild(new SpotLight3D { LightColor = new Color(1f, 0.95f, 0.85f), LightEnergy = 2f, SpotRange = 16f, SpotAngle = 30f, Position = new Vector3(0, 1.3f, -2.6f), RotationDegrees = new Vector3(-12, 0, 0) });
            return made;
        }
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
    /// Mining crawler (Mining Vehicle Mk I), folded for driving: the Blender model models/vehicles/mining_crawler.glb
    /// (blender/build_miner.py --blank --out ...), or primitives when it isn't there or not imported yet. Parts the
    /// view animates as it unfurls: "Leg0".."Leg3" (stabilizer pivots; rotate about local Z from FoldedLeg to
    /// DeployedLeg; the glb's "Leg{i}Foot" hinges turn about local Z from FoldedFoot to 0), "Boom" (arm pivot; rotate
    /// about X up to DeployedBoom), "Drill" under the boom tip (keep it upright, scale "String" Y to reach the ground,
    /// spin "Bit"), "Body" (shakes while digging; holds "Fill" = ore in the hopper, scale Y 0..1, and in the glb the
    /// lift-ram barrels "RamL"/"RamR" that aim at "RamRodL"/"RamRodR" on the Boom), "Dust" (particles while digging),
    /// "WorkLight", "Selected", and "Smoke"/"Sparks"/"Fire" for breakdowns. "Beacon", the "TailLamp" material,
    /// "Wiper0/1" and the decal markers (glb only) are RoverLook's. Front is -Z, like the rover.
    /// </summary>
    public static Node3D Crawler(Color owner)
    {
        var made = Imported("mining_crawler", owner, "Body", "Leg0", "Leg1", "Leg2", "Leg3", "Boom");
        var n = made ?? PrimitiveCrawler(owner);
        var glb = made != null;
        // Where the work gear sits: the glb's power pack is mid-deck under the boom; the primitive's hopper is at the back.
        var smokeAt = glb ? new Vector3(0.35f, 2.75f, -0.4f) : new Vector3(0.4f, 3.2f, 0.6f);
        var sparksAt = glb ? new Vector3(0.78f, 2.25f, -0.2f) : new Vector3(0.9f, 2.4f, -0.6f);
        var fireAt = glb ? new Vector3(0.45f, 2.6f, -0.3f) : new Vector3(0.2f, 2.1f, 0.9f);
        AddCrawlerEffects(n, smokeAt, sparksAt, fireAt);
        // Headlights in the nose (glb: lamps at 1.5 up on the front face, z -2.85).
        n.AddChild(glb
            ? new SpotLight3D { LightColor = new Color(1f, 0.95f, 0.85f), LightEnergy = 1.8f, SpotRange = 16f, SpotAngle = 32f, Position = new Vector3(0, 1.6f, -3.0f), RotationDegrees = new Vector3(-10, 0, 0) }
            : new SpotLight3D { LightColor = new Color(1f, 0.95f, 0.85f), LightEnergy = 1.6f, SpotRange = 14f, SpotAngle = 30f, Position = new Vector3(0, 2.4f, -2.6f), RotationDegrees = new Vector3(-15, 0, 0) });
        // Selection ring: round the hull, the boom tip (z -4.3 folded) and the braced pads (glb: their outer corners
        // land at x 3.1, z -2.9 / 2.6, up to 4.24 from the centre).
        n.AddChild(new MeshInstance3D
        {
            Name = "Selected",
            Mesh = glb ? new TorusMesh { InnerRadius = 4.25f, OuterRadius = 4.55f, Rings = 32 } : new TorusMesh { InnerRadius = 3.6f, OuterRadius = 3.9f, Rings = 32 },
            Position = new Vector3(0, 0.15f, 0),
            MaterialOverride = Glow(new Color(1f, 0.95f, 0.5f), 2f),
            Visible = false,
        });
        return n;
    }

    /// <summary>The crawler's body, legs and boom from primitives (until the glb is imported).</summary>
    private static Node3D PrimitiveCrawler(Color owner)
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
        // Amber roof beacon and red tail lamps, named like the rover parts so RoverLook.Lamps drives them.
        var amber = Glow(new Color(1f, 0.55f, 0.12f), 3f);
        amber.ResourceName = "Amber";
        Add(body, new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 0.2f, RadialSegments = 12 }, new Vector3(0.7f, 3.2f, -1.8f), amber).Name = "Beacon";
        var tail = Glow(new Color(0.95f, 0.08f, 0.05f), 2f);
        tail.ResourceName = "TailLamp";
        foreach (var x in new[] { -1.05f, 1.05f })
            Add(body, new BoxMesh { Size = new Vector3(0.36f, 0.16f, 0.06f) }, new Vector3(x, 1.55f, 2.72f), tail);
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
        return n;
    }

    /// <summary>The crawler's effects (both models): drill dust, work light, and breakdown smoke, sparks and fire.</summary>
    private static void AddCrawlerEffects(Node3D n, Vector3 smokeAt, Vector3 sparksAt, Vector3 fireAt)
    {
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
        n.AddChild(Puffs("Smoke", smokeAt, new Color(0.16f, 0.15f, 0.14f), 40, 3.2f, 1.2f, 2.6f, 0.15f));
        n.AddChild(new CpuParticles3D
        {
            Name = "Sparks",
            Position = sparksAt,
            Amount = 18,
            Lifetime = 0.7f,
            Emitting = false,
            LocalCoords = false,
            Explosiveness = 0.6f,
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 7f,
            Gravity = new Vector3(0, -12f, 0),
            Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 4, Rings = 2, Material = Glow(new Color(1f, 0.8f, 0.35f), 5f) },
        });
        var fire = new Node3D { Name = "Fire", Position = fireAt, Visible = false };
        Add(fire, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.7f, Height = 1.8f, RadialSegments = 7 }, new Vector3(0, 0.9f, 0), Glow(new Color(1f, 0.45f, 0.1f), 4f));
        Add(fire, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.4f, Height = 1.1f, RadialSegments = 6 }, new Vector3(0.1f, 0.6f, 0.1f), Glow(new Color(1f, 0.85f, 0.4f), 5f));
        fire.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 2.5f, OmniRange = 9f, Position = new Vector3(0, 1.2f, 0) });
        n.AddChild(fire);
        n.AddChild(new OmniLight3D { Name = "WorkLight", LightColor = new Color(1f, 0.85f, 0.6f), LightEnergy = 1.6f, OmniRange = 10f, Position = new Vector3(0, 3.5f, -BoomReach), Visible = false });
    }

    /// <summary>A soft particle plume (smoke, dust) that is off until the view turns it on.</summary>
    public static CpuParticles3D Puffs(string name, Vector3 at, Color colour, int amount, float lifetime, float sizeMin, float sizeMax, float drift)
    {
        var c = colour;
        return new CpuParticles3D
        {
            Name = name,
            Position = at,
            Amount = amount,
            Lifetime = lifetime,
            Emitting = false,
            LocalCoords = false,
            Direction = Vector3.Up,
            Spread = 18f,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 3f,
            Gravity = new Vector3(drift, 0.6f, 0),
            DampingMin = 0.3f,
            DampingMax = 0.8f,
            ScaleAmountMin = sizeMin,
            ScaleAmountMax = sizeMax,
            ColorRamp = new Gradient
            {
                Offsets = new[] { 0f, 0.15f, 1f },
                Colors = new[] { new Color(c.R, c.G, c.B, 0f), new Color(c.R, c.G, c.B, 0.75f), new Color(c.R * 1.6f, c.G * 1.6f, c.B * 1.6f, 0f) },
            },
            Mesh = new SphereMesh
            {
                Radius = 0.6f,
                Height = 1.2f,
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
    }

    /// <summary>Leg pivot angles (degrees about local Z): folded up against the hull (85: the Mk I's beams stand
    /// nearly upright beside the deck, clear of the hopper), and braced out on the ground (its pads sit flat at -38).</summary>
    public const float FoldedLeg = 85f;
    public const float DeployedLeg = -38f;

    /// <summary>The glb's foot hinge ("Leg{i}Foot", degrees about the leg's local Z) when folded: jack square to the
    /// beam, pad flat on the beam's inner face; 0 when braced (blender/build_miner.py FOLDED_FOOT).</summary>
    public const float FoldedFoot = 142f;

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
            if (child is Label3D label) label.Visible = false; // painted numbers and signs
            Override(child, material);
        }
    }
}
