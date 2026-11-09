using System;
using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Ruin props for a ruin hex on the colony's world (civilizations phase 2, docs/civilizations-phase2-contract.md
/// "Models"): blender/build_ruins.py → models/natives/ruins.glb, meshes ruin_a (broken walls + toppled pillars), ruin_b
/// (half-buried dome + arch), ruin_c (obelisk ring), each with a ruin_&lt;x&gt;_rim (the pieces beyond r ~7.8, which stay
/// round an Excavation Site), and ruin_far (the far stand-in). Read once like <see cref="NativeTowns"/>: the meshes are
/// kept and the imported scene freed, so a ruin is fresh MeshInstance3Ds sharing the meshes with their own material
/// copies: "RuinStone" (vertex colours carry the weathering; a little of the dead species' colour), "RuinMetal",
/// "Glow" (dark, or a faint teal for precursor sites, <see cref="SetGlow"/>). Without the glb, primitive stand-ins with
/// the same node names. Render-only; a ruin is ~18 across (fits one hex) at scale 1.
/// </summary>
public static class RuinModels
{
    public const string Path = "res://models/natives/ruins.glb";

    /// <summary>Meta on a built ruin's root: the Array of its Glow materials (<see cref="SetGlow"/>).</summary>
    public static readonly StringName GlowMeta = "ruin_glow";

    /// <summary>How many near variants there are (ruin_a, ruin_b, ruin_c).</summary>
    public const int Variants = 3;

    /// <summary>Beyond this distance from the camera the far stand-in shows instead of the core + rim.</summary>
    public const float FarFrom = 650f;

    private static readonly string[] Keys = { "ruin_a", "ruin_b", "ruin_c" };
    private static readonly Color GlowColour = new(0.35f, 0.95f, 0.85f);

    private static Dictionary<string, (Mesh Mesh, Transform3D At)>? _meshes;

    private static Dictionary<string, (Mesh Mesh, Transform3D At)> Meshes
    {
        get
        {
            if (_meshes != null) return _meshes;
            _meshes = new Dictionary<string, (Mesh, Transform3D)>();
            if (ResourceLoader.Exists(Path) && GD.Load<PackedScene>(Path)?.Instantiate() is Node root)
            {
                Collect(root);
                root.Free();
            }
            return _meshes;
        }
    }

    /// <summary>True when the glb was found and held the ruin meshes (else primitives stand in).</summary>
    public static bool Imported => Meshes.Count > 0;

