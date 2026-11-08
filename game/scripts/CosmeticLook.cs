using System;
using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Puts the local player's achievement cosmetics (<see cref="Cosmetics"/>) on their own models (render-only; callers
/// only dress models the local player owns, never other players').
/// <list type="bullet">
/// <item>Vehicles and drop ships (<see cref="Dress"/>): the "Owner" stripes take <see cref="Cosmetics.Paint"/> (else the
/// finish's own trim colour, else they stay the player colour); a <see cref="Cosmetics.Finish"/> re-materials the hull
/// paint surfaces (materials named "…Paint", "Hull", "HullPanel": colour, metallic, roughness, clearcoat, rim from
/// <see cref="Cosmetics.FinishPreset"/>); a <see cref="Cosmetics.Pattern"/> (or the brushed gold finish) swaps those
/// surfaces for shaders/livery.gdshader, which reproduces the plain material and paints the band / chevrons / stripes
/// in Paint and Paint2 in the vehicle's model space. RoverLook's grime overlay stays on top (it is a MaterialOverlay).</item>
/// <item>Dome glass (<see cref="DressGlass"/>): surfaces "Glass" / "GlassFrost" take <see cref="Cosmetics.DomeTint"/>.</item>
/// </list>
/// Each dressed model remembers what its surfaces held, so <see cref="Refresh"/> (once a frame) can re-apply the picks
/// when <see cref="Cosmetics.Version"/> changes, or put the standard look back. Materials are made only when a model is
/// dressed or the picks change (shared copies per source material and version; one livery shader material per patterned
/// surface), never per frame. A surface someone else has replaced since (e.g. a construction hologram) is left alone.
/// </summary>
public static class CosmeticLook
{
    /// <summary>Mean brightness of the baked hull paint textures (clean paint 0.8 with scuffs), so a finish colour on a
    /// textured surface comes out at about the colour asked for.</summary>
    private const float TexMean = 0.78f;

    /// <summary>Brightness of the plain "Hull" paint: a "HullPanel" (darker) keeps its shade under a finish.</summary>
    private const float HullLuma = 0.78f;

    /// <summary>Second pattern colour when none is picked.</summary>
    private static readonly Color DefaultPaint2 = new(0.07f, 0.08f, 0.1f);

    private enum Role { Owner, Hull, Glass, GlassFrost }

    private sealed class Slot
    {
        public required MeshInstance3D Mesh;
        public required int Surface;           // -1 = the mesh's MaterialOverride
        public required Material? Original;     // what the slot held (a surface override may be null = the mesh's own)
        public required BaseMaterial3D Source;  // the material it showed
        public required Role Role;
        public required Transform3D MeshToModel;
        public Material? Written;               // what we last put there (starts as Original)
    }

    private sealed class Dressed
    {
        public required Node3D Model;
        public required Color Owner;
        public readonly List<Slot> Slots = new();
        public readonly List<Label3D> Decals = new();
        public Vector3 BoxMin, BoxSize = Vector3.One;
    }

    private static readonly List<Dressed> Models = new();
    private static readonly Dictionary<(ulong, Role), Material> Copies = new();
    private static int _copiesVersion = int.MinValue, _appliedVersion = int.MinValue;
    private static int _sinceSweep;
    private static Shader? _livery;
    private static readonly StringName DressedMeta = "cosmetics_dressed";

    /// <summary>Ink for our painted names and numbers under the current finish.</summary>
    public static Color Ink => Cosmetics.CurrentFinish?.Ink ?? Cosmetics.NavyInk;

    /// <summary>Dress one of our vehicles or drop ships (call once, right after the factory and RoverLook.Paint).
    /// <paramref name="owner"/> = the player colour (the pattern's paint when no Paint is picked).</summary>
    public static void Dress(Node3D model, Color owner) => Register(model, owner, glass: false);

    /// <summary>Dress one of our domes: every "Glass" / "GlassFrost" surface under <paramref name="model"/> (which may
    /// itself be the glass mesh) follows <see cref="Cosmetics.DomeTint"/>.</summary>
    public static void DressGlass(Node3D model) => Register(model, Colors.White, glass: true);

    /// <summary>Once a frame: when the picks changed, re-apply them to every dressed model still alive.</summary>
    public static void Refresh()
    {
        if (_appliedVersion == Cosmetics.Version) return;
        _appliedVersion = Cosmetics.Version;
        Sweep();
        foreach (var d in Models) Apply(d);
    }

    // ---------------------------------------------------------------- set-up

    private static void Register(Node3D model, Color owner, bool glass)
    {
        if (!GodotObject.IsInstanceValid(model) || model.HasMeta(DressedMeta)) return;
        var d = new Dressed { Model = model, Owner = owner };
        Collect(model, model, d, glass);
        if (d.Slots.Count == 0 && d.Decals.Count == 0) return;
        model.SetMeta(DressedMeta, true);
        if (!glass) FitBox(d);
        if (++_sinceSweep >= 32) Sweep();
        Models.Add(d);
        Apply(d);
    }

