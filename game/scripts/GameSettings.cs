using Godot;

namespace FinalFrontier;

/// <summary>How the game window shows: a normal window, borderless full screen, or exclusive full screen.</summary>
public enum WindowChoice { Windowed, Fullscreen, Exclusive }

/// <summary>
/// Player settings (docs/menu-contract.md): static, kept in user://settings.cfg. Read lazily on first use (so a direct F6
/// run of Planet.tscn gets them too); <see cref="Save"/> writes them, <see cref="Apply"/> pushes the window and audio ones
/// to the engine (the others are read where they act: PlanetDriver's camera, HudKit.ScaleFor, the tutorial).
/// Only settings that do something today are here. [graphics] holds GraphicsQuality's (the visual pass).
/// </summary>
public static class GameSettings
{
    private const string File = "user://settings.cfg";
    private static bool _loaded;

    private static bool _tutorialHints = true;
    private static float _masterVolume = 0.8f;
    private static float _uiVolume = 0.8f;
    private static float _effectsVolume = 1f;
    private static float _ambienceVolume = 0.9f;
    private static WindowChoice _window = WindowChoice.Windowed;
    private static bool _vsync = true;
    private static float _uiScale = 1f;
    private static float _cameraSpeed = 1f;
    private static bool _edgeScroll;

    public const float UiScaleMin = 0.85f, UiScaleMax = 1.25f;
    public const float CameraSpeedMin = 0.4f, CameraSpeedMax = 2.5f;

    /// <summary>Tutorial cards in a campaign start (and on the menu's star-system picker).</summary>
    public static bool TutorialHints { get { Load(); return _tutorialHints; } set { Load(); _tutorialHints = value; } }

    /// <summary>Master bus volume 0..1 (every sound goes through it; <see cref="ApplyAudio"/> sets the bus).</summary>
    public static float MasterVolume { get { Load(); return _masterVolume; } set { Load(); _masterVolume = Mathf.Clamp(value, 0f, 1f); } }

    /// <summary>The UI bus 0..1 (menu hover/click, tutorial cards, placing, refusals).</summary>
    public static float UiVolume { get { Load(); return _uiVolume; } set { Load(); _uiVolume = Mathf.Clamp(value, 0f, 1f); } }

    /// <summary>The Effects bus 0..1 (landings, vehicles driving, wipers, selection hums).</summary>
    public static float EffectsVolume { get { Load(); return _effectsVolume; } set { Load(); _effectsVolume = Mathf.Clamp(value, 0f, 1f); } }

    /// <summary>The Ambience bus 0..1 (the weather at the site: wind, gusts, rain, snowfall, dust storms, thunder).</summary>
    public static float AmbienceVolume { get { Load(); return _ambienceVolume; } set { Load(); _ambienceVolume = Mathf.Clamp(value, 0f, 1f); } }

    public static WindowChoice WindowMode { get { Load(); return _window; } set { Load(); _window = value; } }

    public static bool VSync { get { Load(); return _vsync; } set { Load(); _vsync = value; } }

    /// <summary>Multiplies HudKit.ScaleFor: the build drawer and card, the menus, dossier, inventory, achievements, chooser.
    /// The HUD's top bar / bottom panel and the helm frame keep their own sizes.</summary>
    public static float UiScale { get { Load(); return _uiScale; } set { Load(); _uiScale = Mathf.Clamp(value, UiScaleMin, UiScaleMax); } }

    /// <summary>Keyboard / edge pan speed on the surface and the globe's key turn speed in orbit (1 = as before).</summary>
    public static float CameraSpeed { get { Load(); return _cameraSpeed; } set { Load(); _cameraSpeed = Mathf.Clamp(value, CameraSpeedMin, CameraSpeedMax); } }

    /// <summary>Pan the surface camera when the mouse touches the window's edge.</summary>
    public static bool EdgeScroll { get { Load(); return _edgeScroll; } set { Load(); _edgeScroll = value; } }

    /// <summary>True while the game window doesn't have focus or the mouse is outside it (owner 2026-11-10: "if your
    /// cursor / focus leave the game, mute the sound"): the Master bus is muted then. Set by <see cref="SfxHost"/>.</summary>
    public static bool InBackground { get; private set; }

