using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The outpost's live video feed on the helm screen (orbit only, once the uplink is online): a small LCARS-style window
/// in the bottom-left of the viewscreen ("OUTPOST FEED · site", CAM / SIZE / minimise / close) showing a 480×270
/// SubViewport rendered by its own camera at the outpost: a mast camera panning across the landscape, a slow circuit
/// of the outpost, and a chase camera behind each of our vehicles, cycling every <see cref="CutSeconds"/> (CAM skips
/// ahead). A canvas shader gives it a transmitted look (scanlines, noise, slight colour fringing, vignette) and turns to
/// green low-light imaging at night; rain, snow or blowing dust fall in front of the camera from the telemetry.
///
/// How the surface is seen from orbit: the driver hides PlanetView in orbit (hidden nodes render in no camera), so
/// while the feed is showing this moves every VisualInstance3D under PlanetView (and anything added there later, via
/// SceneTree.NodeAdded) onto its own render layer, <see cref="FeedLayer"/>, shows PlanetView again, and restores the
/// layers when the feed closes or the ship leaves orbit. The orbit camera's cull mask leaves that layer out and the
/// feed camera sees only it, so each camera draws its own world in the one shared World3D. Lights: the surface Sun is
/// switched back on, moved to the feed layer and limited to it (LightCullMask), dimmed by the local time of day; every
/// light under OrbitView has the feed layer cleared from its cull mask, so the orbit sun never lights the surface. The
/// feed camera has its own Environment (sky in the planet class's colour, haze, tone mapping), since the world's
/// environment is set up for space while in orbit. The SubViewport renders only while the window is open
/// (UpdateMode Always, else Disabled). Render-only: it reads the sim and never changes it.
/// </summary>
public partial class SurfaceFeed : Control
{
    /// <summary>Render layer 20: the surface while it is shown in the feed (and nothing else).</summary>
    public const uint FeedLayer = 1u << 19;

    private static readonly Vector2I PortSize = new(480, 270);
    private const float CutSeconds = 24f;

    // ------------------------------------------------------------------ wiring

    private PlanetView _view = null!;
    private OrbitView _orbit = null!;
    private DirectionalLight3D? _sun;
    private bool _setUp;

    /// <summary>The player wants the window (closed with ×, reopened from the helm's FEED pill).</summary>
    public bool Open { get; set; } = true;

    /// <summary>Folded to its title bar (the camera stops rendering).</summary>
    public bool Minimised { get; private set; }

    private bool _large;

    // ------------------------------------------------------------------ 3D

    private SubViewport _port = null!;
    private Camera3D _camera = null!;
    private Godot.Environment _environment = null!;
    private ProceduralSkyMaterial _skyMaterial = null!;
    private GpuParticles3D _weather = null!;
    private ParticleProcessMaterial _weatherProcess = null!;
    private StandardMaterial3D _weatherDraw = null!;
    private QuadMesh _weatherMesh = null!;
    private SkyCondition? _weatherShown;
    private Color _skyColour = new(0.53f, 0.71f, 0.89f);
    private float _skyDaylight = -1f;
    private float _skyCloud = -1f;
    private float _night;

    private bool _active;
    private readonly List<(VisualInstance3D Node, uint Layers, uint LightMask)> _shifted = new();
    private readonly HashSet<ulong> _shiftedIds = new();
    private (uint Layers, uint Mask, float Energy, float Shadow)? _sunSaved;

    private int _cam;
    private int _roverIndex;
    private float _cutTimer;
    private float _time;
    private Vector3 _eye, _look;
    private bool _snap = true;

    // ------------------------------------------------------------------ UI

    private PanelContainer? _frame;
    private Label? _title;
    private Control? _video;
    private TextureRect? _picture;
    private ShaderMaterial _crt = null!;
    private Label? _camLabel;
    private Label? _recDot;
    private Label? _stamp;
    private Label? _ticker;
    private float _s = -1;
    private bool _builtLarge;
    private bool _builtMinimised;
    private FontVariation _regular = new();
    private FontVariation _bold = new();

