using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Draws a planet surface: one continuous terrain mesh over the hex grid (smooth slopes, mountain peaks,
/// coasts running under animated water), trees, deposit crystals, orbital-scan hints, the hover marker,
/// the colony pod's descent, structures and survey rovers. Fog of war is a one-texel-per-tile texture
/// the terrain, water and tree shaders read. Render-only: nothing here changes the sim.
/// </summary>
public partial class PlanetView : Node3D
{
    /// <summary>World width of one hex (flat side to flat side).</summary>
    public const float HexWidth = 10f;

    /// <summary>Height of the water surface.</summary>
    public const float SeaLevel = 0.35f;

    private static readonly float HexRadius = HexWidth / Mathf.Sqrt(3f);

    private Simulation _sim = null!;
    private PlanetSurface _surface = null!;
    private int _localPlayer;

    private float[] _centreHeight = System.Array.Empty<float>();
    private Image _fogImage = null!;
    private ImageTexture _fogTexture = null!;
    private readonly List<ShaderMaterial> _fogMaterials = new();

    private MultiMesh _deposits = null!;
    private readonly List<Label3D> _hintLabels = new();
    private MeshInstance3D _hover = null!;
    private StandardMaterial3D _hoverMaterial = null!;
    private Node3D _pod = null!;
    private readonly Dictionary<int, Node3D> _rovers = new();

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
    private StandardMaterial3D _hologram = null!;
    private MeshInstance3D _rangeRing = null!;
    private int _surveyedCount = -1;
    private LandingPhase _lastPhase = (LandingPhase)(-1);

    public void Build(Simulation sim, int localPlayer)
    {
        _sim = sim;
        _surface = sim.Surface!;
        _localPlayer = localPlayer;

        BuildFogTexture();
        BuildTerrain();
        BuildWater();
        BuildTrees();
        BuildHints();
        _deposits = BuildCrystals();

        _hoverMaterial = Overlay(new Color(1, 1, 1, 0.35f));
        _hover = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = HexRadius * 0.95f, BottomRadius = HexRadius * 0.95f, Height = 0.3f, RadialSegments = 6, Rings = 1 },
            MaterialOverride = _hoverMaterial,
            Visible = false,
        };
        AddChild(_hover);

        _ghostMaterial = Overlay(new Color(0.3f, 1f, 0.4f, 0.45f));
        _hologram = Overlay(new Color(0.45f, 0.8f, 1f, 0.35f));
        _hologram.NoDepthTest = false;
        _rangeRing = new MeshInstance3D { Visible = false };
        AddChild(_rangeRing);

        _pod = BuildingModels.Pod();
        _pod.Visible = false;
        AddChild(_pod);
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
        foreach (var s in me.Surveyed)
            if (s) surveyed++;
        var vision = VisionSignature();
        if (surveyed != _surveyedCount || me.Phase != _lastPhase || vision != _visionSignature)
        {
            if (me.Phase == LandingPhase.Landed && _lastPhase == LandingPhase.Descending) Dust(TileCentre(me.LandingTile));
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
        UpdatePod(me, alpha);
        UpdateStructures(delta);
        UpdateRovers(alpha, delta);
    }

    public void SetHover(int tile, Color color)
    {
        _hover.Visible = tile >= 0;
        if (tile < 0) return;
        _hover.Position = TileCentre(tile) + new Vector3(0, 0.3f, 0);
        _hoverMaterial.AlbedoColor = color;
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
            "ocean" => -2.4f + e * 0.6f,
            "shallows" => -0.5f,
            _ => 1.1f + terrain.Height * 1.5f + e * 1.4f,
        };
    }

    private static long CornerKey(Vector3 p) => ((long)Mathf.RoundToInt(p.X * 10f) << 32) ^ (uint)Mathf.RoundToInt(p.Z * 10f);

    private void BuildTerrain()
    {
        var map = _surface.Map;
        var grid = map.Grid;
        _centreHeight = new float[grid.Count];

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
            var peak = map.Terrain(t).Id == "mountains" ? 3.2f + map.Elevation[t].ToFloat() * 3f : 0f;
            _centreHeight[t] = h + peak;
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var index = 0;
        for (var t = 0; t < grid.Count; t++)
        {
            var terrain = map.Terrain(t);
            var colour = Color.FromHtml(terrain.Color).Lightened((map.Elevation[t].ToFloat() - 0.5f) * 0.15f);
            if (map.IsWater(t)) colour = new Color(0.36f, 0.33f, 0.24f).Lerp(colour, 0.35f); // seabed
            var centreColour = colour;
            if (terrain.Id == "mountains") centreColour = map.Elevation[t].ToFloat() > 0.96f ? new Color(0.94f, 0.95f, 0.97f) : colour.Lightened(0.25f);
            if (terrain.Id == "hills") centreColour = colour.Lightened(0.08f);

            var centre = centres[t] with { Y = _centreHeight[t] };
            var corners = new Vector3[6];
            for (var k = 0; k < 6; k++)
            {
                var p = centres[t] + Corner(k);
                var s = sum[CornerKey(p)];
                corners[k] = p with { Y = s.Total / s.Count };
            }

            // Normals: average the fan's face normals (pointing up, whatever the winding).
            var faceNormals = new Vector3[6];
            for (var k = 0; k < 6; k++)
            {
                var n = (corners[k] - centre).Cross(corners[(k + 1) % 6] - centre).Normalized();
                faceNormals[k] = n.Y < 0 ? -n : n;
            }
            var centreNormal = Vector3.Zero;
            foreach (var n in faceNormals) centreNormal += n;

            st.SetColor(centreColour.SrgbToLinear());
            st.SetUV(new Vector2(0, 0));
            st.SetNormal(centreNormal.Normalized());
            st.AddVertex(centre);
            for (var k = 0; k < 6; k++)
            {
                st.SetColor(colour.SrgbToLinear());
                st.SetUV(new Vector2(1, 0));
                st.SetNormal((faceNormals[k] + faceNormals[(k + 5) % 6]).Normalized());
                st.AddVertex(corners[k]);
            }
            for (var k = 0; k < 6; k++)
            {
                st.AddIndex(index);
                st.AddIndex(index + 1 + k);
                st.AddIndex(index + 1 + (k + 1) % 6);
            }
            index += 7;
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

    /// <summary>Conifers on forest tiles and the odd tree on plains, placed with render-only randomness.</summary>
    private void BuildTrees()
    {
        var map = _surface.Map;
        var rng = new RandomNumberGenerator { Seed = 4242UL };
        var transforms = new List<Transform3D>();
        var colours = new List<Color>();
        for (var t = 0; t < map.Grid.Count; t++)
        {
            var id = map.Terrain(t).Id;
            var count = id == "forest" ? 5 : id == "plains" && rng.Randf() < 0.18f ? 1 : 0;
            for (var i = 0; i < count; i++)
            {
                var angle = rng.RandfRange(0, Mathf.Tau);
                var dist = Mathf.Sqrt(rng.Randf()) * HexRadius * 0.7f;
                var pos = TileCentre(t) + new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);
                var size = rng.RandfRange(0.8f, 1.35f);
                transforms.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(size, size * rng.RandfRange(0.9f, 1.3f), size)), pos + new Vector3(0, 1.6f * size, 0)));
                colours.Add(new Color(0.13f, 0.3f, 0.14f).Lerp(new Color(0.24f, 0.42f, 0.2f), rng.Randf()).SrgbToLinear());
            }
        }
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 1.1f, Height = 3.4f, RadialSegments = 7, Rings = 1 },
        };
        mm.InstanceCount = transforms.Count;
        for (var i = 0; i < transforms.Count; i++)
        {
            mm.SetInstanceTransform(i, transforms[i]);
            mm.SetInstanceColor(i, colours[i]);
        }
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = FogMaterial("res://shaders/foliage.gdshader") });
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

    /// <summary>Set when the fog changed this frame (the minimap redraws its terrain then).</summary>
    public bool FogChanged { get; set; }

    public enum Fog { Visible, Explored, Unexplored }

    /// <summary>
    /// Three levels: Visible = in sight of your buildings or rovers now; Explored = surveyed before, shown dimmed;
    /// Unexplored = near-black. From orbit the scan shows the whole surface.
    /// </summary>
    public Fog FogAt(int tile)
    {
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
            if (u.Owner == _localPlayer) See(u.Tile, u.SurveyRadius + 1);
        if (me.Phase == LandingPhase.Descending) See(me.LandingTile, 2);
    }

    /// <summary>A small cluster of glowing crystals on every surveyed deposit (dim when out of sight).</summary>
    private void PlaceDeposits(SurfacePlayer me)
    {
        var map = _surface.Map;
        var tiles = new List<int>();
        for (var t = 0; t < map.Grid.Count; t++)
            if (me.Surveyed[t] && map.DepositResource[t] >= 0 && map.DepositAmount[t] > FinalFrontier.Sim.Math.Fix64.Zero) tiles.Add(t);
        _deposits.InstanceCount = tiles.Count * 3;
        for (var i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var id = _sim.Data.Resources[map.DepositResource[t]].Id;
            var size = Mathf.Clamp(map.DepositAmount[t].ToFloat() / 500f, 0.7f, 1.5f);
            var colour = (Icons.ResourceColors.TryGetValue(id, out var c) ? c : new Color(0.8f, 0.6f, 1f)).SrgbToLinear();
            colour.A = FogAt(t) == Fog.Visible ? 1f : 0.3f;
            var basePos = TileCentre(t) + new Vector3(2.4f, 0, -1.4f);
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
                Position = sum / n + new Vector3(0, 60, 0),
                FontSize = 44,
                PixelSize = 0.34f,
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

    private void UpdatePod(SurfacePlayer me, float alpha)
    {
        _pod.Visible = me.Phase == LandingPhase.Descending;
        if (!_pod.Visible) return;
        var total = (float)(me.LandsAtTick - me.DescentStartTick);
        var t = Mathf.Clamp((_sim.Tick - me.DescentStartTick + alpha) / total, 0f, 1f);
        var eased = 1f - (1f - t) * (1f - t) * (1f - t); // fast entry, slow final braking
        var ground = TileCentre(me.LandingTile);
        _pod.Position = ground + new Vector3(0, Mathf.Lerp(260f, 0.2f, eased), 0);
        _pod.RotationDegrees = new Vector3(0, t * 40f, 0);
        if (_pod.GetNodeOrNull<Node3D>("Flame") is { } flame) flame.Scale = new Vector3(1, Mathf.Lerp(0.7f, 1.6f, t), 1);
    }

    /// <summary>One-shot dust cloud when the pod touches down.</summary>
    private void Dust(Vector3 at)
    {
        var ramp = new Gradient
        {
            Offsets = new[] { 0f, 0.3f, 1f },
            Colors = new[] { new Color(0.82f, 0.74f, 0.6f, 0.0f), new Color(0.78f, 0.7f, 0.56f, 0.55f), new Color(0.6f, 0.55f, 0.45f, 0f) },
        };
        var dust = new CpuParticles3D
        {
            Position = at + new Vector3(0, 0.5f, 0),
            Amount = 90,
            Lifetime = 2.6f,
            OneShot = true,
            Explosiveness = 0.95f,
            Direction = Vector3.Up,
            Spread = 85f,
            InitialVelocityMin = 6f,
            InitialVelocityMax = 16f,
            Gravity = new Vector3(0, -2.5f, 0),
            DampingMin = 3f,
            DampingMax = 5f,
            ScaleAmountMin = 0.8f,
            ScaleAmountMax = 2.2f,
            ColorRamp = ramp,
            Mesh = new SphereMesh
            {
                Radius = 0.7f,
                Height = 1.4f,
                RadialSegments = 6,
                Rings = 3,
                Material = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            },
            Emitting = true,
        };
        AddChild(dust);
        GetTree().CreateTimer(4.0).Timeout += dust.QueueFree;
    }

    /// <summary>
    /// Keeps one model per structure: a blue hologram that rises with construction progress, swapped for
    /// the solid model on completion. Labels show queue/progress or why a building is idle.
    /// </summary>
    private void UpdateStructures(float delta)
    {
        var seen = new HashSet<int>();
        foreach (var s in _surface.Structures)
        {
            seen.Add(s.Id);
            var complete = s.State == StructureState.Complete;
            var needsModel = !_structures.TryGetValue(s.Id, out var v)
                || (v.State != StructureState.Complete && complete)
                || v.DefId != s.DefId; // a finished upgrade changes the model
            if (needsModel)
            {
                if (v != null)
                {
                    v.Model.QueueFree();
                    v.Label.QueueFree();
                }
                var model = BuildingModels.Build(s.DefId, PlanetDriver.PlayerColors[s.Owner % PlanetDriver.PlayerColors.Length]);
                if (!complete) BuildingModels.Override(model, _hologram);
                model.Position = TileCentre(s.Tile);
                model.RotationDegrees = new Vector3(0, -60f * s.Facing, 0);
                AddChild(model);
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

            if (!complete)
            {
                var f = Mathf.Max(0.08f, _surface.BuildFraction(s).ToFloat());
                v.Model.Scale = new Vector3(1, f, 1);
                v.Label.Text = s.State == StructureState.Queued ? "Queued" : $"Building {Mathf.RoundToInt(f * 100)}%";
                v.Label.Modulate = new Color(0.6f, 0.85f, 1f);
                continue;
            }

            v.Model.Scale = Vector3.One;
            v.Label.Text = s.UpgradingTo != null ? $"Upgrading {Mathf.RoundToInt(_surface.UpgradeFraction(s).ToFloat() * 100)}%"
                : s.Status switch
                {
                    StructureStatus.NoUpkeep => "Idle: can't pay upkeep",
                    StructureStatus.Depleted => "Deposit used up",
                    StructureStatus.LowPower => "Low power",
                    _ => "",
                };
            v.Label.Modulate = s.UpgradingTo != null ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.55f, 0.35f);
            var working = s.Status is StructureStatus.Working or StructureStatus.LowPower;
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
            if (!_rovers.TryGetValue(u.Id, out var model))
            {
                model = BuildingModels.Rover(PlanetDriver.PlayerColors[u.Owner % PlanetDriver.PlayerColors.Length]);
                AddChild(model);
                _rovers[u.Id] = model;
            }

            var from = TileCentre(u.Tile);
            var pos = from;
            var moving = u.Path.Count > 0;
            if (moving)
            {
                var to = TileCentre(u.Path[0]);
                var cost = _surface.Map.Terrain(u.Path[0]).MoveCost;
                var f = Mathf.Clamp((u.Progress.ToFloat() + u.SpeedPerTick.ToFloat() * alpha) / cost, 0f, 1f);
                pos = from.Lerp(to, f);
                var facing = (to - from) with { Y = 0 };
                if (facing.LengthSquared() > 0.001f)
                {
                    var target = Basis.LookingAt(facing.Normalized(), Vector3.Up);
                    model.Basis = model.Basis.Orthonormalized().Slerp(target, Mathf.Min(1f, delta * 8f));
                }
            }
            model.Position = pos + new Vector3(0, 0.1f, 0);
            if (model.GetNodeOrNull<Node3D>("Selected") is { } ring) ring.Visible = SelectedUnits.Contains(u.Id);
            if (model.GetNodeOrNull<Node3D>("Dish") is { } dish) dish.RotateY(delta * (moving ? 3f : 1f));
        }
        foreach (var id in new List<int>(_rovers.Keys))
        {
            if (alive.Contains(id)) continue;
            _rovers[id].QueueFree();
            _rovers.Remove(id);
        }
    }
}
