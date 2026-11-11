using Godot;

namespace FinalFrontier;

/// <summary>
/// Drones, comms and missions models (docs/missions-contract.md B "Models"; glbs by blender/build_drone.py,
/// build_drone_pad.py, build_relay_satellite.py, build_mission_props.py), each with a primitive stand-in carrying the
/// same part names when the glb isn't there or imported yet.
///
/// Scout drone (models/drones/scout_drone.glb): Dawn of Warp quad tilt-rotor, ~2.6 across the rotor tips. Origin = the
/// ground under the skids, -Z front. Parts (FindChild, wherever they sit; spin/tilt them in place): Body, Arm0..3
/// (0 front-left, 1 front-right, 2 rear-left, 3 rear-right; rest = deployed), Tilt0..3 (nacelles under the arms: about
/// local X, -a tips the rotor forward), Rotor0..3 (under the nacelles: spin about local Y), Camera (gimbal, looks along
/// local -Y) + Lens, Beacon, NavL (red) / NavR (green), Strobe, Skid (scale Y 1 down .. 0.35 tucked), NameL/NameR decal
/// markers, material "Owner". Own material copies per drone (OwnMaterials), so its lights blink alone.
///
/// Drone Pad (models/buildings/drone_pad.glb): pad r ~6.5, front (+Z) = the access side. Bay0/Bay1 empties at the
/// landing circles' centres (+Y up), Charger0/1 arm pivots (rest = swung over the bay; the view swings them away about
/// local Y), Door (roll door pivot at the top of the opening: scale Y 1 shut .. 0.08 open), Dish (on the mast; turns
/// about local Y), Beacon, PadLight0/1 (each bay's ring lights, separate meshes, own materials), material "Status"
/// (the ops board), ChargeLight0/1 under Charger0/1 (material "ChargeLight", the charger heads), Build0..N, SignFront, AddOnSpot0..4, RoofSpot0..1, Owner bands.
///
/// Relay satellite (models/orbit/relay_satellite_prewarp.glb for Dawn of Warp, relay_satellite.glb later): orbit view
/// axes like the survey satellite (-Z = along the track, +Y = zenith, -Y = nadir): big parabolic Dish facing nadir,
/// feed Horn, Body, PanelL/PanelR (fold = Basis(Back, ±a)), Beacon, Thruster0..3, Link (material "Link", pulsed).
///
/// Mission props (models/missions/mission_props.glb): top-level nodes probe_wreck, signal_source, photo_point,
/// survey_marker, each within r ~5 of its origin with a sinking skirt; parts Beacon (probe_wreck), Glow
/// (signal_source).
/// </summary>
public static partial class BuildingModels
{
    public const string ScoutDronePath = "res://models/drones/scout_drone.glb";
    public const string DronePadPath = "res://models/buildings/drone_pad.glb";
    public const string RelayPath = "res://models/orbit/relay_satellite.glb";
    public const string RelayPrewarpPath = "res://models/orbit/relay_satellite_prewarp.glb";
    public const string MissionPropsPath = "res://models/missions/mission_props.glb";

    /// <summary>The Drone Pad's bays in its own frame (the stand-in's; the glb's Bay0/Bay1 empties win).</summary>
    public static readonly Vector3[] DronePadBays = { new(-2.75f, 0.55f, 1.3f), new(2.75f, 0.55f, 1.3f) }; // = drone_pad.glb Bay0/Bay1

    // ---------------------------------------------------------------- scout drone

    public static Node3D ScoutDrone(Color owner)
    {
        if (ImportedFile(ScoutDronePath, "scout_drone", owner) is { } n)
        {
            OwnMaterials(n);
            return n;
        }
        return PrimitiveScoutDrone(owner);
    }

