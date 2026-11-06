using Godot;

namespace FinalFrontier;

/// <summary>
/// The Ore Refinery: models/buildings/refinery.glb from blender/build_refinery.py --game (receiving bay with the
/// cargo-pod pad, crusher, belt conveyor, induction furnace with tapping ports and a ring catwalk, casting hall, ingot
/// stacks, duranium/tritanium silos, cooling towers, stacks, control booth), or the old primitive refinery when the
/// glb isn't there or imported yet. <see cref="PlanetView"/> animates it from the structure's status
/// (PlanetView.Refinery.cs).
/// </summary>
public static partial class BuildingModels
{
    public const string RefineryPath = "res://models/buildings/refinery.glb";

    /// <summary>The glb's build pieces in build order (Build0 foundation … Build14 lights), lifted to be direct
    /// children of the model so the construction view finds them; animated parts stay inside their piece.</summary>
    public static readonly string[] RefineryPieces =
    {
        "Build0", "Build1", "Build2", "Build3", "Build4", "Build5", "Build6", "Build7",
        "Build8", "Build9", "Build10", "Build11", "Build12", "Build13", "Build14",
    };

    /// <summary>Names the view drives (found with FindChild, wherever they sit): Crusher (flywheel), Fan0–Fan2,
    /// OreFlow (Ore0–Ore7 ride toward its child OreEnd), PodPad, FurnaceLight (holds "FurnaceOmni"), SparkPoint,
    /// Smoke0/Smoke1; materials Belt (UV scroll), FurnaceGlow, Lamp, PadLight, Window, Interior, Beacon.</summary>
    private static Node3D Refinery(Color owner, Look v)
    {
        if (ImportedFile(RefineryPath, "refinery", owner, RefineryPieces) is not { } n) return PrimitiveRefinery(owner);
        OwnMaterials(n);
        // proportion variety on the inner pivots (not the Build groups, which the construction view may scale)
        foreach (var (part, k) in new[] { ("Stacks", v.Height), ("Silos", v.Height2), ("Towers", v.Height3) })
            if (n.FindChild(part, true, false) is Node3D p)
                p.Scale = new Vector3(p.Scale.X, p.Scale.Y * k, p.Scale.Z);
        if (n.FindChild("FurnaceLight", true, false) is Node3D at)
            at.AddChild(new OmniLight3D { Name = "FurnaceOmni", LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 1.6f, OmniRange = 10f, ShadowEnabled = false });
        if (n.FindChild("PodPad", true, false) is Node3D pad)
            pad.AddChild(new OmniLight3D { Name = "PadOmni", LightColor = new Color(0.45f, 0.65f, 1f), LightEnergy = 0.7f, OmniRange = 5f, ShadowEnabled = false, Position = new Vector3(0, 1.2f, 0) });
        return n;
    }

    /// <summary>The sign on the control booth and the casting hall, the silo plates and the pad number.</summary>
    private static void PaintRefinerySigns(Node3D n, int serial)
    {
        var number = serial > 0 ? $"{serial:00}" : "01";
        PaintMarker(n, "SignFront", $"REFINERY {number}", SignInk);
        PaintMarker(n, "SignHall", "CASTING", SignInk);
        PaintMarker(n, "LabelSilo0", "DURANIUM", PlateInk);
        PaintMarker(n, "LabelSilo1", "TRITANIUM", PlateInk);
        PaintMarker(n, "LabelPad", $"PAD {number}", new Color(0.9f, 0.7f, 0.15f));
    }

    /// <summary>Ore refinery from primitives: a squat smelter with a glowing melt slot, two stacks, an ore hopper and a
    /// spinning cooling fan ("Spin").</summary>
    private static Node3D PrimitiveRefinery(Color owner)
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
}
