using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Space;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Orbit any body (owner 2026-11-08: "every object can be entered into orbit"): the PLANET / ORBIT / ATMOS displays show
// the body the flagship is at (SystemSource.OrbitBodyId). The colony's body keeps the real globe (_globe: the surface's
// painted map, scans, markers). Any other body gets _bodyGlobe instead, turned with the same drag:
//  - a landable class with a surface generator (class M, class L): a look-only globe painted by OrbitPainter from a
//    SurfaceMap generated for that class and body (seed = SystemGenerator.SurfaceSeed in a star-system game, so native
//    towns line up; else the game seed mixed with the body id). It never touches the
//    game's PlanetSurface; the maps are kept per body, the painted textures only for the body shown.
//  - asteroid fields: a field of tumbling rocks instead of a globe.
//  - everything else: shaders/body_globe.gdshader, one mode per kind (gas giant, airless rock, ice, K, H, Y, N).
// Clouds and the air shell show only where the body has thick air (class M, L, N). Render-only.
public partial class OrbitView
{
    private Node3D _bodyGlobe = null!;
    private MeshInstance3D _lookGround = null!;
    private ShaderMaterial? _lookMaterial;
    private MeshInstance3D _procGround = null!;
    private ShaderMaterial _procMaterial = null!;
    private Node3D? _rockField;
    private bool _atColony = true;
    private bool _cloudsOn = true, _airOn = true, _bodySolid = true;
    private string? _orbitBodyId;
    private string? _lookShownFor;
    private readonly Dictionary<string, SurfaceMap> _lookMaps = new();

    /// <summary>Classes painted from a generated surface map (they have a surface generator and a PlanetLook palette).</summary>
    private static readonly HashSet<string> LookOnlyClasses = new() { "class_m", "class_l" };

    /// <summary>True while the globe displays show the colony's own body (scans, markers, tile picking).</summary>
    public bool AtColony => _atColony;

    /// <summary>The body the globe displays show (null = the colony's).</summary>
    public string? OrbitBodyId => _orbitBodyId;

    /// <summary>A look-only generated surface map for another landable body, if one was made (its readings for Live data).</summary>
    public SurfaceMap? LookMapOf(string bodyId) => _lookMaps.TryGetValue(bodyId, out var m) ? m : null;

    private void BuildBodyGlobe(Mesh sphere)
    {
        _bodyGlobe = new Node3D { Name = "BodyGlobe", Visible = false };
        AddChild(_bodyGlobe);
        _lookGround = new MeshInstance3D { Name = "LookGround", Mesh = sphere, Visible = false };
        _bodyGlobe.AddChild(_lookGround);
        _procMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/body_globe.gdshader") };
        _procGround = new MeshInstance3D { Name = "ProcGround", Mesh = sphere, MaterialOverride = _procMaterial, Visible = false };
        _bodyGlobe.AddChild(_procGround);
    }

    private void RefreshBodyGlobe(float delta)
    {
        if (_rockField != null && _rockField.Visible) _rockField.RotateY(delta * 0.01f);
        RefreshCivilization(delta); // a civilization's lights and towns (OrbitView.Civ.cs)
    }

    /// <summary>
    /// Show this body on the globe displays (call every frame with SystemSource.OrbitBodyId; nothing happens unless it
    /// changed). Null, the colony's body, or a body the system doesn't have = the colony's globe.
    /// </summary>
    /// <param name="gameSeed">The game's seed, mixed with the body id for the look-only maps and the patterns.</param>
    public void SetOrbitBody(string? bodyId, ulong gameSeed)
    {
        var body = _source.Body(bodyId);
        var atColony = body == null || body.Id == _source.ColonyBodyId;
        var id = atColony ? null : body!.Id;
        if (id == _orbitBodyId && atColony == _atColony) return;
        _orbitBodyId = id;
        _atColony = atColony;
        if (atColony)
        {
            _cloudsOn = _airOn = _bodySolid = true;
            _lookGround.Visible = _procGround.Visible = false;
            if (_rockField != null) _rockField.Visible = false;
            ClearCivilization();
        }
        else
        {
            ShowOther(body!, gameSeed);
            ShowCivilization(body!); // city lights, smog and towns of a living civilization (OrbitView.Civ.cs)
        }
        _hover.Visible = false;
        RebuildSystem(); // the moon of the new body, then ApplyDisplay
    }