    private static void Collect(Node node, Node3D model, Dressed d, bool glass)
    {
        switch (node)
        {
            case CpuParticles3D or GpuParticles3D:
                return;
            case Label3D label when !glass && label.Name == "Decal":
                d.Decals.Add(label);
                break;
            case MeshInstance3D mi when mi.Mesh != null:
                var toModel = RelativeTo(mi, model);
                if (mi.MaterialOverride != null)
                {
                    // An override hides every surface: only it can be dressed.
                    if (mi.MaterialOverride is BaseMaterial3D over && RoleOf(over.ResourceName, glass) is { } r)
                        d.Slots.Add(new Slot { Mesh = mi, Surface = -1, Original = over, Source = over, Role = r, MeshToModel = toModel, Written = over });
                }
                else
                    for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                    {
                        var original = mi.GetSurfaceOverrideMaterial(i);
                        if ((original ?? mi.Mesh.SurfaceGetMaterial(i)) is BaseMaterial3D shown && RoleOf(shown.ResourceName, glass) is { } r)
                            d.Slots.Add(new Slot { Mesh = mi, Surface = i, Original = original, Source = shown, Role = r, MeshToModel = toModel, Written = original });
                    }
                break;
        }
        foreach (var child in node.GetChildren()) Collect(child, model, d, glass);
    }

    private static Role? RoleOf(string name, bool glass)
    {
        if (glass) return name switch { "Glass" => Role.Glass, "GlassFrost" => Role.GlassFrost, _ => null };
        if (name == "Owner") return Role.Owner;
        if (name is "Hull" or "HullPanel" || (name.Length > 5 && name.EndsWith("Paint", StringComparison.Ordinal))) return Role.Hull;
        return null;
    }

    /// <summary>The hull surfaces' box in model space, for the pattern's proportions.</summary>
    private static void FitBox(Dressed d)
    {
        Aabb? box = null;
        var seen = new HashSet<ulong>();
        foreach (var s in d.Slots)
        {
            if (s.Role != Role.Hull || !seen.Add(s.Mesh.GetInstanceId())) continue;
            var b = s.MeshToModel * s.Mesh.GetAabb();
            box = box is { } a ? a.Merge(b) : b;
        }
        if (box is not { } found) return;
        d.BoxMin = found.Position;
        d.BoxSize = new Vector3(Mathf.Max(found.Size.X, 0.01f), Mathf.Max(found.Size.Y, 0.01f), Mathf.Max(found.Size.Z, 0.01f));
    }

    /// <summary>A node's transform relative to an ancestor, from the local transforms (works before it's in the tree).</summary>
    private static Transform3D RelativeTo(Node3D node, Node3D ancestor)
    {
        if (node == ancestor) return Transform3D.Identity;
        var t = node.Transform;
        for (var p = node.GetParent(); p != null && p != ancestor; p = p.GetParent())
            if (p is Node3D p3) t = p3.Transform * t;
        return t;
    }

    private static void Sweep()
    {
        _sinceSweep = 0;
        Models.RemoveAll(d => !GodotObject.IsInstanceValid(d.Model) || d.Model.IsQueuedForDeletion());
        if (Models.Count == 0) Copies.Clear();
    }

    // ---------------------------------------------------------------- applying the picks

    private static void Apply(Dressed d)
    {
        var finish = Cosmetics.CurrentFinish;
        var pattern = Cosmetics.PatternCode(Cosmetics.Pattern);
        var trim = Cosmetics.Paint ?? finish?.Trim;
        foreach (var s in d.Slots)
        {
            if (!GodotObject.IsInstanceValid(s.Mesh)) continue;
            var want = s.Role switch
            {
                Role.Owner => trim is { } c ? Copy(s.Source, Role.Owner, () => Trimmed(s.Source, c)) : s.Original,
                Role.Hull => HullMaterial(d, s, finish, pattern),
                _ => Cosmetics.DomeTint is { } t ? Copy(s.Source, s.Role, () => Tinted(s.Source, t, s.Role == Role.GlassFrost)) : s.Original,
            };
            Write(s, want);
        }
        var ink = Ink;
        foreach (var label in d.Decals)
            if (GodotObject.IsInstanceValid(label)) label.Modulate = ink; // vehicles with grime: RoverLook.Grime.Ink keeps it
    }

    private static void Write(Slot s, Material? m)
    {
        var current = s.Surface < 0 ? s.Mesh.MaterialOverride : s.Mesh.GetSurfaceOverrideMaterial(s.Surface);
        if (!Same(current, s.Written)) return; // someone else's material now (a hologram, a fade): leave it
        if (Same(current, m)) return;
        if (s.Surface < 0) s.Mesh.MaterialOverride = m;
        else s.Mesh.SetSurfaceOverrideMaterial(s.Surface, m);
        s.Written = m;
    }

    private static bool Same(GodotObject? a, GodotObject? b) =>
        a == null ? b == null : b != null && a.GetInstanceId() == b.GetInstanceId();

