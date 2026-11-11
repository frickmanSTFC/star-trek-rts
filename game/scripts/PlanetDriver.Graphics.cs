using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

// The visual pass on the surface and in orbit (docs/visual-pass-contract.md, section G1; owner 2026-11-11: "Is this
// something we can turn on or off ingame to see what the difference is?"). Render-only.
//
// Classic = exactly the look before the pass: every value this file writes is read back from the scene (Planet.tscn's
// Environment, the Sun, the Moon, the project's rendering settings) in SetupGraphics before anything changes, and put
// back whenever a feature is off. The values PlanetDriver.Sky.cs writes every frame (ambient, fog, glow) keep their old
// numbers as the Classic branch there; the Enhanced numbers sit beside them.
//
// What each switch does (GraphicsQuality's effective flags):
// - Gi (surface only, Forward+): SDFGI with cascades sized to the zoom (3 bands, off from far up), fed with the
//   ambient colour as its sky light so nights stay as bright as Classic; SSIL (screen-space bounce) scaled to the zoom.
// - Fog (surface only, Forward+): volumetric fog within ~1.7× the camera distance (off from far up) at 0.3× the haze
//   density (the height/exponential fog stays, ×0.85), albedo from the weather and the world's grade, forward
//   scattering so a low sun throws shafts; sun scattering strongest at dawn/dusk; lamps and headlights glow in it.
// - Grading (surface + orbit): AgX tone mapping, per-world-class grade (PlanetLook: curve, saturation, contrast, haze
//   tint), exposure lifted at night, softer bloom and a capped glow so lamps/beams/flames bloom without blowing out.
// - Shadows (surface only): soft PCF filter High, shadow splits fitted to the camera (the default 0.1/0.2/0.5 put the
//   first cascade where there is no ground at the strategy pitch), blended splits, a softer blur; SSAO radius/fade by zoom.
// - AntiAlias: GraphicsQuality.ApplyViewport (FSR 2 native AA instead of 4× MSAA).
// F7 flips GraphicsQuality.CompareClassic (surface and orbit): a 1.5 s banner and the frame-time readout top-left.
public partial class PlanetDriver
{
    /// <summary>The Classic values of everything this file writes, read once from the scene before any change.</summary>
    private sealed class ClassicLook
    {
        public Environment.ToneMapper Tonemap;
        public float Exposure, White, AgxWhite, AgxContrast;
        public bool Adjust;
        public float Brightness, Contrast, Saturation;
        public Texture? Correction;
        public float Bloom, LumCap;
        public readonly float[] Levels = new float[7];
        public float SsaoRadius, SsaoIntensity, SsaoPower, SsaoDetail, SsaoLightAffect, SsaoChannel;
        public bool Ssil;
        public float SsilRadius, SsilIntensity;
        public bool Sdfgi;
        public float SdfgiCell, SdfgiEnergy, SdfgiBounce;
        public int SdfgiCascades;
        public Environment.SdfgiyScale SdfgiY;
        public bool SdfgiOcclusion, SdfgiSky;
        public bool Vol;
        public float VolDensity, VolAnisotropy, VolLength, VolDetail, VolAmbient, VolGi, VolSky;
        public Color VolAlbedo, VolEmission;
        public Color Background;
        public float BackgroundEnergy;
        public float SunBlur, SunSplit1, SunSplit2, SunSplit3, SunFogEnergy, MoonFogEnergy;
        public bool SunBlend;
        public RenderingServer.ShadowQuality SoftShadows;
        public RenderingServer.EnvironmentSsaoQuality SsaoQuality;
        public bool SsaoHalf;
        public float SsaoTarget, SsaoFadeFrom, SsaoFadeTo;
        public int SsaoBlur;
        public RenderingServer.EnvironmentSsilQuality SsilQuality;
        public bool SsilHalf;
        public float SsilTarget, SsilFadeFrom, SsilFadeTo;
        public int SsilBlur;
    }

    private ClassicLook? _classic;

    // From PlanetDriver.Sky.cs each surface frame (Enhanced branches).
    private float _gfxNight, _gfxSunElevation = 45f;
    private float _gfxVolDensity;
    private bool _volFogOn;