    /// <summary>Set up once, after PlanetView and OrbitView are built (and this node is in the tree).</summary>
    public void Setup(PlanetView view, OrbitView orbit, DirectionalLight3D? sun)
    {
        _view = view;
        _orbit = orbit;
        _sun = sun;
        _setUp = true;

        // The orbit view never draws or lights the surface's layer.
        _orbit.Camera.CullMask &= ~FeedLayer;
        foreach (var light in Lights(_orbit)) light.LightCullMask &= ~FeedLayer;
    }

    /// <summary>Over the window (the driver skips clicks on the globe there).</summary>
    public bool IsOverUi(Vector2 screenPos) => Visible && _frame != null && _frame.GetGlobalRect().HasPoint(screenPos);

    // ================================================================== Godot

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;

        string[] names = { "Antonio", "Bahnschrift", "Arial Narrow", "Roboto Condensed", "Liberation Sans Narrow", "DejaVu Sans Condensed", "Arial" };
        _regular = new FontVariation { BaseFont = new SystemFont { FontNames = names, FontWeight = 500, FontStretch = 75 } };
        _bold = new FontVariation { BaseFont = new SystemFont { FontNames = names, FontWeight = 700, FontStretch = 75 } };

        _port = new SubViewport
        {
            Name = "FeedPort",
            Size = PortSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            TransparentBg = false,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa,
            // Shares the main World3D (the default): the surface's nodes are already there.
            OwnWorld3D = false,
        };
        AddChild(_port);

