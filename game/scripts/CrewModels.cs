using System;
using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Crew figures (people), made by blender/build_crew.py --game → models/crew/&lt;role&gt;.glb (see blender/crew.md).
/// Not wired into the game yet: <see cref="Build"/> gives a figure, <see cref="Player"/> its AnimationPlayer and
/// <see cref="Play"/> plays an action and shows its prop.
///
/// Each glb holds the skinned figure on a Godot SkeletonProfileHumanoid-named skeleton ("Root", "Hips", "Spine", "Chest",
/// "UpperChest", "Neck", "Head", Left/Right "Shoulder", "UpperArm", "LowerArm", "Hand", "ThumbProximal", "ThumbDistal",
/// "IndexProximal", "IndexIntermediate", "MiddleProximal", "MiddleIntermediate", "UpperLeg", "LowerLeg", "Foot",
/// "Toes"), meshes "&lt;role&gt;" (hero, ~8-10k tris) and "&lt;role&gt;_far" (~1.2k) with blend shapes Feminine / Heavy /
/// Slim, hair styles HairCrop / HairShort / HairSwept / HairBun / HairCurly and Beard (one or none shown), props Tool,
/// Scanner, Crate (+ SampleCase on science, HelmetLamp on eva), marker nodes ToolTip / ScannerTip / LampPoint, and the
/// actions Idle, Walk, Run, Work, Carry, Scan, Wave (in place; looped here). Materials: "Owner" recoloured per player,
/// "Skin" and "Hair" tinted per variant, "Visor", "Lamp", "Screen", "Glow" emissive, "Crew" the shared palette atlas.
/// Front = -Z (Blender +Y), 1 unit = 1 m, origin between the feet; nominal height 1.76 (×0.93-1.06 per variant).
/// When a glb is missing or not imported yet, a primitive stand-in with the same part and action names is built.
/// </summary>
public static class CrewModels
{
    public const string Dir = "res://models/crew/";
    public static readonly string[] Roles = { "engineer", "science", "security", "eva", "colonist" };
    public static readonly string[] Actions = { "Idle", "Walk", "Run", "Work", "Carry", "Scan", "Wave" };
    public static readonly string[] Props = { "Tool", "Scanner", "Crate" };
    public static readonly string[] HairStyles = { "HairCrop", "HairShort", "HairSwept", "HairBun", "HairCurly" };

    /// <summary>Ground speeds (units per second at height 1) that keep the feet planted: the actions are in place.</summary>
    public const float WalkSpeed = 1.09f, CarrySpeed = 0.78f, RunSpeed = 2.92f;

    /// <summary>The hero mesh hands over to the far LOD at this camera distance (with a fade margin).</summary>
    public const float HeroRange = 60f, HeroRangeMargin = 8f;

    // keep in step with SKIN_TONES / HAIR_COLOURS / HAIR_WEIGHTS / STYLE_MASC / STYLE_FEM in blender/build_crew.py
    private static readonly Color[] SkinTones =
    {
        new(0.96f, 0.80f, 0.69f), new(0.92f, 0.74f, 0.60f), new(0.85f, 0.64f, 0.50f), new(0.76f, 0.55f, 0.40f),
        new(0.63f, 0.44f, 0.31f), new(0.52f, 0.34f, 0.23f), new(0.40f, 0.26f, 0.18f), new(0.28f, 0.18f, 0.13f),
    };
    private static readonly Color[] HairColours =
    {
        new(0.06f, 0.05f, 0.05f), new(0.17f, 0.11f, 0.07f), new(0.33f, 0.21f, 0.12f), new(0.48f, 0.24f, 0.12f),
        new(0.66f, 0.50f, 0.30f), new(0.85f, 0.72f, 0.48f), new(0.55f, 0.53f, 0.52f), new(0.86f, 0.85f, 0.83f),
    };
    private static readonly double[] HairWeights = { 5, 6, 5, 2, 2, 2, 1, 1 };
    private static readonly double[] StyleMasc = { 30, 35, 15, 0, 15 };
    private static readonly double[] StyleFem = { 5, 15, 20, 40, 20 };

