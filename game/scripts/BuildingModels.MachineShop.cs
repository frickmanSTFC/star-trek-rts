using Godot;

namespace FinalFrontier;

/// <summary>
/// The Machine Shop (parts economy, docs/parts-contract.md "Model"): models/buildings/machine_shop.glb from
/// blender/build_machine_shop.py --game (fabrication hall with a roll-up door, a glazed window band and roof lantern,
/// two robot arms over a work cell, a hydraulic press, a belt conveyor out to an output rack of kit crates, a control
/// booth, a materials hopper, parts racks, a beacon mast; 13 build pieces, one baked grime atlas), or a primitive
/// stand-in with the same part names when the glb isn't there or imported yet. <see cref="PlanetView"/> animates it
/// from the fabricator's status (PlanetView.MachineShop.cs).
///
/// Parts the view drives (FindChild, wherever they sit):
///   Arm0, Arm1          robot arm turrets: rotate about local Y; rest = aimed at the work.
///   Arm0Joint, ...      their elbows: bend about local X (+ raises the forearm). Each joint's meshes have their own
///                       material copies, so its torch tip ("WeldGlow") lights on its own.
///   SparkPoint0/1       torch tips (under the joints): weld sparks, broken sparks.
///   Press (+ PressLow)  ram + platen, strokes down to the sibling PressLow.
///   Door                roll-up door pivot at the top of the opening: scale Y 1 = shut, 0.08 = open.
///   Conveyor            belt mesh, material "Belt" (UV v every 0.6 along the run; scroll Uv1Offset.y).
///   CrateFlow           crates "Crate0..3" ride from it to its child "CrateEnd".
///   Fan0, Fan1          roof fans (spin about local Y).
///   ShopLight, CellLight  hold "ShopOmni" (the hall) and "WeldOmni" (the work cell's weld flashes).
///   materials           Status (booth board + door lamps), WeldGlow, Belt, Lamp, Window, Beacon; "ShopGlass" is
///                       replaced here by <see cref="ShopGlassMaterial"/>.
/// Footprint: pad 10.5 x 9 (radius ~7), front (+Z) = the access side with the door.
/// </summary>
public static partial class BuildingModels
{
    public const string MachineShopPath = "res://models/buildings/machine_shop.glb";

    private static readonly Color RobotColour = new(0.88f, 0.6f, 0.13f);
    private static readonly Color[] KitColours =
    {
        new(0.78f, 0.32f, 0.12f), new(0.18f, 0.36f, 0.7f), new(0.56f, 0.62f, 0.2f), new(0.12f, 0.52f, 0.5f),
    };

    private static Node3D MachineShop(Color owner, Look v)
    {
        Dressings.TryAdd("machine_shop", new("MACHINE SHOP", 5.25f, 4.5f, System.Array.Empty<Vector3>(), Vector3.Zero, 0f, 0f, false));
        if (ImportedFile(MachineShopPath, "machine_shop", owner, Pieces(13)) is not { } n) return PrimitiveMachineShop(owner, v);
        OwnMaterials(n);
        ShopGlassUp(n);
        // each arm's joint gets its own materials so its torch tip glows only while that arm welds
        foreach (var joint in new[] { "Arm0Joint", "Arm1Joint" })
            if (n.FindChild(joint, true, false) is Node3D j) UnshareAll(j);
        ScaleY(n, "Masts", v.Height3);
        Optional(n, "OptRack", v.IsPlain || v.Chance(0.75f));
        Optional(n, "OptCrates", v.IsPlain || v.Chance(0.55f));
        Optional(n, "OptAc", v.IsPlain || v.Chance(0.6f));
        AddShopLights(n);
        return n;
    }