    private static Node3D PrimitiveScoutDrone(Color owner)
    {
        var n = new Node3D { Name = "scout_drone" };
        var white = Named(Metal(new Color(0.92f, 0.92f, 0.9f), 0.1f, 0.45f), "WhitePaint");
        var orange = Named(Metal(new Color(0.78f, 0.39f, 0.16f), 0.1f, 0.5f), "Orange");
        var dark = Named(Metal(new Color(0.1f, 0.1f, 0.11f), 0.3f, 0.6f), "Dark");
        var grey = Named(Metal(new Color(0.6f, 0.62f, 0.64f), 0.5f, 0.4f), "Grey");
        var foil = Named(Metal(new Color(0.85f, 0.66f, 0.28f), 0.9f, 0.3f), "Foil");
        var ownerMat = Named(Glow(owner, 0.35f), "Owner");

        var body = Group(n, "Body");
        Shape(body, new BoxMesh { Size = new Vector3(0.46f, 0.3f, 1.2f) }, new Vector3(0, 0.58f, 0.05f), white);
        Shape(body, new SphereMesh { Radius = 0.24f, Height = 0.36f, RadialSegments = 12, Rings = 6 }, new Vector3(0, 0.58f, -0.56f), dark);
        Shape(body, new BoxMesh { Size = new Vector3(0.47f, 0.05f, 1.0f) }, new Vector3(0, 0.6f, 0.05f), orange);
        Shape(body, new BoxMesh { Size = new Vector3(0.3f, 0.04f, 0.36f) }, new Vector3(0, 0.75f, 0.1f), foil);
        Shape(body, new SphereMesh { Radius = 0.1f, Height = 0.12f, RadialSegments = 10, Rings = 4 }, new Vector3(0, 0.76f, -0.18f), white);
        Shape(body, new BoxMesh { Size = new Vector3(0.48f, 0.08f, 0.08f) }, new Vector3(0, 0.58f, 0.45f), ownerMat);
        Shape(body, new BoxMesh { Size = new Vector3(0.04f, 0.28f, 0.22f) }, new Vector3(0, 0.84f, 0.6f), white, new Vector3(-20f, 0, 0));

        // Arms: shoulders at the upper sides, booms out to the nacelles, three-blade rotors.
        var shoulders = new[] { new Vector3(-0.24f, 0.66f, -0.32f), new Vector3(0.24f, 0.66f, -0.32f), new Vector3(-0.24f, 0.66f, 0.38f), new Vector3(0.24f, 0.66f, 0.38f) };
        var nacelles = new[] { new Vector3(-0.86f, 0.68f, -0.62f), new Vector3(0.86f, 0.68f, -0.62f), new Vector3(-0.86f, 0.68f, 0.66f), new Vector3(0.86f, 0.68f, 0.66f) };
        for (var k = 0; k < 4; k++)
        {
            var arm = Group(n, $"Arm{k}", shoulders[k]);
            var tip = nacelles[k] - shoulders[k];
            var boom = Shape(arm, new BoxMesh { Size = new Vector3(0.07f, 0.06f, tip.Length()) }, tip * 0.5f, white);
            boom.Basis = Basis.LookingAt(tip.Normalized(), Vector3.Up);
            var tilt = Group(arm, $"Tilt{k}", tip);
            Shape(tilt, new CylinderMesh { TopRadius = 0.075f, BottomRadius = 0.09f, Height = 0.2f, RadialSegments = 10 }, Vector3.Zero, dark);
            Shape(tilt, new CylinderMesh { TopRadius = 0.095f, BottomRadius = 0.095f, Height = 0.04f, RadialSegments = 10 }, new Vector3(0, -0.05f, 0), orange);
            var rotor = Group(tilt, $"Rotor{k}", new Vector3(0, 0.13f, 0));
            Shape(rotor, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 0.05f, RadialSegments = 8 }, Vector3.Zero, grey);
            for (var b = 0; b < 3; b++)
            {
                var blade = Group(rotor, $"Blade{b}");
                blade.Basis = new Basis(Vector3.Up, Mathf.Tau * b / 3f);
                Shape(blade, new BoxMesh { Size = new Vector3(0.42f, 0.012f, 0.06f) }, new Vector3(0.22f, 0, 0), dark, new Vector3(6f, 0, 0));
                Shape(blade, new BoxMesh { Size = new Vector3(0.06f, 0.013f, 0.061f) }, new Vector3(0.4f, 0, 0), orange);
            }
        }

