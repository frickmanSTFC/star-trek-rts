using Godot;

namespace FinalFrontier;

/// <summary>How the game window shows: a normal window, borderless full screen, or exclusive full screen.</summary>
public enum WindowChoice { Windowed, Fullscreen, Exclusive }

/// <summary>
/// Player settings (docs/menu-contract.md): static, kept in user://settings.cfg. Read lazily on first use (so a direct F6
/// run of Planet.tscn gets them too); <see cref="Save"/> writes them, <see cref="Apply"/> pushes the window and audio ones
/// to the engine (the others are read where they act: PlanetDriver's camera, HudKit.ScaleFor, the tutorial).
/// Only settings that do something today are here.
/// </summary>
public static class GameSettings
{
    private const string File = "user://settings.cfg";
    private static bool _loaded;

    private static bool _tutorialHints = true;
    private static float _masterVolume = 0.8f;
    private static WindowChoice _window = WindowChoice.Windowed;
    private static bool _vsync = true;
    private static float _uiScale = 1f;
    private static float _cameraSpeed = 1f;
    private static bool _edgeScroll;

    public const float UiScaleMin = 0.85f, UiScaleMax = 1.25f;
    public const float CameraSpeedMin = 0.4f, CameraSpeedMax = 2.5f;

    /// <summary>Tutorial cards in a campaign start (and on the menu's star-system picker).</summary>
    public static bool TutorialHints { get { Load(); return _tutorialHints; } set { Load(); _tutorialHints = value; } }

    /// <summary>Master bus volume 0..1 (the game has no sounds yet; anything added later goes through the master bus).</summary>
    public static float MasterVolume { get { Load(); return _masterVolume; } set { Load(); _masterVolume = Mathf.Clamp(value, 0f, 1f); } }

    public static WindowChoice WindowMode { get { Load(); return _window; } set { Load(); _window = value; } }

    public static bool VSync { get { Load(); return _vsync; } set { Load(); _vsync = value; } }

    /// <summary>Multiplies HudKit.ScaleFor: the build drawer and card, the menus, dossier, inventory, achievements, chooser.
    /// The HUD's top bar / bottom panel and the helm frame keep their own sizes.</summary>
    public static float UiScale { get { Load(); return _uiScale; } set { Load(); _uiScale = Mathf.Clamp(value, UiScaleMin, UiScaleMax); } }

    /// <summary>Keyboard / edge pan speed on the surface and the globe's key turn speed in orbit (1 = as before).</summary>
    public static float CameraSpeed { get { Load(); return _cameraSpeed; } set { Load(); _cameraSpeed = Mathf.Clamp(value, CameraSpeedMin, CameraSpeedMax); } }

    /// <summary>Pan the surface camera when the mouse touches the window's edge.</summary>
    public static bool EdgeScroll { get { Load(); return _edgeScroll; } set { Load(); _edgeScroll = value; } }

    /// <summary>Reads user://settings.cfg once (missing file or keys = defaults).</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok) return;
        _tutorialHints = cfg.GetValue("game", "tutorial_hints", _tutorialHints).AsBool();
        _masterVolume = Mathf.Clamp((float)cfg.GetValue("audio", "master_volume", _masterVolume).AsDouble(), 0f, 1f);
        var w = cfg.GetValue("display", "window_mode", (int)_window).AsInt32();
        _window = w is >= 0 and <= (int)WindowChoice.Exclusive ? (WindowChoice)w : WindowChoice.Windowed;
        _vsync = cfg.GetValue("display", "vsync", _vsync).AsBool();
        _uiScale = Mathf.Clamp((float)cfg.GetValue("display", "ui_scale", _uiScale).AsDouble(), UiScaleMin, UiScaleMax);
        _cameraSpeed = Mathf.Clamp((float)cfg.GetValue("controls", "camera_speed", _cameraSpeed).AsDouble(), CameraSpeedMin, CameraSpeedMax);
        _edgeScroll = cfg.GetValue("controls", "edge_scroll", _edgeScroll).AsBool();
    }

    /// <summary>Every setting back to its default (not saved: call <see cref="Save"/>).</summary>
    public static void ResetDefaults()
    {
        _loaded = true;
        _tutorialHints = true;
        _masterVolume = 0.8f;
        _window = WindowChoice.Windowed;
        _vsync = true;
        _uiScale = 1f;
        _cameraSpeed = 1f;
        _edgeScroll = false;
    }

    public static void Save()
    {
        Load();
        var cfg = new ConfigFile();
        cfg.SetValue("game", "tutorial_hints", _tutorialHints);
        cfg.SetValue("audio", "master_volume", _masterVolume);
        cfg.SetValue("display", "window_mode", (int)_window);
        cfg.SetValue("display", "vsync", _vsync);
        cfg.SetValue("display", "ui_scale", _uiScale);
        cfg.SetValue("controls", "camera_speed", _cameraSpeed);
        cfg.SetValue("controls", "edge_scroll", _edgeScroll);
        var err = cfg.Save(File);
        if (err != Error.Ok) GD.PushWarning($"Settings: couldn't save {File} ({err}).");
    }

    /// <summary>Window mode, VSync and the master volume to the engine (only what differs, so it is cheap to call often).</summary>
    public static void Apply()
    {
        Load();
        var bus = AudioServer.GetBusIndex("Master");
        if (bus >= 0)
        {
            AudioServer.SetBusVolumeDb(bus, _masterVolume <= 0.001f ? -80f : Mathf.LinearToDb(_masterVolume));
            AudioServer.SetBusMute(bus, _masterVolume <= 0.001f);
        }
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
