using System;
using Godot;

// The visual pass's switches (docs/visual-pass-contract.md; owner 2026-11-11: "do the visual pass after the merge" and
// switchable in game to compare). Render-only: nothing here may touch the sim. Agent G1 owns this file (saving in
// GameSettings [graphics], presets, the live compare key F7 in PlanetDriver.Graphics.cs); agent G2 reads the effective
// flags and listens to Changed. Keep the member names.
//
// Classic = every Enhanced branch off = the game exactly as it looked before the visual pass (the old values are kept as
// the Classic branch wherever a value changes). Who applies what:
// - PlanetDriver.Graphics.cs: the surface / orbit WorldEnvironment (Gi, Fog, Grading, Shadows), the Sun / Moon lights,
//   the F7 compare banner and the frame-time readout; PlanetDriver.Sky.cs holds the Enhanced branches of the values it
//   writes every frame (fog, glow, exposure).
// - ApplyViewport (here): the window's anti-aliasing (AntiAlias), from PlanetDriver and the main menu.
// - MainMenu: its own environment follows Grading.
// - G2 (PlanetView.Decals/Effects, terrain shader): Terrain, Decals, Effects.

/// <summary>Classic = the game exactly as it looked before the visual pass; Enhanced = every feature on; Custom = the
/// player's own mix of the toggles below.</summary>
public enum GraphicsPreset { Classic, Enhanced, Custom }

public static class GraphicsQuality
{
    private static GraphicsPreset _preset = GraphicsPreset.Classic; // owner 2026-11-11: "completely unplayable on my laptop" → off by default
    private static bool _gi = true, _fog = true, _grading = true, _shadows = true, _terrain = true, _decals = true, _effects = true, _antiAlias = true;
    private static bool _showFps;

    // Read user://settings.cfg on first use (GameSettings.Load sets the properties below; it runs once, so the setters
    // calling it again from inside Load return at once).
    private static void Ensure() => FinalFrontier.GameSettings.Load();

    /// <summary>The chosen preset (saved).</summary>
    public static GraphicsPreset Preset { get { Ensure(); return _preset; } set { Ensure(); _preset = value; } }

    /// <summary>The live A/B compare: true = show Classic right now whatever the settings say (F7 flips it; not saved).</summary>
    public static bool CompareClassic { get; set; }

    // The player's toggles (used when Preset is Custom; Enhanced = all true; Classic = all false). Saved.
    public static bool GiSetting { get { Ensure(); return _gi; } set { Ensure(); _gi = value; } }                  // global illumination (SDFGI) + SSIL
    public static bool FogSetting { get { Ensure(); return _fog; } set { Ensure(); _fog = value; } }               // volumetric fog / light shafts
    public static bool GradingSetting { get { Ensure(); return _grading; } set { Ensure(); _grading = value; } }   // tonemap + per-world colour grading, tuned glow
    public static bool ShadowsSetting { get { Ensure(); return _shadows; } set { Ensure(); _shadows = value; } }   // better shadows + SSAO contact shading
    public static bool TerrainSetting { get { Ensure(); return _terrain; } set { Ensure(); _terrain = value; } }   // high-detail terrain textures + detail normals
    public static bool DecalsSetting { get { Ensure(); return _decals; } set { Ensure(); _decals = value; } }      // ground decals: tracks, scorch, dust, paths
    public static bool EffectsSetting { get { Ensure(); return _effects; } set { Ensure(); _effects = value; } }   // extra effects: heat haze, embers, dust motes, spray
    public static bool AntiAliasSetting { get { Ensure(); return _antiAlias; } set { Ensure(); _antiAlias = value; } } // TAA / FSR 2 upscaling
    public static bool ShowFps { get { Ensure(); return _showFps; } set { Ensure(); _showFps = value; } }          // frame-time readout (always on while comparing)

    private static bool On(bool setting) => !CompareClassic && Preset switch
    {
        GraphicsPreset.Classic => false,
        GraphicsPreset.Enhanced => true,
        _ => setting,
    };

    // What is in effect this frame (read these, not the settings).
    public static bool Gi => On(GiSetting);
    public static bool Fog => On(FogSetting);
    public static bool Grading => On(GradingSetting);
    public static bool Shadows => On(ShadowsSetting);
    public static bool Terrain => On(TerrainSetting);
    public static bool Decals => On(DecalsSetting);
    public static bool Effects => On(EffectsSetting);
    public static bool AntiAlias => On(AntiAliasSetting);

    /// <summary>True when nothing Enhanced is in effect (Classic preset, a Custom mix with every toggle off, or F7).</summary>
    public static bool IsClassicNow => !(Gi || Fog || Grading || Shadows || Terrain || Decals || Effects || AntiAlias);

    /// <summary>Raised after any switch changes (settings applied, preset changed, compare flipped). Listeners re-apply
    /// what they own (environment, materials, decal pools); none of them should poll every frame.</summary>
    public static event Action? Changed;

    /// <summary>Call after changing any property above.</summary>
    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>The features in the settings card's order: name, one-line description, getter, setter.</summary>
    public static readonly (string Name, string Hint, Func<bool> Get, Action<bool> Set)[] Features =
    {
        ("Global illumination", "Sunlight bounces off the ground and buildings (SDFGI + screen-space light); surface only", () => GiSetting, v => GiSetting = v),
        ("Volumetric fog", "Light shafts at dawn and dusk, in dust and fog; headlights and lamps glow in the air", () => FogSetting, v => FogSetting = v),
        ("Colour grading", "AgX tone mapping, a grade per world class, softer bloom, brighter nights", () => GradingSetting, v => GradingSetting = v),
        ("Shadows", "Softer, sharper sun shadows fitted to the zoom; deeper contact shading (SSAO)", () => ShadowsSetting, v => ShadowsSetting = v),
        ("Terrain detail", "Detailed ground textures and normals up close", () => TerrainSetting, v => TerrainSetting = v),
        ("Ground decals", "Tyre and track marks, scorch rings, dust and worn paths", () => DecalsSetting, v => DecalsSetting = v),
        ("Extra effects", "Heat haze, embers, dust motes in sunbeams, rotor spray", () => EffectsSetting, v => EffectsSetting = v),
        ("Anti-aliasing", "Temporal AA (FSR 2 at native resolution) instead of 4× MSAA: smoother edges and foliage", () => AntiAliasSetting, v => AntiAliasSetting = v),
    };

