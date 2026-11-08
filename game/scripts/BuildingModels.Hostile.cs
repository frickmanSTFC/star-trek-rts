using Godot;

namespace FinalFrontier;

/// <summary>
/// The hostile-worlds buildings (docs/hostile-worlds-contract.md): the Pressure Dome and the Life Support Plant
/// (blender/build_pressure_dome.py, build_life_support.py --game → models/buildings/&lt;id&gt;.glb, built like the
/// detailed buildings: Build0..N pieces for the exocomps, one baked grime atlas, named parts), or primitive stand-ins
/// with the same part names when a glb isn't there or imported yet. Plus <see cref="SealCollar"/>, an airlock vestibule
/// add-on for crewed buildings on Domes worlds (not wired in yet).
///
/// Parts the view can drive (FindChild, wherever they sit):
///   pressure_dome      DomeGlass (mesh; surfaces "Glass" + "GlassFrost", replaced here by see-through materials),
///                      DomeFrame, Airlock0/1 (+ Airlock0Door/Airlock1Door leaves: slide along local X to open; material
///                      "DoorLight" = the lit door frames), FieldEmitter0..2 (material "Field": heads + the ring strip),
///                      Beacon (mesh), DomeLight (holds "DomeOmni"), SparkPoint. Footprint radius ~8.
///                      The 2-tile containment field itself is the view's shader shell, not part of this model.
///   life_support_plant Fan0/1 (spin about local Y), Tank0..2 (Tank0 O2, Tank1 N2, Tank2 O2), Vent0/1 (steam points),
///                      material "Status" (the control board; green / amber / red), Beacon (mesh), PlantLight (holds
///                      "PlantOmni"), SparkPoint, scale pivots Stacks / Masts. Footprint radius ~7.
/// </summary>
public static partial class BuildingModels
{
    public const string PressureDomePath = "res://models/buildings/pressure_dome.glb";
    public const string LifeSupportPath = "res://models/buildings/life_support_plant.glb";
    public const string SealCollarPath = "res://models/buildings/seal_collar.glb";

    /// <summary>Gas in each Life Support tank, as painted on its LabelTank marker.</summary>
    public static readonly string[] LifeSupportGases = { "O2", "N2", "O2" };

    private static readonly Color DomeFieldColour = new(0.35f, 0.78f, 1f);

    /// <summary>Pressure Dome: geodesic glass dome on a ring foundation, two airlocks, three field emitters, a garden
    /// inside. The glass gets <see cref="DomeGlassMaterial"/> (the glb's import may be close, but this is exact).</summary>
    public static Node3D PressureDome(Color owner, Look v)
    {
        RegisterHostileDressings();
        if (ImportedFile(PressureDomePath, "pressure_dome", owner, Pieces(9)) is not { } n) return PrimitivePressureDome(owner, v);
        OwnMaterials(n);
        GlassUp(n);
        foreach (var k in new[] { 0, 1, 2 }) ScaleY(n, $"FieldEmitter{k}", v.Height);
        if (n.FindChild("DomeLight", true, false) is Node3D at)
            at.AddChild(new OmniLight3D { Name = "DomeOmni", LightColor = new Color(1f, 0.86f, 0.66f), LightEnergy = 1.1f, OmniRange = 9f, ShadowEnabled = false });
        return n;
    }

    /// <summary>Life Support Plant: compressor hall with two roof fans, three scrubber towers, heat exchangers, three
    /// gas tanks, the control kiosk with the Status board, supply pipes into the ground.</summary>
    public static Node3D LifeSupportPlant(Color owner, Look v)
    {
        RegisterHostileDressings();
        if (ImportedFile(LifeSupportPath, "life_support_plant", owner, Pieces(11)) is not { } n) return PrimitiveLifeSupport(owner, v);
        OwnMaterials(n);
        ScaleY(n, "Stacks", v.Height);
        ScaleY(n, "Masts", v.Height3);
        if (n.FindChild("PlantLight", true, false) is Node3D at)
            at.AddChild(new OmniLight3D { Name = "PlantOmni", LightColor = new Color(1f, 0.9f, 0.75f), LightEnergy = 0.8f, OmniRange = 12f, ShadowEnabled = false });
        return n;
    }