    /// <summary>The window band's and roof lantern's glass: see-through, glossy, no shadow.</summary>
    public static StandardMaterial3D ShopGlassMaterial() => new()
    {
        ResourceName = "ShopGlass",
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(0.6f, 0.75f, 0.88f, 0.14f),
        Metallic = 0.1f,
        MetallicSpecular = 0.9f,
        Roughness = 0.05f,
        RimEnabled = true,
        Rim = 0.35f,
        RimTint = 0.3f,
        // a faint glow so the panes don't go black at night (and Variety's Paint leaves them alone)
        EmissionEnabled = true,
        Emission = new Color(0.55f, 0.72f, 0.9f),
        EmissionEnergyMultiplier = 0.04f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static void ShopGlassUp(Node3D n)
    {
        foreach (var name in new[] { "GlassFront", "GlassRoof" })
        {
            if (n.FindChild(name, true, false) is not MeshInstance3D glass || glass.Mesh == null) continue;
            glass.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            for (var i = 0; i < glass.Mesh.GetSurfaceCount(); i++) glass.SetSurfaceOverrideMaterial(i, ShopGlassMaterial());
        }
    }

    /// <summary><see cref="Unshare"/> on every mesh under a node.</summary>
    private static void UnshareAll(Node node)
    {
        if (node is MeshInstance3D mi) Unshare(mi);
        foreach (var child in node.GetChildren()) UnshareAll(child);
    }

    private static void AddShopLights(Node3D n)
    {
        if (n.FindChild("ShopLight", true, false) is Node3D shop && shop.GetNodeOrNull("ShopOmni") == null)
            shop.AddChild(new OmniLight3D { Name = "ShopOmni", LightColor = new Color(1f, 0.9f, 0.76f), LightEnergy = 1.1f, OmniRange = 9f, ShadowEnabled = false });
        if (n.FindChild("CellLight", true, false) is Node3D cell && cell.GetNodeOrNull("WeldOmni") == null)
            cell.AddChild(new OmniLight3D { Name = "WeldOmni", LightColor = new Color(0.6f, 0.78f, 1f), LightEnergy = 0f, OmniRange = 6f, ShadowEnabled = false });
    }

    /// <summary>The signs on the glb's markers (called from <see cref="PaintDetailedSigns"/>).</summary>
    private static void PaintMachineShopSigns(Node3D n, string number)
    {
        PaintMarker(n, "SignFront", $"MACHINE SHOP {number}", SignInk);
        PaintMarker(n, "SignHall", "FABRICATION", SignInk);
        PaintMarker(n, "LabelHopper", "FEED STOCK", PlateInk);
        PaintMarker(n, "LabelRack", "KITS OUT", SignInk);
    }

    // ---------------------------------------------------------------- primitive stand-in (same part names)

    /// <summary>Godot position of a point given in the Blender script's frame (x right, y back, z up).</summary>
    private static Vector3 Bl(float x, float y, float z) => new(x, z, -y);

    /// <summary>A mesh whose material is a surface override (so the view's material lookups find it, as on the glb).</summary>
    private static MeshInstance3D Shape(Node3D parent, Mesh mesh, Vector3 at, Material material, Vector3? rotationDegrees = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, Position = at };
        if (rotationDegrees is { } r) mi.RotationDegrees = r;
        mi.SetSurfaceOverrideMaterial(0, material);
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>An axis-aligned box by its extents in the Blender frame.</summary>
    private static MeshInstance3D BoxBl(Node3D parent, float x0, float x1, float y0, float y1, float z0, float z1, Material material) =>
        Shape(parent, new BoxMesh { Size = new Vector3(x1 - x0, z1 - z0, y1 - y0) }, Bl((x0 + x1) / 2f, (y0 + y1) / 2f, (z0 + z1) / 2f), material);

    private static Node3D PrimitiveMachineShop(Color owner, Look v)
    {
        const float slab = 0.3f, floor = 0.42f, wallTop = 5.32f, roofTop = 5.57f, doorTop = floor + 3.9f;
        var n = new Node3D { Name = "machine_shop" };
        var hull = Named(Metal(HullColour, 0.15f, 0.42f), "Hull");
        var panel = Named(Metal(new Color(0.56f, 0.58f, 0.62f), 0.2f, 0.5f), "Panel");
        var trim = Named(Metal(Trim, 0.4f, 0.5f), "Trim");
        var accent = Named(Metal(new Color(0.14f, 0.3f, 0.55f), 0.3f, 0.45f), "Accent");
        var concrete = Named(Metal(new Color(0.4f, 0.39f, 0.37f), 0f, 0.85f), "Concrete");
        var steel = Named(Metal(new Color(0.55f, 0.57f, 0.6f), 0.85f, 0.32f), "Steel");
        var hazard = Named(Metal(new Color(0.85f, 0.62f, 0.08f), 0f, 0.55f), "Hazard");
        var robot = Named(Metal(RobotColour, 0.25f, 0.4f), "Robot");
        var ownerMat = Named(Glow(owner, 0.35f), "Owner");
        var status = Named(Glow(new Color(0.3f, 1f, 0.5f), 3f), "Status");
        var lamp = Named(Glow(new Color(1f, 0.95f, 0.85f), 4f), "Lamp");
        var window = Named(Glow(new Color(1f, 0.82f, 0.55f), 2.5f), "Window");
        var glass = ShopGlassMaterial();

        // Build0 pad + the variety spots
        var b0 = Group(n, "Build0");
        BoxBl(b0, -5.25f, 5.25f, -4.5f, 4.5f, 0f, slab, concrete);
        BoxBl(b0, -5.26f, 5.26f, -4.51f, 4.51f, 0.13f, 0.24f, ownerMat);
        BoxBl(b0, -4.15f, -0.85f, -2.3f, -1.62f, slab, slab + 0.01f, hazard);
        foreach (var (k, x, y, yaw) in new[] { (0, -6.15f, 0.4f, 90f), (1, -6.15f, 3f, 90f), (2, -2.4f, 5.35f, 0f), (3, 1.6f, 5.35f, 0f), (4, 6.15f, -0.6f, -90f) })
            Spot(b0, $"AddOnSpot{k}", Bl(x, y, 0f), yaw);

        // Build1 the hall: walls round the door / window / conveyor port, roof round the lantern, the door
        var b1 = Group(n, "Build1");
        BoxBl(b1, -5.1f, 2.3f, -1.7f, 4.2f, slab, floor, trim);
        BoxBl(b1, -5f, 2.2f, 3.9f, 4.1f, floor, wallTop, hull);
        BoxBl(b1, -5f, -4.8f, -1.4f, 3.9f, floor, wallTop, hull);
        BoxBl(b1, 2f, 2.2f, -1.4f, 3.9f, floor, wallTop, hull);
        BoxBl(b1, -5f, -4.3f, -1.6f, -1.4f, floor, wallTop, hull);
        BoxBl(b1, -4.3f, -0.7f, -1.6f, -1.4f, doorTop, wallTop, hull);
        BoxBl(b1, -0.7f, -0.3f, -1.6f, -1.4f, floor, wallTop, hull);
        BoxBl(b1, -0.3f, 0.5f, -1.6f, -1.4f, floor, floor + 1.55f, hull);
        BoxBl(b1, 1.4f, 1.95f, -1.6f, -1.4f, floor, floor + 1.55f, hull);
        BoxBl(b1, 0.5f, 1.4f, -1.6f, -1.4f, floor + 1.35f, floor + 1.55f, hull);
        BoxBl(b1, -0.3f, 1.95f, -1.6f, -1.4f, floor + 3.55f, wallTop, hull);
        BoxBl(b1, 1.95f, 2.2f, -1.6f, -1.4f, floor, wallTop, hull);
        BoxBl(b1, -0.3f, 1.95f, -1.51f, -1.49f, floor + 1.55f, floor + 3.55f, glass).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        BoxBl(b1, -5.03f, 2.23f, -1.63f, 4.13f, wallTop - 0.3f, wallTop - 0.12f, ownerMat);
        BoxBl(b1, -5.03f, 2.23f, -1.63f, 4.13f, floor, floor + 0.5f, accent);
        BoxBl(b1, 2.2f, 2.21f, 1f, 3.6f, floor + 2.5f, floor + 3.2f, window);
        BoxBl(b1, -5.1f, 2.3f, -1.7f, -0.95f, wallTop, roofTop, panel);
        BoxBl(b1, -5.1f, 2.3f, 3.45f, 4.2f, wallTop, roofTop, panel);
        BoxBl(b1, -5.1f, -4.4f, -0.95f, 3.45f, wallTop, roofTop, panel);
        BoxBl(b1, -0.7f, 2.3f, -0.95f, 3.45f, wallTop, roofTop, panel);
        BoxBl(b1, -4.4f, -0.7f, -0.95f, 3.45f, roofTop, roofTop + 0.6f, glass).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        BoxBl(b1, -4.5f, -0.5f, -2.02f, -1.6f, doorTop, doorTop + 0.3f, trim);
        var door = Group(b1, "Door", Bl(-2.5f, -1.67f, doorTop));
        Shape(door, new BoxMesh { Size = new Vector3(3.58f, 3.9f, 0.05f) }, new Vector3(0, -1.95f, 0), panel);
        Shape(door, new BoxMesh { Size = new Vector3(3.58f, 0.3f, 0.06f) }, new Vector3(0, -3.75f, 0), hazard);
        Marker(b1, "SignFront", Bl(-2.5f, -1.67f, floor + 4.41f), 0f, 3.4f, 0.27f);
        Marker(b1, "SignHall", Bl(0.82f, -1.66f, floor + 3.89f), 0f, 2f, 0.22f);
        foreach (var k in new[] { 0, 1, 2 })
            Shape(b1, new BoxMesh { Size = new Vector3(0.08f, 0.14f, 0.03f) }, Bl(-0.48f, -1.665f, floor + 2.7f + k * 0.19f), status);
        Group(b1, "ShopLight", Bl(-1.4f, 1.3f, floor + 3.6f));
        Shape(b1, new BoxMesh { Size = new Vector3(3.3f, 0.02f, 0.12f) }, Bl(-2.55f, 1.5f, floor + 4.14f), lamp);

        // Build2 the work cell: fixture table + frame, two robot arms with torches
        var b2 = Group(n, "Build2");
        BoxBl(b2, -3.35f, -1.65f, 0.92f, 2.08f, floor, floor + 0.9f, steel);
        BoxBl(b2, -3.15f, -1.85f, 1.12f, 1.88f, floor + 0.9f, floor + 1.04f, Named(Metal(new Color(0.5f, 0.56f, 0.64f), 0.85f, 0.35f), "Duranium"));
        foreach (var (k, x, yaw) in new[] { (0, -4.2f, -90f), (1, -0.8f, 90f) })
        {
            Shape(b2, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.4f, Height = 0.6f, RadialSegments = 12 }, Bl(x, 1.5f, floor + 0.3f), trim);
            var arm = Group(b2, $"Arm{k}", Bl(x, 1.5f, floor + 0.6f));
            arm.RotationDegrees = new Vector3(0, yaw, 0);
            Shape(arm, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.34f, Height = 0.5f, RadialSegments = 12 }, new Vector3(0, 0.25f, 0), robot);
            var elbow = new Vector3(0, 1.65f, -0.5f);
            Strut(arm, new BoxMesh { Size = new Vector3(0.28f, elbow.DistanceTo(new Vector3(0, 0.5f, 0)), 0.32f) }, new Vector3(0, 0.5f, 0), elbow, robot);
            var joint = Group(arm, $"Arm{k}Joint", elbow);
            Shape(joint, new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.4f, RadialSegments = 10 }, Vector3.Zero, trim, new Vector3(0, 0, 90));
            var wrist = new Vector3(0, -0.45f, -1.15f);
            var tip = new Vector3(0, -0.9f, -1.2f);
            Strut(joint, new BoxMesh { Size = new Vector3(0.2f, wrist.Length(), 0.24f) }, Vector3.Zero, wrist, robot);
            Strut(joint, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = wrist.DistanceTo(tip), RadialSegments = 6 }, wrist, tip, steel);
            Shape(joint, new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 6, Rings = 3 }, tip, Named(Glow(new Color(0.75f, 0.88f, 1f), 0.6f), "WeldGlow"));
            Group(joint, $"SparkPoint{k}", tip);
        }
        Group(b2, "CellLight", Bl(-2.5f, 1.5f, floor + 1.7f));

        // Build3 the press, the conveyor out through the port, the crates riding it
        var b3 = Group(n, "Build3");
        BoxBl(b3, 0.3f, 1.6f, 2.07f, 3.03f, floor, floor + 0.9f, accent);
        foreach (var s in new[] { -1f, 1f })
            BoxBl(b3, 0.95f + s * 0.62f - 0.12f, 0.95f + s * 0.62f + 0.12f, 2.05f, 3.05f, floor + 0.9f, floor + 3.3f, accent);
        BoxBl(b3, 0f, 1.9f, 1.97f, 3.13f, floor + 3.3f, floor + 3.85f, accent);
        BoxBl(b3, -0.01f, 1.91f, 1.96f, 3.14f, floor + 3.5f, floor + 3.62f, ownerMat);
        var press = Group(b3, "Press", Bl(0.95f, 2.55f, floor + 2.35f));
        Shape(press, new BoxMesh { Size = new Vector3(1.1f, 0.3f, 0.8f) }, new Vector3(0, -0.15f, 0), steel);
        Shape(press, new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 0.98f, RadialSegments = 10 }, new Vector3(0, 0.49f, 0), steel);
        Group(b3, "PressLow", Bl(0.95f, 2.55f, floor + 1.5f));
        var a = Bl(0.95f, 1.75f, floor + 0.85f);
        var end = Bl(0.95f, -3.2f, floor + 0.85f);
        var belt = Shape(b3, new BoxMesh { Size = new Vector3(0.6f, 0.04f, a.DistanceTo(end)) }, (a + end) / 2f, Named(Metal(new Color(0.1f, 0.1f, 0.1f), 0f, 0.85f), "Belt"));
        belt.Name = "Conveyor";
        BoxBl(b3, 0.58f, 1.32f, -3.2f, 1.75f, slab, floor + 0.8f, trim);
        var flow = Group(b3, "CrateFlow", a + new Vector3(0, 0.02f, 0));
        Group(flow, "CrateEnd", end - a);
        var crateMat = Named(Metal(KitColours[0], 0.1f, 0.55f), "KitCrate");
        for (var k = 0; k < 4; k++)
        {
            var c = Shape(flow, new BoxMesh { Size = new Vector3(0.42f, 0.3f, 0.42f) }, (end - a) * ((k + 0.5f) / 4f) + new Vector3(0, 0.15f, 0), crateMat);
            c.Name = $"Crate{k}";
        }

        // Build4 output rack of kit crates, the hopper with its feeder
        var b4 = Group(n, "Build4");
        foreach (var x in new[] { 1.55f, 2.5f, 3.45f })
            foreach (var y in new[] { -4.1f, -3.2f })
                BoxBl(b4, x - 0.05f, x + 0.05f, y - 0.05f, y + 0.05f, slab, slab + 2.65f, accent);
        for (var lv = 0; lv < 3; lv++)
            for (var q = 0; q < 4; q++)
            {
                var x = 1.75f + q * 0.45f;
                var z = slab + 0.12f + lv * 1.15f;
                BoxBl(b4, x - 0.18f, x + 0.18f, -3.85f, -3.45f, z, z + 0.3f, Named(Metal(KitColours[(q + lv) % 4], 0.1f, 0.55f), "Kit"));
            }
        Marker(b4, "LabelRack", Bl(2.5f, -4.155f, slab + 2.965f), 0f, 1.6f, 0.22f);
        BoxBl(b4, 3.17f, 4.53f, -1.78f, -0.42f, slab + 2.8f, slab + 3.35f, hull);
        Shape(b4, new CylinderMesh { TopRadius = 0.95f, BottomRadius = 0.2f, Height = 0.9f, RadialSegments = 4 }, Bl(3.85f, -1.1f, slab + 2.35f), panel, new Vector3(0, 45f, 0));
        Marker(b4, "LabelHopper", Bl(3.85f, -1.825f, slab + 3.16f), 0f, 0.85f, 0.17f);

        // Build5 the control booth with the Status board
        var b5 = Group(n, "Build5");
        BoxBl(b5, 3.75f, 5.05f, -4.2f, -2.45f, slab, slab + 2.75f, hull);
        BoxBl(b5, 3.74f, 5.06f, -4.21f, -2.44f, slab + 2.43f, slab + 2.55f, ownerMat);
        BoxBl(b5, 3.9f, 4.9f, -4.215f, -4.2f, slab + 1.7f, slab + 2.2f, window);
        BoxBl(b5, 3.93f, 4.87f, -4.285f, -4.2f, slab + 1.15f, slab + 1.35f, status);
        Spot(b5, "RoofSpot1", Bl(4.05f, -3.73f, slab + 2.89f), 0f);
        Spot(b5, "RoofSpot0", Bl(1.55f, -0.55f, roofTop), 0f);

        // Build6 roof fans, the beacon mast, the floodlight mast
        var b6 = Group(n, "Build6");
        foreach (var (k, y) in new[] { (0, 0.35f), (1, 2.4f) })
        {
            Shape(b6, new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.8f, Height = 0.42f, RadialSegments = 16, CapTop = false }, Bl(0.55f, y, roofTop + 0.21f), hull);
            var fan = Group(b6, $"Fan{k}", Bl(0.55f, y, roofTop + 0.3f));
            Shape(fan, new BoxMesh { Size = new Vector3(1.2f, 0.03f, 0.18f) }, Vector3.Zero, steel);
            Shape(fan, new BoxMesh { Size = new Vector3(0.18f, 0.03f, 1.2f) }, Vector3.Zero, steel);
        }
        var masts = Group(b6, "Masts", new Vector3(0, slab, 0));
        Shape(masts, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.2f, Height = 8f, RadialSegments = 6 }, Bl(4.8f, 3.95f, 4f), trim);
        var beacon = Shape(masts, new SphereMesh { Radius = 0.11f, Height = 0.22f, RadialSegments = 10, Rings = 5 }, Bl(4.8f, 3.95f, 8.2f), Named(Glow(new Color(1f, 0.08f, 0.05f), 8f), "Beacon"));
        beacon.Name = "Beacon";
        Shape(masts, new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = 5.6f, RadialSegments = 8 }, Bl(-4.95f, -4.15f, 2.8f), hull);
        Shape(masts, new BoxMesh { Size = new Vector3(0.5f, 0.12f, 0.3f) }, Bl(-4.95f, -4.15f, 5.6f), lamp);
        masts.Scale = new Vector3(1f, v.Height3, 1f);

        AddShopLights(n);
        return n;
    }
}