    /// <summary>The look picked by a variant seed (the same hash and choices as build_crew.py's variant()).</summary>
    public readonly record struct CrewVariant(float Feminine, float Height, float Heavy, float Slim, int Skin, int Hair,
        string? Style, bool Beard)
    {
        public Color SkinColour => SkinTones[Skin];
        public Color HairColour => HairColours[Hair];
    }

    /// <summary>lowbias32 of seed · golden + k, in [0, 1): CrewModels and build_crew.py's h32 give the same bits.</summary>
    public static double Hash(int seed, int k)
    {
        unchecked
        {
            var x = (uint)seed * 0x9E3779B9u + (uint)k * 0x85EBCA6Bu + 0x632BE5ABu;
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x / 4294967296.0;
        }
    }

    private static int Pick(double[] weights, double r)
    {
        double total = 0, acc = 0;
        foreach (var w in weights) total += w;
        for (var i = 0; i < weights.Length; i++)
        {
            acc += weights[i] / total;
            if (r < acc) return i;
        }
        return weights.Length - 1;
    }

    public static CrewVariant Variant(int seed, string role)
    {
        var r = new double[10];
        for (var k = 0; k < r.Length; k++) r[k] = Hash(seed, k);
        var fem = r[0] < 0.45 ? 0.0 : r[0] < 0.9 ? 1.0 : 0.5;
        var height = fem < 0.5 ? 0.97 + 0.09 * r[1] : 0.93 + 0.08 * r[1];
        double heavy = 0, slim = 0;
        if (r[2] < 0.25) slim = 0.3 + 0.7 * r[3];
        else if (r[2] > 0.72) heavy = 0.3 + 0.7 * r[3];
        var skin = Math.Min(7, (int)(r[4] * 8));
        var hair = Pick(HairWeights, r[5]);
        var bald = fem < 0.5 && r[6] > 0.95;
        string? style = bald ? null : HairStyles[Pick(fem >= 0.5 ? StyleFem : StyleMasc, fem < 0.5 ? r[6] / 0.95 : r[6])];
        var beard = fem < 0.5 && r[7] < 0.25;
        if (role == "eva") (style, beard) = ("HairCrop", false);
        return new CrewVariant((float)fem, (float)height, (float)heavy, (float)slim, skin, hair, style, beard);
    }

    /// <summary>
    /// A crew figure of <paramref name="role"/> (one of <see cref="Roles"/>) in the player's colour, its look picked by
    /// <paramref name="variantSeed"/>: a plain Node3D wrapper (unit scale; the model inside carries the variant's
    /// height) with the glb or the primitive stand-in under it. Starts in Idle, props hidden.
    /// </summary>
    public static Node3D Build(string role, Color ownerColor, int variantSeed)
    {
        if (Array.IndexOf(Roles, role) < 0) role = "colonist";
        var v = Variant(variantSeed, role);
        var n = new Node3D { Name = "Crew_" + role };
        n.SetMeta("crew_role", role);
        n.SetMeta("crew_seed", variantSeed);
        var model = Imported(role, ownerColor, v) ?? Primitive(role, ownerColor, v);
        model.Scale = Vector3.One * v.Height;
        n.AddChild(model);
        var player = model.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
        if (player != null)
        {
            foreach (var name in Actions)
                if (player.HasAnimation(name)) player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
            n.SetMeta("crew_player", n.GetPathTo(player));
        }
        Play(n, "Idle", 0f);
        return n;
    }

    /// <summary>The figure's AnimationPlayer (actions <see cref="Actions"/>), or null.</summary>
    public static AnimationPlayer? Player(Node3D crew) =>
        crew.HasMeta("crew_player") ? crew.GetNodeOrNull<AnimationPlayer>(crew.GetMeta("crew_player").AsNodePath())
            : crew.FindChild("AnimationPlayer", true, false) as AnimationPlayer;

