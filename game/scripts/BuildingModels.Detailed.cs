using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The detailed Blender models of the Mining Complex, the Storage Container yard and the Fusion Reactor
/// (blender/build_mine.py, build_storage.py, build_reactor.py --game → models/buildings/&lt;id&gt;.glb, built like the
/// refinery: Build0..N pieces for the exocomps, one baked grime atlas, named animated parts), or the old primitive
/// models when a glb isn't there or imported yet. <see cref="PlanetView"/> animates them from the structure's status
/// (PlanetView.Detailed.cs). Per-instance variety (<see cref="Look"/>): scale pivots, optional parts, container layout
/// and colours; <see cref="Dress"/> adds the tint, add-ons on the glb's AddOnSpot/RoofSpot markers and the signs.
/// </summary>
public static partial class BuildingModels
{
    public const string MinePath = "res://models/buildings/mine.glb";
    public const string StoragePath = "res://models/buildings/storage_container.glb";
    public const string ReactorPath = "res://models/buildings/fusion_reactor.glb";
    public const string DeepCorePath = "res://models/buildings/deep_core_mine.glb";

    private static string[] Pieces(int count)
    {
        var names = new string[count];
        for (var i = 0; i < count; i++) names[i] = $"Build{i}";
        return names;
    }

    /// <summary>Container paints (the yard's mix; one slot per instance takes the accent colour instead).</summary>
    private static readonly Color[] ContainerColours =
    {
        new(0.72f, 0.36f, 0.22f), new(0.3f, 0.45f, 0.62f), new(0.78f, 0.7f, 0.42f),
        new(0.42f, 0.55f, 0.4f), new(0.62f, 0.62f, 0.64f), new(0.55f, 0.24f, 0.2f),
    };

    /// <summary>Upper-tier layouts of the yard (Slot6+k stands on Slot k; left column 0-2, right 3-5, back to front
    /// = rows 2..0). The first is the canonical look.</summary>
    private static readonly int[][] UpperLayouts =
    {
        new[] { 6, 8, 10 }, new[] { 7, 9 }, new[] { 6, 7, 11 }, new[] { 8, 9, 10 }, new[] { 6, 11 }, new[] { 7, 8, 10, 11 },
    };

    /// <summary>Mining Complex: headframe + sheaves ("Wheel"), skip ("Hoist") and cage ("Cage") on their ropes,
    /// ore bin, belt conveyor ("Belt", Ore0..5 under "OreFlow"), heap ("Dust"), generator ("Fan0", "Smoke0"), drill
    /// rig ("Drill"), office (SignFront).</summary>
    private static Node3D Mine(Color owner, Look v)
    {
        if (ImportedFile(MinePath, "mine", owner, Pieces(14)) is not { } n) return PrimitiveMine(owner, v);
        OwnMaterials(n);
        ScaleY(n, "Mast", v.Height);
        ScaleY(n, "Stack", v.Height2);
        ScaleY(n, "Masts", v.Height3);
        Optional(n, "OptTank", v.IsPlain || v.Chance(0.65f));
        Optional(n, "OptRods", v.IsPlain || v.Chance(0.6f));
        n.AddChild(new OmniLight3D { Name = "MineOmni", LightColor = new Color(1f, 0.82f, 0.6f), LightEnergy = 0.9f, OmniRange = 12f, ShadowEnabled = false, Position = new Vector3(0.5f, 5.5f, 1.5f) });
        return n;
    }