    private void ShowOther(BodyDef body, ulong gameSeed)
    {
        var seed = BodySeed(body.Id, gameSeed);
        _lookGround.Visible = false;
        _procGround.Visible = false;
        if (_rockField != null) _rockField.Visible = false;
        _bodySolid = true;

        if (SystemPlot.IsRocks(body.ClassId))
        {
            _cloudsOn = _airOn = false;
            _bodySolid = false;
            _rockField?.QueueFree();
            _rockField = RockField(seed, body.ClassId == "asteroid_rare");
            _bodyGlobe.AddChild(_rockField);
            return;
        }

        // In a star-system game the look-only map uses the sim's own surface seed for the body, so the natives' towns
        // (StarSystemState.TownsOn, tiles on that map) land on the same ground.
        var mapSeed = _source.State != null ? SystemGenerator.SurfaceSeed(gameSeed, body.Id) : seed;
        if (LookOnlyClasses.Contains(body.ClassId) && ShowLookOnly(body, mapSeed))
        {
            _cloudsOn = _airOn = true;
            _lookGround.Visible = true;
            return;
        }

        var p = ProcLook(body.ClassId);
        _procMaterial.SetShaderParameter("globe_mode", p.Mode);
        _procMaterial.SetShaderParameter("colour_a", p.A);
        _procMaterial.SetShaderParameter("colour_b", p.B);
        _procMaterial.SetShaderParameter("colour_c", p.C);
        _procMaterial.SetShaderParameter("band_count", p.Bands);
        _procMaterial.SetShaderParameter("glow_strength", p.Glow);
        _procMaterial.SetShaderParameter("rim_colour", p.Rim);
        _procMaterial.SetShaderParameter("rim_strength", p.RimStrength);
        _procMaterial.SetShaderParameter("relief", p.Relief);
        _procMaterial.SetShaderParameter("seed", (float)(seed % 997UL) * 0.37f);
        _procGround.Visible = true;
        _cloudsOn = false;
        _airOn = p.Air;
    }

    /// <summary>Paints (or reuses) the look-only globe of a landable body. False if its map can't be generated.</summary>
    private bool ShowLookOnly(BodyDef body, ulong seed)
    {
        if (_lookShownFor == body.Id && _lookMaterial != null) return true;
        PlanetSurfaceDef? def = null;
        foreach (var s in _sim.Data.Surfaces)
            if (s.ClassId == body.ClassId) def = s;
        if (def == null) return false;
        if (!_lookMaps.TryGetValue(body.Id, out var map))
        {
            try
            {
                map = SurfaceMap.Generate(_sim.Data, def, seed); // look only: never the game's PlanetSurface
            }
            catch (System.Exception e)
            {
                GD.PushWarning($"Orbit view: no look-only map for {body.Id} ({body.ClassId}): {e.Message}");
                return false;
            }
            _lookMaps[body.Id] = map;
        }
        var painted = OrbitPainter.Paint(map, TextureWidth, TextureHeight, VTop, VBottom);
        painted.Albedo.GenerateMipmaps();
        painted.Water.GenerateMipmaps();
        painted.Normal.GenerateMipmaps();
        painted.SplatA.GenerateMipmaps();
        painted.SplatB.GenerateMipmaps();
        // A copy of the colony's ground material (photo layers, clouds, air and detail normal), with this body's textures.
        var material = (ShaderMaterial)_groundMaterial.Duplicate();
        material.SetShaderParameter("albedo_tex", ImageTexture.CreateFromImage(painted.Albedo));
        material.SetShaderParameter("water_tex", ImageTexture.CreateFromImage(painted.Water));
        material.SetShaderParameter("normal_tex", ImageTexture.CreateFromImage(painted.Normal));
        material.SetShaderParameter("splat_a", ImageTexture.CreateFromImage(painted.SplatA));
        material.SetShaderParameter("splat_b", ImageTexture.CreateFromImage(painted.SplatB));
        material.SetShaderParameter("photo_mix", PlanetLook.For(body.ClassId).PhotoMix);
        material.SetShaderParameter("sun_dir", _sunDir);
        // the natives' city lights at their lit towns (black where nobody lives), as on the colony's globe (OrbitView.Civ.cs)
        material.SetShaderParameter("lights_tex", ImageTexture.CreateFromImage(CivLightsImage(body, map)));
        _lookMaterial = material;
        _lookGround.MaterialOverride = material;
        _lookShownFor = body.Id;
        return true;
    }

