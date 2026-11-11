using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Comms coverage on the surface (docs/missions-contract.md B; render-only): while the overlay is on (ShowComms, the UI's
// toggle key) or a drone order is armed (ArmComms), each of our coverage sources (PlanetSurface.CommsSources: a core's
// antenna, a relay satellite's footprint) draws the hex rim of the area it covers (shaders/comms_overlay.gdshader:
// cyan for a core, teal-green for a relay, dashed and dim for a relay still drifting to its station), every covered
// hex gets a faint fill, and a ping ring sweeps out from each ready source every 3 s. Meshes are rebuilt only when the
// sources change (kind, tile, radius, ready), never per frame. HexRimMesh is shared with the mission markers.
public partial class PlanetView
{
    private static readonly Color CoreCommsColour = new(0.45f, 0.9f, 1f), RelayCommsColour = new(0.45f, 1f, 0.65f);
    private const float CommsPingSeconds = 3f;

    private Node3D? _commsRoot;
    private bool _commsOn, _commsArmed;
    private long _commsSignature = long.MinValue;
    private static Shader? _rimShader;
    private readonly List<(MeshInstance3D Ring, StandardMaterial3D Material, Vector3 Centre, float Radius, float Phase)> _commsPings = new();
    private readonly HashSet<int> _rimSet = new();
    private readonly HashSet<int> _commsUnion = new();

    /// <summary>The COMMS overlay toggle (the UI's key).</summary>
    public void ShowComms(bool on) => _commsOn = on;

    /// <summary>Whether the toggle is on.</summary>
    public bool CommsShown => _commsOn;

    /// <summary>Shows the overlay while an order that depends on coverage is armed (drone launch / move; the toggle
    /// stays as it was). Call with false when it is disarmed.</summary>
    public void ArmComms(bool armed) => _commsArmed = armed;

    /// <summary>Once a frame from Refresh.</summary>
    private void UpdateComms(float delta)
    {
        var show = _commsOn || _commsArmed;
        if (!show)
        {
            if (_commsRoot != null) _commsRoot.Visible = false;
            return;
        }
        var sources = _surface.CommsSources(_localPlayer);
        var signature = 23L;
        for (var i = 0; i < sources.Count; i++)
        {
            var c = sources[i];
            signature = ((signature * 31 + (int)c.Kind) * 31 + c.Tile) * 31 + c.Radius * 2 + (c.Ready ? 1 : 0);
        }
        if (_commsRoot == null)
        {
            _commsRoot = new Node3D { Name = "CommsOverlay" };
            AddChild(_commsRoot);
        }
        _commsRoot.Visible = true;
        if (signature != _commsSignature)
        {
            _commsSignature = signature;
            RebuildComms(sources);
        }
        for (var i = 0; i < _commsPings.Count; i++)
        {
            var (ring, material, centre, radius, phase) = _commsPings[i];
            var f = Mathf.PosMod(_time / CommsPingSeconds + phase, 1f);
            var r = Mathf.Max(0.5f, radius * f);
            ring.Position = centre;
            ring.Scale = new Vector3(r, 1f, r);
            var a = 0.32f * Mathf.SmoothStep(0f, 0.08f, f) * (1f - Mathf.SmoothStep(0.6f, 1f, f));
            material.AlbedoColor = material.AlbedoColor with { A = a };
        }
    }

    private void RebuildComms(IReadOnlyList<CommsSource> sources)
    {
        foreach (var child in _commsRoot!.GetChildren()) child.QueueFree();
        _commsPings.Clear();
        _commsUnion.Clear();
        var grid = _surface.Map.Grid;
        var index = 0;
        foreach (var c in sources)
        {
            if (c.Tile < 0 || c.Tile >= grid.Count || c.Radius < 0) continue;
            _rimSet.Clear();
            foreach (var t in grid.Within(c.Tile, c.Radius)) _rimSet.Add(t);
            if (c.Ready) _commsUnion.UnionWith(_rimSet);
            var colour = c.Kind == CommsSourceKind.Core ? CoreCommsColour : RelayCommsColour;
            var rim = new MeshInstance3D
            {
                Name = $"CommsRim{index}",
                Mesh = HexRimMesh(_rimSet, 1.1f, 0.45f, 0.5f + 0.35f * (index % 3)),
                MaterialOverride = RimMaterial(colour, c.Ready ? 1f : 0.5f, c.Ready ? 0f : 3.2f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _commsRoot.AddChild(rim);
            if (c.Ready)
            {
                var material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    AlbedoColor = colour with { A = 0f },
                    NoDepthTest = true,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                };
                var ring = new MeshInstance3D
                {
                    Name = $"CommsPing{index}",
                    Mesh = new TorusMesh { InnerRadius = 0.985f, OuterRadius = 1f, Rings = 96, RingSegments = 3 },
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                _commsRoot.AddChild(ring);
                var centre = TileCentre(c.Tile);
                _commsPings.Add((ring, material, centre with { Y = GroundAt(centre) + 1.5f }, (c.Radius + 0.5f) * HexWidth, index * 0.37f));
            }
            index++;
        }
        if (_commsUnion.Count == 0) return;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CylinderMesh { TopRadius = HexRadius * 0.92f, BottomRadius = HexRadius * 0.92f, Height = 0.1f, RadialSegments = 6, Rings = 1 },
        };
        mm.InstanceCount = _commsUnion.Count;
        var i = 0;
        foreach (var t in _commsUnion) mm.SetInstanceTransform(i++, new Transform3D(Basis.Identity, TileCentre(t) + new Vector3(0, 0.3f, 0)));
        _commsRoot.AddChild(new MultiMeshInstance3D { Name = "CommsFill", Multimesh = mm, MaterialOverride = Overlay(CoreCommsColour with { A = 0.05f }), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>A comms_overlay material: colour, strength, dash length (0 = solid).</summary>
    private static ShaderMaterial RimMaterial(Color colour, float strength, float dash)
    {
        _rimShader ??= GD.Load<Shader>("res://shaders/comms_overlay.gdshader");
        var m = new ShaderMaterial { Shader = _rimShader };
        m.SetShaderParameter("rim_colour", new Color(colour.R, colour.G, colour.B));
        m.SetShaderParameter("strength", strength);
        m.SetShaderParameter("dash", dash);
        return m;
    }

    /// <summary>A flat ribbon along the outer edges of a set of hexes (edges whose neighbour is not in the set), just
    /// inside them (`inset` from the edge), `width` wide, `lift` above the ground, each edge in 4 pieces following the
    /// terrain. UV.x = world distance along the edge, UV.y 0 outside .. 1 inside (comms_overlay.gdshader).</summary>
    private ArrayMesh HexRimMesh(HashSet<int> tiles, float width, float lift, float inset)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var any = false;
        foreach (var t in tiles)
        {
            var c = TileCentre(t);
            for (var k = 0; k < 6; k++)
            {
                var a = Corner(k);
                var b = Corner((k + 1) % 6);
                var mid = (a + b) * 0.5f;
                var nb = TileAt(new Vector3(c.X, 0f, c.Z) + mid.Normalized() * HexWidth);
                if (nb >= 0 && tiles.Contains(nb)) continue;
                any = true;
                var inward = -mid.Normalized();
                const int pieces = 4;
                var length = (b - a).Length();
                for (var p = 0; p < pieces; p++)
                {
                    float f0 = p / (float)pieces, f1 = (p + 1) / (float)pieces;
                    var o0 = c + a.Lerp(b, f0) + inward * inset;
                    var o1 = c + a.Lerp(b, f1) + inward * inset;
                    var i0 = o0 + inward * width;
                    var i1 = o1 + inward * width;
                    o0.Y = GroundAt(o0) + lift;
                    o1.Y = GroundAt(o1) + lift;
                    i0.Y = GroundAt(i0) + lift;
                    i1.Y = GroundAt(i1) + lift;
                    float u0 = f0 * length, u1 = f1 * length;
                    st.SetUV(new Vector2(u0, 0f)); st.AddVertex(o0);
                    st.SetUV(new Vector2(u1, 0f)); st.AddVertex(o1);
                    st.SetUV(new Vector2(u1, 1f)); st.AddVertex(i1);
                    st.SetUV(new Vector2(u0, 0f)); st.AddVertex(o0);
                    st.SetUV(new Vector2(u1, 1f)); st.AddVertex(i1);
                    st.SetUV(new Vector2(u0, 1f)); st.AddVertex(i0);
                }
            }
        }
        if (!any)
        {
            // nothing to draw (a set that covers the whole map): one degenerate triangle keeps the mesh valid
            st.SetUV(Vector2.Zero); st.AddVertex(Vector3.Zero);
            st.SetUV(Vector2.Zero); st.AddVertex(Vector3.Zero);
            st.SetUV(Vector2.Zero); st.AddVertex(Vector3.Zero);
        }
        return st.Commit();
    }
}