        var camera = Group(n, "Camera", new Vector3(0, 0.36f, -0.42f));
        Shape(camera, new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 10, Rings = 5 }, Vector3.Zero, dark);
        Shape(camera, new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 0.01f, RadialSegments = 12 }, new Vector3(0, -0.085f, 0),
            Named(Glow(new Color(0.6f, 0.45f, 1f), 1.6f), "Lens")).Name = "Lens";
        Shape(n, new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 8, Rings = 4 }, new Vector3(0, 0.8f, 0.02f), Named(Glow(new Color(1f, 0.18f, 0.08f), 6f), "Beacon")).Name = "Beacon";
        Shape(n, new SphereMesh { Radius = 0.022f, Height = 0.044f, RadialSegments = 8, Rings = 4 }, new Vector3(-0.25f, 0.6f, -0.2f), Named(Glow(new Color(1f, 0.08f, 0.04f), 3f), "NavRed")).Name = "NavL";
        Shape(n, new SphereMesh { Radius = 0.022f, Height = 0.044f, RadialSegments = 8, Rings = 4 }, new Vector3(0.25f, 0.6f, -0.2f), Named(Glow(new Color(0.05f, 1f, 0.2f), 3f), "NavGreen")).Name = "NavR";
        Shape(n, new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 8, Rings = 4 }, new Vector3(0, 0.98f, 0.66f), Named(Glow(new Color(1f, 0.98f, 0.95f), 1f), "Strobe")).Name = "Strobe";

        var skid = Group(n, "Skid", new Vector3(0, 0.43f, 0));
        foreach (var x in new[] { -0.3f, 0.3f })
        {
            Shape(skid, new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.84f, RadialSegments = 8 }, new Vector3(x, -0.4f, 0f), grey, new Vector3(90f, 0, 0));
            foreach (var z in new[] { -0.25f, 0.25f })
                Strut(skid, new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.015f, Height = new Vector3(x * 0.6f, 0.4f, 0f).Length(), RadialSegments = 6 },
                    new Vector3(x, -0.4f, z), new Vector3(x * 0.4f, 0f, z), white);
        }
        return n;
    }

    // ---------------------------------------------------------------- drone pad

    private static Node3D DronePad(Color owner, Look v)
    {
        Dressings.TryAdd("drone_pad", new("DRONE PAD", 5.6f, 5.2f, System.Array.Empty<Vector3>(), Vector3.Zero, 0f, 0f, false));
        if (ImportedFile(DronePadPath, "drone_pad", owner, Pieces(16)) is not { } n) return PrimitiveDronePad(owner);
        OwnMaterials(n);
        // each bay's ring lights and each charger head light on their own
        foreach (var name in new[] { "PadLight0", "PadLight1", "Charger0", "Charger1", "Status" })
            if (n.FindChild(name, true, false) is Node3D part) UnshareAll(part);
        ScaleY(n, "Masts", v.Height3);
        return n;
    }

    /// <summary>The signs on the pad's markers (from <see cref="PaintDetailedSigns"/>).</summary>
    private static void PaintDronePadSigns(Node3D n, string number)
    {
        PaintMarker(n, "SignFront", $"DRONE PAD {number}", SignInk);
        PaintMarker(n, "LabelBay0", "BAY 1", PlateInk);
        PaintMarker(n, "LabelBay1", "BAY 2", PlateInk);
    }

    private static Node3D PrimitiveDronePad(Color owner)
    {
        var n = new Node3D { Name = "drone_pad" };
        var concrete = Named(Metal(new Color(0.42f, 0.41f, 0.39f), 0f, 0.85f), "Concrete");
        var deck = Named(Metal(new Color(0.2f, 0.21f, 0.23f), 0.2f, 0.7f), "Deck");
        var hazard = Named(Metal(new Color(0.85f, 0.62f, 0.08f), 0f, 0.55f), "Hazard");
        var hull = Named(Metal(HullColour, 0.15f, 0.42f), "Hull");
        var trim = Named(Metal(Trim, 0.4f, 0.5f), "Trim");
        var steel = Named(Metal(new Color(0.55f, 0.57f, 0.6f), 0.85f, 0.32f), "Steel");
        var ownerMat = Named(Glow(owner, 0.35f), "Owner");

        // Build0 the pad and the two landing circles
        var b0 = Group(n, "Build0");
        Shape(b0, new CylinderMesh { TopRadius = 6.3f, BottomRadius = 6.5f, Height = 0.3f, RadialSegments = 8 }, new Vector3(0, 0.15f, 0), concrete);
        Shape(b0, new CylinderMesh { TopRadius = 6.32f, BottomRadius = 6.32f, Height = 0.06f, RadialSegments = 8 }, new Vector3(0, 0.22f, 0), ownerMat);
        for (var k = 0; k < 2; k++)
        {
            var at = DronePadBays[k];
            Shape(b0, new CylinderMesh { TopRadius = 2.3f, BottomRadius = 2.3f, Height = 0.03f, RadialSegments = 28 }, at with { Y = 0.31f }, deck);
            Shape(b0, new TorusMesh { InnerRadius = 1.95f, OuterRadius = 2.1f, Rings = 32, RingSegments = 4 }, at with { Y = 0.33f }, hazard);
            Shape(b0, new BoxMesh { Size = new Vector3(0.18f, 0.02f, 1.3f) }, at + new Vector3(-0.42f, 0.02f, 0), hazard);
            Shape(b0, new BoxMesh { Size = new Vector3(0.18f, 0.02f, 1.3f) }, at + new Vector3(0.42f, 0.02f, 0), hazard);
            Shape(b0, new BoxMesh { Size = new Vector3(0.7f, 0.02f, 0.18f) }, at + new Vector3(0, 0.02f, 0), hazard);
            n.AddChild(new Node3D { Name = $"Bay{k}", Position = at });
            var ring = Shape(b0, new TorusMesh { InnerRadius = 2.28f, OuterRadius = 2.4f, Rings = 24, RingSegments = 4 }, at with { Y = 0.34f },
                Named(Glow(new Color(0.9f, 0.95f, 1f), 1.2f), "PadLight"));
            ring.Name = $"PadLight{k}";
            Marker(b0, $"LabelBay{k}", at + new Vector3(0, 0.035f, 2.0f), 0f, 1.0f, 0.3f);
            Spot(b0, $"AddOnSpot{k}", at + new Vector3(k == 0 ? -2.9f : 2.9f, 0.3f, 2.4f), k == 0 ? -90f : 90f);
        }
        Spot(b0, "AddOnSpot2", new Vector3(-4.6f, 0.3f, -3.6f), 180f);

        // Build1 the ops box with its roll door and status board
        var b1 = Group(n, "Build1");
        Shape(b1, new BoxMesh { Size = new Vector3(5.2f, 2.6f, 2.4f) }, new Vector3(0, 1.6f, -3.7f), hull);
        Shape(b1, new BoxMesh { Size = new Vector3(5.3f, 0.14f, 2.5f) }, new Vector3(0, 2.95f, -3.7f), trim);
        Shape(b1, new BoxMesh { Size = new Vector3(5.22f, 0.12f, 2.42f) }, new Vector3(0, 2.75f, -3.7f), ownerMat);
        var door = Group(b1, "Door", new Vector3(0.4f, 2.45f, -2.47f));
        Shape(door, new BoxMesh { Size = new Vector3(2.2f, 2.1f, 0.08f) }, new Vector3(0, -1.05f, 0), Named(Metal(new Color(0.62f, 0.64f, 0.66f), 0.5f, 0.45f), "DoorPanel"));
        Shape(b1, new BoxMesh { Size = new Vector3(0.8f, 0.5f, 0.05f) }, new Vector3(-1.75f, 2.0f, -2.47f), Named(Glow(new Color(0.3f, 1f, 0.5f), 3f), "Status")).Name = "StatusBoard";
        Marker(b1, "SignFront", new Vector3(0.4f, 2.62f, -2.46f), 0f, 2.4f, 0.26f);
        Spot(b1, "RoofSpot0", new Vector3(-1.4f, 3.02f, -3.7f), 0f);

        // Build2 the chargers, as in the glb: pivot outboard of the bay at arm height, rest = arm pointing back (-Z);
        // RotateY(-90°) for Charger0, +90° for Charger1 swings the head (ChargeLight{k}) over the drone
        var b2 = Group(n, "Build2");
        for (var k = 0; k < 2; k++)
        {
            var side = k == 0 ? -1f : 1f;
            Shape(b2, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.16f, Height = 2.1f, RadialSegments = 10 },
                new Vector3(DronePadBays[k].X + side * 2.1f, 1.05f, DronePadBays[k].Z), steel);
            var pivot = Group(b2, $"Charger{k}", new Vector3(DronePadBays[k].X + side * 2.1f, 2.05f, DronePadBays[k].Z));
            Shape(pivot, new BoxMesh { Size = new Vector3(0.18f, 0.14f, 2.3f) }, new Vector3(0, 0f, -1.05f), hazard);
            Shape(pivot, new BoxMesh { Size = new Vector3(0.3f, 0.3f, 0.3f) }, new Vector3(0, -0.15f, -2.1f), trim);
            Shape(pivot, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 0.18f, RadialSegments = 8 }, new Vector3(0, -0.43f, -2.1f),
                Named(Glow(new Color(0.4f, 0.8f, 1f), 0.3f), "ChargeLight")).Name = $"ChargeLight{k}";
        }

        // Build3 the mast: dish and beacon
        var b3 = Group(n, "Build3");
        var masts = Group(b3, "Masts", new Vector3(4.9f, 0.3f, -4.4f));
        Shape(masts, new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.14f, Height = 6.4f, RadialSegments = 8 }, new Vector3(0, 3.2f, 0), steel);
        var dish = Group(masts, "Dish", new Vector3(0, 6.1f, 0));
        Shape(dish, new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.12f, Height = 0.3f, RadialSegments = 16 }, new Vector3(0, 0.1f, -0.25f), hull, new Vector3(-62f, 0, 0));
        Shape(dish, new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.6f, RadialSegments = 6 }, new Vector3(0, 0.3f, -0.5f), steel, new Vector3(-62f, 0, 0));
        Shape(masts, new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 8, Rings = 4 }, new Vector3(0, 6.55f, 0), Named(Glow(new Color(1f, 0.1f, 0.06f), 8f), "Beacon")).Name = "Beacon";
        return n;
    }

    // ---------------------------------------------------------------- relay satellite (orbit view)

    /// <summary>The era's relay satellite (pre-warp in Dawn of Warp), else the standard glb, else the stand-in.</summary>
    public static Node3D RelaySatellite(Color owner, bool prewarp)
    {
        var n = (prewarp ? ImportedFile(RelayPrewarpPath, "relay_satellite", owner) : null) ?? ImportedFile(RelayPath, "relay_satellite", owner);
        if (n == null) return PrimitiveRelay(owner, prewarp);
        OwnMaterials(n);
        return n;
    }

    public static string RelayModelName(bool prewarp) => prewarp ? "Earth comms relay (pre-warp)" : "Subspace relay";

    private static Node3D PrimitiveRelay(Color owner, bool prewarp)
    {
        var n = new Node3D { Name = "relay_satellite" };
        var hull = Metal(prewarp ? new Color(0.86f, 0.68f, 0.3f) : new Color(0.82f, 0.84f, 0.88f), 0.6f, 0.35f);
        var white = Metal(new Color(0.9f, 0.9f, 0.88f), 0.2f, 0.4f);
        var dark = Metal(new Color(0.14f, 0.15f, 0.18f), 0.5f, 0.5f);
        var cells = Metal(new Color(0.1f, 0.18f, 0.42f), 0.4f, 0.25f);
        var body = Group(n, "Body");
        Add(body, new BoxMesh { Size = new Vector3(0.6f, 0.62f, 0.6f) }, Vector3.Zero, hull);
        Add(body, new BoxMesh { Size = new Vector3(0.62f, 0.07f, 0.62f) }, new Vector3(0, 0.24f, 0), Metal(owner, 0.3f, 0.4f));
        var dish = Group(n, "Dish", new Vector3(0, -0.36f, 0));
        Add(dish, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.85f, Height = 0.28f, RadialSegments = 24 }, new Vector3(0, -0.14f, 0), white);
        var horn = Group(n, "Horn", new Vector3(0, -0.98f, 0));
        Add(horn, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.08f, Height = 0.16f, RadialSegments = 10 }, Vector3.Zero, dark);
        for (var k = 0; k < 3; k++)
        {
            var a = Mathf.Tau * k / 3f;
            var rimAt = new Vector3(Mathf.Cos(a) * 0.8f, -0.62f, Mathf.Sin(a) * 0.8f);
            var feed = new Vector3(0, -0.92f, 0);
            Strut(n, new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = rimAt.DistanceTo(feed), RadialSegments = 4 }, rimAt, feed, dark);
        }
        foreach (var (name, side) in new[] { ("PanelL", -1f), ("PanelR", 1f) })
        {
            var hinge = Group(n, name, new Vector3(side * 0.31f, 0.1f, 0f));
            Add(hinge, new BoxMesh { Size = new Vector3(1.3f, 0.025f, 0.55f) }, new Vector3(side * 0.7f, 0, 0), cells);
            Add(hinge, new BoxMesh { Size = new Vector3(0.08f, 0.05f, 0.08f) }, new Vector3(side * 0.04f, 0, 0), dark);
        }
        var link = Add(n, new TorusMesh { InnerRadius = 0.8f, OuterRadius = 0.86f, Rings = 32, RingSegments = 4 }, new Vector3(0, -0.5f, 0), Named(Glow(new Color(0.5f, 1f, 0.8f), 2f), "Link"));
        link.Name = "Link";
        Add(n, new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 8, Rings = 4 }, new Vector3(0, 0.36f, 0.22f), Glow(new Color(1f, 0.2f, 0.1f), 6f)).Name = "Beacon";
        for (var k = 0; k < 4; k++)
        {
            var x = (k % 2 == 0 ? -1f : 1f) * 0.32f;
            var z = k < 2 ? -0.32f : 0.32f;
            Add(n, new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 8, Rings = 4 }, new Vector3(x, 0.2f, z), Glow(new Color(0.6f, 0.8f, 1f), 0f)).Name = $"Thruster{k}";
        }
        return n;
    }

    // ---------------------------------------------------------------- mission props

    /// <summary>A mission site's prop by its MissionSite.Kind (probe_wreck, signal_source, photo_point,
    /// survey_marker; anything else = a survey marker), from mission_props.glb or a stand-in.</summary>
    public static Node3D MissionProp(string kind)
    {
        if (ResourceLoader.Exists(MissionPropsPath) && GD.Load<PackedScene>(MissionPropsPath)?.Instantiate() is Node3D scene)
        {
            if (scene.FindChild(kind, true, false) is Node3D part)
            {
                var t = RelativeTo(part, scene);
                part.GetParent().RemoveChild(part);
                ClearOwner(part);
                part.Transform = t;
                scene.Free();
                var n = new Node3D { Name = kind };
                n.AddChild(part);
                OwnMaterials(n);
                return n;
            }
            scene.Free();
        }
        return PrimitiveMissionProp(kind);
    }

    private static Node3D PrimitiveMissionProp(string kind)
    {
        var n = new Node3D { Name = kind };
        var white = Named(Metal(new Color(0.85f, 0.85f, 0.82f), 0.3f, 0.5f), "WhitePaint");
        var dark = Named(Metal(new Color(0.12f, 0.12f, 0.13f), 0.4f, 0.6f), "Dark");
        var scorch = Named(Metal(new Color(0.08f, 0.07f, 0.06f), 0f, 0.95f), "Scorch");
        var orange = Named(Metal(new Color(0.85f, 0.42f, 0.15f), 0f, 0.7f), "Orange");
        switch (kind)
        {
            case "probe_wreck":
            {
                Shape(n, new CylinderMesh { TopRadius = 4.2f, BottomRadius = 4.6f, Height = 0.06f, RadialSegments = 20 }, new Vector3(0, 0.02f, 0), scorch);
                var capsule = Group(n, "Capsule", new Vector3(0.3f, 0.35f, -0.2f));
                capsule.RotationDegrees = new Vector3(28f, 35f, 12f);
                Shape(capsule, new CylinderMesh { TopRadius = 0.55f, BottomRadius = 1.25f, Height = 1.5f, RadialSegments = 16 }, Vector3.Zero, Named(Metal(new Color(0.55f, 0.5f, 0.42f), 0.6f, 0.6f), "Hull"));
                Shape(capsule, new CylinderMesh { TopRadius = 1.28f, BottomRadius = 1.28f, Height = 0.18f, RadialSegments = 16 }, new Vector3(0, -0.8f, 0), dark);
                Shape(capsule, new BoxMesh { Size = new Vector3(0.6f, 0.4f, 0.04f) }, new Vector3(0, 0.1f, 0.95f), Named(Metal(new Color(0.8f, 0.65f, 0.25f), 0.9f, 0.35f), "Foil"));
                var chute = Shape(n, new BoxMesh { Size = new Vector3(3.6f, 0.03f, 2.2f) }, new Vector3(-2.2f, 0.06f, 1.6f), orange, new Vector3(0, 25f, 3f));
                chute.Name = "Chute";
                Shape(n, new BoxMesh { Size = new Vector3(2.2f, 0.03f, 1.4f) }, new Vector3(-3.4f, 0.08f, 0.4f), white, new Vector3(0, -15f, 2f));
                Shape(n, new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 8, Rings = 4 }, new Vector3(0.1f, 1.25f, -0.55f), Named(Glow(new Color(1f, 0.25f, 0.1f), 3f), "Beacon")).Name = "Beacon";
                break;
            }
            case "signal_source":
            {
                var rock = Named(Metal(new Color(0.32f, 0.3f, 0.28f), 0f, 0.9f), "Rock");
                Shape(n, new SphereMesh { Radius = 2.4f, Height = 2.6f, RadialSegments = 10, Rings = 6 }, new Vector3(0, -0.2f, 0), rock).Scale = new Vector3(1.2f, 0.7f, 1f);
                Shape(n, new SphereMesh { Radius = 1.2f, Height = 1.6f, RadialSegments = 8, Rings = 5 }, new Vector3(1.8f, -0.1f, 1.2f), rock);
                var mast = Group(n, "Transmitter", new Vector3(-0.4f, 1.3f, 0.3f));
                mast.RotationDegrees = new Vector3(14f, 0, -9f);
                Shape(mast, new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.22f, Height = 1.6f, RadialSegments = 8 }, new Vector3(0, 0.6f, 0), dark);
                Shape(mast, new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.06f, Height = 0.16f, RadialSegments = 12 }, new Vector3(0, 1.45f, 0), Named(Metal(new Color(0.5f, 0.55f, 0.6f), 0.8f, 0.3f), "Steel"), new Vector3(30f, 0, 0));
                Shape(mast, new SphereMesh { Radius = 0.26f, Height = 0.52f, RadialSegments = 12, Rings = 6 }, new Vector3(0, 0.05f, 0), Named(Glow(new Color(0.5f, 0.95f, 0.9f), 3f), "Glow")).Name = "Glow";
                break;
            }
            case "photo_point":
            {
                var tripod = Group(n, "Tripod");
                for (var k = 0; k < 3; k++)
                {
                    var a = Mathf.Tau * k / 3f;
                    Strut(tripod, new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.04f, Height = new Vector3(Mathf.Cos(a) * 0.7f, 1.6f, Mathf.Sin(a) * 0.7f).Length(), RadialSegments = 6 },
                        new Vector3(Mathf.Cos(a) * 0.7f, 0f, Mathf.Sin(a) * 0.7f), new Vector3(0, 1.6f, 0), dark);
                }
                Shape(tripod, new BoxMesh { Size = new Vector3(0.4f, 0.3f, 0.5f) }, new Vector3(0, 1.8f, 0), white);
                Shape(tripod, new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.25f, RadialSegments = 10 }, new Vector3(0, 1.8f, -0.35f), dark, new Vector3(90f, 0, 0));
                Shape(n, new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 8, Rings = 4 }, new Vector3(0.15f, 2.0f, 0.1f), Named(Glow(new Color(0.4f, 1f, 0.5f), 3f), "Beacon")).Name = "Beacon";
                break;
            }
            default:
            {
                Shape(n, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 3f, RadialSegments = 8 }, new Vector3(0, 1.5f, 0), white);
                var flag = Shape(n, new BoxMesh { Size = new Vector3(1.1f, 0.7f, 0.02f) }, new Vector3(0.58f, 2.6f, 0), orange);
                flag.Name = "Flag";
                Shape(n, new BoxMesh { Size = new Vector3(0.6f, 0.4f, 0.08f) }, new Vector3(0, 0.3f, 0.4f), dark, new Vector3(-30f, 0, 0));
                Shape(n, new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 8, Rings = 4 }, new Vector3(0, 3.05f, 0), Named(Glow(new Color(1f, 0.6f, 0.15f), 3f), "Beacon")).Name = "Beacon";
                break;
            }
        }
        return n;
    }
}
