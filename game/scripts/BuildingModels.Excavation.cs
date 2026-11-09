using Godot;

namespace FinalFrontier;

/// <summary>
/// The Excavation Site (civilizations phase 2, docs/civilizations-phase2-contract.md "Models"):
/// models/buildings/excavation_site.glb from blender/build_excavation_site.py --game (a stepped dig cut into a mesa of
/// its own spoil, the exposed alien ruin at the bottom with a defence pylon, a gantry crane over the pit, two sifting
/// screens, a survey hut with the status board, a finds table, lamp masts; 11 build pieces, one baked grime atlas), or
/// a primitive stand-in with the same part names when the glb isn't there or imported yet. <see cref="PlanetView"/>
/// animates it from the dig (PlanetView.Excavation.cs).
///
/// Parts the view drives (FindChild, wherever they sit):
///   Crane               gantry: Position lerps between its siblings CraneMin / CraneMax (front-back over the pit).
///   Hook (+ HookLow)    the skip under the crane: Position from its rest (up) to the sibling HookLow (in the pit).
///   Cable               the hoist ropes: unit length 0..-1 along local Y, Scale.Y = the drop to the hook.
///   Sieve0, Sieve1      sifting screens: shake along their local X.
///   Dust0..2            dust puff points (under the sieves, at the bottom of the dig).
///   SparkPoint          tip of the ruin's defence pylon (sparks while the dig is locked); holds "GlowOmni".
///   DigLight            holds "DigOmni" (floodlight over the pit).
///   materials           Status (hut board), Glow (ruin inlays + the pylon), Lamp, Window, Beacon.
/// Footprint: mesa r ~6.5 on a skirt to r ~7.2; front (+Z) = the access side with the steps and the sign.
/// </summary>
public static partial class BuildingModels
{
    public const string ExcavationPath = "res://models/buildings/excavation_site.glb";

    private static Node3D ExcavationSite(Color owner, Look v)
    {
        Dressings.TryAdd("excavation_site", new("EXCAVATION", 6.4f, 6.4f, System.Array.Empty<Vector3>(), Vector3.Zero, 0f, 0f, false));
        if (ImportedFile(ExcavationPath, "excavation_site", owner, Pieces(11)) is not { } n) return PrimitiveExcavation(owner, v);
        OwnMaterials(n);
        ScaleY(n, "Masts", v.Height3);
        Optional(n, "OptAwning", v.IsPlain || v.Chance(0.7f));
        Optional(n, "OptGenerator", v.IsPlain || v.Chance(0.65f));
        Optional(n, "OptBarrow", v.IsPlain || v.Chance(0.6f));
        // where the crane happens to stand when it's built (the view parks it here)
        if (!v.IsPlain && n.FindChild("Crane", true, false) is Node3D crane
            && crane.GetParent()?.GetNodeOrNull<Node3D>("CraneMin") is { } c0 && crane.GetParent()?.GetNodeOrNull<Node3D>("CraneMax") is { } c1)
            crane.Position = c0.Position.Lerp(c1.Position, v.Range(0.2f, 0.8f));
        AddExcavationLights(n);
        return n;
    }

    private static void AddExcavationLights(Node3D n)
    {
        if (n.FindChild("DigLight", true, false) is Node3D dig && dig.GetNodeOrNull("DigOmni") == null)
            dig.AddChild(new OmniLight3D { Name = "DigOmni", LightColor = new Color(1f, 0.9f, 0.75f), LightEnergy = 1.1f, OmniRange = 9f, ShadowEnabled = false });
        if (n.FindChild("SparkPoint", true, false) is Node3D spark && spark.GetNodeOrNull("GlowOmni") == null)
            spark.AddChild(new OmniLight3D { Name = "GlowOmni", LightColor = new Color(0.4f, 1f, 0.9f), LightEnergy = 0f, OmniRange = 4.5f, ShadowEnabled = false });
    }