    /// <summary>A sealing collar: a small pressurised vestibule (lit door, owner band) to stand against a crewed
    /// building's door on a Domes world. Front (door) = +Z, its back (y 0..2.9, z -1.4) meets the wall. The glb if it
    /// was exported (blender/build_seal_collar.py), else primitives. Parts: "CollarDoor" (slides along local X),
    /// material "DoorLight". Not placed by Dress yet.</summary>
    public static Node3D SealCollar(Color owner)
    {
        if (ImportedFile(SealCollarPath, "seal_collar", owner) is { } n)
        {
            OwnMaterials(n);
            return n;
        }
        var c = new Node3D { Name = "seal_collar" };
        var hull = Named(Metal(HullColour, 0.3f, 0.45f), "Hull");
        var trim = Named(Metal(Trim, 0.4f, 0.5f), "Trim");
        Vestibule(c, hull, trim, owner, "Collar", 2.6f);
        return c;
    }

    /// <summary>The dome glass's and frost band's materials (shared look; the view may tint copies).</summary>
    public static StandardMaterial3D DomeGlassMaterial(bool frost = false) => new()
    {
        ResourceName = frost ? "GlassFrost" : "Glass",
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = frost ? new Color(0.86f, 0.93f, 1f, 0.42f) : new Color(0.62f, 0.78f, 0.92f, 0.13f),
        Metallic = 0.15f,
        MetallicSpecular = 0.9f,
        Roughness = frost ? 0.55f : 0.04f,
        RimEnabled = true,
        Rim = frost ? 0.25f : 0.55f,
        RimTint = 0.35f,
        // a faint self-glow so the facets don't go black on the night side (and Variety's Paint leaves it alone)
        EmissionEnabled = true,
        Emission = new Color(0.55f, 0.75f, 1f),
        EmissionEnergyMultiplier = frost ? 0.12f : 0.06f,
        CullMode = BaseMaterial3D.CullModeEnum.Back,
    };

    /// <summary>Put the see-through materials on every "Glass"/"GlassFrost" surface under DomeGlass, no shadows.</summary>
    private static void GlassUp(Node3D n)
    {
        if (n.FindChild("DomeGlass", true, false) is not MeshInstance3D glass || glass.Mesh == null) return;
        glass.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        for (var i = 0; i < glass.Mesh.GetSurfaceCount(); i++)
        {
            var name = (glass.GetSurfaceOverrideMaterial(i) ?? glass.Mesh.SurfaceGetMaterial(i))?.ResourceName ?? "";
            glass.SetSurfaceOverrideMaterial(i, DomeGlassMaterial(name == "GlassFrost"));
        }
    }

    /// <summary>The variety entries (Dress returns early for ids without one); the glbs mark their own spots and signs.</summary>
    private static void RegisterHostileDressings()
    {
        Dressings.TryAdd("pressure_dome", new("DOME", 7.5f, 7.5f, System.Array.Empty<Vector3>(), Vector3.Zero, 0f, 0f, false));
        Dressings.TryAdd("life_support_plant", new("LIFE SUPPORT", 6.5f, 6.5f, System.Array.Empty<Vector3>(), Vector3.Zero, 0f, 0f, false));
    }

    /// <summary>Signs on the hostile glbs' markers (called from <see cref="PaintDetailedSigns"/>).</summary>
    private static void PaintHostileSigns(Node3D n, string defId, string number)
    {
        if (defId == "pressure_dome")
        {
            PaintMarker(n, "SignFront", $"DOME {number}", SignInk);
            PaintMarker(n, "LabelLock1", "AIRLOCK 2", SignInk);
        }
        else if (defId == "life_support_plant")
        {
            PaintMarker(n, "SignFront", $"LIFE SUPPORT {number}", SignInk);
            for (var k = 0; k < LifeSupportGases.Length; k++) PaintMarker(n, $"LabelTank{k}", LifeSupportGases[k], PlateInk);
        }
    }

