using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Domes and containment fields on a Domes world (hostile worlds contract, docs/hostile-worlds-contract.md; render-only).
// - Every structure the sim says covers ground (PlanetSurface.DomeRadiusOf(s) > 0: a finished core 1 tile, a Pressure
//   Dome 2) gets a containment-field shell: a squashed hemisphere reaching (radius + ½) hexes, with a skirt below the
//   equator so it meets sloping ground (shaders/containment_field.gdshader: soft blue-white fresnel rim, slow hex
//   shimmer, nearly invisible face-on, a glow where it meets the ground; it rises from the ground when it first appears
//   and flickers while the base's life support (LifeSupportOf) is below 0.5).
// - Every finished colony core gets a see-through glass dome over its own hex (the core glb has none), with a vehicle
//   bay opening toward its repair pad and a crew door over the core's door, and a few frame ribs.
// - Rival domes only while Shows(s) (in sight or remembered).
// - While placing a crewed building (or a Pressure Dome) the covered hexes light up (ShowCover, called by PlanetDriver)
//   and the shells brighten; a Pressure Dome being placed previews its own 2-tile ring.
// - Crews outside cover wear EVA suits: NeedsSuit / SuitUp, used by the walkers and repair crews.
public partial class PlanetView
{
    /// <summary>A Pressure Dome's field reach in tiles (the contract's number; the sim's DomeRadiusOf is the truth once
    /// one stands, this is only for the placement preview).</summary>
    private const int PressureDomeRadius = 2;

    /// <summary>The shell's height as a share of its radius (a full hemisphere over two tiles would tower ~60 units).</summary>
    private const float FieldHeightShare = 0.6f;

    private const float GlassRadius = 9.6f, GlassHeight = 12.6f; // over the core glb (≈ 8.9 wide, mast 11.9 tall)

    private static readonly Color FieldColour = new(0.62f, 0.8f, 1f);

    private sealed class DomeVisual
    {
        public Node3D? Field;
        public ShaderMaterial? FieldMaterial;
        public int Radius;
        public float Reveal;
        public Node3D? Glass;
        public int TintVersion = int.MinValue; // Cosmetics.Version the field colour was set for (ours only)
    }

    private readonly Dictionary<int, DomeVisual> _domes = new();
    private readonly HashSet<int> _domesSeen = new();
    private Node3D? _domeRoot;
    private static Shader? _fieldShader;
    private static ArrayMesh? _fieldMesh;
    private static ArrayMesh? _glassMesh;
    private static StandardMaterial3D? _glassMaterial, _frameMaterial;
    private MeshInstance3D? _coverRings;
    private long _coverSignature = long.MinValue;
    private float _fieldBoost = 1f;

    /// <summary>True on a world where crewed buildings need a dome or field (the sim's tier).</summary>
    private bool DomesWorld => _surface.Tier == HabitabilityTier.Domes;