        _skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.25f, 0.42f, 0.66f),
            SkyHorizonColor = new Color(0.66f, 0.76f, 0.86f),
            GroundBottomColor = new Color(0.12f, 0.11f, 0.1f),
            GroundHorizonColor = new Color(0.55f, 0.6f, 0.66f),
            SunAngleMax = 20f,
        };
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _skyMaterial, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.6f, 0.72f),
            AmbientLightEnergy = 0.45f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapWhite = 6f,
            FogEnabled = true,
            FogLightColor = new Color(0.62f, 0.7f, 0.8f),
            FogDensity = 0.0035f,
            FogSkyAffect = 0.2f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.03f,
        };
        _camera = new Camera3D
        {
            Name = "FeedCamera",
            Fov = 52f,
            Near = 0.4f,
            Far = 3000f,
            CullMask = FeedLayer,
            Environment = _environment,
        };
        _port.AddChild(_camera);
        _camera.Current = true; // this viewport's camera (the main window keeps its own)

        // Weather in front of the lens: one emitter, reconfigured per condition. World-space particles, so the camera's
        // pan doesn't drag the rain along.
        _weatherProcess = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(16f, 7f, 14f),
            Direction = new Vector3(0, -1, 0),
            Spread = 6f,
        };
        _weatherDraw = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
        };
        _weatherMesh = new QuadMesh { Size = new Vector2(0.03f, 0.7f), Material = _weatherDraw };
        _weather = new GpuParticles3D
        {
            Name = "Weather",
            Layers = FeedLayer,
            Amount = 600,
            Lifetime = 1.5,
            LocalCoords = false,
            Emitting = false,
            ProcessMaterial = _weatherProcess,
            DrawPass1 = _weatherMesh,
            Position = new Vector3(0, 3f, -13f),
            VisibilityAabb = new Aabb(new Vector3(-40, -40, -40), new Vector3(80, 80, 80)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _camera.AddChild(_weather);

        _crt = new ShaderMaterial { Shader = new Shader { Code = CrtShader } };

        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _ExitTree()
    {
        if (IsInsideTree()) GetTree().NodeAdded -= OnNodeAdded;
    }

    /// <summary>
    /// Once a frame from the driver. Show = in orbit, uplink online and Open. Area = where on screen the window may go
    /// (the helm's viewscreen left of its data panel); scale = the helm's 1280×720 scale.
    /// </summary>
    public void Refresh(bool inOrbit, bool show, Rect2 area, float scale, Vector3 outpost, Vector3 mapCentre, string site, string stamp,
        SurfaceReadings? readings, IReadOnlyList<(Node3D Model, string Name)> vehicles, float delta)
    {
        if (!_setUp) return;
        Visible = show;
        SetActive(show && !Minimised, inOrbit);
        if (!show) return;

        if (!Mathf.IsEqualApprox(scale, _s) || _large != _builtLarge || Minimised != _builtMinimised || _frame == null) Build(scale);
        if (_title != null) _title.Text = $"OUTPOST FEED · {site.ToUpperInvariant()}";
        Place(area);

        _time += delta;
        if (_recDot != null) _recDot.Modulate = new Color(1, 1, 1, Mathf.PosMod(_time, 1.2f) < 0.75f ? 1f : 0.15f);
        if (Minimised) return;

        if (_stamp != null) _stamp.Text = stamp;
        if (_ticker != null) _ticker.Text = readings != null ? SurfaceTelemetry.Ticker(readings) : "AWAITING TELEMETRY";
        UpdateLook(readings);
        UpdateCamera(outpost, mapCentre, vehicles, delta);
        UpdateWeather(readings);
    }

    // ================================================================== layers

    private void SetActive(bool on, bool inOrbit)
    {
        if (on == _active) return;
        _active = on;
        if (on)
        {
            _shifted.Clear();
            _shiftedIds.Clear();
            Shift(_view);
            _view.Visible = true;
            _view.SetHover(-1, Colors.White);
            if (_sun != null)
            {
                _sunSaved = (_sun.Layers, _sun.LightCullMask, _sun.LightEnergy, _sun.DirectionalShadowMaxDistance);
                _sun.Layers = FeedLayer;
                _sun.LightCullMask = FeedLayer;
                _sun.DirectionalShadowMaxDistance = 450f;
                _sun.Visible = true;
            }
            _port.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            _snap = true;
            _skyDaylight = -1f;
        }
        else
        {
            foreach (var (node, layers, mask) in _shifted)
            {
                if (!IsInstanceValid(node)) continue;
                node.Layers = layers;
                if (node is Light3D light) light.LightCullMask = mask;
            }
            _shifted.Clear();
            _shiftedIds.Clear();
            // Back to what the driver wants: the surface hidden in orbit, shown on the ground.
            _view.Visible = !inOrbit;
            if (_sun != null)
            {
                if (_sunSaved is { } saved)
                {
                    _sun.Layers = saved.Layers;
                    _sun.LightCullMask = saved.Mask;
                    _sun.LightEnergy = saved.Energy;
                    _sun.DirectionalShadowMaxDistance = saved.Shadow;
                }
                _sunSaved = null;
                _sun.Visible = !inOrbit;
            }
            _port.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            _weather.Emitting = false;
            _weatherShown = null; // set up again (and emitting) when the feed comes back
        }
    }

    /// <summary>Moves a node and everything under it onto the feed layer, remembering what each had.</summary>
    private void Shift(Node node)
    {
        if (node is VisualInstance3D vi && _shiftedIds.Add(vi.GetInstanceId()))
        {
            var light = vi as Light3D;
            _shifted.Add((vi, vi.Layers, light?.LightCullMask ?? 0u));
            // Game overlays (floating status words like "Building 40%", selection rings) are not scenery: no camera
            // draws them while the feed shows (the surface camera is off in orbit anyway). Painted decals stay.
            vi.Layers = IsOverlay(vi) ? 0u : FeedLayer;
            if (light != null) light.LightCullMask = FeedLayer;
        }
        foreach (var child in node.GetChildren()) Shift(child);
    }

    private static bool IsOverlay(VisualInstance3D node) =>
        node is Label3D label && (label.Billboard != BaseMaterial3D.BillboardModeEnum.Disabled || label.NoDepthTest)
        || node.Name == "Selected";

    /// <summary>Anything added under PlanetView while the feed shows (a rover rolling out, a drop ship, dust) joins the feed
    /// layer at once; a light added to the orbit view never lights it.</summary>
    private void OnNodeAdded(Node node)
    {
        if (!_setUp || node is not VisualInstance3D vi) return;
        if (vi is Light3D light && _orbit.IsAncestorOf(light)) light.LightCullMask &= ~FeedLayer;
        if (_active && _view.IsAncestorOf(vi)) Shift(vi);
    }

    private static IEnumerable<Light3D> Lights(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Light3D light) yield return light;
            foreach (var inner in Lights(child)) yield return inner;
        }
    }

    // ================================================================== camera, sky, weather

    private void UpdateCamera(Vector3 outpost, Vector3 mapCentre, IReadOnlyList<(Node3D Model, string Name)> vehicles, float delta)
    {
        var live = new List<(Node3D Model, string Name)>();
        foreach (var v in vehicles)
            if (IsInstanceValid(v.Model) && v.Model.IsInsideTree() && v.Model.IsVisibleInTree()) live.Add(v);

        _cutTimer += delta;
        if (_cutTimer >= CutSeconds) NextCamera(live.Count);
        if (_cam == 2 && live.Count == 0) NextCamera(0);

        Vector3 eye, look;
        string label;
        switch (_cam)
        {
            case 1:
            {
                // A slow circuit of the outpost, looking in at it.
                var angle = _cutTimer / 90f * Mathf.Tau + 0.7f;
                eye = outpost + new Vector3(Mathf.Cos(angle) * 36f, 0f, Mathf.Sin(angle) * 36f);
                eye.Y = _view.GroundAt(eye) + 8f;
                look = outpost + new Vector3(0, 4f, 0);
                label = "CAM 2 · PERIMETER";
                break;
            }
            case 2:
            {
                var (model, name) = live[_roverIndex % live.Count];
                var at = model.GlobalPosition;
                var fwd = -model.GlobalTransform.Basis.Z;
                fwd.Y = 0;
                fwd = fwd.LengthSquared() > 1e-4f ? fwd.Normalized() : Vector3.Forward;
                eye = at - fwd * 11f + new Vector3(0, 4.5f, 0);
                look = at + fwd * 10f + new Vector3(0, 1.2f, 0);
                label = $"CAM 3 · {name.ToUpperInvariant()}";
                break;
            }
            default:
            {
                // The mast: high over the outpost, panning ±60° about the way toward the middle of the map.
                var toCentre = mapCentre - outpost;
                var baseYaw = toCentre.LengthSquared() > 1f ? Mathf.Atan2(toCentre.X, -toCentre.Z) : 0f;
                var yaw = baseYaw + 1.05f * Mathf.Sin(_time / 50f * Mathf.Tau);
                eye = outpost + new Vector3(0, 15f, 0);
                look = eye + new Vector3(Mathf.Sin(yaw), -0.28f, -Mathf.Cos(yaw)) * 30f; // ground ~55 ahead, sky in the top third
                label = "CAM 1 · MAST";
                break;
            }
        }
        eye.Y = Mathf.Max(eye.Y, _view.GroundAt(eye) + 2f);

        if (_snap)
        {
            _eye = eye;
            _look = look;
            _snap = false;
        }
        else
        {
            var k = 1f - Mathf.Exp(-delta * (_cam == 2 ? 4f : 2.5f));
            _eye = _eye.Lerp(eye, k);
            _look = _look.Lerp(look, k);
        }
        var dir = _look - _eye;
        if (dir.LengthSquared() < 1e-4f || Mathf.Abs(dir.Normalized().Y) > 0.98f) dir = new Vector3(0, -0.2f, -1f);
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(dir.Normalized(), Vector3.Up), _eye);

        if (_camLabel != null) _camLabel.Text = _night > 0.5f ? $"{label} · LOW LIGHT" : label;
    }

    /// <summary>Next camera: mast → perimeter → each vehicle in turn (skipped when there are none).</summary>
    public void NextCamera() => NextCamera(int.MaxValue);

    private void NextCamera(int vehicles)
    {
        _cutTimer = 0f;
        _snap = true;
        if (_cam == 2) _roverIndex++;
        _cam = (_cam + 1) % 3;
        if (_cam == 2 && vehicles == 0) _cam = 0;
    }

    /// <summary>Sky colour from the planet class, dimmed at night and greyed by cloud; the Sun follows the local time.</summary>
    public void SetSkyColour(Color sky) => _skyColour = sky;

    private void UpdateLook(SurfaceReadings? r)
    {
        var sun = r?.Sun.ToFloat() ?? 0.6f;
        var daylight = Mathf.SmoothStep(-0.12f, 0.25f, sun);
        var cloud = r?.CloudCover.ToFloat() ?? 0f;
        _night = Mathf.Clamp((-sun - 0.05f) / 0.25f, 0f, 1f);
        _crt.SetShaderParameter("night", _night);
        _crt.SetShaderParameter("dust", r?.Sky == SkyCondition.DustStorm ? 1f : 0f);
        if (_sun != null && _sunSaved is { } saved) _sun.LightEnergy = saved.Energy * Mathf.Lerp(0.18f, 1f, daylight) * (1f - 0.35f * cloud);
        _environment.AmbientLightEnergy = Mathf.Lerp(0.2f, 0.45f, daylight);
        var vacuum = r?.Sky == SkyCondition.Vacuum;
        _environment.FogDensity = vacuum ? 0.0004f : r?.Sky == SkyCondition.Fog ? 0.012f : r?.Sky == SkyCondition.DustStorm ? 0.009f : 0.0035f;

        // The sky only re-renders its radiance when its colours change, so step them.
        if (Mathf.Abs(daylight - _skyDaylight) < 0.03f && Mathf.Abs(cloud - _skyCloud) < 0.05f) return;
        _skyDaylight = daylight;
        _skyCloud = cloud;
        var grey = new Color(0.6f, 0.62f, 0.66f);
        var top = _skyColour.Darkened(0.35f).Lerp(grey.Darkened(0.3f), cloud * 0.7f);
        var horizon = _skyColour.Lightened(0.25f).Lerp(grey, cloud * 0.7f);
        var dark = new Color(0.01f, 0.012f, 0.03f);
        if (vacuum)
        {
            top = dark;
            horizon = dark.Lightened(0.03f);
        }
        _skyMaterial.SkyTopColor = dark.Lerp(top, daylight);
        _skyMaterial.SkyHorizonColor = dark.Lerp(horizon, Mathf.Max(daylight, 0.08f));
        _skyMaterial.GroundHorizonColor = _skyMaterial.SkyHorizonColor;
        _environment.FogLightColor = _skyMaterial.SkyHorizonColor;
    }

    private void UpdateWeather(SurfaceReadings? r)
    {
        var sky = r?.Sky;
        var kind = sky is SkyCondition.Rain or SkyCondition.Snow or SkyCondition.DustStorm ? sky : null;
        if (kind != _weatherShown)
        {
            _weatherShown = kind;
            switch (kind)
            {
                case SkyCondition.Rain:
                    _weather.Amount = 700;
                    _weather.Lifetime = 1.2;
                    _weatherMesh.Size = new Vector2(0.03f, 0.8f);
                    _weatherDraw.BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY;
                    _weatherDraw.AlbedoColor = new Color(0.75f, 0.82f, 0.9f, 0.45f);
                    _weatherProcess.InitialVelocityMin = 14f;
                    _weatherProcess.InitialVelocityMax = 18f;
                    _weatherProcess.Gravity = new Vector3(0, -9.8f, 0);
                    break;
                case SkyCondition.Snow:
                    _weather.Amount = 500;
                    _weather.Lifetime = 4.0;
                    _weatherMesh.Size = new Vector2(0.09f, 0.09f);
                    _weatherDraw.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
                    _weatherDraw.AlbedoColor = new Color(1f, 1f, 1f, 0.85f);
                    _weatherProcess.InitialVelocityMin = 1f;
                    _weatherProcess.InitialVelocityMax = 2.2f;
                    _weatherProcess.Gravity = new Vector3(0, -1.2f, 0);
                    break;
                case SkyCondition.DustStorm:
                    _weather.Amount = 260;
                    _weather.Lifetime = 2.5;
                    _weatherMesh.Size = new Vector2(1.4f, 1.4f);
                    _weatherDraw.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
                    _weatherDraw.AlbedoColor = new Color(0.72f, 0.6f, 0.42f, 0.22f);
                    _weatherProcess.InitialVelocityMin = 6f;
                    _weatherProcess.InitialVelocityMax = 12f;
                    _weatherProcess.Gravity = new Vector3(0, -0.3f, 0);
                    break;
            }
            _weather.Emitting = kind != null;
        }
        if (kind == null || r == null) return;

        // Blow it downwind: the reading is where the wind comes from (0° = north = -Z).
        var bearing = Mathf.DegToRad(r.WindFromDeg);
        var downwind = -new Vector3(Mathf.Sin(bearing), 0f, -Mathf.Cos(bearing));
        var wind = Mathf.Clamp(r.WindMs.ToFloat() / 12f, 0f, 1.5f);
        _weatherProcess.Direction = kind == SkyCondition.DustStorm
            ? (downwind + new Vector3(0, 0.08f, 0)).Normalized()
            : (new Vector3(0, -1, 0) + downwind * wind * 0.5f).Normalized();
    }

    // ================================================================== window

    private int P(float v) => Mathf.Max(1, Mathf.RoundToInt(v * _s));
    private int F(int v) => Mathf.Max(8, Mathf.RoundToInt(v * _s));

    private void Build(float scale)
    {
        _s = scale;
        _builtLarge = _large;
        _builtMinimised = Minimised;
        foreach (var child in GetChildren())
        {
            if (child == _port) continue;
            RemoveChild(child);
            child.QueueFree();
        }

        var spacing = Mathf.RoundToInt(0.6f * _s);
        _regular.SpacingGlyph = spacing;
        _bold.SpacingGlyph = spacing;
        Theme = new Theme { DefaultFont = _regular, DefaultFontSize = F(13) };

        var videoSize = _large ? new Vector2(480, 270) : new Vector2(320, 180);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.85f),
            BorderColor = HelmScreen.Peach,
            BorderWidthLeft = P(4),
            ContentMarginLeft = P(8),
            ContentMarginRight = P(6),
            ContentMarginTop = P(6),
            ContentMarginBottom = P(6),
        };
        _frame = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        _frame.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", P(5));

        // Title bar: peach caps block, then the window's pills.
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(P(videoSize.X), 0) };
        head.AddThemeConstantOverride("separation", P(4));
        var barStyle = new StyleBoxFlat { BgColor = HelmScreen.Peach, ContentMarginLeft = P(8), ContentMarginRight = P(8), ContentMarginTop = P(2), ContentMarginBottom = P(2) };
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddThemeStyleboxOverride("panel", barStyle);
        _title = Text("OUTPOST FEED", F(14), Colors.Black, true);
        _title.ClipText = true;
        bar.AddChild(_title);
        head.AddChild(bar);
        head.AddChild(Pill("CAM", "Next camera", HelmScreen.Lavender, 0, 0, NextCamera));
        head.AddChild(Pill(_large ? "SMALL" : "LARGE", "Window size", HelmScreen.Sky, 0, 0, () => _large = !_large));
        head.AddChild(Pill(Minimised ? "+" : "–", Minimised ? "Show the feed" : "Fold the feed to its title bar", HelmScreen.Sky, 0, 0, () => Minimised = !Minimised));
        head.AddChild(Pill("×", "Close the feed (reopen with FEED on the helm)", HelmScreen.Rust, 0, P(10), () => Open = false));
        box.AddChild(head);

        _video = new Control { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(P(videoSize.X), P(videoSize.Y)), ClipContents = true, Visible = !Minimised };
        _picture = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Texture = _port.GetTexture(),
            Material = _crt,
        };
        _picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _video.AddChild(_picture);

        _camLabel = Overlay("CAM 1 · MAST", F(12), HelmScreen.Ink);
        _camLabel.Position = new Vector2(P(8), P(5));
        _video.AddChild(_camLabel);

        var rec = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        rec.AddThemeConstantOverride("separation", P(4));
        _recDot = Overlay("●", F(12), HelmScreen.Red);
        rec.AddChild(_recDot);
        rec.AddChild(Overlay("REC", F(12), HelmScreen.Red));
        _stamp = Overlay("", F(12), HelmScreen.Ink);
        rec.AddChild(_stamp);
        // Pinned to the top-right corner, growing leftward as the timestamp changes width.
        rec.SetAnchorsPreset(LayoutPreset.TopRight);
        rec.GrowHorizontal = GrowDirection.Begin;
        rec.OffsetLeft = -P(8);
        rec.OffsetRight = -P(8);
        rec.OffsetTop = P(5);
        rec.OffsetBottom = P(5);
        _video.AddChild(rec);

        var strip = new ColorRect { Color = new Color(0, 0, 0, 0.5f), MouseFilter = MouseFilterEnum.Ignore };
        strip.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        strip.OffsetTop = -P(20);
        _video.AddChild(strip);
        _ticker = Overlay("", F(12), HelmScreen.Cyan);
        _ticker.ClipText = true;
        _ticker.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _ticker.OffsetTop = -P(19);
        _ticker.OffsetLeft = P(8);
        _ticker.OffsetRight = -P(8);
        _video.AddChild(_ticker);
        box.AddChild(_video);

        _frame.AddChild(box);
        AddChild(_frame);
    }

    /// <summary>Bottom-left of the free viewscreen (left of the open data panel), clear of the frame.</summary>
    private void Place(Rect2 area)
    {
        if (_frame == null) return;
        _frame.Size = Vector2.Zero; // shrink to content
        var size = _frame.Size;
        var at = new Vector2(area.Position.X + P(12), area.End.Y - P(12) - size.Y) - GlobalPosition;
        _frame.Position = new Vector2(Mathf.Max(area.Position.X - GlobalPosition.X, at.X), Mathf.Max(area.Position.Y - GlobalPosition.Y, at.Y));
    }

    private Label Text(string text, int size, Color colour, bool bold = false)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        if (bold) label.AddThemeFontOverride("font", _bold);
        return label;
    }

    /// <summary>Text over the video: bold with a dark shadow so it reads on bright ground.</summary>
    private Label Overlay(string text, int size, Color colour)
    {
        var label = Text(text, size, colour, true);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private HelmPill Pill(string text, string tip, Color colour, int roundLeft, int roundRight, System.Action onPress)
    {
        var pill = new HelmPill { Id = text, TooltipText = tip };
        pill.AddThemeFontOverride("font", _bold);
        pill.AddThemeFontSizeOverride("font_size", F(13));
        pill.Set(text, colour, false, roundLeft, roundRight, P(8), 0);
        pill.Pressed += onPress;
        return pill;
    }

    // ================================================================== the transmitted look

    private const string CrtShader = @"
shader_type canvas_item;

uniform float night : hint_range(0.0, 1.0) = 0.0;
uniform float dust : hint_range(0.0, 1.0) = 0.0;

float feed_hash(vec2 p) {
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
    vec2 uv = UV;
    // A faint line jitter, now and then a little more.
    float burst = step(0.985, feed_hash(vec2(floor(TIME * 3.0), 7.0)));
    uv.x += (feed_hash(vec2(floor(uv.y * 270.0), floor(TIME * 24.0))) - 0.5) * (0.0012 + burst * 0.006);

    // Slight colour fringing.
    float fringe = 0.0011;
    vec3 col = vec3(0.0);
    col.r = texture(TEXTURE, uv + vec2(fringe, 0.0)).r;
    col.g = texture(TEXTURE, uv).g;
    col.b = texture(TEXTURE, uv - vec2(fringe, 0.0)).b;

    // Blowing dust: warm and flat.
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(col, vec3(lum) * vec3(1.15, 0.95, 0.7) + vec3(0.06, 0.04, 0.0), dust * 0.55);

    // Night: amplified low-light imaging in green.
    lum = dot(col, vec3(0.299, 0.587, 0.114));
    vec3 green = vec3(0.35, 1.0, 0.45) * pow(clamp(lum * 3.2, 0.0, 1.0), 0.8);
    col = mix(col, green, night);

    // Scanlines, a slow rolling band, grain and a vignette.
    col *= 0.9 + 0.1 * sin(uv.y * 270.0 * PI);
    float band = fract(uv.y * 0.6 - TIME * 0.05);
    col *= 1.0 - 0.05 * smoothstep(0.0, 0.08, band) * (1.0 - smoothstep(0.08, 0.16, band));
    float grain = feed_hash(UV * vec2(480.0, 270.0) + vec2(fract(TIME * 7.0) * 91.0, fract(TIME * 3.0) * 37.0));
    col += (grain - 0.5) * mix(0.05, 0.16, night);
    vec2 d = UV - 0.5;
    col *= 1.0 - dot(d, d) * 0.85;

    COLOR = vec4(col, 1.0);
}
";
}