    /// <summary>The game seed mixed with an FNV-1a hash of the body id (stable across runs and machines).</summary>
    public static ulong BodySeed(string bodyId, ulong gameSeed)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in bodyId)
        {
            h ^= ch;
            h *= 1099511628211UL;
        }
        return h ^ (gameSeed * 0x9E3779B97F4A7C15UL);
    }

    private sealed record Proc(int Mode, Color A, Color B, Color C, float Bands, float Glow, Color Rim, float RimStrength, float Relief, bool Air);

    /// <summary>The procedural globe for a planet class (body_globe.gdshader modes; render-only palette).</summary>
    private static Proc ProcLook(string classId) => classId switch
    {
        "class_j" => new(0, new Color(0.86f, 0.74f, 0.56f), new Color(0.6f, 0.4f, 0.26f), new Color(0.96f, 0.91f, 0.82f), 9f, 0f, new Color(0.95f, 0.82f, 0.6f), 0.35f, 0f, false),
        "class_t" => new(0, new Color(0.56f, 0.68f, 0.86f), new Color(0.28f, 0.4f, 0.64f), new Color(0.86f, 0.91f, 0.97f), 13f, 0f, new Color(0.6f, 0.75f, 1f), 0.4f, 0f, false),
        "nebula" => new(0, new Color(0.62f, 0.45f, 0.78f), new Color(0.3f, 0.2f, 0.45f), new Color(0.85f, 0.75f, 0.95f), 5f, 0f, new Color(0.7f, 0.5f, 1f), 0.6f, 0f, false),
        "ice_moon" or "comet" => new(2, new Color(0.84f, 0.91f, 0.97f), new Color(0.62f, 0.74f, 0.87f), new Color(0.55f, 0.36f, 0.28f), 0f, 0f, new Color(0.7f, 0.85f, 1f), 0.15f, 0.8f, false),
        "class_k" => new(3, new Color(0.72f, 0.42f, 0.26f), new Color(0.44f, 0.25f, 0.17f), new Color(0.93f, 0.91f, 0.88f), 0f, 0f, new Color(0.95f, 0.65f, 0.45f), 0.18f, 1f, false),
        "class_h" or "class_l" => new(4, new Color(0.86f, 0.68f, 0.42f), new Color(0.58f, 0.4f, 0.24f), new Color(0.96f, 0.86f, 0.64f), 0f, 0f, new Color(0.95f, 0.8f, 0.55f), 0.2f, 1f, false),
        "class_y" => new(5, new Color(0.18f, 0.11f, 0.09f), new Color(0.4f, 0.17f, 0.1f), new Color(1f, 0.42f, 0.12f), 0f, 2.6f, new Color(1f, 0.4f, 0.2f), 0.35f, 1f, false),
        "class_n" or "class_m" => new(6, new Color(0.1f, 0.32f, 0.56f), new Color(0.03f, 0.13f, 0.32f), new Color(0.95f, 0.97f, 1f), 0f, 0f, new Color(0.45f, 0.65f, 1f), 0.3f, 0.6f, true),
        _ => new(1, new Color(0.6f, 0.56f, 0.5f), new Color(0.36f, 0.34f, 0.31f), new Color(0.74f, 0.7f, 0.62f), 0f, 0f, new Color(0f, 0f, 0f), 0f, 1.2f, false),
    };

    /// <summary>An asteroid field: a flattened swarm of lumpy rocks, a few big ones near the middle (render-only).</summary>
    private static Node3D RockField(ulong seed, bool rare)
    {
        var field = new Node3D { Name = "RockField" };
        var rng = new RandomNumberGenerator { Seed = seed };
        const int count = 420;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new SphereMesh { Radius = 1f, Height = 1.6f, RadialSegments = 7, Rings = 4 },
            InstanceCount = count,
        };
        var baseColour = rare ? new Color(0.62f, 0.55f, 0.72f) : new Color(0.55f, 0.51f, 0.46f);
        for (var i = 0; i < count; i++)
        {
            var big = i < 14;
            var r = big ? rng.RandfRange(0f, 70f) : Mathf.Sqrt(rng.Randf()) * 175f;
            var a = rng.RandfRange(0f, Mathf.Tau);
            var y = (rng.Randf() + rng.Randf() + rng.Randf() - 1.5f) * (big ? 10f : 22f);
            var size = big ? rng.RandfRange(7f, 15f) : Mathf.Pow(rng.Randf(), 3f) * 5.5f + 0.5f;
            var axis = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f));
            if (axis.LengthSquared() < 0.01f) axis = Vector3.Up;
            var stretch = new Vector3(rng.RandfRange(0.7f, 1.4f), rng.RandfRange(0.6f, 1.1f), rng.RandfRange(0.7f, 1.5f)) * size;
            var basis = new Basis(axis.Normalized(), rng.RandfRange(0f, Mathf.Tau)) * Basis.FromScale(stretch);
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r)));
            var tint = rng.RandfRange(0.7f, 1.15f);
            var c = new Color(baseColour.R * tint, baseColour.G * tint, baseColour.B * tint);
            if (rare && rng.Randf() < 0.15f) c = new Color(0.78f, 0.7f, 0.92f);
            mm.SetInstanceColor(i, c);
        }
        field.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return field;
    }
}