    /// <summary>Once a frame from Refresh: add, update and remove the fields and glass domes.</summary>
    private void UpdateDomes(float delta)
    {
        if (!DomesWorld)
        {
            if (_domes.Count > 0) ClearDomes();
            return;
        }
        _domeRoot ??= AddDomeRoot();
        var seen = _domesSeen;
        seen.Clear();
        foreach (var s in _surface.Structures)
        {
            var radius = _surface.DomeRadiusOf(s);
            var core = _sim.Data.Building(s.DefId).IsCore && s.State == StructureState.Complete;
            if ((radius <= 0 && !core) || !Shows(s)) continue;
            seen.Add(s.Id);
            if (!_domes.TryGetValue(s.Id, out var v)) _domes[s.Id] = v = new DomeVisual();
            var centre = TileCentre(s.Tile);
            centre.Y = GroundAt(centre);

            if (core && v.Glass == null)
            {
                v.Glass = NewGlassDome();
                v.Glass.Position = centre;
                v.Glass.Basis = new Basis(Vector3.Up, Mathf.DegToRad(-60f * s.Facing));
                if (s.Owner == _localPlayer) CosmeticLook.DressGlass(v.Glass); // achievement dome tint (ours only)
                _domeRoot.AddChild(v.Glass);
            }
            else if (!core && v.Glass != null)
            {
                v.Glass.QueueFree();
                v.Glass = null;
            }

            if (radius > 0 && (v.Field == null || v.Radius != radius))
            {
                v.Field?.QueueFree();
                (v.Field, v.FieldMaterial) = NewField(radius, s.Id);
                v.Field.Position = centre;
                v.Radius = radius;
                v.Reveal = 0f;
                v.TintVersion = int.MinValue;
                _domeRoot.AddChild(v.Field);
            }
            else if (radius <= 0 && v.Field != null)
            {
                v.Field.QueueFree();
                v.Field = null;
                v.FieldMaterial = null;
                v.Radius = 0;
            }
            if (v.FieldMaterial != null)
            {
                v.Reveal = Mathf.Min(1f, v.Reveal + delta / 1.8f);
                var baseId = s.BaseId != 0 ? s.BaseId : s.Id;
                var life = Mathf.Clamp(_surface.LifeSupportOf(baseId).ToFloat(), 0f, 1f);
                var flicker = life < 0.5f ? Mathf.Clamp(1f - life * 2f, 0.25f, 1f) : 0f;
                // Other players' remembered fields (out of sight) stay dim and steady: we can't see their state.
                var live = ShowsLive(s.Owner, s.Tile);
                v.FieldMaterial.SetShaderParameter("reveal", v.Reveal);
                v.FieldMaterial.SetShaderParameter("flicker", live ? flicker : 0f);
                v.FieldMaterial.SetShaderParameter("strength", (live ? 1f : 0.5f) * _fieldBoost);
                if (s.Owner == _localPlayer && v.TintVersion != Cosmetics.Version)
                {
                    // Achievement dome tint on our own fields (others keep the standard blue-white).
                    v.TintVersion = Cosmetics.Version;
                    v.FieldMaterial.SetShaderParameter("field_colour", Cosmetics.DomeTint is { } tint ? FieldColour.Lerp(tint, 0.75f) : FieldColour);
                }
            }
        }
        if (seen.Count == _domes.Count) return;
        foreach (var id in _domes.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _domes[id].Field?.QueueFree();
            _domes[id].Glass?.QueueFree();
            _domes.Remove(id);
        }
    }

    private Node3D AddDomeRoot()
    {
        var root = new Node3D { Name = "Domes" };
        AddChild(root);
        return root;
    }

    private void ClearDomes()
    {
        foreach (var v in _domes.Values)
        {
            v.Field?.QueueFree();
            v.Glass?.QueueFree();
        }
        _domes.Clear();
    }

    // ---------------------------------------------------------------- cover rings while placing

    /// <summary>
    /// While placing a building (PlanetDriver calls this every frame; null = not placing): on a Domes world, light the
    /// hexes the player's domes and fields cover when the building is crewed, and brighten the shells; a Pressure Dome
    /// also previews its own ring at <paramref name="tile"/>. Hidden otherwise.
    /// </summary>
    public void ShowCover(string? placing, int tile)
    {
        var def = placing == null ? null : _sim.Data.Buildings.FirstOrDefault(b => b.Id == placing);
        var dome = placing == "pressure_dome";
        var show = DomesWorld && def != null && (dome || IsCrewed(def));
        _fieldBoost = show ? 1.8f : 1f;
        if (!show)
        {
            if (_coverRings != null) _coverRings.Visible = false;
            _coverSignature = long.MinValue;
            return;
        }

        // Rebuild only when what is covered (or the previewed dome's tile) changes.
        var signature = 17L + (dome ? tile + 1 : 0) * 7919L;
        foreach (var s in _surface.Structures)
            if (s.Owner == _localPlayer) signature = signature * 31 + s.Id * 13 + _surface.DomeRadiusOf(s);
        if (_coverRings != null && signature == _coverSignature)
        {
            _coverRings.Visible = true;
            return;
        }
        _coverSignature = signature;

        var covered = new HashSet<int>();
        foreach (var s in _surface.Structures)
        {
            if (s.Owner != _localPlayer) continue;
            var r = _surface.DomeRadiusOf(s);
            if (r > 0) foreach (var t in _surface.Map.Grid.Within(s.Tile, r)) covered.Add(t);
        }
        var preview = new HashSet<int>();
        if (dome && tile >= 0)
            foreach (var t in _surface.Map.Grid.Within(tile, PressureDomeRadius))
                if (!covered.Contains(t)) preview.Add(t);

        if (_coverRings == null)
        {
            _coverRings = new MeshInstance3D { Name = "CoverRings" };
            AddChild(_coverRings);
        }
        foreach (var c in _coverRings.GetChildren()) c.QueueFree();
        AddRing(covered, new Color(0.62f, 0.8f, 1f, 0.16f));
        AddRing(preview, new Color(0.55f, 1f, 0.85f, 0.14f));
        _coverRings.Visible = true;
    }

