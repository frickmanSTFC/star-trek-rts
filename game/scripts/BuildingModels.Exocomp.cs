using Godot;

namespace FinalFrontier;

/// <summary>
/// The construction exocomp (ConstructionView): models/drones/exocomp.glb from blender/build_exocomp.py --game, or a
/// primitive stand-in with the same part names. Front = -Z (Blender +Y), about 1.9 long. Parts: "Body" (everything
/// that bobs), "Emitter" (the beam comes out of its origin, at the nose under the sensor), "Light" (the sensor strip),
/// "Thruster0..3" (lift pads underneath), "ArmL"/"ArmR" (tool arms). "Owner" material recoloured per player.
/// </summary>
public static partial class BuildingModels
{
    public const string ExocompPath = "res://models/drones/exocomp.glb";

    public static Node3D Exocomp(Color owner) => ImportedFile(ExocompPath, "exocomp", owner) ?? PrimitiveExocomp(owner);

    private static Node3D PrimitiveExocomp(Color owner)
    {
        var n = new Node3D { Name = "exocomp" };
        var body = new Node3D { Name = "Body" };
        n.AddChild(body);
        var hull = Metal(new Color(0.86f, 0.87f, 0.9f), 0.55f, 0.32f);
        var dark = Metal(new Color(0.16f, 0.17f, 0.2f), 0.5f, 0.5f);
        var glow = Glow(new Color(0.5f, 0.85f, 1f), 3f);
        var shell = Add(body, new SphereMesh { Radius = 0.6f, Height = 0.8f, RadialSegments = 16, Rings = 8 }, Vector3.Zero, hull);
        shell.Scale = new Vector3(1f, 1f, 1.35f);
        Add(body, new CylinderMesh { TopRadius = 0.52f, BottomRadius = 0.42f, Height = 0.18f, RadialSegments = 16 }, new Vector3(0, -0.3f, 0), dark);
        Add(body, new TorusMesh { InnerRadius = 0.56f, OuterRadius = 0.64f, Rings = 24, RingSegments = 6 }, new Vector3(0, -0.05f, 0), Metal(owner, 0.3f, 0.4f));
        Add(body, new BoxMesh { Size = new Vector3(0.62f, 0.3f, 0.12f) }, new Vector3(0, 0.05f, -0.76f), dark);
        Add(body, new BoxMesh { Size = new Vector3(0.44f, 0.07f, 0.05f) }, new Vector3(0, 0.07f, -0.83f), glow).Name = "Light";
        foreach (var (name, x) in new[] { ("ArmL", -0.5f), ("ArmR", 0.5f) })
        {
            var arm = new Node3D { Name = name, Position = new Vector3(x, -0.12f, -0.3f), RotationDegrees = new Vector3(-25f, 0, 0) };
            Add(arm, new BoxMesh { Size = new Vector3(0.09f, 0.09f, 0.6f) }, new Vector3(0, 0, -0.3f), dark);
            Add(arm, new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 6, Rings = 3 }, new Vector3(0, 0, -0.62f), glow);
            body.AddChild(arm);
        }
        var emitter = new Node3D { Name = "Emitter", Position = new Vector3(0, -0.22f, -0.95f) };
        Add(emitter, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.09f, Height = 0.22f, RadialSegments = 8 }, new Vector3(0, 0, 0.1f), dark, new Vector3(90, 0, 0));
        Add(emitter, new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 8, Rings = 4 }, Vector3.Zero, glow);
        body.AddChild(emitter);
        var pads = new[] { new Vector3(-0.3f, -0.4f, -0.35f), new Vector3(0.3f, -0.4f, -0.35f), new Vector3(-0.3f, -0.4f, 0.35f), new Vector3(0.3f, -0.4f, 0.35f) };
        for (var i = 0; i < pads.Length; i++)
            Add(body, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.04f, RadialSegments = 10 }, pads[i], glow).Name = $"Thruster{i}";
        return n;
    }
}