    /// <summary>Storage yard: twelve container slots (Slot0-5 ground, Slot6-11 upper; the look picks which upper ones
    /// stand and paints each), the gantry ("Gantry" → "Trolley" → "Hook" + "Cable"), the fill gauge (FillLight0-7,
    /// each its own material copy), the pod pad ("PodPad"), the office (SignFront).</summary>
    private static Node3D StorageContainer(Color owner, Look v)
    {
        if (ImportedFile(StoragePath, "storage_container", owner, Pieces(11)) is not { } n) return PrimitiveStorage(owner, v);
        OwnMaterials(n);
        var upper = UpperLayouts[v.Int(UpperLayouts.Length)];
        var offset = v.Int(ContainerColours.Length);
        var accentSlot = v.IsPlain ? -1 : v.Int(12);
        for (var k = 0; k < 12; k++)
        {
            if (n.FindChild($"Slot{k}", true, false) is not Node3D slot) continue;
            var shown = k < 6 || System.Array.IndexOf(upper, k) >= 0;
            slot.Visible = shown;
            if (!shown) continue;
            if (!v.IsPlain) slot.RotateY(Mathf.DegToRad(v.Range(k < 6 ? -1.2f : -3f, k < 6 ? 1.2f : 3f)));
            var colour = k == accentSlot ? v.Accent : ContainerColours[(k * 5 + offset) % ContainerColours.Length];
            Tint(slot, "Container", colour);
        }
        for (var k = 0; k < 8; k++)
            if (n.FindChild($"FillLight{k}", true, false) is MeshInstance3D light) Unshare(light);
        if (!v.IsPlain)
        {
            // where the crane happens to stand
            if (n.FindChild("Gantry", true, false) is Node3D gantry && gantry.GetParent().GetNodeOrNull<Node3D>("GantryMin") is { } g0 && gantry.GetParent().GetNodeOrNull<Node3D>("GantryMax") is { } g1)
                gantry.Position = g0.Position.Lerp(g1.Position, v.Range(0.15f, 0.85f));
            if (n.FindChild("Trolley", true, false) is Node3D trolley && trolley.GetParent().GetNodeOrNull<Node3D>("TrolleyMin") is { } t0 && trolley.GetParent().GetNodeOrNull<Node3D>("TrolleyMax") is { } t1)
                trolley.Position = t0.Position.Lerp(t1.Position, v.Range(0f, 1f));
        }
        ScaleY(n, "Masts", v.Height);
        if (n.FindChild("PodPad", true, false) is Node3D pad)
            pad.AddChild(new OmniLight3D { Name = "PadOmni", LightColor = new Color(0.45f, 0.65f, 1f), LightEnergy = 0.6f, OmniRange = 4.5f, ShadowEnabled = false, Position = new Vector3(0, 1.2f, 0) });
        n.AddChild(new OmniLight3D { Name = "YardOmni", LightColor = new Color(1f, 0.93f, 0.82f), LightEnergy = 0.7f, OmniRange = 12f, ShadowEnabled = false, Position = new Vector3(0f, 5f, 0f) });
        return n;
    }

    /// <summary>Fusion Reactor: tokamak + core column ("CoreGlow" material, "CoreLight" holds "CoreOmni"), spinning
    /// constrictor rings Ring0-2, cooling-tower fans Fan0/1 (steam at Vent0/1), plasma conduits ("Plasma"), control
    /// building (SignFront, "Status" board), transformer + pylon.</summary>
    private static Node3D Reactor(Color owner, Look v)
    {
        if (ImportedFile(ReactorPath, "fusion_reactor", owner, Pieces(14)) is not { } n) return PrimitiveReactor(owner, v);
        OwnMaterials(n);
        ScaleY(n, "Towers", v.Height);
        ScaleY(n, "Pylon", v.Height2);
        ScaleY(n, "Masts", v.Height3);
        Optional(n, "OptTank", v.IsPlain || v.Chance(0.7f));
        Optional(n, "OptRadiator", v.IsPlain || v.Chance(0.6f));
        if (n.FindChild("CoreLight", true, false) is Node3D at)
            at.AddChild(new OmniLight3D { Name = "CoreOmni", LightColor = new Color(0.35f, 0.75f, 1f), LightEnergy = 1.8f, OmniRange = 14f, ShadowEnabled = false });
        return n;
    }

    /// <summary>Deep-Core Mine: tall derrick over a glowing shaft ("CoreHeat"), top drive + drill string ("Drill",
    /// strokes to "DrillLow"), seismic stabilisers, pressure tanks (scale pivots Tanks0/1), coolant pump ("Pump"),
    /// substation ("Fan0", glowing "Coil"s), spoil belt ("Belt"), steam at "Vent0", driller's cabin (SignFront).</summary>
    private static Node3D DeepCoreMine(Color owner, Look v)
    {
        if (ImportedFile(DeepCorePath, "deep_core_mine", owner, Pieces(14)) is not { } n) return PrimitiveDeepCore(owner, v);
        OwnMaterials(n);
        ScaleY(n, "Tanks0", v.Height);
        ScaleY(n, "Tanks1", v.Height2);
        ScaleY(n, "Masts", v.Height3);
        Optional(n, "OptRack", v.IsPlain || v.Chance(0.7f));
        n.AddChild(new OmniLight3D { Name = "HeatOmni", LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 1.4f, OmniRange = 9f, ShadowEnabled = false, Position = new Vector3(0.6f, 1.6f, 0.4f) });
        n.AddChild(new OmniLight3D { Name = "MineOmni", LightColor = new Color(1f, 0.9f, 0.75f), LightEnergy = 0.8f, OmniRange = 13f, ShadowEnabled = false, Position = new Vector3(0f, 7f, 1.5f) });
        return n;
    }