    private void AddRing(HashSet<int> tiles, Color colour)
    {
        if (tiles.Count == 0) return;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CylinderMesh { TopRadius = HexRadius * 0.9f, BottomRadius = HexRadius * 0.9f, Height = 0.12f, RadialSegments = 6, Rings = 1 },
        };
        mm.InstanceCount = tiles.Count;
        var i = 0;
        foreach (var t in tiles) mm.SetInstanceTransform(i++, new Transform3D(Basis.Identity, TileCentre(t) + new Vector3(0, 0.25f, 0)));
        _coverRings!.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = Overlay(colour) });
    }

    private static readonly System.Reflection.PropertyInfo? CrewedProperty = typeof(BuildingDef).GetProperty("Crewed");

    /// <summary>
    /// Whether a building needs cover on a Domes world. Reads <c>BuildingDef.Crewed</c> once the sim has it (the contract
    /// stubs don't yet); until then the design doc's automated buildings (mines, storage, reactor, refinery) count as
    /// uncrewed and everything else, cores excepted, as crewed.
    /// </summary>
    private static bool IsCrewed(BuildingDef def)
    {
        if (CrewedProperty?.GetValue(def) is bool crewed) return crewed;
        if (def.IsCore) return false;
        return def.Id is not ("mine" or "deep_core_mine" or "storage_container" or "fusion_reactor" or "refinery" or "pressure_dome");
    }

    // ---------------------------------------------------------------- EVA suits

    /// <summary>Whether a person of this player standing on this tile is outside cover (EVA suit) on this world.</summary>
    private bool NeedsSuit(int owner, int tile)
    {
        if (tile < 0) return false;
        return _surface.Tier switch
        {
            HabitabilityTier.Suits => true,
            HabitabilityTier.Domes or HabitabilityTier.Tech => !_surface.Covered(owner, tile),
            _ => false,
        };
    }

    // ---------------------------------------------------------------- meshes

    private (Node3D Node, ShaderMaterial Material) NewField(int radius, int id)
    {
        _fieldShader ??= GD.Load<Shader>("res://shaders/containment_field.gdshader");
        _fieldMesh ??= DomeMesh(48, 14);
        var r = (radius + 0.5f) * HexWidth;
        var material = new ShaderMaterial { Shader = _fieldShader };
        material.SetShaderParameter("field_colour", FieldColour);
        material.SetShaderParameter("hex_cells", 6f + 3f * radius);
        material.SetShaderParameter("seed", (id * 0.6180339f) % 1f * 100f);
        material.SetShaderParameter("reveal", 0f);
        var node = new MeshInstance3D
        {
            Name = $"Field{id}",
            Mesh = _fieldMesh,
            MaterialOverride = material,
            Scale = new Vector3(r, r * FieldHeightShare, r),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        return (node, material);
    }

    private static Node3D NewGlassDome()
    {
        _glassMesh ??= GlassMesh();
        var node = new MeshInstance3D
        {
            Name = "GlassDome",
            Mesh = _glassMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        return node;
    }

    /// <summary>The core's glass dome: a squashed hemisphere (GlassRadius × GlassHeight) with two openings in the
    /// core's own frame: the vehicle bay toward the repair pad (BuildingModels.RepairPadOffset) and the crew door over
    /// the core's door (+Z); plus frame ribs (8 meridians, 2 parallels and a foot ring) as a second surface.</summary>
    private static ArrayMesh GlassMesh()
    {
        _glassMaterial ??= new StandardMaterial3D
        {
            ResourceName = "Glass", // CosmeticLook.DressGlass tints ours by this name
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.72f, 0.84f, 0.95f, 0.13f),
            Metallic = 0.4f,
            Roughness = 0.04f,
            MetallicSpecular = 0.9f,
            RimEnabled = true,
            Rim = 0.6f,
            RimTint = 0.3f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _frameMaterial ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.62f, 0.65f, 0.68f),
            Metallic = 0.7f,
            Roughness = 0.35f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var pad = BuildingModels.RepairPadOffset;
        var padAngle = Mathf.Atan2(pad.Z, pad.X);
        var doorAngle = Mathf.Pi / 2f; // +Z
        bool Open(float angle, float y)
        {
            var dPad = Mathf.Abs(Mathf.Wrap(angle - padAngle, -Mathf.Pi, Mathf.Pi));
            var dDoor = Mathf.Abs(Mathf.Wrap(angle - doorAngle, -Mathf.Pi, Mathf.Pi));
            return (dPad < 0.62f && y < 7.5f) || (dDoor < 0.24f && y < 3.4f);
        }
        Vector3 At(float angle, float lat) => new(Mathf.Cos(angle) * Mathf.Cos(lat) * GlassRadius, Mathf.Sin(lat) * GlassHeight, Mathf.Sin(angle) * Mathf.Cos(lat) * GlassRadius);

        var mesh = new ArrayMesh();
        // Glass.
        const int segs = 64, rings = 16;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (var j = 0; j < rings; j++)
            for (var i = 0; i < segs; i++)
            {
                float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
                float l0 = Mathf.Pi / 2f * j / rings, l1 = Mathf.Pi / 2f * (j + 1) / rings;
                var mid = At((a0 + a1) * 0.5f, (l0 + l1) * 0.5f);
                if (Open(Mathf.Atan2(mid.Z, mid.X), mid.Y)) continue;
                Quad(st, At(a0, l0), At(a1, l0), At(a1, l1), At(a0, l1));
            }
        st.SetMaterial(_glassMaterial);
        st.Commit(mesh);

        // Frame: 8 meridians (stopping at the openings), 2 parallels and a foot ring.
        var fr = new SurfaceTool();
        fr.Begin(Mesh.PrimitiveType.Triangles);
        const float half = 0.11f;
        for (var m = 0; m < 8; m++)
        {
            var a = Mathf.Tau * (m + 0.5f) / 8f;
            for (var j = 0; j < rings; j++)
            {
                float l0 = Mathf.Pi / 2f * j / rings, l1 = Mathf.Pi / 2f * (j + 1) / rings;
                var p0 = At(a, l0);
                var p1 = At(a, l1);
                if (Open(a, (p0.Y + p1.Y) * 0.5f)) continue;
                var side = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * half;
                Quad(fr, p0 - side, p0 + side, p1 + side, p1 - side);
            }
        }
        foreach (var lat in new[] { 0.02f, 0.5f, 1.02f })
            for (var i = 0; i < segs; i++)
            {
                float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
                var p0 = At(a0, lat);
                var p1 = At(a1, lat);
                if (Open(Mathf.Atan2((p0.Z + p1.Z) * 0.5f, (p0.X + p1.X) * 0.5f), (p0.Y + p1.Y) * 0.5f)) continue;
                var h = new Vector3(0, half * 1.6f, 0);
                Quad(fr, p0 - h, p1 - h, p1 + h, p0 + h);
            }
        fr.SetMaterial(_frameMaterial);
        fr.Commit(mesh);
        return mesh;
    }

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var n = (b - a).Cross(d - a);
        if (n.LengthSquared() < 1e-10f) n = (c - b).Cross(d - b);
        n = n.Normalized();
        if (n.Dot((a + b + c + d) * 0.25f) < 0f) n = -n; // outward
        foreach (var p in new[] { a, b, c, a, c, d })
        {
            st.SetNormal(n);
            st.AddVertex(p);
        }
    }

    /// <summary>A unit dome (radius 1, height 1; y up) for the field shell: latitude rings from the top down to the
    /// equator, then a vertical skirt to y = -0.35 (it reaches down into dips round the edge). UV = (azimuth / τ,
    /// 0 at the top .. 1 at the equator).</summary>
    private static ArrayMesh DomeMesh(int segs, int rings)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        var rows = rings + 2; // + the skirt
        for (var j = 0; j <= rows; j++)
            for (var i = 0; i <= segs; i++)
            {
                var a = Mathf.Tau * i / segs;
                Vector3 p, n;
                if (j <= rings)
                {
                    var lat = Mathf.Pi / 2f * (1f - j / (float)rings);
                    p = new Vector3(Mathf.Cos(a) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Sin(a) * Mathf.Cos(lat));
                    n = p;
                }
                else
                {
                    var y = -0.35f * (j - rings) / 2f;
                    p = new Vector3(Mathf.Cos(a), y, Mathf.Sin(a));
                    n = new Vector3(p.X, 0, p.Z);
                }
                verts.Add(p);
                normals.Add(n.Normalized());
                uvs.Add(new Vector2(i / (float)segs, j / (float)rings));
            }
        for (var j = 0; j < rows; j++)
            for (var i = 0; i < segs; i++)
            {
                int a = j * (segs + 1) + i, b = a + 1, c = a + segs + 1, d = c + 1;
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
