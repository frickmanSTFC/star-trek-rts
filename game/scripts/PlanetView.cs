using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Draws a planet surface: one continuous terrain mesh over the hex grid (smooth slopes, mountain peaks,
/// coasts running under animated water), trees, deposit crystals, orbital-scan hints, the hover marker,
/// the colony pod's descent, structures, survey rovers, mining crawlers and their cargo pods. Fog of war is a one-texel-per-tile texture
/// the terrain, water and tree shaders read. Render-only: nothing here changes the sim.
/// </summary>
public partial class PlanetView : Node3D
{
    /// <summary>World width of one hex (flat side to flat side).</summary>
    /// <summary>
    /// World width of one hex (flat side to flat side). Buildings, rovers and trees keep their own size,
    /// so this sets how big the ground is compared with them (owner: hexes much bigger than buildings).
    /// </summary>
    public const float HexWidth = 25f;

    /// <summary>Height of the water surface.</summary>
    public const float SeaLevel = 0.5f;

    private static readonly float HexRadius = HexWidth / Mathf.Sqrt(3f);

    private Simulation _sim = null!;
    private PlanetSurface _surface = null!;
    private int _localPlayer;

    private float[] _centreHeight = System.Array.Empty<float>();
    private float[] _cornerHeight = System.Array.Empty<float>(); // tile * 6 + corner
    private Image _fogImage = null!;
    private ImageTexture _fogTexture = null!;
    private readonly List<ShaderMaterial> _fogMaterials = new();

    private MultiMesh _deposits = null!;
    private readonly List<Label3D> _hintLabels = new();
    private MeshInstance3D _hover = null!;
    private StandardMaterial3D _hoverMaterial = null!;
    private DropShipView _dropShips = null!;
    private LandingView _landing = null!;
    private readonly Dictionary<int, Node3D> _rovers = new();
    private readonly Dictionary<int, RoverLook.Grime> _grime = new();
    private readonly Dictionary<int, RoverLook.Lamps> _lamps = new();
    private readonly Dictionary<int, Label3D> _unitLabels = new();
    private readonly Dictionary<int, Node3D> _pods = new();
    private readonly Dictionary<int, long> _seenBreak = new();
    private readonly Dictionary<int, float> _doorOpen = new();
    private readonly Dictionary<int, CrewVisual> _crews = new();

    /// <summary>The two repair crew members working on one unit, and where they walk back to.</summary>
    private sealed class CrewVisual
    {
        public required Node3D[] People;
        public Vector3 Door;
        public float Back = -1f; // walking back in: 0..1, or -1 while out at work
    }
    private float _time;

    private sealed class StructureVisual
    {
        public required Node3D Model;
        public required Label3D Label;
        public required StructureState State;
        public required string DefId;
    }

    private readonly Dictionary<int, StructureVisual> _structures = new();
    private Node3D? _ghost;
    private string? _ghostId;
    private StandardMaterial3D _ghostMaterial = null!;
    private ConstructionView _construction = null!;
    private MeshInstance3D _rangeRing = null!;
    private int _surveyedCount = -1;
    private int _spentCount = -1;
    private LandingPhase _lastPhase = (LandingPhase)(-1);

    public void Build(Simulation sim, int localPlayer)
    {
        _sim = sim;
        _surface = sim.Surface!;
        _localPlayer = localPlayer;

        BuildFogTexture();
        BuildTerrain();
        BuildWater();
        BuildScatter();
        // Region hints are read on the orbital globe now (no floating words over the map).
        _deposits = BuildCrystals();

        _hoverMaterial = Overlay(new Color(1, 1, 1, 0.35f));
        _hoverMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        _hoverMaterial.NoDepthTest = false; // vehicles, buildings and trees in front hide the outline (owner 2026-11-05)
        _hover = new MeshInstance3D
        {
            MaterialOverride = _hoverMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_hover);

        _ghostMaterial = Overlay(new Color(0.3f, 1f, 0.4f, 0.45f));
        _rangeRing = new MeshInstance3D { Visible = false };
        AddChild(_rangeRing);

        CheckTileSize(); // PlanetView.Lanes.cs
        _dropShips = new DropShipView(this, sim, localPlayer) { Name = "DropShips" };
        AddChild(_dropShips);
        _landing = new LandingView(this, sim, localPlayer, _dropShips) { Name = "Landing" };
        AddChild(_landing);
        _construction = new ConstructionView(this, sim) { Name = "Construction" };
        AddChild(_construction);
        Refresh(0f);
    }

    private static StandardMaterial3D Overlay(Color colour) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = colour,
        NoDepthTest = true,
    };

    // ---------------------------------------------------------------- coordinates

    public Vector3 TileCentre(int tile)
    {
        var c = _surface.Map.Grid.Centre(tile);
        return new Vector3(c.X.ToFloat() * HexWidth, TopY(tile), c.Y.ToFloat() * HexWidth);
    }

    /// <summary>Height of the ground at a tile's centre (the water surface for sea tiles).</summary>
    public float TopY(int tile) => _surface.Map.IsWater(tile) ? SeaLevel : _centreHeight[tile];

    /// <summary>Approximate ground height under any world point (the water surface over the sea; 0 off the map).</summary>
    public float GroundAt(Vector3 p)
    {
        var tile = TileAt(p);
        if (tile < 0) return 0f;
        if (_surface.Map.IsWater(tile)) return SeaLevel;
        var c = TileCentre(tile);
        return GroundHeight(tile, new Vector3(p.X - c.X, 0, p.Z - c.Z));
    }

    public Vector3 MapCentre()
    {
        var g = _surface.Map.Grid;
        return new Vector3((g.Width - 0.5f) * HexWidth / 2f, 0, (g.Height - 1) * 0.866f * HexWidth / 2f);
    }

    /// <summary>The tile under a world point (nearest centre), or -1 off the map.</summary>
    public int TileAt(Vector3 p)
    {
        var g = _surface.Map.Grid;
        var row = Mathf.RoundToInt(p.Z / (0.866f * HexWidth));
        var col = Mathf.RoundToInt(p.X / HexWidth - ((row & 1) == 1 ? 0.5f : 0f));
        var best = -1;
        var bestDist = float.MaxValue;
        for (var r = row - 1; r <= row + 1; r++)
            for (var c = col - 1; c <= col + 1; c++)
            {
                if (!g.InBounds(c, r)) continue;
                var i = g.Index(c, r);
                var centre = TileCentre(i);
                var d = new Vector2(centre.X - p.X, centre.Z - p.Z).LengthSquared();
                if (d < bestDist)
                {
                    best = i;
                    bestDist = d;
                }
            }
        return bestDist <= HexRadius * HexRadius ? best : -1;
    }

    /// <summary>The tile under a camera ray, following the terrain's height (a few refinement steps).</summary>
    public int PickTile(Vector3 origin, Vector3 dir)
    {
        if (Mathf.Abs(dir.Y) < 1e-5f) return -1;
        var height = 2f;
        var tile = -1;
        for (var i = 0; i < 4; i++)
        {
            var t = (height - origin.Y) / dir.Y;
            if (t < 0) return -1;
            var next = TileAt(origin + dir * t);
            _lastPickPoint = origin + dir * t; // PlanetView.Lanes.cs: which vehicle on the tile is under the mouse
            if (next < 0 || next == tile) return next < 0 ? tile : next;
            tile = next;
            height = TopY(tile);
        }
        return tile;
    }

    // ---------------------------------------------------------------- per frame

    /// <param name="alpha">Fraction of the way to the next sim tick (for smooth motion).</param>
    public void Refresh(float alpha)
    {
        var me = _surface.Players[_localPlayer];
        var surveyed = 0;
        var spent = 0; // surveyed deposits that are used up: redraw their markers when one runs out (or is reopened)
        var map = _surface.Map;
        for (var t = 0; t < me.Surveyed.Length; t++)
        {
            if (!me.Surveyed[t]) continue;
            surveyed++;
            if (map.IsDepleted(t)) spent++;
        }
        var vision = VisionSignature();
        if (surveyed != _surveyedCount || spent != _spentCount || me.Phase != _lastPhase || vision != _visionSignature)
        {
            _spentCount = spent;
            _surveyedCount = surveyed;
            _lastPhase = me.Phase;
            _visionSignature = vision;
            UpdateVisibility(me);
            UpdateFogTexture();
            PlaceDeposits(me);
            foreach (var l in _hintLabels) l.Visible = me.Phase == LandingPhase.InOrbit;
            FogChanged = true;
        }

        var delta = (float)GetProcessDeltaTime();
        _landing.Update(alpha, delta); // the first landing's drop ships and assembly (hides the hub until the uplink)
        UpdateFounders(delta); // PlanetView.Founding.cs: used-up construction rovers parked at their new hubs (exocomp docks)
        UpdateStructures(delta);
        UpdateRefineries(delta); // PlanetView.Refinery.cs: the refinery's machinery, furnace and lamps by status
        UpdateDetailedBuildings(delta); // PlanetView.Detailed.cs: mine hoist, storage crane + fill gauge, reactor core
        UpdateScatter();
        _time += delta;
        _dropShips.Update(alpha, delta); // before the rovers: a unit being unloaded follows its ship's ramp
        UpdateRovers(alpha, delta);
        UpdatePods(alpha);
    }

    /// <summary>Outline the tile under the mouse: a thin line along the hex edge that follows the ground (owner
    /// 2026-11-04: a filled hexagon was overwhelming). The line is twice as opaque as the colour asked for, since it is
    /// so much thinner than the old fill.</summary>
    /// <param name="width">Line width in world units (the driver scales it with the zoom: thin up close).</param>
    public void SetHover(int tile, Color color, float width = 0.72f)
    {
        _hover.Visible = tile >= 0;
        if (tile < 0) return;
        _hoverMaterial.AlbedoColor = color with { A = Mathf.Min(1f, color.A * 2.2f) };
        if (tile == _hoverShown && Mathf.Abs(width - _hoverWidth) <= _hoverWidth * 0.04f) return;
        _hoverShown = tile;
        _hoverWidth = width;
        var c = TileCentre(tile);
        _hover.Position = c with { Y = 0 };
        _hover.Mesh = HexOutline(tile, width);
    }

    private int _hoverShown = -1;
    private float _hoverWidth;

    /// <summary>A flat band along a tile's edge (outer edge at 97% of the corner radius, <paramref name="width"/> wide),
    /// each side split in four so it hugs the terrain, lifted 0.5 above the ground (above the water on sea tiles).
    /// Positions are relative to the tile centre in X/Z and absolute in Y.</summary>
    private Mesh HexOutline(int tile, float width)
    {
        var inner = Mathf.Max(0.5f, 0.97f - width / HexRadius);
        const int perSide = 4;
        var water = _surface.Map.IsWater(tile);
        Vector3 Edge(int k, float f, float scale)
        {
            var p = Corner(k).Lerp(Corner((k + 1) % 6), f) * scale;
            var y = GroundHeight(tile, p);
            if (water) y = Mathf.Max(y, SeaLevel);
            return p with { Y = y + 0.5f };
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (var k = 0; k < 6; k++)
            for (var i = 0; i < perSide; i++)
            {
                float f0 = (float)i / perSide, f1 = (float)(i + 1) / perSide;
                Vector3 o0 = Edge(k, f0, 0.97f), o1 = Edge(k, f1, 0.97f), i0 = Edge(k, f0, inner), i1 = Edge(k, f1, inner);
                st.AddVertex(o0); st.AddVertex(o1); st.AddVertex(i1);
                st.AddVertex(o0); st.AddVertex(i1); st.AddVertex(i0);
            }
        return st.Commit();
    }

    /// <summary>Step the wipers of these vehicles to the next setting (automatic → on → off → automatic), all to the
    /// same one, starting from the first vehicle's. Null when none of them has wipers.</summary>
    public RoverLook.WiperMode? CycleWipers(IEnumerable<int> unitIds)
    {
        RoverLook.WiperMode? next = null;
        foreach (var id in unitIds)
        {
            if (!_lamps.TryGetValue(id, out var lamps) || !lamps.HasWipers) continue;
            next ??= lamps.Wipers switch { RoverLook.WiperMode.Auto => RoverLook.WiperMode.On, RoverLook.WiperMode.On => RoverLook.WiperMode.Off, _ => RoverLook.WiperMode.Auto };
            lamps.Wipers = next.Value;
        }
        return next;
    }

    /// <summary>Rover ids the view should draw highlighted.</summary>
    public HashSet<int> SelectedUnits { get; } = new();

    // ---------------------------------------------------------------- terrain

    /// <summary>Corner offsets of a pointy-top hex (corner 0 points toward +Z, then clockwise seen from above).</summary>
    private static Vector3 Corner(int k)
    {
        var a = Mathf.DegToRad(60f * k);
        return new Vector3(Mathf.Sin(a) * HexRadius, 0, Mathf.Cos(a) * HexRadius);
    }

    /// <summary>Ground height a tile pulls its corners toward (no peaks; those are only at mountain centres).</summary>
    private float BaseHeight(int tile)
    {
        var map = _surface.Map;
        var terrain = map.Terrain(tile);
        var e = map.Elevation[tile].ToFloat();
        return terrain.Id switch
        {
            "ocean" => -4.5f + e * 1f,
            "shallows" => -0.9f,
            "mountains" => 16f + (e - 0.9f) * 120f, // the top ~9% of elevation: 16 at the foot of a range, ~28 at its heart
            _ => 1.4f + terrain.Height * 2.6f + e * 2.6f,
        };
    }

    private static long CornerKey(Vector3 p) => ((long)Mathf.RoundToInt(p.X * 10f) << 32) ^ (uint)Mathf.RoundToInt(p.Z * 10f);

    /// <summary>Render-only bumpiness inside a hex (same everywhere for a position).</summary>
    private static float Bump(Vector3 p)
    {
        var h = Mathf.Sin(p.X * 0.21f + Mathf.Cos(p.Z * 0.17f) * 1.7f) * Mathf.Cos(p.Z * 0.19f - p.X * 0.05f);
        return h * 0.5f + Mathf.Sin(p.X * 0.53f + p.Z * 0.47f) * 0.2f;
    }

    /// <summary>Crags for mountains: -1..1, sharper than Bump. Render-only.</summary>
    private static float Jag(Vector3 p)
    {
        var a = Mathf.Sin(p.X * 0.31f + p.Z * 0.12f) * Mathf.Cos(p.Z * 0.27f - p.X * 0.09f);
        var b = Mathf.Sin(p.X * 0.83f - p.Z * 0.71f);
        return Mathf.Clamp(a * 0.75f + b * 0.35f, -1f, 1f);
    }

    /// <summary>Mountain colours by height: dark rock low, grey crags, snow above the snow line.</summary>
    private static Color RockColour(float height, Vector3 p)
    {
        var rock = new Color(0.33f, 0.31f, 0.3f).Lerp(new Color(0.5f, 0.48f, 0.46f), Mathf.Clamp((height - 12f) / 20f, 0f, 1f));
        var snowLine = 34f + Bump(p) * 4f;
        return height > snowLine ? new Color(0.93f, 0.95f, 0.98f) : rock;
    }

    private void BuildTerrain()
    {
        var map = _surface.Map;
        var grid = map.Grid;
        _centreHeight = new float[grid.Count];
        _cornerHeight = new float[grid.Count * 6];

        // Each corner is shared by up to three tiles: its height is their average, so the surface is continuous.
        var sum = new Dictionary<long, (float Total, int Count)>();
        var centres = new Vector3[grid.Count];
        for (var t = 0; t < grid.Count; t++)
        {
            var c = grid.Centre(t);
            centres[t] = new Vector3(c.X.ToFloat() * HexWidth, 0, c.Y.ToFloat() * HexWidth);
            var h = BaseHeight(t);
            for (var k = 0; k < 6; k++)
            {
                var key = CornerKey(centres[t] + Corner(k));
                sum[key] = sum.TryGetValue(key, out var s) ? (s.Total + h, s.Count + 1) : (h, 1);
            }
            var peak = map.Terrain(t).Id == "mountains" ? 18f + (map.Elevation[t].ToFloat() - 0.9f) * 260f + Jag(centres[t]) * 6f : 0f;
            _centreHeight[t] = h + peak;
        }

        // Per hex: centre, an inner ring halfway out (bumped a little on land), and the six shared corners.
        // 13 vertices, 18 triangles; UV.x runs 0 (centre) to 1 (corner) for the hex outline in the shader.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var index = 0;
        var pos = new Vector3[13];
        var cols = new Color[13];
        var uvs = new float[13];
        var normals = new Vector3[13];
        var tris = new List<(int, int, int)>();
        for (var k = 0; k < 6; k++)
        {
            tris.Add((0, 1 + k, 1 + (k + 1) % 6));                     // inner fan
            tris.Add((1 + k, 7 + k, 7 + (k + 1) % 6));                 // ring, outer triangle
            tris.Add((1 + k, 7 + (k + 1) % 6, 1 + (k + 1) % 6));       // ring, inner triangle
        }

        for (var t = 0; t < grid.Count; t++)
        {
            var terrain = map.Terrain(t);
            var water = map.IsWater(t);
            var colour = Color.FromHtml(terrain.Color).Lightened((map.Elevation[t].ToFloat() - 0.5f) * 0.15f);
            if (water) colour = new Color(0.36f, 0.33f, 0.24f).Lerp(colour, 0.35f); // seabed
            var centreColour = colour;
            if (terrain.Id == "hills") centreColour = colour.Lightened(0.08f);

            pos[0] = centres[t] with { Y = _centreHeight[t] };
            for (var k = 0; k < 6; k++)
            {
                var p = centres[t] + Corner(k);
                var s = sum[CornerKey(p)];
                pos[7 + k] = p with { Y = s.Total / s.Count };
                _cornerHeight[t * 6 + k] = pos[7 + k].Y;
            }
            for (var k = 0; k < 6; k++)
            {
                var mid = pos[0].Lerp(pos[7 + k], 0.5f);
                if (terrain.Id == "mountains")
                {
                    // Broad, jagged shoulders: the ring sits high and every point gets its own crag.
                    mid = pos[0].Lerp(pos[7 + k], 0.42f);
                    mid.Y += Jag(mid) * 7f;
                }
                else if (!water) mid.Y += Bump(mid) * (terrain.Id == "hills" ? 1.4f : 0.7f);
                pos[1 + k] = mid;
            }
            cols[0] = centreColour;
            uvs[0] = 0;
            for (var k = 0; k < 6; k++)
            {
                cols[1 + k] = centreColour.Lerp(colour, 0.5f);
                uvs[1 + k] = 0.5f;
                cols[7 + k] = colour;
                uvs[7 + k] = 1f;
            }
            if (terrain.Id == "mountains")
                for (var v = 0; v < 13; v++) cols[v] = RockColour(pos[v].Y, pos[v]);

            // Normals: sum each triangle's face normal (pointing up, whatever the winding) into its vertices.
            System.Array.Clear(normals);
            foreach (var (a, b, c) in tris)
            {
                var n = (pos[b] - pos[a]).Cross(pos[c] - pos[a]);
                if (n.Y < 0) n = -n;
                normals[a] += n;
                normals[b] += n;
                normals[c] += n;
            }

            for (var v = 0; v < 13; v++)
            {
                st.SetColor(cols[v].SrgbToLinear());
                st.SetUV(new Vector2(uvs[v], 0));
                st.SetNormal(normals[v].Normalized());
                st.AddVertex(pos[v]);
            }
            foreach (var (a, b, c) in tris)
            {
                st.AddIndex(index + a);
                st.AddIndex(index + b);
                st.AddIndex(index + c);
            }
            index += 13;
        }

        var material = FogMaterial("res://shaders/terrain.gdshader");
        AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On });
    }

    private void BuildWater()
    {
        var map = _surface.Map;
        var grid = map.Grid;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var index = 0;
        for (var t = 0; t < grid.Count; t++)
        {
            if (!map.IsWater(t)) continue;
            var c = grid.Centre(t);
            var centre = new Vector3(c.X.ToFloat() * HexWidth, SeaLevel, c.Y.ToFloat() * HexWidth);
            // COLOR.r: 1 = deep ocean, 0 = shallows (the shader blends its two colours with it).
            var depth = map.Terrain(t).Id == "ocean" ? new Color(1, 0, 0) : new Color(0, 0, 0);
            st.SetColor(depth);
            st.SetNormal(Vector3.Up);
            st.AddVertex(centre);
            for (var k = 0; k < 6; k++)
            {
                st.SetColor(depth);
                st.SetNormal(Vector3.Up);
                st.AddVertex(centre + Corner(k) * 1.04f); // slight overlap hides seams between water hexes
            }
            for (var k = 0; k < 6; k++)
            {
                st.AddIndex(index);
                st.AddIndex(index + 1 + k);
                st.AddIndex(index + 1 + (k + 1) % 6);
            }
            index += 7;
        }
        if (index == 0) return;
        AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = FogMaterial("res://shaders/water.gdshader"), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    // Trees, grass and rocks: BuildScatter / UpdateScatter in PlanetView.Scatter.cs.

    /// <summary>
    /// Approximate ground height at an offset from a tile's centre: blends from the centre height toward the
    /// edge between the two nearest corners (ignores the small render-only bumps).
    /// </summary>
    private float GroundHeight(int tile, Vector3 offset)
    {
        var deg = Mathf.RadToDeg(Mathf.Atan2(offset.X, offset.Z));
        if (deg < 0) deg += 360f;
        var k = Mathf.Min(5, (int)(deg / 60f));
        var f = (deg - k * 60f) / 60f;
        var edge = Mathf.Lerp(_cornerHeight[tile * 6 + k], _cornerHeight[tile * 6 + (k + 1) % 6], f);
        var r = Mathf.Clamp(offset.Length() / HexRadius, 0f, 1f);
        return Mathf.Lerp(_centreHeight[tile], edge, r);
    }

    private MultiMesh BuildCrystals()
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.55f, Height = 2.6f, RadialSegments = 5, Rings = 1 },
        };
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/crystal.gdshader") };
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material });
        return mm;
    }

    private ShaderMaterial FogMaterial(string path)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(path) };
        var g = _surface.Map.Grid;
        material.SetShaderParameter("fog_tex", _fogTexture);
        material.SetShaderParameter("map_tiles", new Vector2(g.Width, g.Height));
        material.SetShaderParameter("hex_width", HexWidth);
        _fogMaterials.Add(material);
        return material;
    }

    // ---------------------------------------------------------------- fog of war (render-only)

    /// <summary>How far your buildings and rovers can see, in tiles. Render-only: the sim tracks surveying, not sight.</summary>
    public const int StructureSight = 3;

    private bool[] _visible = System.Array.Empty<bool>();
    private long _visionSignature = -1;

    private bool _fogOff;

    /// <summary>Test panel: show the whole planet as if in sight (render only; the sim's surveying is unchanged).</summary>
    public bool FogOff
    {
        get => _fogOff;
        set
        {
            _fogOff = value;
            Redraw();
        }
    }

    /// <summary>Redraw the fog and the deposit markers next frame (after a test-panel edit changed them).</summary>
    public void Redraw() => _visionSignature = long.MinValue;

    /// <summary>The dirt on a vehicle's model, or null before it is drawn.</summary>
    public RoverLook.Grime? GrimeOf(int unitId) => _grime.GetValueOrDefault(unitId);

    /// <summary>A unit's model, or null before it is drawn (the helm's surface feed follows it).</summary>
    public Node3D? UnitModel(int unitId) => _rovers.GetValueOrDefault(unitId);

    /// <summary>Set when the fog changed this frame (the minimap redraws its terrain then).</summary>
    public bool FogChanged { get; set; }

    public enum Fog { Visible, Explored, Unexplored }

    /// <summary>
    /// Three levels: Visible = in sight of your buildings or rovers now; Explored = surveyed before, shown dimmed;
    /// Unexplored = near-black. From orbit the scan shows the whole surface.
    /// </summary>
    public Fog FogAt(int tile)
    {
        if (_fogOff) return Fog.Visible;
        var me = _surface.Players[_localPlayer];
        if (me.Phase == LandingPhase.InOrbit) return Fog.Visible;
        if (tile < _visible.Length && _visible[tile]) return Fog.Visible;
        return me.Surveyed[tile] ? Fog.Explored : Fog.Unexplored;
    }

    /// <summary>The colour a tile shows on the minimap, fog applied.</summary>
    public Color TileColour(int tile)
    {
        var map = _surface.Map;
        var colour = Color.FromHtml(map.Terrain(tile).Color).Lightened((map.Elevation[tile].ToFloat() - 0.5f) * 0.15f);
        return FogAt(tile) switch
        {
            Fog.Visible => colour,
            Fog.Explored => colour.Darkened(0.55f),
            _ => colour.Lerp(new Color(0.008f, 0.012f, 0.03f), FogDarkness),
        };
    }

    /// <summary>How close unexplored ground gets to black (0..1).</summary>
    public float FogDarkness { get; set; } = 0.92f;

    private void BuildFogTexture()
    {
        var g = _surface.Map.Grid;
        _fogImage = Image.CreateEmpty(g.Width, g.Height, false, Image.Format.Rgba8);
        _fogImage.Fill(Colors.White);
        _fogTexture = ImageTexture.CreateFromImage(_fogImage);
    }

    private void UpdateFogTexture()
    {
        var g = _surface.Map.Grid;
        var explored = 0.42f;
        var unexplored = 1f - FogDarkness;
        for (var t = 0; t < g.Count; t++)
        {
            var v = FogAt(t) switch { Fog.Visible => 1f, Fog.Explored => explored, _ => unexplored };
            _fogImage.SetPixel(g.Column(t), g.Row(t), new Color(v, v, v));
        }
        _fogTexture.Update(_fogImage);
    }

    private long VisionSignature()
    {
        long h = _surface.Structures.Count * 7919L + _surface.Players[_localPlayer].LandingTile;
        foreach (var u in _surface.Units)
            if (u.Owner == _localPlayer) h = h * 31 + u.Tile + 1;
        return h;
    }

    private void UpdateVisibility(SurfacePlayer me)
    {
        var grid = _surface.Map.Grid;
        if (_visible.Length != grid.Count) _visible = new bool[grid.Count];
        System.Array.Clear(_visible);
        void See(int centre, int radius)
        {
            foreach (var t in grid.Within(centre, radius)) _visible[t] = true;
        }
        foreach (var s in _surface.Structures)
            if (s.Owner == _localPlayer) See(s.Tile, StructureSight);
        foreach (var u in _surface.Units)
            if (u.Owner == _localPlayer) See(u.Tile, _surface.SurveyRadiusOf(u) + 1); // damaged sensors see less
        if (me.Phase == LandingPhase.Descending) See(me.LandingTile, 2);
    }

    /// <summary>
    /// A small cluster of glowing crystals on every surveyed deposit (dim when out of sight). Used-up deposits
    /// shrink to small grey stubs, so they no longer look mineable but can still be found for a Deep-Core Mine.
    /// </summary>
    private void PlaceDeposits(SurfacePlayer me)
    {
        var map = _surface.Map;
        var tiles = new List<int>();
        for (var t = 0; t < map.Grid.Count; t++)
            if (me.Surveyed[t] && map.DepositResource[t] >= 0) tiles.Add(t);
        _deposits.InstanceCount = tiles.Count * 3;
        for (var i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var id = _sim.Data.Resources[map.DepositResource[t]].Id;
            var spent = map.IsDepleted(t);
            var size = spent ? 0.45f : Mathf.Clamp(map.DepositAmount[t].ToFloat() / 400f, 1f, 2.2f);
            var colour = spent ? new Color(0.09f, 0.085f, 0.08f)
                : (Icons.ResourceColors.TryGetValue(id, out var c) ? c : new Color(0.8f, 0.6f, 1f)).SrgbToLinear();
            colour.A = FogAt(t) == Fog.Visible ? 1f : 0.3f;
            var basePos = TileCentre(t) + new Vector3(6.5f, 0, -3.5f);
            for (var k = 0; k < 3; k++)
            {
                var offset = new Vector3((k - 1) * 0.9f, 0, (k % 2) * 0.8f - 0.3f);
                var tilt = new Basis(new Vector3(k - 1, 0, 0.5f - k * 0.3f).Normalized(), 0.35f);
                var s = size * (k == 1 ? 1.2f : 0.8f);
                _deposits.SetInstanceTransform(i * 3 + k, new Transform3D(tilt.Scaled(new Vector3(s, s, s)), basePos + offset + new Vector3(0, 1.1f * s, 0)));
                _deposits.SetInstanceColor(i * 3 + k, colour);
            }
        }
    }

    private void BuildHints()
    {
        var map = _surface.Map;
        var data = _sim.Data;
        for (var region = 0; region < map.RegionCount; region++)
        {
            var sum = Vector3.Zero;
            var n = 0;
            for (var t = 0; t < map.Grid.Count; t++)
            {
                if (map.Region(t) != region) continue;
                sum += TileCentre(t);
                n++;
            }
            if (n == 0) continue;
            var lines = new List<string>();
            foreach (var strength in new[] { HintStrength.Rich, HintStrength.Likely, HintStrength.Traces })
            {
                var names = new List<string>();
                foreach (var h in map.Hints(region))
                    if (h.Strength == strength) names.Add(data.Resources[h.Resource].Name);
                if (names.Count > 0) lines.Add($"{strength}: {string.Join(", ", names)}");
            }
            if (lines.Count == 0) lines.Add("No readings");
            var label = new Label3D
            {
                Text = string.Join("\n", lines),
                Position = sum / n + new Vector3(0, 140, 0),
                FontSize = 44,
                PixelSize = 0.8f,
                OutlineSize = 12,
                Modulate = new Color(1f, 0.86f, 0.5f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
            };
            AddChild(label);
            _hintLabels.Add(label);
        }
    }

    // ---------------------------------------------------------------- dynamic objects

    /// <summary>
    /// Keeps one model per structure: while unfinished, <see cref="ConstructionView"/> builds it up piece by piece with
    /// exocomps and beams; on completion it is swapped for a fresh solid model. Labels show queue/progress or why a building is idle.
    /// </summary>
    private void UpdateStructures(float delta)
    {
        var seen = new HashSet<int>();
        foreach (var s in _surface.Structures)
        {
            seen.Add(s.Id);
            // An assembling core (down from orbit) is a solid model, not a hologram; LandingView hides it while it plays.
            var complete = s.State is StructureState.Complete or StructureState.Assembling;
            var needsModel = !_structures.TryGetValue(s.Id, out var v)
                || (v.State is not (StructureState.Complete or StructureState.Assembling) && complete)
                || v.DefId != s.DefId; // a finished upgrade changes the model
            if (needsModel)
            {
                if (v != null)
                {
                    v.Model.QueueFree();
                    v.Label.QueueFree();
                }
                var model = BuildingModels.Build(s.DefId, PlanetDriver.PlayerColors[s.Owner % PlanetDriver.PlayerColors.Length], VisualSeed(s), VisualSerial(s));
                model.Position = TileCentre(s.Tile);
                model.RotationDegrees = new Vector3(0, -60f * s.Facing, 0);
                AddChild(model);
                _construction.Attach(s, model); // build pieces + exocomps while unfinished, the completion beat after
                var label = new Label3D
                {
                    Position = TileCentre(s.Tile) + new Vector3(0, 12f, 0),
                    FontSize = 36,
                    PixelSize = 0.09f,
                    OutlineSize = 10,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                };
                AddChild(label);
                v = new StructureVisual { Model = model, Label = label, State = s.State, DefId = s.DefId };
                _structures[s.Id] = v;
            }
            v!.State = s.State;
            var assembling = _landing.Hides(s); // the landed outpost is still coming together (LandingView draws it)
            v.Model.Visible = v.Label.Visible = !assembling;

            if (!complete)
            {
                var f = _surface.BuildFraction(s).ToFloat(); // ConstructionView builds the model up piece by piece
                v.Label.Text = s.State == StructureState.Queued ? "Queued" : $"Building {Mathf.RoundToInt(f * 100)}%";
                v.Label.Modulate = new Color(0.6f, 0.85f, 1f);
                continue;
            }

            v.Model.Scale = Vector3.One;
            if (s.Assembling)
            {
                // (LandingView lets go within the last tick, so no label for that sliver.)
                v.Label.Text = s.ReadyTick - _sim.Tick > 1 ? $"Assembling {PlanetSurface.AssembleSecondsLeft(s, _sim.Tick)} s" : "";
                v.Label.Modulate = new Color(0.6f, 0.85f, 1f);
                continue;
            }
            var upgrading = s.UpgradingTo != null || s.UpgradingLevel > 0;
            v.Label.Text = upgrading ? $"Upgrading {Mathf.RoundToInt(_surface.UpgradeFraction(s).ToFloat() * 100)}%"
                : s.Broken ? (s.AutoRepairing ? "Drones repairing" : "Broken down")
                : s.Status switch
                {
                    StructureStatus.NoUpkeep => "Idle: can't pay upkeep",
                    StructureStatus.Depleted => "Deposit used up",
                    StructureStatus.LowPower => "Low power",
                    StructureStatus.StorageFull => "Storage full",
                    StructureStatus.NoInput when _sim.Data.Building(s.DefId).IsFactory => "",
                    StructureStatus.NoInput when _sim.Data.Building(s.DefId).IsLab => "Idle: pick a tech",
                    StructureStatus.NoInput => s.DefId == "refinery" ? "Idle: no ore" : "Idle: no input",
                    _ when _sim.Data.Building(s.DefId).Wears && s.Condition < Fix64.FromInt(50) => $"Worn {Mathf.FloorToInt(s.Condition.ToFloat())}%",
                    _ => "",
                };
            v.Label.Modulate = upgrading ? new Color(0.6f, 0.85f, 1f) : s.Broken ? new Color(1f, 0.38f, 0.3f) : new Color(1f, 0.55f, 0.35f);
            ShowUpkeep(s, v);
            var working = s.Status is StructureStatus.Working or StructureStatus.LowPower;
            if (_sim.Data.Building(s.DefId).IsFactory) AnimateBay(s, v, working, delta);
            if (working && v.Model.GetNodeOrNull<Node3D>("Spin") is { } spin)
                spin.RotateY(delta * (s.Status == StructureStatus.LowPower ? 1f : 2.5f));
        }

        // Cancelled buildings disappear.
        foreach (var id in new List<int>(_structures.Keys))
        {
            if (seen.Contains(id)) continue;
            _structures[id].Model.QueueFree();
            _structures[id].Label.QueueFree();
            _structures.Remove(id);
            _construction.Forget(id);
        }
        _construction.Update(delta);
        UpdateDrones(delta);
        UpdateBuildingCrews(delta);
    }

    // ---------------------------------------------------------------- building upkeep (render-only)

    private readonly Dictionary<int, Node3D> _drones = new();
    private readonly Dictionary<int, Node3D[]> _buildingCrews = new();

    /// <summary>A broken building smokes until it's fixed (the plume is added the first time it's needed).</summary>
    private void ShowUpkeep(SurfaceStructure s, StructureVisual v)
    {
        var smoke = v.Model.GetNodeOrNull<CpuParticles3D>("BrokenSmoke");
        if (smoke == null && s.Broken)
        {
            smoke = BuildingModels.Puffs("BrokenSmoke", new Vector3(0, 5f, 0), new Color(0.14f, 0.13f, 0.12f), 36, 3.4f, 1.4f, 3f, 0.2f);
            v.Model.AddChild(smoke);
        }
        if (smoke != null && smoke.Emitting != s.Broken) smoke.Emitting = s.Broken;
    }

    /// <summary>Maintenance drones circle auto-maintained buildings while they fix them.</summary>
    private void UpdateDrones(float delta)
    {
        var busy = new HashSet<int>();
        foreach (var s in _surface.Structures)
        {
            if (!s.AutoRepairing) continue;
            busy.Add(s.Id);
            if (!_drones.TryGetValue(s.Id, out var drone))
            {
                drone = BuildingModels.Drone(PlanetDriver.PlayerColors[s.Owner % PlanetDriver.PlayerColors.Length]);
                AddChild(drone);
                drone.Position = TileCentre(s.Tile) + new Vector3(0, 14f, 0); // flies down from above
                _drones[s.Id] = drone;
            }
            var a = _time * 1.3f + s.Id;
            var target = TileCentre(s.Tile) + new Vector3(Mathf.Cos(a) * 4f, 6.5f + Mathf.Sin(a * 2f) * 0.5f, Mathf.Sin(a) * 4f);
            drone.Position = drone.Position.Lerp(target, Mathf.Min(1f, delta * 3f));
            if (drone.GetNodeOrNull<CpuParticles3D>("Sparks") is { } sparks && sparks.Emitting == s.MaintenanceWaiting) sparks.Emitting = !s.MaintenanceWaiting;
        }
        foreach (var id in new List<int>(_drones.Keys))
        {
            if (busy.Contains(id)) continue;
            _drones[id].QueueFree();
            _drones.Remove(id);
        }
    }

    /// <summary>
    /// Each base's crew: two people walk from the core's door to the building on the job, weld at its front while
    /// they fix it, and walk back. The walk follows the sim's crew timing.
    /// </summary>
    private void UpdateBuildingCrews(float delta)
    {
        var active = new HashSet<int>();
        foreach (var core in _surface.Structures)
        {
            if (core.CrewPhase == CrewPhase.None) continue;
            active.Add(core.Id);
            if (!_buildingCrews.TryGetValue(core.Id, out var people))
            {
                var colour = PlanetDriver.PlayerColors[core.Owner % PlanetDriver.PlayerColors.Length];
                people = new[] { BuildingModels.Crew(colour), BuildingModels.Crew(colour) };
                foreach (var p in people) AddChild(p);
                _buildingCrews[core.Id] = people;
            }
            var job = _surface.FindStructure(core.CrewJob);
            var door = CoreLocal(core, new Vector3(0, 0, 5.8f));
            var jobCentre = job != null ? TileCentre(job.Tile) : door;
            var jobBasis = job != null ? new Basis(Vector3.Up, Mathf.DegToRad(-60f * job.Facing)) : Basis.Identity;
            var walk = Mathf.Max(1, core.CrewWalkTicks);
            var t = core.CrewPhase switch
            {
                CrewPhase.Out => Mathf.Clamp(core.CrewTicks / (float)walk, 0f, 1f),
                CrewPhase.Working => 1f,
                _ => 1f - Mathf.Clamp(core.CrewTicks / (float)walk, 0f, 1f),
            };
            var walking = core.CrewPhase != CrewPhase.Working;
            var welding = !walking && job != null && !job.MaintenanceWaiting;
            for (var i = 0; i < people.Length; i++)
            {
                var person = people[i];
                var spot = jobCentre + jobBasis * new Vector3(i == 0 ? -2.4f : 2.4f, 0, 4.6f);
                var at = door.Lerp(spot, Mathf.SmoothStep(0f, 1f, Mathf.Clamp(t * 1.08f - i * 0.08f, 0f, 1f)));
                var faceTo = walking ? (core.CrewPhase == CrewPhase.Back ? door : spot) : jobCentre;
                if (at.DistanceTo(person.Position) > 60f) person.Position = at; // first frame
                person.Position = at;
                var dir = (faceTo - at) with { Y = 0 };
                if (dir.LengthSquared() > 0.01f) person.Basis = person.Basis.Orthonormalized().Slerp(Basis.LookingAt(dir.Normalized(), Vector3.Up), Mathf.Min(1f, delta * 10f));
                var swing = walking ? Mathf.Sin(_time * 9f + i * 1.7f) * 30f : 0f;
                if (person.GetNodeOrNull<Node3D>("LegL") is { } l) l.RotationDegrees = new Vector3(swing, 0, 0);
                if (person.GetNodeOrNull<Node3D>("LegR") is { } r) r.RotationDegrees = new Vector3(-swing, 0, 0);
                if (person.GetNodeOrNull<Node3D>("Arm") is { } arm)
                {
                    arm.RotationDegrees = new Vector3(welding ? -15f + Mathf.Sin(_time * 6f + i) * 10f : 20f, 0, 0);
                    if (arm.GetNodeOrNull<Node3D>("Torch") is { } torch)
                    {
                        if (torch.GetNodeOrNull<CpuParticles3D>("Sparks") is { } sparks && sparks.Emitting != welding) sparks.Emitting = welding;
                        if (torch.GetNodeOrNull<Node3D>("Flame") is { } flame) flame.Visible = welding && Mathf.Sin(_time * 40f + i * 3f) > -0.6f;
                    }
                }
            }
        }
        foreach (var id in new List<int>(_buildingCrews.Keys))
        {
            if (active.Contains(id)) continue;
            foreach (var p in _buildingCrews[id]) p.QueueFree();
            _buildingCrews.Remove(id);
        }
    }

    /// <summary>Show a see-through building at a tile while placing (green if allowed, red if not); null hides it.</summary>
    public void SetGhost(string? buildingId, int tile, int facing, bool allowed)
    {
        if (buildingId != _ghostId)
        {
            _ghost?.QueueFree();
            _ghost = null;
            _ghostId = buildingId;
            if (buildingId != null)
            {
                _ghost = BuildingModels.Build(buildingId, Colors.White);
                BuildingModels.Override(_ghost, _ghostMaterial);
                AddChild(_ghost);
            }
        }
        if (_ghost == null) return;
        _ghost.Visible = tile >= 0;
        if (tile < 0) return;
        _ghost.Position = TileCentre(tile) + new Vector3(0, 0.1f, 0);
        _ghost.RotationDegrees = new Vector3(0, -60f * facing, 0);
        _ghostMaterial.AlbedoColor = allowed ? new Color(0.3f, 1f, 0.4f, 0.45f) : new Color(1f, 0.3f, 0.25f, 0.45f);
    }

    /// <summary>Outline of where the player may build (hexes within the colony's build radius).</summary>
    public void ShowBuildRange(int centre, int radius)
    {
        _rangeRing.Visible = centre >= 0;
        if (centre < 0) return;
        var material = Overlay(new Color(0.55f, 0.85f, 1f, 0.1f));
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = HexRadius * 0.88f, BottomRadius = HexRadius * 0.88f, Height = 0.12f, RadialSegments = 6, Rings = 1 } };
        var tiles = _surface.Map.Grid.Within(centre, radius);
        mm.InstanceCount = tiles.Count;
        for (var i = 0; i < tiles.Count; i++) mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, TileCentre(tiles[i]) + new Vector3(0, 0.2f, 0)));
        foreach (var c in _rangeRing.GetChildren()) c.QueueFree();
        _rangeRing.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material });
    }

    public void HideBuildRange() => _rangeRing.Visible = false;

    private void UpdateRovers(float alpha, float delta)
    {
        var alive = new HashSet<int>();
        foreach (var u in _surface.Units)
        {
            alive.Add(u.Id);
            var def = _sim.Data.SurfaceUnit(u.DefId);
            if (!_rovers.TryGetValue(u.Id, out var model))
            {
                var colour = PlanetDriver.PlayerColors[u.Owner % PlanetDriver.PlayerColors.Length];
                model = BuildingModels.Vehicle(def, colour);
                RoverLook.Paint(model, u.Name, u.Number);
                _grime[u.Id] = RoverLook.Grime.Attach(model, def.CanMine || def.CanFound ? 3.2f : 2.4f);
                _lamps[u.Id] = RoverLook.Lamps.Attach(model, u.Id);
                if (_lamps[u.Id].Area is { } wiped) _grime[u.Id].SetWipers(wiped);
                // Wheel spray from behind the rear wheels (rover) or just behind the tracks' rear sprockets (crawler,
                // Mining Vehicle Mk I: tracks at x ±1.13, ending at z 2.16 behind the scrapers); +Z is the back.
                // Construction rover: rear wheels at x ±1.35, z 2.2 (radius 0.62).
                var (sx, sz) = def.CanMine ? (1.13f, 2.4f) : def.CanFound ? (1.35f, 2.95f) : def.Mark switch { 1 => (1.3f, 1.75f), 3 => (1.5f, 2.5f), _ => (1.35f, 2.3f) };
                model.AddChild(new SprayRig(new[] { new Vector3(-sx, 0.15f, sz), new Vector3(sx, 0.15f, sz) }));
                AddChild(model);
                _rovers[u.Id] = model;
                var label = new Label3D
                {
                    FontSize = 34,
                    PixelSize = 0.09f,
                    OutlineSize = 10,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    Modulate = new Color(1f, 0.55f, 0.35f),
                };
                AddChild(label);
                _unitLabels[u.Id] = label;
            }

            UntiltRover(u.Id, model); // a flat tyre's lean comes off before the heading code turns it (RoverDamage)

            // Where the sim's lanes put it (PlanetView.Lanes.cs): round buildings, parked at access points. The one on its
            // own colony core's pad lines up with it and gets washed there.
            var pos = UnitPos(u, alpha);
            var moving = Rolling(u);
            var padCore = u.Path.Count == 0 && u.Mode == UnitMode.Mobile ? PadCore(u, u.Tile) : null;
            var onPad = padCore != null && OnPad(u, padCore);
            if (onPad)
            {
                // Parked on the repair pad: line up with it.
                var target = new Basis(Vector3.Up, Mathf.DegToRad(-60f * padCore!.Facing + BuildingModels.RepairPadYaw));
                model.Basis = model.Basis.Orthonormalized().Slerp(target, Mathf.Min(1f, delta * 4f));
            }
            if (moving && Heading(u) is var facing && facing != Vector3.Zero)
            {
                // Heading from its motion; the slerp rounds off the bends of the route.
                var target = Basis.LookingAt(facing, Vector3.Up);
                model.Basis = model.Basis.Orthonormalized().Slerp(target, Mathf.Min(1f, delta * 8f));
            }
            var rolled = RollOutFrom(u, pos, alpha, out var rolling);
            if (rolling)
            {
                if (model.Position == Vector3.Zero) model.Position = rolled; // just created: start at the door, not the map origin
                var heading = (rolled - model.Position) with { Y = 0 };
                if (heading.LengthSquared() > 0.0004f)
                    model.Basis = model.Basis.Orthonormalized().Slerp(Basis.LookingAt(heading.Normalized(), Vector3.Up), Mathf.Min(1f, delta * 8f));
                pos = rolled;
            }
            var aboard = false;
            if (_dropShips.Unloading(u, pos, alpha, out var carried, out var driving))
            {
                // Just delivered: it waits in its drop ship's bay, drives down the ramp, then on to its tile.
                if (carried.Basis is { } pose) model.Basis = pose;
                pos = carried.Origin;
                rolling = driving;
                aboard = carried.Basis != null && !driving; // still in the bay (maybe flying): no selection ring
            }
            model.Position = pos + new Vector3(0, 0.1f, 0);
            if (def.CanFound) AnimateConstructor(model, u, delta); // unfolding on its site (PlanetView.Founding.cs)
            UpdateRoverDamage(u, def, model, moving || rolling, delta); // flat wheels, lean, damage looks (rovers only)
            var ground = SprayRig.KindFor(_surface.Map, TileAt(pos));
            if (model.GetNodeOrNull<SprayRig>("SprayRig") is { } spray) spray.Spray(ground, moving || rolling);
            var dirty = false;
            if (_grime.TryGetValue(u.Id, out var grime))
            {
                if (_lamps.TryGetValue(u.Id, out var wipers)) grime.Wiped = wipers.Wiped;
                grime.Update(ground, moving || rolling, onPad, delta);
                dirty = grime.Dust + grime.Mud + grime.Snow > 0.02f;
            }
            if (_lamps.TryGetValue(u.Id, out var lamps))
            {
                // Wipers run on wet, muddy, snowy or dusty ground, and while the pad washes a dirty vehicle.
                var wipe = ((moving || rolling) && ground is SprayKind.Water or SprayKind.Mud or SprayKind.Snow or SprayKind.Dust) || (onPad && dirty);
                lamps.Update(moving || rolling, u.Broken || u.Integrity < Fix64.FromInt(50), wipe, delta);
            }
            if (model.GetNodeOrNull<Node3D>("Selected") is { } ring) ring.Visible = SelectedUnits.Contains(u.Id) && !aboard;
            if (model.GetNodeOrNull<Node3D>("Dish") is { } dish) dish.RotateY(delta * (moving ? 3f : 1f));

            var text = "";
            if (def.CanMine)
            {
                AnimateCrawler(model, u, def, alpha, delta);
                ShowCondition(model, u, def, delta);
                UpdateCrew(u, model, alpha, delta);
                text = u.Broken ? RepairLabel(u) : u.MiningStatus switch
                {
                    MiningStatus.StorageFull => "Storage full",
                    MiningStatus.NoDestination => "No storage to send to",
                    MiningStatus.Depleted when u.Mode != UnitMode.Mobile || u.Path.Count == 0 => "Deposit used up",
                    _ => "",
                };
            }
            else text = RoverText(u, def, model, alpha, delta);
            if (text.Length == 0 && def.CanFound) text = FounderLabel(u);
            var lbl = _unitLabels[u.Id];
            lbl.Text = text;
            lbl.Position = model.Position + new Vector3(0, 9f, 0);
        }
        foreach (var id in new List<int>(_rovers.Keys))
        {
            if (alive.Contains(id)) continue;
            _rovers[id].QueueFree();
            _rovers.Remove(id);
            _grime.Remove(id);
            _lamps.Remove(id);
            ForgetRover(id);
            _unitLabels[id].QueueFree();
            _unitLabels.Remove(id);
        }
        foreach (var id in new List<int>(_crews.Keys))
        {
            if (alive.Contains(id)) continue;
            foreach (var p in _crews[id].People) p.QueueFree();
            _crews.Remove(id);
        }
    }

    // ---------------------------------------------------------------- vehicle bay (render-only)

    private const float RollOutSeconds = 2.6f;

    /// <summary>
    /// Vehicle Bay: the assembly lights come on while it works, the label shows the order's progress, and the door
    /// rolls up just before a vehicle is done and stays up while it drives out.
    /// </summary>
    private void AnimateBay(SurfaceStructure s, StructureVisual v, bool working, float delta)
    {
        var fraction = _surface.ProductionFraction(s).ToFloat();
        if (working && s.Production.Count > 0 && s.UpgradingTo == null)
        {
            var name = _sim.Data.GroundVehicleNames.GetValueOrDefault(s.Production[0], s.Production[0]);
            v.Label.Text = $"Assembling {name} {Mathf.FloorToInt(fraction * 100)}%" + (s.Status == StructureStatus.LowPower ? " (low power)" : "");
            v.Label.Modulate = new Color(0.6f, 0.85f, 1f);
        }
        if (v.Model.GetNodeOrNull<Node3D>("WorkGlow") is { } glow) glow.Visible = working;

        var leaving = false;
        foreach (var u in _surface.Units)
            if (u.BuiltAtTile == s.Tile && u.BuiltAtTick >= 0 && (_sim.Tick - u.BuiltAtTick) < RollOutSeconds * Simulation.TickRate) leaving = true;
        var target = leaving || (working && fraction > 0.85f) ? 1f : 0f;
        var open = _doorOpen.GetValueOrDefault(s.Id);
        open = Mathf.MoveToward(open, target, delta / 0.9f);
        _doorOpen[s.Id] = open;
        if (v.Model.GetNodeOrNull<Node3D>("Door") is { } door) door.Scale = new Vector3(1, Mathf.Lerp(1f, 0.08f, Mathf.SmoothStep(0f, 1f, open)), 1);
    }

    /// <summary>Just after rolling out of a bay, a vehicle drives from the door to its tile instead of appearing there.</summary>
    private Vector3 RollOutFrom(SurfaceUnit u, Vector3 spot, float alpha, out bool rolling)
    {
        rolling = false;
        if (u.BuiltAtTick < 0) return spot;
        var age = (_sim.Tick - u.BuiltAtTick + alpha) / Simulation.TickRate;
        if (age >= RollOutSeconds || u.Path.Count > 0 || u.Mode != UnitMode.Mobile) return spot;
        var bay = _surface.StructureAt(u.BuiltAtTile);
        if (bay == null) return spot;
        rolling = true;
        var door = TileCentre(bay.Tile) + new Basis(Vector3.Up, Mathf.DegToRad(-60f * bay.Facing)) * BuildingModels.BayDoorOffset;
        var inside = TileCentre(bay.Tile) + new Basis(Vector3.Up, Mathf.DegToRad(-60f * bay.Facing)) * new Vector3(0, 0, -0.8f);
        var t = age / RollOutSeconds;
        // First out through the door, then on to the tile it was given.
        return t < 0.35f ? inside.Lerp(door, Mathf.SmoothStep(0f, 1f, t / 0.35f)) : door.Lerp(spot, Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.65f));
    }

    // ---------------------------------------------------------------- breakdowns and repair (render-only)

    /// <summary>The unit's own finished colony core on this tile (it has the repair pad), or null.</summary>
    private SurfaceStructure? PadCore(SurfaceUnit u, int tile)
    {
        var s = _surface.StructureAt(tile);
        return s != null && s.Owner == u.Owner && s.State == StructureState.Complete && _sim.Data.Building(s.DefId).IsCore ? s : null;
    }

    private Vector3 CoreLocal(SurfaceStructure core, Vector3 local) =>
        TileCentre(core.Tile) + new Basis(Vector3.Up, Mathf.DegToRad(-60f * core.Facing)) * local;

    private string RepairLabel(SurfaceUnit u)
    {
        if (!_surface.IsRepairing(u)) return "Broken down";
        if (u.RepairWaiting) return "Repair waiting: not enough resources";
        if (_surface.CrewOut(u) < Fix64.One) return "Repair crew on the way";
        return $"Repairing {Mathf.FloorToInt(u.Integrity.ToFloat())}%";
    }

    /// <summary>
    /// While broken: sparks for a minor fault, smoke for a major breakdown, smoke and fire after an explosion
    /// (the fire goes out once the crew start work). The bang itself plays once, when the breakdown happens.
    /// </summary>
    private void ShowCondition(Node3D model, SurfaceUnit u, SurfaceUnitDef def, float delta)
    {
        if (!_seenBreak.TryGetValue(u.Id, out var seen)) _seenBreak[u.Id] = seen = u.BrokeAtTick; // don't replay old ones
        if (u.BrokeAtTick != seen)
        {
            _seenBreak[u.Id] = u.BrokeAtTick;
            Blast(model.Position, u.Breakdown);
        }
        var atWork = _surface.IsRepairing(u) && _surface.CrewOut(u) >= Fix64.One;
        var severity = u.Broken ? u.Breakdown : -1;
        void Emit(string name, bool on)
        {
            if (model.GetNodeOrNull<CpuParticles3D>(name) is { } p && p.Emitting != on) p.Emitting = on;
        }
        Emit("Smoke", severity >= 1);
        Emit("Sparks", severity == 0 && !atWork);
        if (model.GetNodeOrNull<Node3D>("Fire") is { } fire)
        {
            fire.Visible = severity == 2 && !atWork;
            if (fire.Visible) fire.Scale = new Vector3(1, 0.8f + 0.25f * Mathf.Sin(_time * 17f) + 0.15f * Mathf.Sin(_time * 29f), 1);
        }
    }

    /// <summary>One-shot effect at a breakdown: a spark burst (minor), a bang and dust (major) or a fireball, debris and a shock ring (explosion).</summary>
    private void Blast(Vector3 at, int severity)
    {
        var root = new Node3D { Position = at + new Vector3(0, 2.5f, 0) };
        AddChild(root);
        var big = severity >= 2;
        var light = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.25f), LightEnergy = big ? 14f : severity == 1 ? 6f : 2.5f, OmniRange = big ? 45f : 22f };
        root.AddChild(light);
        var tween = root.CreateTween().SetParallel();
        tween.TweenProperty(light, "light_energy", 0f, big ? 1.4 : 0.6);

        // Sparks for every breakdown.
        root.AddChild(new CpuParticles3D
        {
            Amount = big ? 80 : 40,
            Lifetime = 1.1f,
            OneShot = true,
            Explosiveness = 1f,
            Direction = Vector3.Up,
            Spread = 80f,
            InitialVelocityMin = 5f,
            InitialVelocityMax = big ? 22f : 11f,
            Gravity = new Vector3(0, -14f, 0),
            Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 4, Rings = 2, Material = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.75f, 0.35f), EmissionEnergyMultiplier = 5f } },
            Emitting = true,
        });
        if (severity >= 1)
        {
            var puff = BuildingModels.Puffs("Burst", Vector3.Zero, big ? new Color(0.12f, 0.1f, 0.09f) : new Color(0.55f, 0.5f, 0.42f), big ? 60 : 30, 2.6f, big ? 2f : 1.2f, big ? 4.5f : 2.6f, 0f);
            puff.OneShot = true;
            puff.Explosiveness = 0.9f;
            puff.Spread = 80f;
            puff.InitialVelocityMax = big ? 9f : 5f;
            puff.Emitting = true;
            root.AddChild(puff);
        }
        if (big)
        {
            // Fireball: an emissive sphere that swells and fades.
            var fireMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.55f, 0.15f, 0.95f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = new Color(1f, 0.5f, 0.12f),
                EmissionEnergyMultiplier = 6f,
            };
            var ball = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f }, MaterialOverride = fireMat, Scale = Vector3.One * 0.5f };
            root.AddChild(ball);
            tween.TweenProperty(ball, "scale", Vector3.One * 7f, 0.55).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tween.TweenProperty(fireMat, "albedo_color:a", 0f, 0.8).SetDelay(0.15);
            tween.TweenProperty(fireMat, "emission_energy_multiplier", 0f, 0.8).SetDelay(0.15);

            // Shock ring along the ground.
            var ringMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.6f, 0.8f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 32 }, MaterialOverride = ringMat, Position = new Vector3(0, -2.2f, 0) };
            root.AddChild(ring);
            tween.TweenProperty(ring, "scale", new Vector3(16f, 1f, 16f), 0.7).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(ringMat, "albedo_color:a", 0f, 0.7);

            // Debris thrown out and falling back.
            root.AddChild(new CpuParticles3D
            {
                Amount = 36,
                Lifetime = 2f,
                OneShot = true,
                Explosiveness = 1f,
                Direction = Vector3.Up,
                Spread = 60f,
                InitialVelocityMin = 8f,
                InitialVelocityMax = 18f,
                Gravity = new Vector3(0, -22f, 0),
                AngularVelocityMin = -360f,
                AngularVelocityMax = 360f,
                ScaleAmountMin = 0.6f,
                ScaleAmountMax = 1.6f,
                Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.2f, 0.5f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.18f, 0.16f), Roughness = 0.8f } },
                Emitting = true,
            });
        }
        GetTree().CreateTimer(4.0).Timeout += root.QueueFree;
    }

    /// <summary>
    /// The repair crew: two people walk out of the core's door to either side of the crawler, weld (sparks,
    /// flicker) until it's fixed, then walk back in. Their walk out follows the sim's crew time.
    /// </summary>
    private void UpdateCrew(SurfaceUnit u, Node3D model, float alpha, float delta)
    {
        var repairing = _surface.IsRepairing(u);
        _crews.TryGetValue(u.Id, out var crew);
        if (repairing && (crew == null || crew.Back >= 0f))
        {
            var core = PadCore(u, u.Tile)!;
            if (crew == null)
            {
                var colour = PlanetDriver.PlayerColors[u.Owner % PlanetDriver.PlayerColors.Length];
                crew = new CrewVisual { People = new[] { BuildingModels.Crew(colour), BuildingModels.Crew(colour) } };
                foreach (var p in crew.People) AddChild(p);
                _crews[u.Id] = crew;
            }
            crew.Door = CoreLocal(core, new Vector3(0, 0, 5.8f));
            crew.Back = -1f;
        }
        if (crew == null) return;

        var sides = new[] { new Vector3(-2.6f, 0, 0.4f), new Vector3(2.6f, 0, -0.6f) };
        var crewTicks = Mathf.Max(1, (_sim.Data.SurfaceUnit(u.DefId).Repair?.CrewSeconds ?? 0) * Simulation.TickRate);
        var walkOut = repairing ? Mathf.Clamp((u.RepairTicks + alpha) / crewTicks, 0f, 1f) : 1f;
        if (!repairing)
        {
            crew.Back = Mathf.Min(1f, Mathf.Max(0f, crew.Back) + delta / 1.6f);
            if (crew.Back >= 1f)
            {
                foreach (var p in crew.People) p.QueueFree();
                _crews.Remove(u.Id);
                return;
            }
        }
        for (var i = 0; i < crew.People.Length; i++)
        {
            var person = crew.People[i];
            var work = model.Position + model.Basis * sides[i];
            work.Y = model.Position.Y;
            var t = repairing ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp(walkOut * 1.15f - i * 0.15f, 0f, 1f)) : 1f - crew.Back;
            var at = crew.Door.Lerp(work, t);
            var walking = repairing ? t < 1f : true;
            var faceTo = walking ? (repairing ? work : crew.Door) : model.Position;
            person.Position = at;
            var dir = (faceTo - at) with { Y = 0 };
            if (dir.LengthSquared() > 0.01f) person.Basis = person.Basis.Orthonormalized().Slerp(Basis.LookingAt(dir.Normalized(), Vector3.Up), Mathf.Min(1f, delta * 10f));

            // Walk cycle while moving; a slight crouch and torch work at the crawler.
            var swing = walking ? Mathf.Sin(_time * 9f + i * 1.7f) * 30f : 0f;
            if (person.GetNodeOrNull<Node3D>("LegL") is { } l) l.RotationDegrees = new Vector3(swing, 0, 0);
            if (person.GetNodeOrNull<Node3D>("LegR") is { } r) r.RotationDegrees = new Vector3(-swing, 0, 0);
            var welding = repairing && !walking && !u.RepairWaiting;
            if (person.GetNodeOrNull<Node3D>("Arm") is { } arm)
            {
                arm.RotationDegrees = new Vector3(welding ? -15f + Mathf.Sin(_time * 6f + i) * 10f : 20f, 0, 0);
                if (arm.GetNodeOrNull<Node3D>("Torch") is { } torch)
                {
                    if (torch.GetNodeOrNull<CpuParticles3D>("Sparks") is { } sparks && sparks.Emitting != welding) sparks.Emitting = welding;
                    if (torch.GetNodeOrNull<Node3D>("Flame") is { } flame) flame.Visible = welding && Mathf.Sin(_time * 40f + i * 3f) > -0.6f;
                }
            }
        }
    }

    private static float Stage(float t, float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - from) / (to - from), 0f, 1f));

    /// <summary>
    /// Unfurling, in stages: the legs swing out and brace on the ground, the boom lifts over the front, then the
    /// drill string lowers into the ground. While digging the bit spins, dust rises, the hull shudders and the
    /// hopper fills up; it empties when a cargo pod leaves. Packing plays it backwards.
    /// </summary>
    private void AnimateCrawler(Node3D model, SurfaceUnit u, SurfaceUnitDef def, float alpha, float delta)
    {
        var total = Mathf.Max(1, def.DeploySeconds * Simulation.TickRate);
        var f = _surface.DeployFraction(u).ToFloat();
        if (u.Mode == UnitMode.Deploying) f = Mathf.Min(1f, f + alpha / total);
        if (u.Mode == UnitMode.Packing) f = Mathf.Max(0f, f - alpha / total);

        var legs = Stage(f, 0f, 0.4f);
        var feet = Stage(f, 0.02f, 0.3f); // the pads swing down off the beams while the legs go out, before they touch
        var boom = Stage(f, 0.3f, 0.75f);
        var drill = Stage(f, 0.7f, 1f);
        for (var i = 0; i < 4; i++)
            if (model.GetNodeOrNull<Node3D>($"Leg{i}") is { } leg)
            {
                leg.RotationDegrees = new Vector3(0, i >= 2 ? 180 : 0, Mathf.Lerp(BuildingModels.FoldedLeg, BuildingModels.DeployedLeg, legs)); // legs 2 and 3 are on the left
                if (leg.GetNodeOrNull<Node3D>($"Leg{i}Foot") is { } foot)
                    foot.RotationDegrees = new Vector3(0, 0, Mathf.Lerp(BuildingModels.FoldedFoot, 0f, feet));
            }
        var angle = BuildingModels.DeployedBoom * boom;
        var arm = model.GetNodeOrNull<Node3D>("Boom");
        if (arm != null)
        {
            arm.RotationDegrees = new Vector3(angle, 0, 0);
            if (arm.GetNodeOrNull<Node3D>("Drill") is { } head)
            {
                head.RotationDegrees = new Vector3(-angle, 0, 0); // hang straight down
                var reach = Mathf.Lerp(0.3f, BuildingModels.BoomTipHeight - 1.0f, drill);
                if (head.GetNodeOrNull<Node3D>("String") is { } str) str.Scale = new Vector3(1, reach, 1);
                if (head.GetNodeOrNull<Node3D>("Bit") is { } bit)
                {
                    bit.Position = new Vector3(0, -reach, 0);
                    if (u.Mode == UnitMode.Deployed && u.MiningStatus == MiningStatus.Mining) bit.RotateY(delta * 9f);
                }
            }
        }

        var digging = u.Mode == UnitMode.Deployed && u.MiningStatus == MiningStatus.Mining;
        if (model.GetNodeOrNull<CpuParticles3D>("Dust") is { } dust && dust.Emitting != digging) dust.Emitting = digging;
        if (model.GetNodeOrNull<Light3D>("WorkLight") is { } lamp) lamp.Visible = f > 0.5f;
        if (model.GetNodeOrNull<Node3D>("Body") is { } body)
        {
            body.Position = digging ? new Vector3(0, Mathf.Sin(_time * 38f) * 0.04f, Mathf.Sin(_time * 23f) * 0.03f) : Vector3.Zero;
            if (body.GetNodeOrNull<Node3D>("Fill") is { } fill)
            {
                var target = def.Hopper > Fix64.Zero ? Mathf.Clamp(u.Hopper.ToFloat() / def.Hopper.ToFloat(), 0.01f, 1f) : 0.01f;
                // Rise smoothly while filling; drop at once when a load is sent.
                var y = target < fill.Scale.Y ? target : Mathf.Lerp(fill.Scale.Y, target, Mathf.Min(1f, delta * 6f));
                fill.Scale = new Vector3(1, y, 1);
            }
            // Lift rams (glb): each barrel on the hull points at its rod's pin on the boom, and the rod back at the
            // barrel's lug, so the pair telescopes as the boom rises. Both turn about X only; their glb axis is -Z.
            if (arm != null)
                foreach (var side in new[] { "L", "R" })
                {
                    if (body.GetNodeOrNull<Node3D>($"Ram{side}") is not { } barrel || arm.GetNodeOrNull<Node3D>($"RamRod{side}") is not { } rod) continue;
                    var d = arm.Transform * rod.Position - body.Transform * barrel.Position; // lug → pin, in model space
                    barrel.Rotation = new Vector3(Mathf.Atan2(d.Y, -d.Z), 0, 0);
                    rod.Rotation = new Vector3(Mathf.Atan2(-d.Y, d.Z) - Mathf.DegToRad(angle), 0, 0);
                }
        }
    }

    /// <summary>Cargo pods fly a high arc from the crawler to the storage building they're headed for (a refinery: onto its pad).</summary>
    private void UpdatePods(float alpha)
    {
        var alive = new HashSet<int>();
        foreach (var p in _surface.Pods)
        {
            alive.Add(p.Id);
            if (!_pods.TryGetValue(p.Id, out var model))
            {
                model = BuildingModels.CargoPod(PlanetDriver.PlayerColors[p.Owner % PlanetDriver.PlayerColors.Length]);
                AddChild(model);
                _pods[p.Id] = model;
            }
            // a high arc; onto a refinery's pad it ends by settling straight down (PlanetView.Refinery.cs)
            var (here, basis) = PodPose(p, alpha);
            model.Position = here;
            if (basis is { } heading) model.Basis = heading;
        }
        foreach (var id in new List<int>(_pods.Keys))
        {
            if (alive.Contains(id)) continue;
            _pods[id].QueueFree();
            _pods.Remove(id);
        }
    }
}