    // Zoom-dependent state (changed with hysteresis, never every frame).
    private int _sdfgiBand = -1;
    private double _sdfgiBandAt = -10;
    private float _aoFadeFor = -1f, _splitTilt = -1f;
    private bool _gfxShadowsApplied, _gfxGiSurface;

    // Orbit glow (EnterOrbit's numbers: the Classic branch in orbit).
    private const float OrbitGlowIntensity = 0.85f, OrbitGlowBloom = 0.04f, OrbitGlowThreshold = 0.9f, OrbitGlowL5 = 0.6f, OrbitGlowL6 = 0.35f;

    // The compare banner and the frame-time readout.
    private CanvasLayer _gfxLayer = null!;
    private PanelContainer _gfxBanner = null!, _gfxFps = null!;
    private Label _gfxBannerText = null!, _gfxBannerSub = null!, _gfxFpsText = null!, _gfxFpsSub = null!;
    private double _gfxBannerUntil = -1, _gfxFpsAt;
    private float _gfxFrameSum, _gfxFrameMax;
    private int _gfxFrames;
    private double _gfxModeSince;
    private readonly Dictionary<string, float> _gfxModeMs = new();
    private bool _gfxMeasuring;

    /// <summary>In _Ready after SetupSky (the Moon exists), before EnterOrbit changes the environment.</summary>
    private void SetupGraphics()
    {
        if (_environment != null) _classic = Capture(_environment);
        GraphicsQuality.Changed += OnGraphicsChanged;
        GetTree().NodeAdded += OnGraphicsNodeAdded;
        KeepOutOfGi(_view);

        _gfxLayer = new CanvasLayer { Name = "GraphicsLayer", Layer = 7 }; // above the HUD, test panel and chooser; under the pause card (8)
        AddChild(_gfxLayer);
        var root = new Control { Name = "GraphicsHud", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gfxLayer.AddChild(root);

        _gfxBanner = HudKit.PanelBox(new Color(HudKit.Panel, 0.86f), HudKit.Accent, 8, 34, 14);
        _gfxBanner.MouseFilter = Control.MouseFilterEnum.Ignore;
        var bannerBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bannerBox.AddThemeConstantOverride("separation", 2);
        _gfxBannerText = HudKit.Label("ENHANCED", 46, HudKit.Ink, condensed: true, bold: true);
        _gfxBannerText.HorizontalAlignment = HorizontalAlignment.Center;
        _gfxBannerSub = HudKit.Label("", 14, HudKit.Dim, condensed: true);
        _gfxBannerSub.HorizontalAlignment = HorizontalAlignment.Center;
        bannerBox.AddChild(_gfxBannerText);
        bannerBox.AddChild(_gfxBannerSub);
        _gfxBanner.AddChild(bannerBox);
        _gfxBanner.Visible = false;
        root.AddChild(_gfxBanner);

        _gfxFps = HudKit.PanelBox(new Color(HudKit.Panel, 0.78f), HudKit.Line, 5, 10, 5);
        _gfxFps.MouseFilter = Control.MouseFilterEnum.Ignore;
        _gfxFps.Position = new Vector2(12, PlanetHud.TopHeight + 10);
        var fpsBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        fpsBox.AddThemeConstantOverride("separation", 0);
        _gfxFpsText = HudKit.Label("", 15, HudKit.Ink, condensed: true, bold: true);
        _gfxFpsSub = HudKit.Label("", 12, HudKit.Dim, condensed: true);
        fpsBox.AddChild(_gfxFpsText);
        fpsBox.AddChild(_gfxFpsSub);
        _gfxFps.AddChild(fpsBox);
        _gfxFps.Visible = false;
        root.AddChild(_gfxFps);
        _gfxModeSince = _clock;
    }

    public override void _ExitTree()
    {
        GraphicsQuality.Changed -= OnGraphicsChanged;
        if (IsInsideTree()) GetTree().NodeAdded -= OnGraphicsNodeAdded;
        GraphicsQuality.CompareClassic = false; // the compare is per visit (the menu shows the saved settings)
        if (_gfxMeasuring) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), false);
        if (_classic is { } c)
        {
            // The renderer-wide settings back to the project's (the menu and the next game start from them).
            RenderingServer.DirectionalSoftShadowFilterSetQuality(c.SoftShadows);
            RenderingServer.EnvironmentSetSsaoQuality(c.SsaoQuality, c.SsaoHalf, c.SsaoTarget, c.SsaoBlur, c.SsaoFadeFrom, c.SsaoFadeTo);
            RenderingServer.EnvironmentSetSsilQuality(c.SsilQuality, c.SsilHalf, c.SsilTarget, c.SsilBlur, c.SsilFadeFrom, c.SsilFadeTo);
        }
    }

    private void OnGraphicsChanged()
    {
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        ApplyGraphics();
    }

    /// <summary>SDFGI would voxelise every grass/tree MultiMesh and particle system whenever a cascade scrolls: they stay
    /// out of it (GI mode Disabled; there are no VoxelGI / lightmaps in the game, so this changes nothing in Classic).</summary>
    private static void KeepOutOfGi(Node node)
    {
        if (node is MultiMeshInstance3D or GpuParticles3D or CpuParticles3D) ((GeometryInstance3D)node).GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        foreach (var child in node.GetChildren()) KeepOutOfGi(child);
    }

    private void OnGraphicsNodeAdded(Node node)
    {
        if (node is MultiMeshInstance3D or GpuParticles3D or CpuParticles3D) ((GeometryInstance3D)node).GIMode = GeometryInstance3D.GIModeEnum.Disabled;
    }

    private ClassicLook Capture(Environment env)
    {
        var c = new ClassicLook
        {
            Tonemap = env.TonemapMode, Exposure = env.TonemapExposure, White = env.TonemapWhite,
            AgxWhite = env.TonemapAgxWhite, AgxContrast = env.TonemapAgxContrast,
            Adjust = env.AdjustmentEnabled, Brightness = env.AdjustmentBrightness, Contrast = env.AdjustmentContrast,
            Saturation = env.AdjustmentSaturation, Correction = env.AdjustmentColorCorrection,
            Bloom = env.GlowBloom, LumCap = env.GlowHdrLuminanceCap,
            SsaoRadius = env.SsaoRadius, SsaoIntensity = env.SsaoIntensity, SsaoPower = env.SsaoPower, SsaoDetail = env.SsaoDetail,
            SsaoLightAffect = env.SsaoLightAffect, SsaoChannel = env.SsaoAOChannelAffect,
            Ssil = env.SsilEnabled, SsilRadius = env.SsilRadius, SsilIntensity = env.SsilIntensity,
            Sdfgi = env.SdfgiEnabled, SdfgiCell = env.SdfgiMinCellSize, SdfgiCascades = env.SdfgiCascades, SdfgiEnergy = env.SdfgiEnergy,
            SdfgiBounce = env.SdfgiBounceFeedback, SdfgiY = env.SdfgiYScale, SdfgiOcclusion = env.SdfgiUseOcclusion, SdfgiSky = env.SdfgiReadSkyLight,
            Vol = env.VolumetricFogEnabled, VolDensity = env.VolumetricFogDensity, VolAlbedo = env.VolumetricFogAlbedo,
            VolEmission = env.VolumetricFogEmission, VolAnisotropy = env.VolumetricFogAnisotropy, VolLength = env.VolumetricFogLength,
            VolDetail = env.VolumetricFogDetailSpread, VolAmbient = env.VolumetricFogAmbientInject, VolGi = env.VolumetricFogGIInject,
            VolSky = env.VolumetricFogSkyAffect,
            Background = env.BackgroundColor, BackgroundEnergy = env.BackgroundEnergyMultiplier,
            SunBlur = _sun?.ShadowBlur ?? 1f, SunSplit1 = _sun?.DirectionalShadowSplit1 ?? 0.1f, SunSplit2 = _sun?.DirectionalShadowSplit2 ?? 0.2f,
            SunSplit3 = _sun?.DirectionalShadowSplit3 ?? 0.5f, SunBlend = _sun?.DirectionalShadowBlendSplits ?? false,
            SunFogEnergy = _sun?.LightVolumetricFogEnergy ?? 1f, MoonFogEnergy = _moonLight?.LightVolumetricFogEnergy ?? 1f,
            // The project's rendering settings (defaults when project.godot doesn't set them).
            SoftShadows = (RenderingServer.ShadowQuality)Setting("rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality", 2),
            SsaoQuality = (RenderingServer.EnvironmentSsaoQuality)Setting("rendering/environment/ssao/quality", 2),
            SsaoHalf = ProjectSettings.GetSetting("rendering/environment/ssao/half_size", true).AsBool(),
            SsaoTarget = SettingF("rendering/environment/ssao/adaptive_target", 0.5f),
            SsaoBlur = Setting("rendering/environment/ssao/blur_passes", 2),
            SsaoFadeFrom = SettingF("rendering/environment/ssao/fadeout_from", 50f),
            SsaoFadeTo = SettingF("rendering/environment/ssao/fadeout_to", 300f),
            SsilQuality = (RenderingServer.EnvironmentSsilQuality)Setting("rendering/environment/ssil/quality", 2),
            SsilHalf = ProjectSettings.GetSetting("rendering/environment/ssil/half_size", true).AsBool(),
            SsilTarget = SettingF("rendering/environment/ssil/adaptive_target", 0.5f),
            SsilBlur = Setting("rendering/environment/ssil/blur_passes", 4),
            SsilFadeFrom = SettingF("rendering/environment/ssil/fadeout_from", 50f),
            SsilFadeTo = SettingF("rendering/environment/ssil/fadeout_to", 300f),
        };
        for (var i = 0; i < 7; i++) c.Levels[i] = env.GetGlowLevel(i);
        return c;

        static int Setting(string key, int fallback) => ProjectSettings.GetSetting(key, fallback).AsInt32();
        static float SettingF(string key, float fallback) => (float)ProjectSettings.GetSetting(key, fallback).AsDouble();
    }

    /// <summary>Puts the current switches on the environment, lights and window: on Changed, after EnterOrbit / ExitOrbit,
    /// and (for the zoom-dependent parts) every frame from UpdateGraphics.</summary>
    private void ApplyGraphics()
    {
        GraphicsQuality.ApplyViewport(GetViewport());
        if (_classic is not { } c || _environment is not { } env) return;
        var look = _view.Look;
        var surface = !_inOrbit;

        // Grading: tone mapping + grade (surface and orbit).
        if (GraphicsQuality.Grading)
        {
            env.TonemapMode = Environment.ToneMapper.Agx;
            env.TonemapAgxWhite = 6f;          // the same white as Classic's ACES (lamps and flames roll off, not clip)
            env.TonemapAgxContrast = 1.2f;
            env.TonemapExposure = surface ? Mathf.Lerp(1.05f, 1.22f, _gfxNight) : 1.05f; // Sky.cs eases it on the surface
            env.AdjustmentEnabled = true;
            env.AdjustmentBrightness = 1f;
            env.AdjustmentContrast = look.GradeContrast;
            env.AdjustmentSaturation = look.GradeSaturation;
            env.AdjustmentColorCorrection = look.GradeTexture;
        }
        else
        {
            env.TonemapMode = c.Tonemap;
            env.TonemapExposure = c.Exposure;
            env.TonemapWhite = c.White;
            env.TonemapAgxWhite = c.AgxWhite;
            env.TonemapAgxContrast = c.AgxContrast;
            env.AdjustmentEnabled = c.Adjust;
            env.AdjustmentBrightness = c.Brightness;
            env.AdjustmentContrast = c.Contrast;
            env.AdjustmentSaturation = c.Saturation;
            env.AdjustmentColorCorrection = c.Correction;
        }

        // Glow: in orbit the whole set (nothing writes it per frame there); on the surface bloom, cap and the tight level
        // (Sky.cs writes threshold and intensity every frame).
        if (!surface)
        {
            var on = GraphicsQuality.Grading;
            env.GlowIntensity = on ? 0.9f : OrbitGlowIntensity;
            env.GlowBloom = on ? 0.025f : OrbitGlowBloom;
            env.GlowHdrThreshold = on ? 0.85f : OrbitGlowThreshold;
            env.GlowHdrLuminanceCap = on ? 10f : c.LumCap;
            env.SetGlowLevel(5, on ? 0.55f : OrbitGlowL5);
            env.SetGlowLevel(6, on ? 0.3f : OrbitGlowL6);
            env.SetGlowLevel(1, on ? Mathf.Max(c.Levels[1], 0.2f) : c.Levels[1]);
        }
        else if (GraphicsQuality.Grading)
        {
            env.GlowBloom = 0.02f;              // was 0.06: everything bright-ish glowed a little and the day looked hazy
            env.GlowHdrLuminanceCap = 10f;      // lamps / thruster cores bloom without a white-out
            env.SetGlowLevel(1, Mathf.Max(c.Levels[1], 0.25f)); // a tight halo round small lamps and beams
        }
        else
        {
            env.GlowBloom = c.Bloom;
            env.GlowHdrLuminanceCap = c.LumCap;
            env.SetGlowLevel(1, c.Levels[1]);
        }

        // Gi / Fog: surface only; the zoom picks the details (UpdateGraphics). Off → the Classic values now.
        _gfxGiSurface = surface && GraphicsQuality.Gi && GraphicsQuality.ForwardPlus;
        if (_gfxGiSurface)
        {
            env.SsilEnabled = true;
            env.SsilIntensity = 1.2f;
            _sdfgiBand = -1; // pick again this frame
        }
        else
        {
            env.SsilEnabled = c.Ssil;
            env.SsilRadius = c.SsilRadius;
            env.SsilIntensity = c.SsilIntensity;
            _sdfgiBand = -1;
            RestoreSdfgi(env, c);
        }
        if (!(surface && GraphicsQuality.Fog && GraphicsQuality.ForwardPlus)) RestoreVolumetric(env, c);

        // Shadows: surface only (in orbit the Sun lights only the outpost feed, which keeps its Classic set-up).
        var shadows = surface && GraphicsQuality.Shadows;
        if (shadows != _gfxShadowsApplied || !shadows)
        {
            _gfxShadowsApplied = shadows;
            _aoFadeFor = -1f;
            if (shadows)
            {
                RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftHigh);
                env.SsaoIntensity = 2.2f;
                env.SsaoPower = 1.7f;
                env.SsaoDetail = 0.7f;
                env.SsaoLightAffect = 0.12f; // a touch of contact shade in direct sun too
                env.SsaoAOChannelAffect = 0.5f;
                if (_sun != null)
                {
                    _sun.ShadowBlur = 1.4f;
                    _sun.DirectionalShadowBlendSplits = true;
                }
            }
            else
            {
                RenderingServer.DirectionalSoftShadowFilterSetQuality(c.SoftShadows);
                RenderingServer.EnvironmentSetSsaoQuality(c.SsaoQuality, c.SsaoHalf, c.SsaoTarget, c.SsaoBlur, c.SsaoFadeFrom, c.SsaoFadeTo);
                env.SsaoRadius = c.SsaoRadius;
                env.SsaoIntensity = c.SsaoIntensity;
                env.SsaoPower = c.SsaoPower;
                env.SsaoDetail = c.SsaoDetail;
                env.SsaoLightAffect = c.SsaoLightAffect;
                env.SsaoAOChannelAffect = c.SsaoChannel;
                if (_sun != null)
                {
                    _sun.ShadowBlur = c.SunBlur;
                    _sun.DirectionalShadowSplit1 = c.SunSplit1;
                    _sun.DirectionalShadowSplit2 = c.SunSplit2;
                    _sun.DirectionalShadowSplit3 = c.SunSplit3;
                    _sun.DirectionalShadowBlendSplits = c.SunBlend;
                }
            }
        }
        if (_gfxGiSurface) RenderingServer.EnvironmentSetSsilQuality(c.SsilQuality, c.SsilHalf, c.SsilTarget, c.SsilBlur, c.SsilFadeFrom, c.SsilFadeTo);
        UpdateZoomGraphics(true);
    }

    private static void RestoreSdfgi(Environment env, ClassicLook c)
    {
        if (env.SdfgiEnabled != c.Sdfgi) env.SdfgiEnabled = c.Sdfgi;
        env.BackgroundColor = c.Background;
        env.BackgroundEnergyMultiplier = c.BackgroundEnergy;
    }

    private void RestoreVolumetric(Environment env, ClassicLook c)
    {
        _volFogOn = false;
        if (env.VolumetricFogEnabled != c.Vol) env.VolumetricFogEnabled = c.Vol;
        env.VolumetricFogDensity = c.VolDensity;
        env.VolumetricFogAlbedo = c.VolAlbedo;
        env.VolumetricFogEmission = c.VolEmission;
        env.VolumetricFogAnisotropy = c.VolAnisotropy;
        env.VolumetricFogLength = c.VolLength;
        env.VolumetricFogDetailSpread = c.VolDetail;
        env.VolumetricFogAmbientInject = c.VolAmbient;
        env.VolumetricFogGIInject = c.VolGi;
        env.VolumetricFogSkyAffect = c.VolSky;
        if (_sun != null) _sun.LightVolumetricFogEnergy = c.SunFogEnergy;
        if (_moonLight != null) _moonLight.LightVolumetricFogEnergy = c.MoonFogEnergy;
    }

    /// <summary>Once a frame after UpdateSky: the zoom-dependent parts, the banner and the frame-time readout.</summary>
    private void UpdateGraphics(float delta)
    {
        UpdateGraphicsHud(delta);
        UpdateZoomGraphics(false);
    }

    // SDFGI bands by camera distance: (cell size, cascades, enter above, leave below). Cascades are centred on the camera,
    // so the ground at ~the camera distance must sit inside them; past the last band SDFGI is off (the ambient light
    // takes over, as in Classic).
    private static readonly (float Cell, int Cascades, float Up, float Down)[] SdfgiBands =
    {
        (0.5f, 4, 0f, 0f),        // close: ~0–240 (cascade 0 32 units, reach ~512)
        (1.25f, 4, 240f, 210f),   // mid: ~240–650 (reach ~1280)
        (2.5f, 4, 650f, 580f),    // far: ~650–1400 (reach ~2560)
    };
    private const float SdfgiOffAbove = 1400f, SdfgiOnBelow = 1250f;

    private void UpdateZoomGraphics(bool force)
    {
        if (_classic is not { } c || _environment is not { } env || _inOrbit) return;
        var d = _distance;

        if (_gfxGiSurface)
        {
            // Band with hysteresis and a short dwell (each change rebuilds the cascades: a hitch, so not while zooming through).
            var band = _sdfgiBand;
            if (band < 0) band = d >= SdfgiOffAbove ? SdfgiBands.Length : PickBand(d);
            else if (band == SdfgiBands.Length) { if (d < SdfgiOnBelow) band = PickBand(d); }
            else if (d >= SdfgiOffAbove) band = SdfgiBands.Length;
            else
            {
                while (band + 1 < SdfgiBands.Length && d > SdfgiBands[band + 1].Up) band++;
                while (band > 0 && d < SdfgiBands[band].Down) band--;
            }
            if (band != _sdfgiBand && (_sdfgiBand < 0 || _clock - _sdfgiBandAt >= 0.8))
            {
                _sdfgiBand = band;
                _sdfgiBandAt = _clock;
                if (band >= SdfgiBands.Length) RestoreSdfgi(env, c);
                else
                {
                    var b = SdfgiBands[band];
                    env.SdfgiCascades = b.Cascades;
                    env.SdfgiMinCellSize = b.Cell;
                    env.SdfgiYScale = Environment.SdfgiyScale.Scale75Percent;
                    env.SdfgiUseOcclusion = true; // fewer leaks through walls and hills
                    env.SdfgiReadSkyLight = true;
                    env.SdfgiEnergy = 1f;
                    env.SdfgiBounceFeedback = 0.45f;
                    if (!env.SdfgiEnabled) env.SdfgiEnabled = true;
                }
            }
            // SDFGI replaces the ambient light inside its cascades and reads the background as the sky: give it the
            // ambient Sky.cs set this frame (blue-dark at night, the lightning flash too), so day fill and the owner's
            // brighter nights carry over. On the surface the background is hidden behind the sky dome and the fog.
            if (_sdfgiBand >= 0 && _sdfgiBand < SdfgiBands.Length)
            {
                env.BackgroundColor = env.AmbientLightColor;
                env.BackgroundEnergyMultiplier = env.AmbientLightEnergy;
            }
            var ssil = Mathf.Clamp(d * 0.03f, 4f, 16f);
            if (force || Mathf.Abs(env.SsilRadius - ssil) > 0.1f) env.SsilRadius = ssil;
        }

        if (GraphicsQuality.Fog && GraphicsQuality.ForwardPlus)
        {
            var want = _volFogOn ? d < 850f : d < 750f; // from far up the froxels can't reach the ground: off
            if (want != _volFogOn || force)
            {
                _volFogOn = want;
                if (!want) RestoreVolumetric(env, c);
                else
                {
                    env.VolumetricFogEnabled = true;
                    env.VolumetricFogDetailSpread = 2f;     // detail near the camera
                    env.VolumetricFogAmbientInject = 0.35f; // fog in shadow isn't black
                    env.VolumetricFogGIInject = 1f;
                    env.VolumetricFogSkyAffect = 0.3f;
                    env.VolumetricFogEmission = Colors.Black;
                }
            }
            if (_volFogOn)
            {
                env.VolumetricFogLength = Mathf.Clamp(d * 1.7f, 120f, 1000f);
                env.VolumetricFogDensity = _gfxVolDensity;
                var dust = Mathf.Clamp(_look.Dust * 1.4f, 0f, 1f);
                var albedo = new Color(0.82f, 0.85f, 0.9f).Lerp(_view.Look.FogDust, dust);
                if (GraphicsQuality.Grading) albedo *= _view.Look.GradeFogTint;
                env.VolumetricFogAlbedo = new Color(Mathf.Min(albedo.R, 1f), Mathf.Min(albedo.G, 1f), Mathf.Min(albedo.B, 1f));
                env.VolumetricFogAnisotropy = Mathf.Lerp(0.6f, 0.3f, dust); // forward scatter: halos and shafts toward a low sun
                // Shafts at dawn and dusk: the sun scatters most when it is low, little at noon (no washed-out day).
                var low = 1f - Mathf.SmoothStep(4f, 35f, _gfxSunElevation);
                if (_sun != null) _sun.LightVolumetricFogEnergy = Mathf.Lerp(0.35f, 1.7f, low);
                if (_moonLight != null) _moonLight.LightVolumetricFogEnergy = 1.3f;
            }
        }

        if (GraphicsQuality.Shadows && _sun != null)
        {
            // Splits fitted to the camera: at the 55° strategy pitch the ground starts ~0.84× the distance away, so the
            // first cascade ends ~1.05×; close to the ground (20° pitch) it hugs the vehicles in front.
            var t = Mathf.SmoothStep(MinZoom, TiltFrom, d);
            if (force || Mathf.Abs(t - _splitTilt) > 0.004f)
            {
                _splitTilt = t;
                _sun.DirectionalShadowSplit1 = Mathf.Lerp(0.06f, 0.48f, t);
                _sun.DirectionalShadowSplit2 = Mathf.Lerp(0.16f, 0.6f, t);
                _sun.DirectionalShadowSplit3 = Mathf.Lerp(0.4f, 0.75f, t);
            }
            var radius = Mathf.Clamp(d * 0.012f, 1.5f, 6f);
            if (force || Mathf.Abs(env.SsaoRadius - radius) > 0.05f) env.SsaoRadius = radius;
            // SSAO fades out 50–300 from the camera by default, i.e. never at the strategy zoom: scale it (global setting,
            // so only when the zoom moved by more than 12%).
            if (force || _aoFadeFor < 0f || Mathf.Abs(d - _aoFadeFor) > 0.12f * _aoFadeFor)
            {
                _aoFadeFor = d;
                RenderingServer.EnvironmentSetSsaoQuality(c.SsaoQuality, c.SsaoHalf, c.SsaoTarget, c.SsaoBlur,
                    Mathf.Max(c.SsaoFadeFrom, d * 1.5f), Mathf.Max(c.SsaoFadeTo, d * 3.2f));
                if (_gfxGiSurface)
                    RenderingServer.EnvironmentSetSsilQuality(c.SsilQuality, c.SsilHalf, c.SsilTarget, c.SsilBlur,
                        Mathf.Max(c.SsilFadeFrom, d * 1.5f), Mathf.Max(c.SsilFadeTo, d * 3.2f));
            }
        }
    }

    private static int PickBand(float d)
    {
        var band = 0;
        while (band + 1 < SdfgiBands.Length && d > SdfgiBands[band + 1].Up) band++;
        return band;
    }

    // ---------------------------------------------------------------- F7 compare + readout

    /// <summary>F7 (surface and orbit): Classic ↔ the player's settings. One hook at the top of _UnhandledInput.</summary>
    private bool GraphicsInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F7 }) return false;
        GraphicsQuality.CompareClassic = !GraphicsQuality.CompareClassic;
        GraphicsQuality.NotifyChanged();
        ShowGraphicsBanner();
        GetViewport().SetInputAsHandled();
        return true;
    }

    private void ShowGraphicsBanner()
    {
        _gfxBannerText.Text = GraphicsQuality.CompareClassic ? "CLASSIC" : "ENHANCED";
        _gfxBannerText.AddThemeColorOverride("font_color", GraphicsQuality.CompareClassic ? HudKit.Ink : HudKit.Accent);
        _gfxBannerSub.Text = GraphicsQuality.Preset switch
        {
            GraphicsPreset.Classic => "Graphics preset is Classic: pick Enhanced in Settings › Graphics to compare",
            GraphicsPreset.Custom when !GraphicsQuality.CompareClassic => "your custom mix · F7 compares",
            _ => GraphicsQuality.CompareClassic ? "the look before the visual pass · F7 back" : "F7 compares with Classic",
        };
        _gfxBanner.Visible = true;
        _gfxBanner.Modulate = Colors.White;
        _gfxBanner.ResetSize();
        _gfxBannerUntil = _clock + 1.5;
        _gfxModeSince = _clock;
    }

    private void UpdateGraphicsHud(float delta)
    {
        // Banner: centred, 1.5 s, the last 0.3 s fading.
        if (_gfxBanner.Visible)
        {
            var left = _gfxBannerUntil - _clock;
            if (left <= 0) _gfxBanner.Visible = false;
            else
            {
                var size = GetViewport().GetVisibleRect().Size;
                _gfxBanner.Position = (size - _gfxBanner.Size) * new Vector2(0.5f, 0.42f);
                _gfxBanner.Modulate = new Color(1, 1, 1, Mathf.Clamp((float)left / 0.3f, 0f, 1f));
            }
        }

        // Frame-time readout: while comparing or with "Show frame time" on.
        var show = GraphicsQuality.CompareClassic || GraphicsQuality.ShowFps;
        if (_gfxFps.Visible != show) _gfxFps.Visible = show;
        var rid = GetViewport().GetViewportRid();
        if (show != _gfxMeasuring)
        {
            _gfxMeasuring = show;
            RenderingServer.ViewportSetMeasureRenderTime(rid, show); // GPU time is real even when VSync caps the frame rate
            _gfxModeMs.Clear();
        }
        if (!show) return;
        _gfxFrameSum += delta;
        _gfxFrameMax = Mathf.Max(_gfxFrameMax, delta);
        _gfxFrames++;
        if (_clock - _gfxFpsAt < 0.25) return;
        _gfxFpsAt = _clock;
        var ms = _gfxFrames > 0 ? 1000f * _gfxFrameSum / _gfxFrames : 0f;
        var max = 1000f * _gfxFrameMax;
        _gfxFrameSum = _gfxFrameMax = 0f;
        _gfxFrames = 0;
        var gpu = (float)RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
        var mode = GraphicsQuality.ModeName;
        // Remember each mode's GPU time once it has settled (SDFGI / fog take a moment after a flip).
        if (_clock - _gfxModeSince > 0.8 && gpu > 0f)
            _gfxModeMs[mode] = _gfxModeMs.TryGetValue(mode, out var was) ? Mathf.Lerp(was, gpu, 0.2f) : gpu;
        _gfxFpsText.Text = $"{mode} · {Engine.GetFramesPerSecond():0} FPS · {ms:0.0} ms (max {max:0.0})";
        var parts = new List<string> { $"GPU {gpu:0.0} ms" };
        foreach (var name in new[] { "CLASSIC", "CUSTOM", "ENHANCED" })
            if (name != mode && _gfxModeMs.TryGetValue(name, out var other)) parts.Add($"{name.ToLowerInvariant()} {other:0.0}");
        if (GameSettings.VSync) parts.Add("VSync caps FPS");
        _gfxFpsSub.Text = string.Join(" · ", parts) + "  ·  F7 compare";
    }
}