    /// <summary>The signs on the glb's markers (called from <see cref="PaintDetailedSigns"/>).</summary>
    private static void PaintExcavationSigns(Node3D n, string number)
    {
        PaintMarker(n, "SignFront", $"EXCAVATION {number}", SignInk);
        PaintMarker(n, "LabelFinds", "FINDS", PlateInk);
        PaintMarker(n, "LabelHut", "SURVEY", PlateInk);
    }

    // ---------------------------------------------------------------- primitive stand-in (same part names)

    private static Node3D PrimitiveExcavation(Color owner, Look v)
    {
        // heights from the Blender script: mesa top 1.4, terraces 0.95 / 0.5, bottom 0.06; pit x -4.4..1.8, y -2.8..2.6
        const float surf = 1.4f, bottom = 0.06f;
        var n = new Node3D { Name = "excavation_site" };
        var soil = Named(Metal(new Color(0.44f, 0.34f, 0.24f), 0f, 0.95f), "Soil");
        var deep = Named(Metal(new Color(0.25f, 0.18f, 0.13f), 0f, 0.95f), "SoilDeep");
        var stone = Named(Metal(new Color(0.66f, 0.65f, 0.62f), 0f, 0.85f), "RuinStone");
        var rmetal = Named(Metal(new Color(0.36f, 0.31f, 0.24f), 0.75f, 0.42f), "RuinMetal");
        var hull = Named(Metal(HullColour, 0.15f, 0.42f), "Hull");
        var trim = Named(Metal(Trim, 0.4f, 0.5f), "Trim");
        var accent = Named(Metal(new Color(0.14f, 0.3f, 0.55f), 0.3f, 0.45f), "Accent");
        var hazard = Named(Metal(new Color(0.85f, 0.62f, 0.08f), 0f, 0.55f), "Hazard");
        var wood = Named(Metal(new Color(0.46f, 0.36f, 0.25f), 0f, 0.85f), "Wood");
        var ownerMat = Named(Glow(owner, 0.35f), "Owner");
        var status = Named(Glow(new Color(0.3f, 1f, 0.5f), 3f), "Status");
        var glow = Named(Glow(new Color(0.35f, 0.95f, 0.85f), 1.2f), "Glow");
        var lamp = Named(Glow(new Color(1f, 0.95f, 0.85f), 4f), "Lamp");
        var window = Named(Glow(new Color(1f, 0.82f, 0.55f), 2.5f), "Window");

        // Build0 the spoil mesa round the pit (four blocks), its floor, the steps; the variety spots
        var b0 = Group(n, "Build0");
        BoxBl(b0, -5.4f, -4.4f, -5f, 5f, 0f, surf, soil);
        BoxBl(b0, 1.8f, 5.4f, -5f, 5f, 0f, surf, soil);
        BoxBl(b0, -4.4f, 1.8f, -5f, -2.8f, 0f, surf, soil);
        BoxBl(b0, -4.4f, 1.8f, 2.6f, 5f, 0f, surf, soil);
        BoxBl(b0, -4.4f, 1.8f, -2.8f, 2.6f, 0f, bottom, deep);
        BoxBl(b0, -4.4f, -3.9f, -2.8f, 2.6f, 0f, 0.95f, deep);
        BoxBl(b0, 1.3f, 1.8f, -2.8f, 2.6f, 0f, 0.95f, deep);
        BoxBl(b0, 0.55f, 1.95f, -6.2f, -5f, 0f, surf * 0.5f, wood);
        foreach (var (k, x, y, yaw) in new[] { (0, -7.45f, -0.8f, 90f), (1, -6.9f, 3.25f, 90f), (2, -2.45f, 7.2f, 0f), (3, 2.7f, 7.05f, 0f), (4, 7.4f, -1.25f, -90f) })
            Spot(b0, $"AddOnSpot{k}", Bl(x, y, 0f), yaw);

        // Build1 the ruin at the bottom: wall blocks, column stumps, the defence pylon with its glow
        var b1 = Group(n, "Build1");
        foreach (var (x0, x1, h) in new[] { (-3.2f, -2.3f, 0.8f), (-2.2f, -1.2f, 1.3f), (-1.1f, -0.1f, 0.5f), (0f, 0.8f, 1.1f) })
            BoxBl(b1, x0, x1, 1.0f, 1.4f, bottom, bottom + h, stone);
        foreach (var (x, y, h) in new[] { (-2.9f, 0.55f, 0.85f), (-1.75f, 0.6f, 1.3f) })
            Shape(b1, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.3f, Height = h, RadialSegments = 8 }, Bl(x, y, bottom + h / 2f), stone);
        Shape(b1, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.4f, Height = 1.6f, RadialSegments = 6 }, Bl(-0.35f, 0.15f, bottom + 0.8f), stone);
        Shape(b1, new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.08f, RadialSegments = 6 }, Bl(-0.35f, 0.15f, bottom + 0.75f), rmetal);
        Shape(b1, new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 6, Rings = 3 }, Bl(-0.35f, 0.15f, bottom + 1.62f), glow);
        Group(b1, "SparkPoint", Bl(-0.35f, 0.15f, bottom + 1.68f));
        Group(b1, "Dust2", Bl(-1.6f, -0.3f, bottom + 0.1f));

        // Build2 the crane on its rails: gantry (Crane), hoist ropes (Cable), the skip (Hook)
        var b2 = Group(n, "Build2");
        foreach (var x in new[] { -4.8f, 2.2f })
            BoxBl(b2, x - 0.05f, x + 0.05f, -3.15f, 2.95f, surf + 0.08f, surf + 0.16f, trim);
        Group(b2, "CraneMin", Bl(-1.3f, -1.4f, surf));
        Group(b2, "CraneMax", Bl(-1.3f, 1.2f, surf));
        var crane = Group(b2, "Crane", Bl(-1.3f, -0.1f, surf));
        foreach (var x in new[] { -3.5f, 3.5f })
            Shape(crane, new BoxMesh { Size = new Vector3(0.18f, 3.45f, 0.3f) }, new Vector3(x, 1.725f, 0f), hull);
        Shape(crane, new BoxMesh { Size = new Vector3(7.2f, 0.42f, 0.36f) }, new Vector3(0f, 3.57f, 0f), hazard);
        Shape(crane, new BoxMesh { Size = new Vector3(0.62f, 0.4f, 0.55f) }, new Vector3(0f, 3.23f, 0f), accent);
        Shape(crane, new BoxMesh { Size = new Vector3(0.64f, 0.08f, 0.57f) }, new Vector3(0f, 3.35f, 0f), ownerMat);
        var cable = Group(crane, "Cable", new Vector3(0f, 3.03f, 0f));
        Shape(cable, new BoxMesh { Size = new Vector3(0.04f, 1f, 0.04f) }, new Vector3(0f, -0.5f, 0f), Named(Metal(new Color(0.05f, 0.05f, 0.055f), 0.6f, 0.5f), "Cable"));
        cable.Scale = new Vector3(1f, 2.26f, 1f);
        var hook = Group(crane, "Hook", new Vector3(0f, 0.35f, 0f));
        Shape(hook, new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.22f, Height = 0.5f, RadialSegments = 8 }, new Vector3(0f, -0.25f, 0f), Named(Metal(new Color(0.82f, 0.32f, 0.1f), 0.1f, 0.5f), "Bucket"));
        Shape(hook, new BoxMesh { Size = new Vector3(0.3f, 0.22f, 0.16f) }, new Vector3(0f, 0.32f, 0f), hazard);
        Group(crane, "HookLow", new Vector3(0f, bottom + 0.62f - surf, 0f));
        Group(b2, "DigLight", Bl(-1.3f, -0.1f, surf + 2.6f));

        // Build3 the sieves on their frames
        var b3 = Group(n, "Build3");
        foreach (var (k, x, y) in new[] { (0, -3.25f, 3.95f), (1, -1.05f, 4.05f) })
        {
            foreach (var s in new[] { -0.85f, 0.85f })
                Shape(b3, new BoxMesh { Size = new Vector3(0.08f, 1.75f, 0.08f) }, Bl(x + s, y, surf + 0.875f), wood);
            Shape(b3, new BoxMesh { Size = new Vector3(1.8f, 0.08f, 0.08f) }, Bl(x, y, surf + 1.73f), wood);
            var sieve = Group(b3, $"Sieve{k}", Bl(x, y, surf + 0.85f));
            sieve.RotationDegrees = new Vector3(-12f, 0f, 0f);
            Shape(sieve, new BoxMesh { Size = new Vector3(1.25f, 0.14f, 0.8f) }, Vector3.Zero, wood);
            Group(b3, $"Dust{k}", Bl(x, y + 0.05f, surf + 0.55f));
        }

        // Build4 the survey hut with the status board, the beacon; the finds table; the sign
        var b4 = Group(n, "Build4");
        BoxBl(b4, 2.45f, 5f, 1.65f, 3.6f, surf, surf + 2.6f, hull);
        BoxBl(b4, 2.44f, 5.01f, 1.64f, 3.61f, surf + 2.18f, surf + 2.32f, ownerMat);
        BoxBl(b4, 3.98f, 4.77f, 1.565f, 1.59f, surf + 0.93f, surf + 1.13f, status);
        BoxBl(b4, 3.85f, 4.85f, 1.635f, 1.65f, surf + 1.45f, surf + 2.1f, window);
        Marker(b4, "LabelHut", Bl(4.3f, 1.585f, surf + 2.45f), 0f, 1f, 0.18f);
        var beacon = Shape(b4, new SphereMesh { Radius = 0.1f, Height = 0.2f, RadialSegments = 8, Rings = 4 }, Bl(4.65f, 3.25f, surf + 4.73f), Named(Glow(new Color(1f, 0.08f, 0.05f), 8f), "Beacon"));
        beacon.Name = "Beacon";
        Shape(b4, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 2.1f, RadialSegments = 6 }, Bl(4.65f, 3.25f, surf + 3.6f), trim);
        Spot(b4, "RoofSpot0", Bl(3.85f, 2.82f, surf + 2.6f), 0f);
        BoxBl(b4, 2.55f, 4.65f, -2.75f, -1.95f, surf + 0.8f, surf + 0.85f, wood);
        BoxBl(b4, 4.95f, 5.55f, -2.9f, -1.6f, surf, surf + 0.55f, Named(Metal(new Color(0.36f, 0.44f, 0.3f), 0.1f, 0.6f), "FindCrate"));
        Marker(b4, "LabelFinds", Bl(5.25f, -2.91f, surf + 0.28f), 0f, 0.46f, 0.13f);
        BoxBl(b4, -2.4f, 0.4f, -5.41f, -5.35f, surf + 0.35f, surf + 0.9f, trim);
        Marker(b4, "SignFront", Bl(-1f, -5.415f, surf + 0.62f), 0f, 2.5f, 0.3f);
        Spot(b4, "RoofSpot1", Bl(-5.55f, -1.6f, surf + 1.25f), 0f);

        // Build5 the lamp masts
        var b5 = Group(n, "Build5");
        var masts = Group(b5, "Masts", new Vector3(0f, surf, 0f));
        foreach (var (x, y, h) in new[] { (5.6f, 0.75f, 5.2f), (-5.25f, 2.6f, 5f) })
        {
            Shape(masts, new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = h, RadialSegments = 6 }, Bl(x, y, h / 2f), hull);
            Shape(masts, new BoxMesh { Size = new Vector3(0.5f, 0.15f, 0.3f) }, Bl(x, y, h), lamp);
        }
        masts.Scale = new Vector3(1f, v.Height3, 1f);

        AddExcavationLights(n);
        return n;
    }
}