    /// <summary>The signs on a detailed glb's markers (Dress calls this when the model has a "SignFront").</summary>
    private static void PaintDetailedSigns(Node3D n, string defId, int serial)
    {
        var number = serial > 0 ? $"{serial:00}" : "01";
        switch (defId)
        {
            case "mine":
                PaintMarker(n, "SignFront", $"MINE {number}", SignInk);
                PaintMarker(n, "SignWinder", "HOIST", SignInk);
                PaintMarker(n, "LabelBin", "ORE", PlateInk);
                break;
            case "storage_container":
                PaintMarker(n, "SignFront", $"STORAGE {number}", SignInk);
                PaintMarker(n, "LabelPad", $"PAD {number}", new Color(0.9f, 0.7f, 0.15f));
                PaintMarker(n, "LabelHex", "VOLATILES", PlateInk);
                break;
            case "deep_core_mine":
                PaintMarker(n, "SignFront", $"DEEP CORE {number}", SignInk);
                PaintMarker(n, "LabelTank", "COOLANT", PlateInk);
                PaintMarker(n, "LabelSub", "DANGER", PlateInk);
                break;
            case "fusion_reactor":
                PaintMarker(n, "SignFront", $"REACTOR {number}", SignInk);
                PaintMarker(n, "LabelTank", "DEUTERIUM", PlateInk);
                PaintMarker(n, "LabelPylon", "HIGH VOLTAGE", PlateInk);
                break;
        }
    }

    /// <summary>Spots a glb marks for the variety add-ons ("AddOnSpot0..", "RoofSpot0.."): position and yaw (degrees)
    /// relative to the model, facing the spot's local +Z.</summary>
    private static List<(Vector3 At, float Yaw)> GlbSpots(Node3D model, string prefix)
    {
        var spots = new List<(Vector3, float)>();
        for (var k = 0; k < 16; k++)
        {
            if (model.FindChild($"{prefix}{k}", true, false) is not Node3D spot) break;
            var t = RelativeTo(spot, model);
            var f = t.Basis.Z;
            spots.Add((t.Origin, Mathf.RadToDeg(Mathf.Atan2(f.X, f.Z))));
        }
        return spots;
    }

    private static void ScaleY(Node3D n, string part, float k)
    {
        if (n.FindChild(part, true, false) is Node3D p) p.Scale = new Vector3(p.Scale.X, p.Scale.Y * k, p.Scale.Z);
    }

    /// <summary>Show or hide an optional part (hidden parts are skipped by the construction view too).</summary>
    private static void Optional(Node3D n, string part, bool shown)
    {
        if (n.FindChild(part, true, false) is Node3D p) p.Visible = shown;
    }

    /// <summary>Give every surface under `node` drawn with material `name` its own copy in `colour`.</summary>
    private static void Tint(Node node, string name, Color colour)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if ((mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i)) is BaseMaterial3D m && m.ResourceName == name)
                {
                    var copy = (BaseMaterial3D)m.Duplicate();
                    copy.AlbedoColor = new Color(colour.R, colour.G, colour.B, m.AlbedoColor.A);
                    mi.SetSurfaceOverrideMaterial(i, copy);
                }
        foreach (var child in node.GetChildren()) Tint(child, name, colour);
    }

    /// <summary>A mesh's own copies of its surface materials (to light one of a row of identical lamps).</summary>
    private static void Unshare(MeshInstance3D mi)
    {
        if (mi.Mesh == null) return;
        for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            if ((mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i)) is Material m)
                mi.SetSurfaceOverrideMaterial(i, (Material)m.Duplicate());
    }
}
