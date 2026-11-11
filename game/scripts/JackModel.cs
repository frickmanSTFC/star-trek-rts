using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Jack Skellington for the raid joke (docs/jack-contract.md "Models"): loads models/creatures/jack.glb (pivot nodes Hips,
/// Torso, Head, ArmUpperL/R, ArmLowerL/R, HandL/R, Pumpkin, LegUpperL/R, LegLowerL/R, FootL/R, each with an identity rest
/// rotation at its joint), or builds a primitive stand-in with the same names. <see cref="Rig.Pose"/> sets each joint as
/// rest × rotation, so the animation code in PlanetView.Jack.cs never cares which one it got. Facing: Godot -Z.
/// Also the pumpkin (models/creatures/pumpkin.glb) and bat (bat.glb: Body, WingL, WingR) props. Render-only.
/// </summary>
public static class JackModel
{
    public const float Height = 16f;
    private const string JackPath = "res://models/creatures/jack.glb";
    private const string PumpkinPath = "res://models/creatures/pumpkin.glb";
    private const string BatPath = "res://models/creatures/bat.glb";

    private static readonly string[] Joints =
    {
        "Hips", "Torso", "Head", "ArmUpperL", "ArmLowerL", "HandL", "ArmUpperR", "ArmLowerR", "HandR", "Pumpkin",
        "LegUpperL", "LegLowerL", "FootL", "LegUpperR", "LegLowerR", "FootR",
    };

    /// <summary>Joint angles in degrees (about the joint's local X unless named otherwise; + swings a limb forward, -Z).</summary>
    public struct Pose
    {
        public float Hips, HipsRoll, TorsoPitch, TorsoTwist, HeadPitch, HeadYaw, HeadRoll;
        public float ArmL, ArmLOut, ElbowL, ArmR, ArmROut, ElbowR;
        public float LegL, KneeL, FootL, LegR, KneeR, FootR;
        public float Bob;
        public bool PumpkinInHand;
    }

    public sealed class Rig
    {
        public Node3D Root = null!;
        public readonly Dictionary<string, Node3D> Parts = new();
        private readonly Dictionary<string, Transform3D> _rest = new();
        private Vector3 _hipsRest;

        internal void Keep()
        {
            foreach (var name in Joints)
                if (Root.FindChild(name, true, false) is Node3D n)
                {
                    Parts[name] = n;
                    _rest[name] = n.Transform;
                }
            if (Parts.TryGetValue("Hips", out var hips)) _hipsRest = hips.Position;
        }

        public Node3D? Hand => Parts.GetValueOrDefault("HandR");

        private void Set(string name, Basis turn)
        {
            if (!Parts.TryGetValue(name, out var n)) return;
            var rest = _rest[name];
            n.Transform = new Transform3D(rest.Basis * turn, rest.Origin);
        }

        private static Basis X(float deg) => new(Vector3.Right, Mathf.DegToRad(deg));
        private static Basis Y(float deg) => new(Vector3.Up, Mathf.DegToRad(deg));
        private static Basis Z(float deg) => new(Vector3.Back, Mathf.DegToRad(deg));

        public void Apply(in Pose p)
        {
            Set("Hips", X(p.Hips) * Z(p.HipsRoll));
            if (Parts.TryGetValue("Hips", out var hips)) hips.Position = _hipsRest + new Vector3(0, p.Bob, 0);
            Set("Torso", Y(p.TorsoTwist) * X(p.TorsoPitch));
            Set("Head", Y(p.HeadYaw) * X(p.HeadPitch) * Z(p.HeadRoll));
            // Out = away from the body: the left arm (-X) turns about -Z, the right (+X) about +Z.
            Set("ArmUpperL", Z(-p.ArmLOut) * X(p.ArmL));
            Set("ArmLowerL", X(p.ElbowL));
            Set("ArmUpperR", Z(p.ArmROut) * X(p.ArmR));
            Set("ArmLowerR", X(p.ElbowR));
            Set("LegUpperL", X(p.LegL));
            Set("LegLowerL", X(-p.KneeL));
            Set("FootL", X(p.FootL));
            Set("LegUpperR", X(p.LegR));
            Set("LegLowerR", X(-p.KneeR));
            Set("FootR", X(p.FootR));
            if (Parts.TryGetValue("Pumpkin", out var pk)) pk.Visible = p.PumpkinInHand;
        }
    }

    public static Rig Build()
    {
        var rig = new Rig();
        Node3D? model = null;
        if (ResourceLoader.Exists(JackPath) && GD.Load<PackedScene>(JackPath)?.Instantiate() is Node3D m) model = m;
        if (model == null || model.FindChild("Hips", true, false) == null)
        {
            model?.QueueFree();
            model = Primitive();
        }
        var root = new Node3D { Name = "JackRaider" };
        root.AddChild(model);
        rig.Root = root;
        rig.Keep();
        GlowUp(root);
        return rig;
    }

    /// <summary>Emissive materials (the pumpkin's face) bright enough to read by day.</summary>
    private static void GlowUp(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if (mi.Mesh.SurfaceGetMaterial(i) is StandardMaterial3D sm && sm.EmissionEnabled)
                {
                    var own = (StandardMaterial3D)sm.Duplicate();
                    own.EmissionEnergyMultiplier = 3f;
                    own.SetMeta(VehicleLights.NoBoostMeta, true);
                    mi.SetSurfaceOverrideMaterial(i, own);
                }
        foreach (var c in node.GetChildren()) GlowUp(c);
    }

    public static Node3D Pumpkin()
    {
        if (ResourceLoader.Exists(PumpkinPath) && GD.Load<PackedScene>(PumpkinPath)?.Instantiate() is Node3D m)
        {
            var n = new Node3D { Name = "JackPumpkin" };
            n.AddChild(m);
            GlowUp(n);
            return n;
        }
        return PrimitivePumpkin(1.6f);
    }

    public static Node3D Bat(out Node3D? wingL, out Node3D? wingR)
    {
        Node3D n;
        if (ResourceLoader.Exists(BatPath) && GD.Load<PackedScene>(BatPath)?.Instantiate() is Node3D m && m.FindChild("WingL", true, false) != null)
        {
            n = new Node3D { Name = "JackBat" };
            n.AddChild(m);
        }
        else n = PrimitiveBat();
        wingL = n.FindChild("WingL", true, false) as Node3D;
        wingR = n.FindChild("WingR", true, false) as Node3D;
        return n;
    }

    // ---------------------------------------------------------------- stand-ins (used until the glbs are imported)

    private static StandardMaterial3D Mat(Color c, float rough = 0.7f) => new() { AlbedoColor = c, Roughness = rough };

    private static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
    };

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Vector3 at, Material mat, Vector3? scale = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = mat };
        if (scale is { } s) mi.Scale = s;
        parent.AddChild(mi);
        return mi;
    }

    private static Node3D Joint(Node3D parent, string name, Vector3 at)
    {
        var n = new Node3D { Name = name, Position = at };
        parent.AddChild(n);
        return n;
    }

    private static MeshInstance3D Limb(Node3D joint, float length, float radius, Material mat) =>
        Add(joint, new CylinderMesh { TopRadius = radius, BottomRadius = radius * 0.8f, Height = length, RadialSegments = 8 },
            new Vector3(0, -length / 2, 0), mat);

    /// <summary>Same joints as the glb: hips 7.4 up, shoulders ±1.15 at 12.6, neck 13.4, head to 16.</summary>
    private static Node3D Primitive()
    {
        var bone = Mat(new Color(0.95f, 0.94f, 0.9f), 0.5f);
        var suit = Mat(new Color(0.06f, 0.06f, 0.07f), 0.8f);
        var black = Mat(new Color(0.01f, 0.01f, 0.01f), 0.9f);
        var jack = new Node3D { Name = "Jack" };
        var hips = Joint(jack, "Hips", new Vector3(0, 7.4f, 0));
        Add(hips, new BoxMesh { Size = new Vector3(1.6f, 1.1f, 0.9f) }, new Vector3(0, 0.1f, 0), suit);
        Add(hips, new BoxMesh { Size = new Vector3(1.5f, 2.6f, 0.15f) }, new Vector3(0, -1.0f, 0.5f), suit); // coat tails
        var torso = Joint(hips, "Torso", new Vector3(0, 0.6f, 0));
        Add(torso, new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.55f, Height = 4.6f, RadialSegments = 10 }, new Vector3(0, 2.3f, 0), suit, new Vector3(1.15f, 1, 0.7f));
        Add(torso, new BoxMesh { Size = new Vector3(1.6f, 0.45f, 0.2f) }, new Vector3(0, 4.55f, -0.55f), black); // bat bow tie
        var head = Joint(torso, "Head", new Vector3(0, 4.8f, 0));
        Add(head, new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.22f, Height = 0.8f }, new Vector3(0, 0.3f, 0), bone);
        Add(head, new SphereMesh { Radius = 1.45f, Height = 3.2f }, new Vector3(0, 2.0f, 0), bone);
        foreach (var x in new[] { -0.55f, 0.55f })
            Add(head, new SphereMesh { Radius = 0.42f, Height = 0.62f }, new Vector3(x, 2.25f, -1.22f), black);
        Add(head, new BoxMesh { Size = new Vector3(1.7f, 0.08f, 0.1f) }, new Vector3(0, 1.3f, -1.3f), black); // grin
        foreach (var (side, x) in new[] { ("L", -1.15f), ("R", 1.15f) })
        {
            var up = Joint(torso, "ArmUpper" + side, new Vector3(x, 4.2f, 0));
            Limb(up, 2.7f, 0.22f, suit);
            var low = Joint(up, "ArmLower" + side, new Vector3(0, -2.7f, 0));
            Limb(low, 2.5f, 0.18f, suit);
            var hand = Joint(low, "Hand" + side, new Vector3(0, -2.5f, 0));
            Add(hand, new BoxMesh { Size = new Vector3(0.25f, 1.2f, 0.45f) }, new Vector3(0, -0.6f, 0), bone);
            if (side == "R")
            {
                var pk = PrimitivePumpkin(1.4f);
                pk.Name = "Pumpkin";
                pk.Position = new Vector3(0, -1.5f, -0.4f);
                hand.AddChild(pk);
            }
            var leg = Joint(hips, "LegUpper" + side, new Vector3(x * 0.45f, -0.3f, 0));
            Limb(leg, 3.6f, 0.26f, suit);
            var shin = Joint(leg, "LegLower" + side, new Vector3(0, -3.6f, 0));
            Limb(shin, 3.4f, 0.2f, suit);
            var foot = Joint(shin, "Foot" + side, new Vector3(0, -3.4f, 0));
            Add(foot, new BoxMesh { Size = new Vector3(0.45f, 0.35f, 1.6f) }, new Vector3(0, -0.15f, -0.5f), black);
        }
        return jack;
    }

    private static Node3D PrimitivePumpkin(float size)
    {
        var n = new Node3D { Name = "JackPumpkin" };
        Add(n, new SphereMesh { Radius = size / 2, Height = size * 0.8f }, Vector3.Zero, Mat(new Color(0.95f, 0.45f, 0.05f), 0.6f));
        Add(n, new BoxMesh { Size = new Vector3(size * 0.55f, size * 0.12f, 0.05f) }, new Vector3(0, -size * 0.08f, -size * 0.49f), Glow(new Color(1f, 0.75f, 0.2f), 4f));
        Add(n, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.1f, Height = size * 0.3f }, new Vector3(0, size * 0.48f, 0), Mat(new Color(0.15f, 0.3f, 0.08f)));
        return n;
    }

    private static Node3D PrimitiveBat()
    {
        var n = new Node3D { Name = "JackBat" };
        var dark = Mat(new Color(0.08f, 0.05f, 0.1f), 0.8f);
        var body = new Node3D { Name = "Body" };
        n.AddChild(body);
        Add(body, new SphereMesh { Radius = 0.18f, Height = 0.5f }, Vector3.Zero, dark);
        foreach (var (name, x) in new[] { ("WingL", -0.12f), ("WingR", 0.12f) })
        {
            var w = Joint(body, name, new Vector3(x, 0, 0));
            Add(w, new BoxMesh { Size = new Vector3(0.6f, 0.03f, 0.35f) }, new Vector3(x > 0 ? 0.3f : -0.3f, 0, 0), dark);
        }
        return n;
    }
}