    /// <summary>Plays an action (blending over <paramref name="blend"/> s) and shows only that action's prop
    /// (Work → Tool, Scan → Scanner, Carry → Crate). Before the figure is in the tree the player starts on its first
    /// process frame instead.</summary>
    public static void Play(Node3D crew, string action, float blend = 0.2f)
    {
        var prop = action switch { "Work" => "Tool", "Scan" => "Scanner", "Carry" => "Crate", _ => null };
        foreach (var p in Props)
            if (crew.FindChild(p, true, false) is Node3D node) node.Visible = p == prop;
        if (Player(crew) is not { } player || !player.HasAnimation(action)) return;
        if (player.IsInsideTree()) player.Play(action, blend);
        else player.Autoplay = action;
    }

    /// <summary>Speed the feet are made for, so a walker's ground speed can match (scaled by the figure's height).</summary>
    public static float SpeedOf(string action) => action switch
    {
        "Walk" => WalkSpeed,
        "Run" => RunSpeed,
        "Carry" => CarrySpeed,
        _ => 0f,
    };

    // ------------------------------------------------------------------ glb

    private static readonly Dictionary<(ulong, Color), Material> Tinted = new();

    private static Node3D? Imported(string role, Color owner, CrewVariant v)
    {
        var path = $"{Dir}{role}.glb";
        if (!ResourceLoader.Exists(path)) return null;
        if (GD.Load<PackedScene>(path)?.Instantiate() is not Node3D model) return null;
        model.Name = "Model";
        foreach (var style in HairStyles)
            if (model.FindChild(style, true, false) is Node3D h) h.Visible = style == v.Style;
        if (model.FindChild("Beard", true, false) is Node3D beard) beard.Visible = v.Beard;
        Dress(model, role, owner, v);
        return model;
    }

