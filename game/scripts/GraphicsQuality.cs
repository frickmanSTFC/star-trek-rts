using System;

// The visual pass's switches (docs/visual-pass-contract.md; owner 2026-11-11: "do the visual pass after the merge" and
// switchable in game to compare). Render-only: nothing here may touch the sim. STUB (contract): agent G1 owns this file
// (saving in GameSettings, presets, the live compare key); agent G2 reads the flags. Keep the member names.

/// <summary>Classic = the game exactly as it looked before the visual pass; Enhanced = every feature on; Custom = the
/// player's own mix of the toggles below.</summary>
public enum GraphicsPreset { Classic, Enhanced, Custom }

public static class GraphicsQuality
{
    /// <summary>The chosen preset (saved).</summary>
    public static GraphicsPreset Preset { get; set; } = GraphicsPreset.Enhanced;

    /// <summary>The live A/B compare: true = show Classic right now whatever the settings say (F7 flips it; not saved).</summary>
    public static bool CompareClassic { get; set; }

    // The player's toggles (used when Preset is Custom; Enhanced = all true; Classic = all false). Saved.
    public static bool GiSetting { get; set; } = true;           // global illumination (SDFGI) + SSIL
    public static bool FogSetting { get; set; } = true;          // volumetric fog / light shafts
    public static bool GradingSetting { get; set; } = true;      // tonemap + per-world colour grading, tuned glow
    public static bool ShadowsSetting { get; set; } = true;      // better shadows + SSAO contact shading
    public static bool TerrainSetting { get; set; } = true;      // high-detail terrain textures + detail normals
    public static bool DecalsSetting { get; set; } = true;       // ground decals: tracks, scorch, dust, paths
    public static bool EffectsSetting { get; set; } = true;      // extra effects: heat haze, embers, dust motes, spray
    public static bool AntiAliasSetting { get; set; } = true;    // TAA / FSR 2 upscaling
    public static bool ShowFps { get; set; }                      // frame-time readout (always on while comparing)

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

    /// <summary>Raised after any switch changes (settings applied, preset changed, compare flipped). Listeners re-apply
    /// what they own (environment, materials, decal pools); none of them should poll every frame.</summary>
    public static event Action? Changed;

    /// <summary>Call after changing any property above.</summary>
    public static void NotifyChanged() => Changed?.Invoke();
}