    /// <summary>Picks a preset (Custom keeps the toggles as they are). Not saved: GameSettings.Save.</summary>
    public static void SetPreset(GraphicsPreset preset) => Preset = preset;

    /// <summary>One toggle from the settings card: a Classic / Enhanced preset first becomes Custom starting from its own
    /// values (all off / all on), then the toggle changes.</summary>
    public static void SetFeature(int index, bool on)
    {
        if (index < 0 || index >= Features.Length) return;
        if (Preset != GraphicsPreset.Custom)
        {
            var all = Preset == GraphicsPreset.Enhanced;
            foreach (var f in Features) f.Set(all);
            Preset = GraphicsPreset.Custom;
        }
        Features[index].Set(on);
    }

    /// <summary>A toggle as the settings card shows it: the preset's value, or the player's for Custom.</summary>
    public static bool FeatureShown(int index) => Preset switch
    {
        GraphicsPreset.Classic => false,
        GraphicsPreset.Enhanced => true,
        _ => Features[index].Get(),
    };

    /// <summary>Defaults (GameSettings.ResetDefaults): Classic (owner 2026-11-11: off by default), the Custom toggles all on
    /// so picking Custom starts from the full set, no frame-time readout.</summary>
    public static void ResetDefaults()
    {
        _preset = GraphicsPreset.Classic;
        _gi = _fog = _grading = _shadows = _terrain = _decals = _effects = _antiAlias = true;
        _showFps = false;
        CompareClassic = false;
    }

    /// <summary>Banner / readout name of what is on screen now.</summary>
    public static string ModeName => CompareClassic || Preset == GraphicsPreset.Classic ? "CLASSIC"
        : Preset == GraphicsPreset.Enhanced ? "ENHANCED" : IsClassicNow ? "CLASSIC" : "CUSTOM";

    // ---------------------------------------------------------------- anti-aliasing (the window's viewport)

    private static bool _viewportCaptured;
    private static Viewport.Msaa _classicMsaa;
    private static Viewport.ScreenSpaceAAEnum _classicScreenAa;
    private static bool _classicTaa;
    private static Viewport.Scaling3DModeEnum _classicScaling;
    private static float _classicScale, _classicSharpness;

    /// <summary>Forward+ (desktop Vulkan / D3D12 / Metal): SDFGI, SSIL, volumetric fog, TAA and FSR 2 exist there only.</summary>
    public static bool ForwardPlus => RenderingServer.GetCurrentRenderingMethod() == "forward_plus";

    /// <summary>The window's 3D anti-aliasing: Classic = the project's (4× MSAA, no TAA, native bilinear); AntiAlias =
    /// FSR 2 at scale 1 (temporal AA at native resolution, sharpness 0.25) with MSAA off, or 4× MSAA + SMAA where FSR 2
    /// doesn't exist (not Forward+). The Classic values are read from the viewport the first time, before any change.
    /// Cheap when nothing changed.</summary>
    public static void ApplyViewport(Viewport? viewport)
    {
        if (viewport == null) return;
        if (!_viewportCaptured)
        {
            _viewportCaptured = true;
            _classicMsaa = viewport.Msaa3D;
            _classicScreenAa = viewport.ScreenSpaceAA;
            _classicTaa = viewport.UseTaa;
            _classicScaling = viewport.Scaling3DMode;
            _classicScale = viewport.Scaling3DScale;
            _classicSharpness = viewport.FsrSharpness;
        }
        Viewport.Msaa msaa;
        Viewport.ScreenSpaceAAEnum screen;
        bool taa;
        Viewport.Scaling3DModeEnum scaling;
        float scale, sharpness;
        if (!AntiAlias)
            (msaa, screen, taa, scaling, scale, sharpness) = (_classicMsaa, _classicScreenAa, _classicTaa, _classicScaling, _classicScale, _classicSharpness);
        else if (ForwardPlus)
            // FSR 2 at 1.0 = its native-resolution AA mode (it replaces TAA; UseTaa stays off so they don't stack).
            (msaa, screen, taa, scaling, scale, sharpness) = (Viewport.Msaa.Disabled, Viewport.ScreenSpaceAAEnum.Disabled, false, Viewport.Scaling3DModeEnum.Fsr2, 1f, 0.25f);
        else
            (msaa, screen, taa, scaling, scale, sharpness) = (Viewport.Msaa.Msaa4X, Viewport.ScreenSpaceAAEnum.Smaa, false, _classicScaling, _classicScale, _classicSharpness);
        if (viewport.Msaa3D != msaa) viewport.Msaa3D = msaa;
        if (viewport.ScreenSpaceAA != screen) viewport.ScreenSpaceAA = screen;
        if (viewport.UseTaa != taa) viewport.UseTaa = taa;
        if (viewport.Scaling3DMode != scaling) viewport.Scaling3DMode = scaling;
        if (!Mathf.IsEqualApprox(viewport.Scaling3DScale, scale)) viewport.Scaling3DScale = scale;
        if (!Mathf.IsEqualApprox(viewport.FsrSharpness, sharpness)) viewport.FsrSharpness = sharpness;
    }
}
