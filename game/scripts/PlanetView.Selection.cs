using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Selection you can see at every zoom (owner 2026-11-12: "We need a better way to see our vehicles. Can you put a select
// circle around them that is easy to see."). Render-only.
//
// - Every selected vehicle (survey rovers Mk I–III, mining crawlers, construction rovers, scout drones; one at a time or a
//   whole group, the same ring each) gets a bold ring in SelectColour: a bright core, a thin dark rim so it reads on snow
//   and sand, and a soft glow that breathes, with a bright arc running round it. The vehicle under the mouse gets a dimmer,
//   thinner ring that doesn't pulse. A selected building gets the same ring round its footprint.
// - The ring follows the terrain mesh exactly (SurfaceAt, the triangles the ground is drawn with) and its shader pulls it
//   toward the camera along the view ray, so it lies on top of the ground, its bumps, short grass and a repair pad's slab
//   on any slope without z-fighting (shaders/selection_ring.gdshaderinc). A faint copy of the core line shows through trees,
//   buildings and the vehicle's own hull (selection_ring_xray.gdshader).
// - Its size and line width keep a floor in pixels: up close the ring hugs the vehicle (just outside its hull, from the
//   model's own box), from the strategy view and the far overview (~1000+) it grows so it never shrinks below
//   RingMinRadiusPx / RingMinCorePx on screen.
// - From far out (MarkerFrom → MarkerFull) a small screen-space chevron hangs over each selected vehicle (MarkerHalfPx,
//   drawn over everything) so a selection can be found on a big map. A vehicle still riding in its drop ship's bay has no
//   ring (it's inside the hull) but keeps its chevron at any zoom; one driving down the ramp gets a flat ring at its
//   wheels (it's off the ground); one parked on a repair pad keeps the ground ring, which the pull lifts over the slab.
// - Both meshes are rebuilt once a frame from UpdateSelection (driver, after Refresh), in world space; the ring heights are
//   cached per ring and only re-sampled when it moves or its radius changes. The nodes are named Selected* so the helm's
//   surface feed treats them as overlays (layer 0, CLAUDE.md "Rendering trick"); in orbit they are hidden.
public partial class PlanetView
{
    /// <summary>Selected rings (and the far markers): a bright green-cyan that reads on every planet palette, day or night.</summary>
    public static readonly Color SelectColour = new(0.3f, 1f, 0.62f);
    private const float HoverStrength = 0.5f;      // the hover ring's opacity (selected = 1)
    private const float HoverWidth = 0.7f;         // the hover ring's line width, × the selected one
    private const float RingGap = 0.25f;           // world units between the vehicle's hull and the ring's inner edge
    private const float RingCoreShare = 0.08f;     // up close the core line is this share of the hull radius wide…
    private const float RingMinCoreWorld = 0.2f;   // …but at least this wide (world units; ~10 px at the closest zoom)
    private const float RingMinCorePx = 4.5f;      // and never thinner than this on screen (from ~50 out this sets it)
    private const float RingMinRadiusPx = 20f;     // nor a ring smaller than this (radius, pixels): bigger than the vehicle from far up
    private const float RingLift = 0.3f;           // above the terrain mesh (plus the shader's pull toward the camera)
    private const float RingBand = 1.6f;           // the mesh band's half width in core widths (= RING_BAND in the shader)
    private const int RingSegments = 56;
    private const float RingResample = 0.05f;      // re-sample the ground once the ring moved / grew by this share of its radius
    private const float BuildingRingGap = 1.2f;    // a selected building's ring: just outside its lane footprint
    private const float MarkerFrom = 380f;         // camera distance where the far chevrons start to fade in…
    private const float MarkerFull = 620f;         // …and are fully there (the default view is ~420, the overview ~1000+)
    private const float MarkerHalfPx = 11f;        // half the chevron's size in pixels
    private const float MarkerLiftPx = 14f;        // pixels between the vehicle's top and the chevron's tip
    private const float DroneRingBelow = 0.6f;     // a drone's ring: flat, this far under its body

    /// <summary>The vehicle under the mouse (the driver's hover pick; 0 = none): drawn with the dimmer hover ring.</summary>
    public int HoveredUnit { get; set; }

    /// <summary>The selected building (the driver's _selectedStructure; 0 = none): drawn with a selected ring.</summary>
    public int SelectedStructure { get; set; }

    /// <summary>Units in their drop ship's bay right now (UpdateRovers): no ring, but the far marker at any zoom.</summary>
    private readonly HashSet<int> _aboard = new();

    private sealed class RingCache
    {
        public Vector3 Centre;
        public float Radius = -1f;
        public bool Flat;
        public readonly float[] Heights = new float[RingSegments + 1];
        public int Frame;
    }

    private readonly Dictionary<int, RingCache> _ringCache = new();
    private readonly List<int> _ringStale = new();
    private int _ringFrame;

    private MeshInstance3D? _ringNode, _markerNode;
    private ArrayMesh? _ringMesh, _markerMesh;
    private Godot.Collections.Array? _ringArrays, _markerArrays;

    // Reused vertex buffers (grown, never shrunk; copied out at the exact size only when the count changes).
    private readonly List<Vector3> _rv = new(), _mv = new();
    private readonly List<Color> _rc = new(), _mc = new();
    private readonly List<Vector2> _ruv = new(), _ruv2 = new(), _muv = new(), _muv2 = new();
    private readonly List<int> _ri = new(), _mi = new();
    private Vector3[] _rvA = System.Array.Empty<Vector3>(), _mvA = System.Array.Empty<Vector3>();
    private Color[] _rcA = System.Array.Empty<Color>(), _mcA = System.Array.Empty<Color>();
    private Vector2[] _ruvA = System.Array.Empty<Vector2>(), _ruv2A = System.Array.Empty<Vector2>(), _muvA = System.Array.Empty<Vector2>(), _muv2A = System.Array.Empty<Vector2>();
    private int[] _riA = System.Array.Empty<int>(), _miA = System.Array.Empty<int>();

    private void SetupSelection()
    {
        var root = new Node3D { Name = "SelectionOverlay" };
        AddChild(root);
        var xray = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/selection_ring_xray.gdshader"), RenderPriority = 9 };
        var ring = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/selection_ring.gdshader"), RenderPriority = 8, NextPass = xray };
        _ringMesh = new ArrayMesh();
        _ringNode = new MeshInstance3D
        {
            Name = "SelectedRings", Mesh = _ringMesh, MaterialOverride = ring,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            ExtraCullMargin = 40f, Visible = false,
        };
        root.AddChild(_ringNode);
        var marker = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/selection_marker.gdshader"), RenderPriority = 10 };
        _markerMesh = new ArrayMesh();
        _markerNode = new MeshInstance3D
        {
            Name = "SelectedMarkers", Mesh = _markerMesh, MaterialOverride = marker,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            ExtraCullMargin = 200f, Visible = false,
        };
        root.AddChild(_markerNode);
    }

    /// <summary>Once a frame after Refresh (PlanetDriver._Process): the rings and far markers for this frame. Camera null
    /// (in orbit) hides them; <paramref name="zoom"/> is the driver's camera distance (fades the far markers in).</summary>
    public void UpdateSelection(Camera3D? camera, float zoom)
    {
        if (_ringNode == null || _markerNode == null) SetupSelection();
        _rv.Clear(); _rc.Clear(); _ruv.Clear(); _ruv2.Clear(); _ri.Clear();
        _mv.Clear(); _mc.Clear(); _muv.Clear(); _muv2.Clear(); _mi.Clear();
        _ringFrame++;
        if (camera != null && camera.IsInsideTree())
        {
            var eye = camera.GlobalPosition;
            var height = GetViewport().GetVisibleRect().Size.Y;
            // World units per pixel at distance 1 (vertical FOV, the default KeepAspect).
            var perPixel = 2f * Mathf.Tan(Mathf.DegToRad(camera.Fov) * 0.5f) / Mathf.Max(height, 1f);
            var markers = Mathf.SmoothStep(MarkerFrom, MarkerFull, zoom);

            foreach (var id in SelectedUnits) AddUnitSelection(id, true, eye, perPixel, markers);
            if (HoveredUnit != 0 && !SelectedUnits.Contains(HoveredUnit)) AddUnitSelection(HoveredUnit, false, eye, perPixel, 0f);
            if (SelectedStructure != 0 && _surface.FindStructure(SelectedStructure) is { } s && s.Owner == _localPlayer)
            {
                var centre = TileCentre(s.Tile);
                var hull = _surface.BlockRadius(s.Tile).ToFloat() + BuildingRingGap;
                AddRing(-s.Id, centre, hull, false, centre.Y, SelectColour, 1f, true, s.Id * 0.137f, eye, perPixel);
            }
        }
        Commit(_ringNode!, _ringMesh!, ref _ringArrays, _rv, _rc, _ruv, _ruv2, _ri, ref _rvA, ref _rcA, ref _ruvA, ref _ruv2A, ref _riA);
        Commit(_markerNode!, _markerMesh!, ref _markerArrays, _mv, _mc, _muv, _muv2, _mi, ref _mvA, ref _mcA, ref _muvA, ref _muv2A, ref _miA);

        // Forget rings not drawn for a while (a deselected vehicle, a gone building).
        if (_ringCache.Count > 48)
        {
            _ringStale.Clear();
            foreach (var (key, c) in _ringCache)
                if (_ringFrame - c.Frame > 120) _ringStale.Add(key);
            foreach (var key in _ringStale) _ringCache.Remove(key);
        }
    }

    private void AddUnitSelection(int id, bool selected, Vector3 eye, float perPixel, float markers)
    {
        if (_surface.FindUnit(id) is not { } u) return;
        var colour = SelectColour;
        var strength = selected ? 1f : HoverStrength;
        var phase = id * 0.6180339f % 1f;
        Node3D? model;
        Vector3 at;
        bool flat;
        float flatY;
        if (_scoutDrones.TryGetValue(id, out var drone))
        {
            if (!drone.Placed || !drone.Holder.Visible) return;
            model = drone.Holder;
            at = drone.Pos;
            var airborne = at.Y - GroundAt(at) > 1.5f;
            flat = airborne;
            flatY = at.Y - (airborne ? DroneRingBelow : 0f);
        }
        else
        {
            if (!_rovers.TryGetValue(id, out model) || !model.Visible) return;
            at = model.Position;
            // Raised off the terrain (on a drop ship's ramp, in its bay): a flat ring at the wheels; else follow the ground.
            var wheels = at.Y - 0.1f;
            flat = wheels - GroundAt(at) > 0.45f;
            flatY = wheels;
        }
        var hull = HullRadius(u, model);
        var aboard = _aboard.Contains(id);
        if (!aboard) AddRing(id, at, hull, flat, flatY, colour, strength, selected, phase, eye, perPixel);
        var markerAlpha = aboard && selected ? 1f : markers;
        if (selected && markerAlpha > 0.01f)
            AddMarker(at + new Vector3(0, MarkerHeight(model), 0), colour, markerAlpha);
    }

    /// <summary>The vehicle's reach from its centre in X/Z (its model's box, People.cs LocalBox), larger while a crawler or
    /// construction rover is unfolded (the sim's deployed footprint).</summary>
    private float HullRadius(SurfaceUnit u, Node3D model)
    {
        var box = LocalBox(model);
        var reach = box.Size == Vector2.Zero ? 2.6f
            : Mathf.Max(Mathf.Max(Mathf.Abs(box.Position.X), Mathf.Abs(box.End.X)), Mathf.Max(Mathf.Abs(box.Position.Y), Mathf.Abs(box.End.Y)));
        reach = Mathf.Clamp(reach, 1.6f, 9f);
        if (u.Mode != UnitMode.Mobile) reach = Mathf.Max(reach, 0.8f * _sim.Data.DeployedFootprintOf(u.DefId).ToFloat());
        return reach;
    }

    /// <summary>Height of the model's top above its origin (cached on the model), for the chevron.</summary>
    private static float MarkerHeight(Node3D model)
    {
        const string meta = "select_top";
        if (model.HasMeta(meta)) return model.GetMeta(meta).AsSingle();
        var top = 0f;
        var have = false;
        void Walk(Node node, Transform3D toModel)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is not Node3D n || !n.Visible || n is GpuParticles3D or CpuParticles3D) continue;
                var name = n.Name.ToString();
                if (name.StartsWith("LightCone") || name.StartsWith("Selected")) continue;
                var t = toModel * n.Transform;
                if (n is MeshInstance3D mi && mi.Mesh != null)
                {
                    top = Mathf.Max(top, (t * mi.Mesh.GetAabb()).End.Y);
                    have = true;
                }
                Walk(n, t);
            }
        }
        Walk(model, Transform3D.Identity); // model space, like LocalBox (a drone's scale is on the model under its holder)
        top = have ? Mathf.Clamp(top, 1f, 12f) : 3f;
        if (have) model.SetMeta(meta, top);
        return top;
    }

    /// <param name="key">Cache key: a unit id, or minus a structure id.</param>
    /// <param name="hull">How far the thing reaches from its centre: the ring's inner edge sits just outside it.</param>
    private void AddRing(int key, Vector3 centre, float hull, bool flat, float flatY, Color colour, float strength, bool selected,
        float phase, Vector3 eye, float perPixel)
    {
        var distance = Mathf.Max(eye.DistanceTo(centre), 1f);
        var px = perPixel * distance; // world units per pixel at the ring
        var core = Mathf.Max(Mathf.Max(hull * RingCoreShare, RingMinCoreWorld), RingMinCorePx * px);
        if (!selected) core *= HoverWidth;
        var radius = Mathf.Max(hull + RingGap + core * 0.5f, RingMinRadiusPx * px);
        var half = core * RingBand;
        var inner = Mathf.Max(radius - half, 0.05f);
        var outer = radius + half;

        // Ground heights along the centre line (inner and outer edge share them), re-sampled only when it moved or grew.
        if (!_ringCache.TryGetValue(key, out var cache)) _ringCache[key] = cache = new RingCache();
        cache.Frame = _ringFrame;
        // (A moving ring re-samples every few frames: off by a few % of its radius, the error is a slope × that, well under
        // RingLift plus the shader's pull, and a group of moving vehicles doesn't sample the terrain every frame.)
        var drift = RingResample * radius;
        var stale = cache.Flat != flat || cache.Radius < 0f || Mathf.Abs(cache.Radius - radius) > RingResample * radius
                    || new Vector2(cache.Centre.X - centre.X, cache.Centre.Z - centre.Z).LengthSquared() > drift * drift
                    || (flat && Mathf.Abs(cache.Centre.Y - flatY) > 0.02f);
        if (stale)
        {
            cache.Flat = flat;
            cache.Radius = radius;
            cache.Centre = centre with { Y = flatY };
            for (var i = 0; i <= RingSegments; i++)
            {
                var a = Mathf.Tau * i / RingSegments;
                var p = centre + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius;
                cache.Heights[i] = flat ? flatY : Mathf.Max(GroundExact(p), GroundAt(p));
            }
        }

        var linear = colour.SrgbToLinear() with { A = strength };
        var kind = new Vector2(selected ? 1f : 0f, phase);
        var start = _rv.Count;
        for (var i = 0; i <= RingSegments; i++)
        {
            var a = Mathf.Tau * i / RingSegments;
            var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            var y = cache.Heights[i] + RingLift;
            var u = (float)i / RingSegments;
            _rv.Add(new Vector3(centre.X + dir.X * inner, y, centre.Z + dir.Z * inner));
            _rv.Add(new Vector3(centre.X + dir.X * outer, y, centre.Z + dir.Z * outer));
            _ruv.Add(new Vector2(u, 0f));
            _ruv.Add(new Vector2(u, 1f));
            _rc.Add(linear);
            _rc.Add(linear);
            _ruv2.Add(kind);
            _ruv2.Add(kind);
        }
        for (var i = 0; i < RingSegments; i++)
        {
            var v = start + i * 2;
            _ri.Add(v); _ri.Add(v + 1); _ri.Add(v + 2);
            _ri.Add(v + 1); _ri.Add(v + 3); _ri.Add(v + 2);
        }
    }

    private void AddMarker(Vector3 anchor, Color colour, float alpha)
    {
        var linear = colour.SrgbToLinear() with { A = alpha };
        var sizes = new Vector2(MarkerHalfPx, MarkerLiftPx);
        var start = _mv.Count;
        for (var k = 0; k < 4; k++)
        {
            _mv.Add(anchor);
            _mc.Add(linear);
            _muv2.Add(sizes);
        }
        _muv.Add(new Vector2(0, 0));
        _muv.Add(new Vector2(1, 0));
        _muv.Add(new Vector2(0, 1));
        _muv.Add(new Vector2(1, 1));
        _mi.Add(start); _mi.Add(start + 1); _mi.Add(start + 2);
        _mi.Add(start + 1); _mi.Add(start + 3); _mi.Add(start + 2);
    }

    /// <summary>The terrain mesh's own height under a world point (Scatter's SurfaceAt: the bumped triangles the ground is
    /// drawn with), the water surface over the sea, 0 off the map.</summary>
    private float GroundExact(Vector3 p)
    {
        var tile = TileAt(p);
        if (tile < 0) return 0f;
        if (_surface.Map.IsWater(tile)) return SeaLevel;
        var c = TileCentre(tile);
        return SurfaceAt(tile, new Vector3(p.X - c.X, 0, p.Z - c.Z)).Y;
    }

    /// <summary>Hands this frame's vertices to the mesh (one surface), or hides the node when there is nothing to draw.</summary>
    private static void Commit(MeshInstance3D node, ArrayMesh mesh, ref Godot.Collections.Array? arrays, List<Vector3> v, List<Color> c,
        List<Vector2> uv, List<Vector2> uv2, List<int> idx, ref Vector3[] vA, ref Color[] cA, ref Vector2[] uvA, ref Vector2[] uv2A, ref int[] iA)
    {
        mesh.ClearSurfaces();
        if (idx.Count == 0)
        {
            if (node.Visible) node.Visible = false;
            return;
        }
        if (vA.Length != v.Count) { vA = new Vector3[v.Count]; cA = new Color[v.Count]; uvA = new Vector2[v.Count]; uv2A = new Vector2[v.Count]; }
        if (iA.Length != idx.Count) iA = new int[idx.Count];
        v.CopyTo(vA);
        c.CopyTo(cA);
        uv.CopyTo(uvA);
        uv2.CopyTo(uv2A);
        idx.CopyTo(iA);
        if (arrays == null)
        {
            arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
        }
        arrays[(int)Mesh.ArrayType.Vertex] = vA;
        arrays[(int)Mesh.ArrayType.Color] = cA;
        arrays[(int)Mesh.ArrayType.TexUV] = uvA;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2A;
        arrays[(int)Mesh.ArrayType.Index] = iA;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (!node.Visible) node.Visible = true;
    }
}