    /// <summary>The host's focus / mouse notifications: mute or unmute Master (volumes unchanged).</summary>
    public static void SetBackground(bool background)
    {
        if (InBackground == background) return;
        InBackground = background;
        ApplyAudio();
    }

    /// <summary>Reads user://settings.cfg once (missing file or keys = defaults).</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok) return;
        _tutorialHints = cfg.GetValue("game", "tutorial_hints", _tutorialHints).AsBool();
        _masterVolume = Mathf.Clamp((float)cfg.GetValue("audio", "master_volume", _masterVolume).AsDouble(), 0f, 1f);
        _uiVolume = Mathf.Clamp((float)cfg.GetValue("audio", "ui_volume", _uiVolume).AsDouble(), 0f, 1f);
        _effectsVolume = Mathf.Clamp((float)cfg.GetValue("audio", "effects_volume", _effectsVolume).AsDouble(), 0f, 1f);
        _ambienceVolume = Mathf.Clamp((float)cfg.GetValue("audio", "ambience_volume", _ambienceVolume).AsDouble(), 0f, 1f);
        var w = cfg.GetValue("display", "window_mode", (int)_window).AsInt32();
        _window = w is >= 0 and <= (int)WindowChoice.Exclusive ? (WindowChoice)w : WindowChoice.Windowed;
        _vsync = cfg.GetValue("display", "vsync", _vsync).AsBool();
        _uiScale = Mathf.Clamp((float)cfg.GetValue("display", "ui_scale", _uiScale).AsDouble(), UiScaleMin, UiScaleMax);
        _cameraSpeed = Mathf.Clamp((float)cfg.GetValue("controls", "camera_speed", _cameraSpeed).AsDouble(), CameraSpeedMin, CameraSpeedMax);
        _edgeScroll = cfg.GetValue("controls", "edge_scroll", _edgeScroll).AsBool();
        LoadGraphics(cfg);
    }

    /// <summary>[graphics] (visual pass, GraphicsQuality): preset 0 Classic / 1 Enhanced / 2 Custom, the Custom toggles
    /// and the frame-time readout. Missing keys keep the defaults (Enhanced, all on).</summary>
    private static void LoadGraphics(ConfigFile cfg)
    {
        var preset = cfg.GetValue("graphics", "preset", (int)GraphicsQuality.Preset).AsInt32();
        GraphicsQuality.Preset = preset is >= 0 and <= (int)GraphicsPreset.Custom ? (GraphicsPreset)preset : GraphicsPreset.Enhanced;
        GraphicsQuality.GiSetting = cfg.GetValue("graphics", "gi", GraphicsQuality.GiSetting).AsBool();
        GraphicsQuality.FogSetting = cfg.GetValue("graphics", "volumetric_fog", GraphicsQuality.FogSetting).AsBool();
        GraphicsQuality.GradingSetting = cfg.GetValue("graphics", "grading", GraphicsQuality.GradingSetting).AsBool();
        GraphicsQuality.ShadowsSetting = cfg.GetValue("graphics", "shadows", GraphicsQuality.ShadowsSetting).AsBool();
        GraphicsQuality.TerrainSetting = cfg.GetValue("graphics", "terrain", GraphicsQuality.TerrainSetting).AsBool();
        GraphicsQuality.DecalsSetting = cfg.GetValue("graphics", "decals", GraphicsQuality.DecalsSetting).AsBool();
        GraphicsQuality.EffectsSetting = cfg.GetValue("graphics", "effects", GraphicsQuality.EffectsSetting).AsBool();
        GraphicsQuality.AntiAliasSetting = cfg.GetValue("graphics", "anti_alias", GraphicsQuality.AntiAliasSetting).AsBool();
        GraphicsQuality.ShowFps = cfg.GetValue("graphics", "show_frame_time", GraphicsQuality.ShowFps).AsBool();
    }

    private static void SaveGraphics(ConfigFile cfg)
    {
        cfg.SetValue("graphics", "preset", (int)GraphicsQuality.Preset);
        cfg.SetValue("graphics", "gi", GraphicsQuality.GiSetting);
        cfg.SetValue("graphics", "volumetric_fog", GraphicsQuality.FogSetting);
        cfg.SetValue("graphics", "grading", GraphicsQuality.GradingSetting);
        cfg.SetValue("graphics", "shadows", GraphicsQuality.ShadowsSetting);
        cfg.SetValue("graphics", "terrain", GraphicsQuality.TerrainSetting);
        cfg.SetValue("graphics", "decals", GraphicsQuality.DecalsSetting);
        cfg.SetValue("graphics", "effects", GraphicsQuality.EffectsSetting);
        cfg.SetValue("graphics", "anti_alias", GraphicsQuality.AntiAliasSetting);
        cfg.SetValue("graphics", "show_frame_time", GraphicsQuality.ShowFps);
    }

    /// <summary>Every setting back to its default (not saved: call <see cref="Save"/>).</summary>
    public static void ResetDefaults()
    {
        _loaded = true;
        _tutorialHints = true;
        _masterVolume = 0.8f;
        _uiVolume = 0.8f;
        _effectsVolume = 1f;
        _ambienceVolume = 0.9f;
        _window = WindowChoice.Windowed;
        _vsync = true;
        _uiScale = 1f;
        _cameraSpeed = 1f;
        _edgeScroll = false;
        GraphicsQuality.ResetDefaults();
    }

    public static void Save()
    {
        Load();
        var cfg = new ConfigFile();
        cfg.SetValue("game", "tutorial_hints", _tutorialHints);
        cfg.SetValue("audio", "master_volume", _masterVolume);
        cfg.SetValue("audio", "ui_volume", _uiVolume);
        cfg.SetValue("audio", "effects_volume", _effectsVolume);
        cfg.SetValue("audio", "ambience_volume", _ambienceVolume);
        cfg.SetValue("display", "window_mode", (int)_window);
        cfg.SetValue("display", "vsync", _vsync);
        cfg.SetValue("display", "ui_scale", _uiScale);
        cfg.SetValue("controls", "camera_speed", _cameraSpeed);
        cfg.SetValue("controls", "edge_scroll", _edgeScroll);
        SaveGraphics(cfg);
        var err = cfg.Save(File);
        if (err != Error.Ok) GD.PushWarning($"Settings: couldn't save {File} ({err}).");
    }

    /// <summary>The volumes to the buses: Master, UI, Effects and Ambience; creates the game's buses if the
    /// project has none (Sfx). Cheap: the settings card calls it on every slider step.</summary>
    public static void ApplyAudio()
    {
        Load();
        Sfx.EnsureBuses();
        SetBus("Master", _masterVolume);
        if (InBackground && AudioServer.GetBusIndex("Master") is var master and >= 0) AudioServer.SetBusMute(master, true);
        SetBus(Sfx.UiBus, _uiVolume);
        SetBus(Sfx.EffectsBus, _effectsVolume);
        SetBus(Sfx.AmbienceBus, _ambienceVolume);

        static void SetBus(string name, float volume)
        {
            var bus = AudioServer.GetBusIndex(name);
            if (bus < 0) return;
            AudioServer.SetBusVolumeDb(bus, volume <= 0.001f ? -80f : Mathf.LinearToDb(volume));
            AudioServer.SetBusMute(bus, volume <= 0.001f);
        }
    }

    /// <summary>Window mode, VSync and the volumes to the engine (only what differs, so it is cheap to call often).</summary>
    public static void Apply()
    {
        ApplyAudio();
        var vsync = _vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled;
        if (DisplayServer.WindowGetVsyncMode() != vsync) DisplayServer.WindowSetVsyncMode(vsync);
        var mode = _window switch
        {
            WindowChoice.Fullscreen => DisplayServer.WindowMode.Fullscreen,
            WindowChoice.Exclusive => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        var now = DisplayServer.WindowGetMode();
        // A maximised or minimised window counts as windowed (don't fight the player's own window size).
        var nowChoice = now switch
        {
            DisplayServer.WindowMode.Fullscreen => WindowChoice.Fullscreen,
            DisplayServer.WindowMode.ExclusiveFullscreen => WindowChoice.Exclusive,
            _ => WindowChoice.Windowed,
        };
        if (nowChoice != _window) DisplayServer.WindowSetMode(mode);
    }
}
