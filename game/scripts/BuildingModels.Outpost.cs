using Godot;

namespace FinalFrontier;

/// <summary>
/// The Level 1 outpost (the landed Colony Hub): models/buildings/outpost_l1.glb from blender/build_outpost_l1.py --game.
/// Three wedge modules (ModuleA command + comms mast, ModuleB hab + solar wings, ModuleC ops + vault, radiators and a
/// floodlight) that come down under three drop ships and assemble on the tile (<see cref="LandingView"/>). The glb is in
/// its rest pose = assembled and unfurled, which is how the hub always looks once the uplink is online.
/// </summary>
public static partial class BuildingModels
{
    public const string OutpostPath = "res://models/buildings/outpost_l1.glb";

    /// <summary>Each module's direction from the hub centre in the hub's own frame (front = +Z): A front, B back-left,
    /// C back-right (build_outpost_l1.py WEDGES).</summary>
    public static readonly Vector3[] OutpostModuleDirs =
    {
        new(0f, 0f, 1f),
        new(-0.8660254f, 0f, -0.5f),
        new(0.8660254f, 0f, -0.5f),
    };

    public static readonly string[] OutpostModules = { "ModuleA", "ModuleB", "ModuleC" };

    /// <summary>The glb (or a primitive stand-in with the same part names), the repair pad, the hub light, the
    /// floodlight's spot and the beacon's glow. Names the landing animation drives: ModuleA/B/C, Mast, MastTop, Dish,
    /// Beacon, SolarB0/1(+Outer), RadiatorC0/1, Flood, RepairPad, HubLight, FloodLight, BeaconLight.</summary>
    private static Node3D OutpostL1(Color owner)
    {
        var n = ImportedFile(OutpostPath, "outpost_l1", owner) ?? PrimitiveOutpost(owner);
        RepairPad(n, owner);
        n.AddChild(new OmniLight3D { Name = "HubLight", LightColor = owner, LightEnergy = 1.4f, OmniRange = 18f, Position = new Vector3(0, 6.5f, 0) });
        if (n.FindChild("Flood", true, false) is Node3D flood)
        {
            // The lamp head faces the arm's local +X (outward) and 35 degrees down; a spot points along its own -Z.
            var spot = new SpotLight3D
            {
                Name = "FloodLight",
                LightColor = new Color(1f, 0.95f, 0.85f),
                LightEnergy = 2.5f,
                SpotRange = 16f,
                SpotAngle = 45f,
                ShadowEnabled = false,
                Position = new Vector3(0.3f, 2.2f, 0f),
            };
            spot.Basis = Basis.LookingAt(new Vector3(0.82f, -0.57f, 0f), Vector3.Up);
            flood.AddChild(spot);
        }
        if (n.FindChild("Beacon", true, false) is Node3D beacon)
            beacon.AddChild(new OmniLight3D { Name = "BeaconLight", LightColor = new Color(1f, 0.15f, 0.08f), LightEnergy = 0.8f, OmniRange = 8f, ShadowEnabled = false });
        return n;
    }

    /// <summary>Stand-in for outpost_l1.glb before Godot has imported it: three round pods round the centre with the
    /// glb's part names (no solar wings, radiators or floodlight).</summary>
    private static Node3D PrimitiveOutpost(Color owner)
    {
        var n = new Node3D { Name = "outpost_l1" };
        var hull = Metal(HullColour);
        var trim = Metal(Trim, 0.4f, 0.5f);
        var window = Glow(new Color(1f, 0.8f, 0.5f), 3f);
        window.ResourceName = "Window";
        var skid = Glow(new Color(0.25f, 0.55f, 1f), 1f);
        skid.ResourceName = "Skid";
        var seam = Glow(new Color(0.35f, 0.7f, 1f), 1.5f);
        seam.ResourceName = "Seam";
        for (var k = 0; k < 3; k++)
        {
            var m = new Node3D { Name = OutpostModules[k] };
            var c = OutpostModuleDirs[k] * 2.45f;
            Add(m, new CylinderMesh { TopRadius = 2.3f, BottomRadius = 2.45f, Height = 2.4f, RadialSegments = 24 }, c + new Vector3(0, 1.5f, 0), hull);
            Add(m, new CylinderMesh { TopRadius = 2.1f, BottomRadius = 2.1f, Height = 0.3f, RadialSegments = 24 }, c + new Vector3(0, 0.15f, 0), trim);
            Add(m, new TorusMesh { InnerRadius = 2.05f, OuterRadius = 2.16f, Rings = 24 }, c + new Vector3(0, 0.15f, 0), skid);
            Add(m, new TorusMesh { InnerRadius = 2.3f, OuterRadius = 2.42f, Rings = 24 }, c + new Vector3(0, 1.75f, 0), window);
            Add(m, new TorusMesh { InnerRadius = 2.38f, OuterRadius = 2.48f, Rings = 24 }, c + new Vector3(0, 1.0f, 0), Glow(owner, 1.2f));
            Add(m, new SphereMesh { Radius = 2.0f, Height = 2.4f, IsHemisphere = true, RadialSegments = 20 }, c + new Vector3(0, 2.7f, 0), hull);
            Add(m, new BoxMesh { Size = new Vector3(0.1f, 3.6f, 0.1f) }, c + new Vector3(0, 2.6f, 0), seam);
            n.AddChild(m);
        }
        var a = n.GetNode<Node3D>("ModuleA");
        var mast = new Node3D { Name = "Mast", Position = new Vector3(0, 3.9f, 2.45f) };
        Add(mast, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.22f, Height = 3.4f }, new Vector3(0, 1.7f, 0), hull);
        var top = new Node3D { Name = "MastTop", Position = new Vector3(0, 3.4f, 0) };
        Add(top, new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 3.1f }, new Vector3(0, 1.25f, 0), Metal(new Color(0.55f, 0.57f, 0.6f), 0.85f, 0.32f));
        var dish = new Node3D { Name = "Dish", Position = new Vector3(1f, 1.55f, 0), Basis = new Basis(Vector3.Forward, Mathf.DegToRad(-40f)) };
        Add(dish, new CylinderMesh { TopRadius = 0.95f, BottomRadius = 0.25f, Height = 0.3f, RadialSegments = 20 }, new Vector3(0, 0.15f, 0), hull);
        top.AddChild(dish);
        var beaconGlow = Glow(new Color(1f, 0.08f, 0.05f), 8f);
        beaconGlow.ResourceName = "Beacon";
        Add(top, new SphereMesh { Radius = 0.17f, Height = 0.34f }, new Vector3(0, 3.08f, 0), beaconGlow).Name = "Beacon";
        mast.AddChild(top);
        a.AddChild(mast);
        return n;
    }
}