    // ---------------------------------------------------------------- primitive stand-ins (same part names)

    private static T Named<T>(T m, string name) where T : Material
    {
        m.ResourceName = name;
        return m;
    }

    private static Node3D Group(Node3D parent, string name, Vector3? at = null)
    {
        var g = new Node3D { Name = name, Position = at ?? Vector3.Zero };
        parent.AddChild(g);
        return g;
    }

    /// <summary>A marker the way the glbs carry them: Godot scale (width, 1, height), text normal local +Y, here
    /// turned to face +Z of `yawDeg`.</summary>
    private static void Marker(Node3D parent, string name, Vector3 at, float yawDeg, float width, float height)
    {
        var m = new Node3D { Name = name, Position = at, RotationDegrees = new Vector3(90f, yawDeg, 0f) };
        m.Scale = new Vector3(width, 1f, height);
        parent.AddChild(m);
    }

    /// <summary>A spot empty (AddOnSpot/RoofSpot): an add-on there faces its local +Z.</summary>
    private static void Spot(Node3D parent, string name, Vector3 at, float yawDeg) =>
        parent.AddChild(new Node3D { Name = name, Position = at, RotationDegrees = new Vector3(0f, yawDeg, 0f) });

    /// <summary>Godot position of a point at `deg` round the centre (Blender's angle: 0 = +X, 90 = back = -Z).</summary>
    private static Vector3 Round(float deg, float r, float y = 0f)
    {
        var a = Mathf.DegToRad(deg);
        return new Vector3(r * Mathf.Cos(a), y, -r * Mathf.Sin(a));
    }

    /// <summary>A vestibule (airlock / sealing collar) in `parent`'s frame: door at +Z, body back to z -depth.</summary>
    private static void Vestibule(Node3D parent, Material hull, Material trim, Color owner, string prefix, float depth)
    {
        var doorLight = Named(Glow(new Color(1f, 0.8f, 0.5f), 4f), "DoorLight");
        var ownerMat = Named(Glow(owner, 0.35f), "Owner");
        var cz = 1.25f - depth / 2f;
        Add(parent, new BoxMesh { Size = new Vector3(2.5f, 1.65f, depth) }, new Vector3(0, 1.02f, cz), hull);
        Add(parent, new CylinderMesh { TopRadius = 1.25f, BottomRadius = 1.25f, Height = depth, RadialSegments = 16 }, new Vector3(0, 1.85f, cz), hull, new Vector3(90, 0, 0));
        Add(parent, new BoxMesh { Size = new Vector3(2.53f, 1.45f, 0.14f) }, new Vector3(0, 1.02f, 0.9f), ownerMat);
        Add(parent, new BoxMesh { Size = new Vector3(1.6f, 2.25f, 0.12f) }, new Vector3(0, 1.32f, 1.3f), trim);
        var door = Add(parent, new BoxMesh { Size = new Vector3(1.12f, 1.95f, 0.08f) }, new Vector3(0, 1.2f, 1.38f), Named(Metal(new Color(0.62f, 0.65f, 0.7f), 0.4f, 0.4f), "DoorPanel"));
        door.Name = $"{prefix}Door";
        foreach (var x in new[] { -0.62f, 0.62f })
            Add(parent, new BoxMesh { Size = new Vector3(0.07f, 2f, 0.05f) }, new Vector3(x, 1.25f, 1.42f), doorLight);
        Add(parent, new BoxMesh { Size = new Vector3(1.3f, 0.07f, 0.05f) }, new Vector3(0, 2.28f, 1.42f), doorLight);
        Add(parent, new BoxMesh { Size = new Vector3(2.2f, 0.42f, 0.12f) }, new Vector3(0, 2.7f, 1.31f), trim);
    }