    /// <summary>One shared copy per source material, role and pick version.</summary>
    private static Material Copy(BaseMaterial3D source, Role role, Func<Material> make)
    {
        if (_copiesVersion != Cosmetics.Version)
        {
            Copies.Clear();
            _copiesVersion = Cosmetics.Version;
        }
        var key = (source.GetInstanceId(), role);
        if (!Copies.TryGetValue(key, out var m)) Copies[key] = m = make();
        return m;
    }

    private static Material? HullMaterial(Dressed d, Slot s, Cosmetics.FinishLook? finish, int pattern)
    {
        var brushed = finish is { Brushed: true };
        if (finish == null && pattern == 0) return s.Original;
        var plain = finish is { } f ? (BaseMaterial3D)Copy(s.Source, Role.Hull, () => Finished(s.Source, f)) : s.Source;
        if (pattern == 0 && !brushed) return plain;
        return Livery(plain, s, d, pattern, brushed);
    }

    private static BaseMaterial3D Trimmed(BaseMaterial3D source, Color c)
    {
        var m = (BaseMaterial3D)source.Duplicate();
        m.AlbedoColor = new Color(c.R, c.G, c.B, source.AlbedoColor.A);
        if (m.EmissionEnabled) m.Emission = c;
        return m;
    }

    private static BaseMaterial3D Finished(BaseMaterial3D source, Cosmetics.FinishLook f)
    {
        var m = (BaseMaterial3D)source.Duplicate();
        var shade = source.AlbedoTexture != null ? 1f / TexMean : Luma(source.AlbedoColor) / HullLuma;
        m.AlbedoColor = new Color(Mathf.Min(1f, f.Hull.R * shade), Mathf.Min(1f, f.Hull.G * shade), Mathf.Min(1f, f.Hull.B * shade), source.AlbedoColor.A);
        m.Metallic = f.Metallic;
        m.Roughness = f.Roughness;
        m.ClearcoatEnabled = f.Clearcoat > 0f;
        m.Clearcoat = f.Clearcoat;
        m.ClearcoatRoughness = f.ClearcoatRoughness;
        m.RimEnabled = f.Rim > 0f;
        m.Rim = f.Rim;
        m.RimTint = f.RimTint;
        return m;
    }

    private static BaseMaterial3D Tinted(BaseMaterial3D source, Color tint, bool frost)
    {
        var m = (BaseMaterial3D)source.Duplicate();
        var a = source.AlbedoColor.A;
        m.AlbedoColor = frost
            ? new Color(Mathf.Lerp(source.AlbedoColor.R, tint.R, 0.5f), Mathf.Lerp(source.AlbedoColor.G, tint.G, 0.5f), Mathf.Lerp(source.AlbedoColor.B, tint.B, 0.5f), a)
            : new Color(tint.R, tint.G, tint.B, Mathf.Clamp(a * 1.8f, 0.12f, 0.4f));
        if (m.EmissionEnabled) m.Emission = tint;
        if (m.RimEnabled) m.RimTint = Mathf.Max(m.RimTint, 0.6f); // the rim glints in the tint
        return m;
    }

    /// <summary>The livery shader for one surface (its own material: the mesh's rest transform is a uniform).</summary>
    private static ShaderMaterial Livery(BaseMaterial3D b, Slot s, Dressed d, int pattern, bool brushed)
    {
        _livery ??= GD.Load<Shader>("res://shaders/livery.gdshader");
        var m = new ShaderMaterial { Shader = _livery };
        m.SetShaderParameter("albedo", b.AlbedoColor);
        if (b.AlbedoTexture != null) m.SetShaderParameter("albedo_texture", b.AlbedoTexture);
        m.SetShaderParameter("tex_mean", b.AlbedoTexture != null ? TexMean : 1f);
        m.SetShaderParameter("metallic", b.Metallic);
        m.SetShaderParameter("roughness", b.Roughness);
        m.SetShaderParameter("specular", b.MetallicSpecular);
        m.SetShaderParameter("clearcoat", b.ClearcoatEnabled ? b.Clearcoat : 0f);
        m.SetShaderParameter("clearcoat_roughness", b.ClearcoatRoughness);
        m.SetShaderParameter("rim", b.RimEnabled ? b.Rim : 0f);
        m.SetShaderParameter("rim_tint", b.RimTint);
        m.SetShaderParameter("brushed", brushed ? 1f : 0f);
        m.SetShaderParameter("pattern", pattern);
        m.SetShaderParameter("paint", Cosmetics.Paint ?? d.Owner);
        m.SetShaderParameter("paint2", Cosmetics.Paint2 ?? DefaultPaint2);
        m.SetShaderParameter("mesh_to_model", s.MeshToModel);
        m.SetShaderParameter("box_min", d.BoxMin);
        m.SetShaderParameter("box_size", d.BoxSize);
        return m;
    }

    private static float Luma(Color c) => (c.R + c.G + c.B) / 3f;
}
