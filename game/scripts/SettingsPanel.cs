using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The settings card (GameSettings), shared by the main menu's SETTINGS page and the in-game pause card. Every change is
/// applied at once (GameSettings.Apply) and saved (user://settings.cfg). DONE raises <see cref="Done"/>; RESET DEFAULTS
/// puts every setting back. Built in code in the HUD's look (HudKit).
/// </summary>
public partial class SettingsPanel : PanelContainer
{
    private float _s = 1f;
    private bool _inGame;
    private VBoxContainer _box = null!;

    public event System.Action? Done;

    /// <param name="s">UI scale (HudKit.ScaleFor).</param>
    /// <param name="inGame">Opened from the pause card (says which settings act at once in the running game).</param>
    public void Build(float s, bool inGame)
    {
        _s = s;
        _inGame = inGame;
        AddThemeStyleboxOverride("panel", HudKit.Box(HudKit.Panel, HudKit.Accent, 10 * s, 22 * s, 18 * s));
        CustomMinimumSize = new Vector2(640 * s, 0);
        MouseFilter = MouseFilterEnum.Stop;
        _box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _box.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
        AddChild(_box);
        Fill();
    }

    private void Fill()
    {
        var s = _s;
        var title = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        title.AddChild(HudKit.Label("SETTINGS", 26 * s, HudKit.Ink, condensed: true, bold: true));
        title.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var note = HudKit.Label("applied at once · saved for the next run", 12 * s, HudKit.Dim, condensed: true);
        note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        title.AddChild(note);
        _box.AddChild(title);

        Section("GAME");
        Toggle("Tutorial hints", "Guided cards in a campaign start and on the star-system picker", GameSettings.TutorialHints,
            v => GameSettings.TutorialHints = v);

        Section("DISPLAY");
        Choice("Window", new[] { ("WINDOW", WindowChoice.Windowed), ("FULL SCREEN", WindowChoice.Fullscreen), ("EXCLUSIVE", WindowChoice.Exclusive) },
            GameSettings.WindowMode, v => GameSettings.WindowMode = v,
            "Full screen: borderless; exclusive: the monitor's own mode");
        Toggle("VSync", "Frame rate locked to the display (off: no cap)", GameSettings.VSync, v => GameSettings.VSync = v);
        Slider("UI scale", "Menus, cards, build drawer, dossier, inventory (not the top bar or helm)",
            GameSettings.UiScaleMin, GameSettings.UiScaleMax, 0.05f, GameSettings.UiScale, v => GameSettings.UiScale = v, v => $"{v * 100f:0}%");

        Section("CONTROLS");
        Slider("Camera speed", "Panning on the surface, turning the globe in orbit",
            GameSettings.CameraSpeedMin, GameSettings.CameraSpeedMax, 0.1f, GameSettings.CameraSpeed, v => GameSettings.CameraSpeed = v, v => $"×{v:0.0}");
        Toggle("Edge scrolling", "Pan when the mouse touches the window's edge", GameSettings.EdgeScroll, v => GameSettings.EdgeScroll = v);

        Section("AUDIO");
        // Volumes act while the slider moves; a preview plays once it is let go.
        Slider("Master volume", "Every sound in the game",
            0f, 1f, 0.05f, GameSettings.MasterVolume, v =>
            {
                GameSettings.MasterVolume = v;
                GameSettings.ApplyAudio();
            }, v => $"{v * 100f:0}%", () => Sfx.Ui("click"));
        Slider("Interface", "Menus, buttons, tutorial cards, placing and refusals",
            0f, 1f, 0.05f, GameSettings.UiVolume, v =>
            {
                GameSettings.UiVolume = v;
                GameSettings.ApplyAudio();
            }, v => $"{v * 100f:0}%", () => Sfx.Ui("click"));
        Slider("Effects", "Landings, vehicles driving, wipers and the hum of what you select",
            0f, 1f, 0.05f, GameSettings.EffectsVolume, v =>
            {
                GameSettings.EffectsVolume = v;
                GameSettings.ApplyAudio();
            }, v => $"{v * 100f:0}%", () => Sfx.Preview("select/default_vehicle", Sfx.EffectsBus));
        Slider("Weather", "Wind, gusts, rain, snowfall, dust storms and thunder at the site",
            0f, 1f, 0.05f, GameSettings.AmbienceVolume, v =>
            {
                GameSettings.AmbienceVolume = v;
                GameSettings.ApplyAudio();
            }, v => $"{v * 100f:0}%", () => Sfx.Preview("ambience/wind_gust_b", Sfx.AmbienceBus));

        _box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6 * s), MouseFilter = MouseFilterEnum.Ignore });
        var bar = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * s));
        var reset = HudKit.ActionButton("RESET DEFAULTS", false, s);
        reset.CustomMinimumSize = new Vector2(170 * s, 36 * s);
        reset.Pressed += () =>
        {
            GameSettings.ResetDefaults();
            Changed();
            Callable.From(Refill).CallDeferred(); // not inside the button's own signal: Refill frees it
        };
        bar.AddChild(reset);
        bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var done = HudKit.ActionButton(_inGame ? "BACK" : "DONE", true, s);
        done.CustomMinimumSize = new Vector2(140 * s, 36 * s);
        done.Pressed += () => Done?.Invoke();
        bar.AddChild(done);
        _box.AddChild(bar);
    }

    private void Refill()
    {
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        HudKit.Clear(_box);
        Fill();
    }

    private static void Changed()
    {
        GameSettings.Apply();
        GameSettings.Save();
    }

    private void Section(string title)
    {
        _box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2 * _s), MouseFilter = MouseFilterEnum.Ignore });
        _box.AddChild(HudKit.Label(title, 13 * _s, HudKit.Accent, condensed: true, bold: true));
        _box.AddChild(new ColorRect { Color = HudKit.Line, CustomMinimumSize = new Vector2(0, 1), MouseFilter = MouseFilterEnum.Ignore });
    }

    /// <summary>A row: name + hint on the left, the control on the right.</summary>
    private HBoxContainer Row(string name, string hint)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(14 * _s));
        var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        text.AddChild(HudKit.Label(name, 16 * _s, HudKit.Ink, condensed: true, bold: true));
        var h = HudKit.Label(hint, 12 * _s, HudKit.Dim);
        h.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        h.CustomMinimumSize = new Vector2(260 * _s, 0);
        text.AddChild(h);
        row.AddChild(text);
        _box.AddChild(row);
        return row;
    }

    private Button Tile(string text, bool on)
    {
        var b = new Button { Text = text, ToggleMode = true, ButtonPressed = on, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(70 * _s, 30 * _s) };
        HudKit.StyleTile(b, 5 * _s, 6 * _s);
        b.AddThemeFontOverride("font", HudKit.Condensed(true));
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(13 * _s));
        b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return b;
    }

    private void Toggle(string name, string hint, bool value, System.Action<bool> set)
    {
        var row = Row(name, hint);
        var b = Tile(value ? "ON" : "OFF", value);
        b.Toggled += on =>
        {
            b.Text = on ? "ON" : "OFF";
            set(on);
            Changed();
        };
        row.AddChild(b);
    }

    private void Choice<T>(string name, (string Text, T Value)[] options, T value, System.Action<T> set, string hint)
    {
        var row = Row(name, hint);
        var group = new ButtonGroup();
        var tiles = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        tiles.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * _s));
        foreach (var (text, v) in options)
        {
            var b = Tile(text, EqualityComparer<T>.Default.Equals(v, value));
            b.ButtonGroup = group;
            var pick = v;
            b.Toggled += on =>
            {
                if (!on) return;
                set(pick);
                Changed();
            };
            tiles.AddChild(b);
        }
        row.AddChild(tiles);
    }

    private void Slider(string name, string hint, float min, float max, float step, float value, System.Action<float> set, System.Func<float, string> show,
        System.Action? committed = null)
    {
        var row = Row(name, hint);
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(150 * _s, 24 * _s),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        var track = HudKit.Box(HudKit.Chip, HudKit.BtnEdge, 3 * _s);
        track.ContentMarginTop = track.ContentMarginBottom = 3 * _s;
        slider.AddThemeStyleboxOverride("slider", track);
        var fill = HudKit.Box(new Color(HudKit.Accent, 0.75f), new Color(0, 0, 0, 0), 3 * _s, 0, 0, 0);
        fill.ContentMarginTop = fill.ContentMarginBottom = 3 * _s;
        slider.AddThemeStyleboxOverride("grabber_area", fill);
        slider.AddThemeStyleboxOverride("grabber_area_highlight", fill);
        var label = HudKit.Label(show(value), 14 * _s, HudKit.Ink, condensed: true);
        label.CustomMinimumSize = new Vector2(46 * _s, 0);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        slider.ValueChanged += v =>
        {
            set((float)v);
            label.Text = show((float)v);
        };
        // Save once a drag ends (not on every step of it); clicks on the track and the wheel save at once.
        var dragging = false;
        slider.DragStarted += () => dragging = true;
        slider.DragEnded += _ =>
        {
            dragging = false;
            Changed();
            committed?.Invoke();
        };
        slider.ValueChanged += _ =>
        {
            if (dragging) return;
            Changed();
            committed?.Invoke();
        };
        row.AddChild(slider);
        row.AddChild(label);
    }
}