    private static void Collect(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null && mi.Name.ToString().StartsWith("ruin_", StringComparison.Ordinal))
            _meshes![mi.Name.ToString()] = (mi.Mesh, mi.Transform);
        foreach (var child in node.GetChildren()) Collect(child);
    }

    /// <summary>The variant a precursor species always gets (Preservers: the obelisk ring, Iconians: the gateway dome,
    /// T'Kon: the walled colonnade), or -1 for an invented species.</summary>
    public static int PrecursorVariant(string species)
    {
        if (species.Contains("Preserver", StringComparison.OrdinalIgnoreCase)) return 2;
        if (species.Contains("Iconian", StringComparison.OrdinalIgnoreCase)) return 1;
        if (species.Contains("T'Kon", StringComparison.OrdinalIgnoreCase) || species.Contains("TKon", StringComparison.OrdinalIgnoreCase)) return 0;
        return -1;
    }

    /// <summary>
    /// A ruin: children "Core" (inside r ~7.8; hide it under an Excavation Site), "Rim" (the outliers) and "Far" (the
    /// cheap stand-in shown beyond <see cref="FarFrom"/>; the core and rim fade out there). The stone takes a little of
    /// <paramref name="tint"/>; the Glow starts dark (call <see cref="SetGlow"/>).
    /// </summary>
    public static Node3D Build(int variant, Color tint)
    {
        var key = Keys[(int)((uint)variant % Variants)];
        var root = new Node3D { Name = "Ruin" };
        var mats = new Materials(tint);
        var core = Part(key, "Core", mats);
        var rim = Part($"{key}_rim", "Rim", mats);
        var far = Part("ruin_far", "Far", mats);
        foreach (var near in new[] { core, rim })
        {
            near.VisibilityRangeEnd = FarFrom;
            near.VisibilityRangeEndMargin = 30f;
            root.AddChild(near);
        }
        far.VisibilityRangeBegin = FarFrom;
        far.VisibilityRangeBeginMargin = 30f;
        far.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        root.AddChild(far);
        root.SetMeta(GlowMeta, mats.Glows);
        return root;
    }

    /// <summary>The Glow inlays' strength: 0 = dark (an ordinary dead species), ~0.5 = the faint glow of a precursor
    /// site. Cheap (no tree walk) on a root from <see cref="Build"/>.</summary>
    public static void SetGlow(Node3D ruin, float level)
    {
        if (!ruin.HasMeta(GlowMeta)) return;
        foreach (var v in ruin.GetMeta(GlowMeta).AsGodotArray())
            if (v.AsGodotObject() is BaseMaterial3D m)
            {
                m.EmissionEnabled = level > 0.001f;
                m.EmissionEnergyMultiplier = level;
            }
    }

    // ------------------------------------------------------------------ materials

    /// <summary>One set of material copies per ruin.</summary>
    private sealed class Materials
    {
        private readonly Color _tint;
        private readonly Dictionary<string, BaseMaterial3D> _made = new();
        public readonly Godot.Collections.Array Glows = new();

        public Materials(Color tint) => _tint = tint;

        public BaseMaterial3D Get(string name, Material? source)
        {
            if (_made.TryGetValue(name, out var made)) return made;
            var m = source is BaseMaterial3D b ? (BaseMaterial3D)b.Duplicate() : new StandardMaterial3D { CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            m.ResourceName = name;
            switch (name)
            {
                case "RuinStone":
                    var stone = source is BaseMaterial3D s ? s.AlbedoColor : new Color(0.47f, 0.45f, 0.41f);
                    m.AlbedoColor = stone.Lerp(new Color(_tint.R, _tint.G, _tint.B) * 0.8f, 0.18f);
                    m.VertexColorUseAsAlbedo = source != null;
                    m.Metallic = 0f;
                    m.Roughness = 0.92f;
                    break;
                case "RuinMetal":
                    if (source == null) m.AlbedoColor = new Color(0.34f, 0.29f, 0.22f);
                    m.VertexColorUseAsAlbedo = source != null;
                    m.Metallic = 0.7f;
                    m.Roughness = 0.45f;
                    break;
                case "Glow":
                    m.AlbedoColor = new Color(0.1f, 0.22f, 0.2f);
                    m.VertexColorUseAsAlbedo = false;
                    m.Emission = GlowColour;
                    m.EmissionEnabled = false;
                    m.EmissionEnergyMultiplier = 0f;
                    m.SetMeta(VehicleLights.NoBoostMeta, true); // PlanetView.Sky's night boost: SetGlow drives these
                    Glows.Add(m);
                    break;
            }
            _made[name] = m;
            return m;
        }
    }

    // ------------------------------------------------------------------ parts

    private static MeshInstance3D Part(string key, string name, Materials mats)
    {
        if (!Meshes.TryGetValue(key, out var t)) return Primitive(key, name, mats);
        var mi = new MeshInstance3D { Name = name, Mesh = t.Mesh, Transform = t.At };
        for (var i = 0; i < t.Mesh.GetSurfaceCount(); i++)
        {
            var src = t.Mesh.SurfaceGetMaterial(i);
            var mat = src?.ResourceName ?? "";
            if (mat.Length > 0) mi.SetSurfaceOverrideMaterial(i, mats.Get(mat, src));
        }
        return mi;
    }

    // ------------------------------------------------------------------ primitive stand-ins (no glb)

    /// <summary>A rough stand-in made of boxes and cylinders, merged into one ArrayMesh per part so the node layout
    /// matches the glb's (one MeshInstance3D per part, surface 0 stone, 1 metal, 2 glow).</summary>
    private static MeshInstance3D Primitive(string key, string name, Materials mats)
    {
        var stone = new SurfaceTool();
        var metal = new SurfaceTool();
        var glow = new SurfaceTool();
        stone.Begin(Mesh.PrimitiveType.Triangles);
        metal.Begin(Mesh.PrimitiveType.Triangles);
        glow.Begin(Mesh.PrimitiveType.Triangles);
        var rim = key.EndsWith("_rim", StringComparison.Ordinal);
        var kind = key.Length > 5 ? key[5] : 'f';
        if (rim)
        {
            for (var i = 0; i < 4; i++)
            {
                var a = Mathf.Tau * i / 4 + 0.6f;
                AddBox(stone, new Vector3(8.6f * Mathf.Cos(a), 0f, 8.6f * Mathf.Sin(a)), new Vector3(1.4f, 0.7f + 0.3f * i, 0.8f), Mathf.RadToDeg(a));
            }
        }
        else if (kind == 'f')
        {
            AddCylinder(stone, Vector3.Zero, 7.8f, 0.15f, 9);
            AddBox(stone, new Vector3(-3f, 0f, 2f), new Vector3(2.6f, 1.5f, 0.6f), 20f);
            AddCylinder(stone, new Vector3(1.5f, 0f, -1f), 0.6f, 4.5f, 5);
        }
        else
        {
            AddCylinder(stone, Vector3.Zero, 8f, 0.15f, 12);
            for (var i = 0; i < 7; i++)
            {
                var a = Mathf.Tau * i / 7;
                var p = new Vector3(5.5f * Mathf.Cos(a), 0f, 5.5f * Mathf.Sin(a));
                if (kind == 'c') AddCylinder(stone, p, 0.7f, 5f + i % 3, 6);
                else AddBox(stone, p, new Vector3(2.6f, 0.6f + (i % 3) * 0.9f, 0.6f), -Mathf.RadToDeg(a) + 90f);
            }
            if (kind == 'b') AddCylinder(stone, new Vector3(0.8f, 0f, -1.6f), 4f, 3.6f, 12);
            AddCylinder(metal, new Vector3(0f, 0.15f, 0f), 1.5f, 0.3f, 12);
            AddCylinder(glow, new Vector3(0f, 0.45f, 0f), 0.7f, 0.1f, 9);
        }
        var mesh = new ArrayMesh();
        foreach (var (st, mat) in new[] { (stone, "RuinStone"), (metal, "RuinMetal"), (glow, "Glow") })
        {
            var arrays = st.CommitToArrays();
            if (arrays.Count == 0 || arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length == 0)
            {
                // keep the surface order (stone, metal, glow) with a degenerate triangle
                var d = new SurfaceTool();
                d.Begin(Mesh.PrimitiveType.Triangles);
                d.SetNormal(Vector3.Up);
                for (var i = 0; i < 3; i++) d.AddVertex(Vector3.Zero);
                arrays = d.CommitToArrays();
            }
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mats.Get(mat, null));
        }
        var mi = new MeshInstance3D { Name = name, Mesh = mesh };
        for (var i = 0; i < mesh.GetSurfaceCount(); i++) mi.SetSurfaceOverrideMaterial(i, mesh.SurfaceGetMaterial(i));
        return mi;
    }

    private static void AddBox(SurfaceTool st, Vector3 at, Vector3 size, float yawDeg)
    {
        var b = new Basis(Vector3.Up, Mathf.DegToRad(yawDeg));
        var h = size / 2f;
        Vector3 P(float x, float y, float z) => at + b * new Vector3(x * h.X, y * size.Y, z * h.Z);
        var c = new[] { P(-1, 0, -1), P(1, 0, -1), P(1, 0, 1), P(-1, 0, 1), P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1) };
        int[][] faces = { new[] { 4, 7, 6, 5 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
        foreach (var f in faces) Quad(st, c[f[0]], c[f[1]], c[f[2]], c[f[3]]);
    }

    private static void AddCylinder(SurfaceTool st, Vector3 at, float r, float h, int segs)
    {
        for (var i = 0; i < segs; i++)
        {
            var a0 = Mathf.Tau * i / segs;
            var a1 = Mathf.Tau * (i + 1) / segs;
            var p0 = at + new Vector3(r * Mathf.Cos(a0), 0f, r * Mathf.Sin(a0));
            var p1 = at + new Vector3(r * Mathf.Cos(a1), 0f, r * Mathf.Sin(a1));
            var up = new Vector3(0f, h, 0f);
            Quad(st, p0, p1, p1 + up, p0 + up);
            Tri(st, at + up, p1 + up, p0 + up);
        }
    }

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Tri(st, a, b, c);
        Tri(st, a, c, d);
    }

    /// <summary>A flat-shaded triangle (the stand-in's materials are double-sided, so winding doesn't matter).</summary>
    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        var n = (b - a).Cross(c - a);
        if (n.Y < 0f) n = -n; // upward-ish normals read best from the RTS camera
        n = n.LengthSquared() > 1e-12f ? n.Normalized() : Vector3.Up;
        st.SetNormal(n);
        st.AddVertex(a);
        st.SetNormal(n);
        st.AddVertex(b);
        st.SetNormal(n);
        st.AddVertex(c);
    }
}