    /// <summary>Owner/Skin/Hair materials, blend shapes and the LOD ranges, for every mesh under the model.</summary>
    private static void Dress(Node node, string role, Color owner, CrewVariant v)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                if (mi.Mesh.SurfaceGetMaterial(i) is not BaseMaterial3D m) continue;
                var tint = m.ResourceName switch
                {
                    "Owner" => owner,
                    "Skin" => v.SkinColour,
                    "Hair" => v.HairColour,
                    _ => (Color?)null,
                };
                if (tint is { } c) mi.SetSurfaceOverrideMaterial(i, TintedCopy(m, c, m.ResourceName == "Owner"));
            }
            SetShape(mi, "Feminine", v.Feminine);
            SetShape(mi, "Heavy", v.Heavy);
            SetShape(mi, "Slim", v.Slim);
            var name = mi.Name.ToString();
            if (name == role + "_far")
            {
                mi.VisibilityRangeBegin = HeroRange;
                mi.VisibilityRangeBeginMargin = HeroRangeMargin;
                mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
            else if (name == role || name == "Beard" || Array.IndexOf(HairStyles, name) >= 0)
            {
                mi.VisibilityRangeEnd = HeroRange;
                mi.VisibilityRangeEndMargin = HeroRangeMargin;
                mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
        }
        foreach (var child in node.GetChildren()) Dress(child, role, owner, v);
    }

    private static Material TintedCopy(BaseMaterial3D m, Color c, bool owner)
    {
        var key = (m.GetInstanceId(), c);
        if (Tinted.TryGetValue(key, out var made)) return made;
        var copy = (BaseMaterial3D)m.Duplicate();
        copy.AlbedoColor = c;
        if (owner)
        {
            copy.EmissionEnabled = true;
            copy.Emission = c;
            copy.EmissionEnergyMultiplier = 0.35f;
        }
        Tinted[key] = copy;
        return copy;
    }

    private static void SetShape(MeshInstance3D mi, string shape, float value)
    {
        var i = mi.FindBlendShapeByName(shape);
        if (i >= 0) mi.SetBlendShapeValue(i, value);
    }

    // ------------------------------------------------------------------ primitive stand-in

    private static StandardMaterial3D Mat(Color c, float rough = 0.7f, float emit = 0f)
    {
        var m = new StandardMaterial3D { AlbedoColor = c, Roughness = rough };
        if (emit > 0)
        {
            m.EmissionEnabled = true;
            m.Emission = c;
            m.EmissionEnergyMultiplier = emit;
        }
        return m;
    }

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Vector3 pos, Material mat, string? name = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, Position = pos, MaterialOverride = mat };
        if (name != null) mi.Name = name;
        parent.AddChild(mi);
        return mi;
    }

    private static Node3D Pivot(Node3D parent, string name, Vector3 pos)
    {
        var p = new Node3D { Name = name, Position = pos };
        parent.AddChild(p);
        return p;
    }

    /// <summary>Capsule figure (front -Z) with the glb's part names (Hips, Head, Left/Right UpperArm and UpperLeg pivots;
    /// Tool, Scanner, Crate, HelmetLamp) and an AnimationPlayer with the same seven actions.</summary>
    private static Node3D Primitive(string role, Color owner, CrewVariant v)
    {
        var suitColour = role switch
        {
            "engineer" => new Color(0.26f, 0.26f, 0.28f),
            "science" => new Color(0.2f, 0.29f, 0.39f),
            "security" => new Color(0.15f, 0.15f, 0.17f),
            "eva" => new Color(0.85f, 0.85f, 0.83f),
            _ => new Color(0.59f, 0.44f, 0.27f),
        };
        var suit = Mat(suitColour);
        var dark = Mat(new Color(0.12f, 0.11f, 0.1f));
        var skin = Mat(v.SkinColour, 0.6f);
        var own = Mat(owner, 0.5f, 0.35f);
        var w = 1f + 0.12f * v.Heavy - 0.1f * v.Slim;
        var n = new Node3D { Name = "Model" };
        var hips = Pivot(n, "Hips", new Vector3(0, 0.95f, 0));
        Part(hips, new CapsuleMesh { Radius = 0.17f * w, Height = 0.62f }, new Vector3(0, 0.27f, 0), suit);
        Part(hips, new BoxMesh { Size = new Vector3(0.4f * w, 0.1f, 0.24f) }, new Vector3(0, 0.47f, 0), own); // shoulder yoke
        var head = Pivot(hips, "Head", new Vector3(0, 0.62f, 0));
        if (role == "eva")
        {
            Part(head, new SphereMesh { Radius = 0.15f, Height = 0.3f }, new Vector3(0, 0.1f, 0), Mat(new Color(0.9f, 0.9f, 0.88f), 0.3f));
            Part(head, new BoxMesh { Size = new Vector3(0.2f, 0.1f, 0.05f) }, new Vector3(0, 0.1f, -0.13f), Mat(new Color(0.8f, 0.55f, 0.15f), 0.1f));
            Part(head, new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.06f }, new Vector3(0.15f, 0.14f, -0.04f),
                Mat(new Color(1f, 0.95f, 0.85f), 0.2f, 6f), "HelmetLamp").RotationDegrees = new Vector3(90, 0, 0);
            Part(hips, new BoxMesh { Size = new Vector3(0.32f, 0.44f, 0.15f) }, new Vector3(0, 0.3f, 0.22f), Mat(new Color(0.6f, 0.6f, 0.62f)));
        }
        else
        {
            Part(head, new SphereMesh { Radius = 0.1f, Height = 0.24f }, new Vector3(0, 0.09f, 0), skin);
            if (v.Style != null)
                Part(head, new SphereMesh { Radius = 0.105f, Height = 0.15f, IsHemisphere = true }, new Vector3(0, 0.13f, 0.005f),
                    Mat(v.HairColour, 0.8f), v.Style);
        }
        foreach (var (side, s) in new[] { ("Right", 1f), ("Left", -1f) })
        {
            var arm = Pivot(hips, side + "UpperArm", new Vector3(s * 0.21f * w, 0.46f, 0));
            Part(arm, new CapsuleMesh { Radius = 0.05f * w, Height = 0.58f }, new Vector3(0, -0.27f, 0), suit);
            Part(arm, new SphereMesh { Radius = 0.05f, Height = 0.1f }, new Vector3(0, -0.6f, 0), role == "eva" ? dark : skin);
            var leg = Pivot(n, side + "UpperLeg", new Vector3(s * 0.1f, 0.92f, 0));
            Part(leg, new CapsuleMesh { Radius = 0.075f * w, Height = 0.86f }, new Vector3(0, -0.43f, 0), role == "colonist" ? dark : suit);
            Part(leg, new BoxMesh { Size = new Vector3(0.11f, 0.1f, 0.26f) }, new Vector3(0, -0.87f, -0.05f), dark);
        }
        var hand = n.GetNode<Node3D>("Hips/RightUpperArm");
        Part(hand, new BoxMesh { Size = new Vector3(0.05f, 0.05f, 0.22f) }, new Vector3(0, -0.6f, -0.1f), Mat(new Color(0.6f, 0.45f, 0.1f)), "Tool");
        Part(hand, new BoxMesh { Size = new Vector3(0.08f, 0.03f, 0.14f) }, new Vector3(0, -0.6f, -0.06f), Mat(new Color(0.3f, 0.75f, 0.8f), 0.3f, 1.5f), "Scanner");
        Part(hips, new BoxMesh { Size = new Vector3(0.48f, 0.3f, 0.34f) }, new Vector3(0, 0.15f, -0.29f), Mat(new Color(0.55f, 0.56f, 0.55f)), "Crate");
        n.AddChild(PrimitivePlayer());
        return n;
    }

    private static AnimationPlayer PrimitivePlayer()
    {
        var lib = new AnimationLibrary();
        // (action, length s, hips bob, leg swing deg, arm swing deg, right arm raise deg, hips lean deg)
        foreach (var (name, len, bob, leg, arm, raise, lean) in new[]
                 {
                     ("Idle", 3f, 0.006f, 0f, 2f, 0f, 0f), ("Walk", 32 / 30f, 0.02f, 24f, 18f, 0f, 2f),
                     ("Run", 20 / 30f, 0.04f, 40f, 40f, 0f, 8f), ("Work", 2f, 0.01f, 0f, 0f, 70f, 14f),
                     ("Carry", 36 / 30f, 0.015f, 18f, 0f, 75f, 3f), ("Scan", 3.2f, 0.004f, 0f, 0f, 60f, 4f),
                     ("Wave", 4 / 3f, 0.004f, 0f, 0f, 160f, 0f),
                 })
        {
            var a = new Animation { Length = len, LoopMode = Animation.LoopModeEnum.Linear };
            int Track(string path)
            {
                var t = a.AddTrack(Animation.TrackType.Value);
                a.TrackSetPath(t, path);
                return t;
            }
            var hipsPos = Track("Hips:position");
            var hipsRot = Track("Hips:rotation_degrees");
            var legR = Track("RightUpperLeg:rotation_degrees");
            var legL = Track("LeftUpperLeg:rotation_degrees");
            var armR = Track("Hips/RightUpperArm:rotation_degrees");
            var armL = Track("Hips/LeftUpperArm:rotation_degrees");
            for (var k = 0; k <= 8; k++)
            {
                var t = len * k / 8f;
                var ph = Mathf.Tau * k / 8f;
                var two = leg > 0 ? Mathf.Cos(2 * ph) : Mathf.Sin(ph);
                a.TrackInsertKey(hipsPos, t, new Vector3(0, 0.95f - bob * (0.5f + 0.5f * two), 0));
                a.TrackInsertKey(hipsRot, t, new Vector3(-lean, 0, 0));
                a.TrackInsertKey(legR, t, new Vector3(leg * Mathf.Sin(ph), 0, 0));
                a.TrackInsertKey(legL, t, new Vector3(-leg * Mathf.Sin(ph), 0, 0));
                var wave = name == "Wave" ? 25f * Mathf.Sin(2 * ph) : name == "Work" ? 8f * Mathf.Sin(2 * ph) : 0f;
                a.TrackInsertKey(armR, t, new Vector3(raise - arm * Mathf.Sin(ph), 0, name == "Wave" ? 20f + wave : wave));
                a.TrackInsertKey(armL, t, new Vector3(name == "Carry" ? raise : arm * Mathf.Sin(ph), 0, 0));
            }
            lib.AddAnimation(name, a);
        }
        var player = new AnimationPlayer { Name = "AnimationPlayer" };
        player.AddAnimationLibrary("", lib);
        return player;
    }
}