    private static Node3D PrimitivePressureDome(Color owner, Look v)
    {
        var n = new Node3D { Name = "pressure_dome" };
        var hull = Named(Metal(HullColour, 0.15f, 0.42f), "Hull");
        var trim = Named(Metal(Trim, 0.4f, 0.5f), "Trim");
        var frame = Named(Metal(new Color(0.32f, 0.34f, 0.38f), 0.55f, 0.4f), "Frame");
        var field = Named(Glow(DomeFieldColour, 5f), "Field");
        var lamp = Named(Glow(new Color(1f, 0.95f, 0.85f), 3f), "Lamp");
        var ownerMat = Named(Glow(owner, 0.35f), "Owner");
        // Build0 foundation: apron, ring wall, owner band, the field strip
        var b0 = Group(n, "Build0");
        Add(b0, new CylinderMesh { TopRadius = 8f, BottomRadius = 8f, Height = 0.1f, RadialSegments = 32 }, new Vector3(0, 0.05f, 0), Named(Metal(new Color(0.4f, 0.39f, 0.37f), 0f, 0.85f), "Concrete"));
        Add(b0, new CylinderMesh { TopRadius = 7.35f, BottomRadius = 7.55f, Height = 0.9f, RadialSegments = 48 }, new Vector3(0, 0.45f, 0), hull);
        Add(b0, new CylinderMesh { TopRadius = 7.57f, BottomRadius = 7.57f, Height = 0.16f, RadialSegments = 48 }, new Vector3(0, 0.22f, 0), ownerMat);
        for (var k = 0; k < 5; k++) Spot(b0, $"AddOnSpot{k}", Round(k * 60f, 8.2f), k * 60f - 90f);
        // Build1 garden: deck, walkway, planters, a tree
        var b1 = Group(n, "Build1");
        Add(b1, new CylinderMesh { TopRadius = 6.75f, BottomRadius = 6.75f, Height = 0.1f, RadialSegments = 32 }, new Vector3(0, 0.91f, 0), Named(Metal(new Color(0.5f, 0.52f, 0.55f), 0.15f, 0.6f), "Deck"));
        Add(b1, new BoxMesh { Size = new Vector3(1.4f, 0.04f, 13f) }, new Vector3(0, 0.98f, 0), Named(Metal(new Color(0.72f, 0.71f, 0.68f), 0f, 0.7f), "Walkway"));
        var leaf = Named(Metal(new Color(0.18f, 0.42f, 0.15f), 0f, 0.7f), "Leaf");
        foreach (var a in new[] { 0f, 45f, 135f, 180f, 225f, 315f })
            Add(b1, new BoxMesh { Size = new Vector3(1.5f, 0.6f, 0.8f) }, Round(a, 4.8f, 1.25f), leaf, new Vector3(0, a - 90f, 0));
        Add(b1, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.08f, Height = 1.6f, RadialSegments = 6 }, new Vector3(0, 1.75f, 0), Named(Metal(new Color(0.25f, 0.17f, 0.11f), 0f, 0.85f), "Bark"));
        Add(b1, new SphereMesh { Radius = 0.9f, Height = 2f, RadialSegments = 10, Rings = 6 }, new Vector3(0, 3f, 0), leaf);
        // Build2 frame: base hoop, three meridian rings, two latitude hoops, crown + mast
        var b2 = Group(n, "Build2");
        var domeFrame = Group(b2, "DomeFrame");
        foreach (var a in new[] { 0f, 60f, 120f })
        {
            var rib = Add(domeFrame, new TorusMesh { InnerRadius = 6.92f, OuterRadius = 7.08f, Rings = 40, RingSegments = 4 }, new Vector3(0, 0.9f, 0), frame);
            rib.Basis = new Basis(Vector3.Up, Mathf.DegToRad(a)) * new Basis(Vector3.Right, Mathf.Pi / 2f) * Basis.FromScale(new Vector3(1f, 1f, 5.9f / 7f));
        }
        foreach (var (y, r) in new[] { (0.95f, 7f), (3.3f, 6.25f), (5.4f, 4.3f) })
            Add(domeFrame, new TorusMesh { InnerRadius = r - 0.08f, OuterRadius = r + 0.08f, Rings = 40, RingSegments = 4 }, new Vector3(0, y, 0), frame);
        Add(domeFrame, new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.28f, RadialSegments = 12 }, new Vector3(0, 6.82f, 0), trim);
        Add(domeFrame, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.6f, RadialSegments = 6 }, new Vector3(0, 7.25f, 0), trim);
        // Build3 airlocks (front and back-left; local +Z out of the dome)
        var b3 = Group(n, "Build3");
        foreach (var (k, a) in new[] { (0, -90f), (1, 150f) })
        {
            var lockNode = Group(b3, $"Airlock{k}", Round(a, 7.15f));
            lockNode.RotationDegrees = new Vector3(0, a + 90f, 0);
            Vestibule(lockNode, hull, trim, owner, $"Airlock{k}", 2.75f);
            if (k == 0) Marker(lockNode, "SignFront", new Vector3(0, 2.7f, 1.38f), 0f, 2f, 0.26f);
            else Marker(lockNode, "LabelLock1", new Vector3(0, 2.7f, 1.38f), 0f, 2f, 0.22f);
            Spot(lockNode, $"RoofSpot{k}", new Vector3(-0.4f, 3.1f, 0.3f), 0f);
        }
        // Build4 field emitters
        var b4 = Group(n, "Build4");
        foreach (var (k, a) in new[] { (0, 90f), (1, 210f), (2, -30f) })
        {
            var em = Group(b4, $"FieldEmitter{k}", Round(a, 7.85f));
            Add(em, new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.22f, Height = 3.2f, RadialSegments = 8 }, new Vector3(0, 1.6f, 0), hull);
            Add(em, new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.6f, RadialSegments = 10 }, new Vector3(0, 3.65f, 0), field);
            Add(em, new CylinderMesh { TopRadius = 0.24f, BottomRadius = 0.24f, Height = 0.13f, RadialSegments = 10 }, new Vector3(0, 4.02f, 0), trim);
            em.Scale = new Vector3(1f, v.Height, 1f);
        }
        // Build5 glass (+ a frosted band at the foot)
        var b5 = Group(n, "Build5");
        var glass = Add(b5, new SphereMesh { Radius = 6.95f, Height = 11.8f, IsHemisphere = true, RadialSegments = 32, Rings = 12 }, new Vector3(0, 0.95f, 0), DomeGlassMaterial());
        glass.Name = "DomeGlass";
        glass.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var frost = Add(b5, new CylinderMesh { TopRadius = 6.9f, BottomRadius = 6.97f, Height = 0.5f, RadialSegments = 32, CapTop = false, CapBottom = false }, new Vector3(0, 1.2f, 0), DomeGlassMaterial(true));
        frost.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        // Build6 lights: field strip, lamp posts, beacon, the dome light
        var b6 = Group(n, "Build6");
        Add(b6, new CylinderMesh { TopRadius = 7.47f, BottomRadius = 7.47f, Height = 0.08f, RadialSegments = 48, CapTop = false, CapBottom = false }, new Vector3(0, 0.64f, 0), field);
        foreach (var z in new[] { -4.5f, -2f, 2f, 4.5f })
            Add(b6, new BoxMesh { Size = new Vector3(0.2f, 0.06f, 0.2f) }, new Vector3(1f, 2.9f, z), lamp);
        var beacon = Add(b6, new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 10, Rings = 5 }, new Vector3(0, 7.65f, 0), Named(Glow(new Color(1f, 0.08f, 0.05f), 8f), "Beacon"));
        beacon.Name = "Beacon";
        var light = Group(b6, "DomeLight", new Vector3(0, 3.4f, 0));
        light.AddChild(new OmniLight3D { Name = "DomeOmni", LightColor = new Color(1f, 0.86f, 0.66f), LightEnergy = 1.1f, OmniRange = 9f, ShadowEnabled = false });
        Group(n, "SparkPoint", Round(30f, 8f, 1.3f));
        return n;
    }

    private static Node3D PrimitiveLifeSupport(Color owner, Look v)
    {
        var n = new Node3D { Name = "life_support_plant" };
        var hull = Named(Metal(HullColour, 0.15f, 0.42f), "Hull");
        var panel = Named(Metal(new Color(0.56f, 0.58f, 0.62f), 0.2f, 0.5f), "Panel");
        var trim = Named(Metal(Trim, 0.4f, 0.5f), "Trim");
        var ownerMat = Named(Glow(owner, 0.35f), "Owner");
        // Build0 pad
        var b0 = Group(n, "Build0");
        var pad = Add(b0, new CylinderMesh { TopRadius = 7f, BottomRadius = 7f, Height = 0.3f, RadialSegments = 8 }, new Vector3(0, 0.15f, 0), Named(Metal(new Color(0.4f, 0.39f, 0.37f), 0f, 0.85f), "Concrete"));
        pad.RotationDegrees = new Vector3(0, 22.5f, 0);
        foreach (var (k, x, z, yaw) in new[] { (0, -7.5f, -2.4f, 90f), (1, 7.5f, -1.2f, -90f), (2, -2.2f, -7.4f, 0f), (3, 2.6f, -7.4f, 0f), (4, -6.3f, 3.9f, 60f) })
            Spot(b0, $"AddOnSpot{k}", new Vector3(x, 0, z), yaw);
        // Build1 compressor hall + roof fans
        var b1 = Group(n, "Build1");
        Add(b1, new BoxMesh { Size = new Vector3(5.2f, 2.9f, 3.6f) }, new Vector3(-2.3f, 1.75f, -2.3f), hull);
        Add(b1, new BoxMesh { Size = new Vector3(5.22f, 0.12f, 3.62f) }, new Vector3(-2.3f, 2.65f, -2.3f), ownerMat);
        Add(b1, new BoxMesh { Size = new Vector3(1.8f, 0.5f, 0.05f) }, new Vector3(-0.45f, 1.95f, -0.48f), Named(Glow(new Color(1f, 0.82f, 0.55f), 3f), "Window"));
        foreach (var (k, x) in new[] { (0, -3.55f), (1, -1.05f) })
        {
            Add(b1, new CylinderMesh { TopRadius = 1.02f, BottomRadius = 1.02f, Height = 0.4f, RadialSegments = 20, CapTop = false }, new Vector3(x, 3.4f, -2.35f), hull);
            var fan = Group(b1, $"Fan{k}", new Vector3(x, 3.5f, -2.35f));
            Add(fan, new BoxMesh { Size = new Vector3(1.6f, 0.03f, 0.22f) }, Vector3.Zero, panel);
            Add(fan, new BoxMesh { Size = new Vector3(0.22f, 0.03f, 1.6f) }, Vector3.Zero, panel);
        }
        // Build2 scrubbers (Stacks pivot, Vent0/1 on the first two)
        var b2 = Group(n, "Build2");
        var stacks = Group(b2, "Stacks", new Vector3(0, 0.3f, 0));
        foreach (var (k, x, z, top) in new[] { (0, 1.5f, -3.2f, 7.6f), (1, 3.2f, -3.35f, 8.4f), (2, 4.9f, -3f, 7f) })
        {
            Add(b2, new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.78f, Height = 3.4f, RadialSegments = 16 }, new Vector3(x, 2f, z), hull);
            Add(stacks, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.34f, Height = top - 3.8f, RadialSegments = 12 }, new Vector3(x, (3.5f + top - 0.3f) / 2f, z), panel);
            if (k < 2) Group(stacks, $"Vent{k}", new Vector3(x, top + 0.2f, z));
        }
        stacks.Scale = new Vector3(1f, v.Height, 1f);
        // Build3 tanks
        var b3 = Group(n, "Build3");
        for (var k = 0; k < 3; k++)
        {
            var (x, z) = k switch { 0 => (-0.45f, 3.3f), 1 => (1.35f, 3.65f), _ => (3.15f, 3.3f) };
            var tank = Group(b3, $"Tank{k}", new Vector3(x, 0, z));
            Add(tank, new CylinderMesh { TopRadius = 0.64f, BottomRadius = 0.64f, Height = 2f, RadialSegments = 16 }, new Vector3(0, 1.8f, 0), hull);
            Add(tank, new SphereMesh { Radius = 0.64f, Height = 0.9f, IsHemisphere = true, RadialSegments = 16, Rings = 4 }, new Vector3(0, 2.8f, 0), hull);
            var gas = LifeSupportGases[k] == "O2" ? Named(Metal(new Color(0.18f, 0.4f, 0.78f), 0.2f, 0.45f), "TankO2") : Named(Metal(new Color(0.13f, 0.14f, 0.16f), 0.2f, 0.5f), "TankN2");
            Add(tank, new CylinderMesh { TopRadius = 0.65f, BottomRadius = 0.65f, Height = 0.55f, RadialSegments = 16, CapTop = false, CapBottom = false }, new Vector3(0, 2.4f, 0), gas);
            Marker(tank, $"LabelTank{k}", new Vector3(0, 1.85f, 0.7f), 0f, 0.7f, 0.32f);
        }
        // Build4 control kiosk with the Status board, the sign, the supply pipe into the ground
        var b4 = Group(n, "Build4");
        Add(b4, new BoxMesh { Size = new Vector3(2.4f, 2.3f, 1.7f) }, new Vector3(-3.95f, 1.45f, 2.3f), hull);
        Add(b4, new BoxMesh { Size = new Vector3(1.4f, 0.3f, 0.04f) }, new Vector3(-3.85f, 1.35f, 3.17f), Named(Glow(new Color(0.3f, 1f, 0.5f), 3f), "Status"));
        Marker(b4, "SignFront", new Vector3(-3.95f, 2.65f, 3.2f), 0f, 2.5f, 0.2f);
        Spot(b4, "RoofSpot0", new Vector3(-4.35f, 2.75f, 2.1f), 0f);
        Spot(b4, "RoofSpot1", new Vector3(-3.9f, 3.2f, -0.95f), 0f);
        var pipe = Named(Metal(new Color(0.78f, 0.76f, 0.7f), 0f, 0.7f), "Insulation");
        Add(b4, new BoxMesh { Size = new Vector3(0.24f, 0.24f, 4.2f) }, new Vector3(-1.6f, 0.65f, 3.8f), pipe);
        Add(b4, new BoxMesh { Size = new Vector3(1.8f, 0.05f, 1.1f) }, new Vector3(-1.6f, 0.31f, 5.9f), trim);
        // Build5 lights: antenna + beacon, the plant light
        var b5 = Group(n, "Build5");
        Add(b5, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 2.6f, RadialSegments = 6 }, new Vector3(0, 4.5f, -3.75f), trim);
        var beacon = Add(b5, new SphereMesh { Radius = 0.1f, Height = 0.2f, RadialSegments = 10, Rings = 5 }, new Vector3(0, 5.9f, -3.75f), Named(Glow(new Color(1f, 0.08f, 0.05f), 8f), "Beacon"));
        beacon.Name = "Beacon";
        var light = Group(b5, "PlantLight", new Vector3(-0.6f, 3f, 1f));
        light.AddChild(new OmniLight3D { Name = "PlantOmni", LightColor = new Color(1f, 0.9f, 0.75f), LightEnergy = 0.8f, OmniRange = 12f, ShadowEnabled = false });
        Group(n, "SparkPoint", new Vector3(4.1f, 1.2f, 1.4f));
        return n;
    }
}
