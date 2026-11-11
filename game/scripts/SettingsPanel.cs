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

        // Two pages: GENERAL (game, display, controls, audio) and GRAPHICS (the visual pass), so the card stays one
        // screen tall at 1280×720.
        var tabs = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        tabs.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
        var tabGroup = new ButtonGroup();
        foreach (var (text, page) in new[] { ("GENERAL", 0), ("GRAPHICS", 1), ("DEBUG", 2) })
        {
            var b = Tile(text, _page == page);
            b.ButtonGroup = tabGroup;
            b.CustomMinimumSize = new Vector2(120 * s, 30 * s);
            var pick = page;
            b.Toggled += on =>
            {
                if (!on || _page == pick) return;
                _page = pick;
                Callable.From(Refill).CallDeferred();
            };
            tabs.AddChild(b);
        }
        _box.AddChild(tabs);

        if (_page == 1) FillGraphics();
        else if (_page == 2) FillDebug();
        else FillGeneral();

        _box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6 * s), MouseFilter = MouseFilterEnum.Ignore });
        var bar = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * s));
        var reset = HudKit.ActionButton("RESET DEFAULTS", false, s);
        reset.CustomMinimumSize = new Vector2(170 * s, 36 * s);
        reset.Pressed += () =>
        {
            GameSettings.ResetDefaults();
            Changed();
            GraphicsQuality.NotifyChanged();
            DebugOptions.NotifyChanged();
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

    /// <summary>DEBUG (DebugOptions, [debug] in settings.cfg): the F8 overlay and its sections, the world draws and the
    /// renderer view (two columns of compact rows so the card stays one 720p screen tall), then the frame rate: the FPS
    /// cap here, VSync stays in General › Display (a row here says what it is and jumps there). Every change applies at
    /// once (DebugOptions.Changed) and is saved.</summary>
    private void FillDebug()
    {
        Section("DEBUG OVERLAY · F8");
        var sections = Grid();
        DebugToggle(sections, "Debug overlay", "Cards top-left; F8 turns them on and off in the game and on the menu",
            DebugOptions.Overlay, v => DebugOptions.Overlay = v);
        foreach (var (name, hint, get, set) in DebugOptions.Sections) DebugToggle(sections, name, hint, get(), set);

        Section("WORLD (SURFACE, WHILE THE OVERLAY IS ON)");
        var draws = Grid();
        foreach (var (name, hint, get, set) in DebugOptions.Draws) DebugToggle(draws, name, hint, get(), set);
        Choice("Renderer view", new[]
            {
                ("OFF", DebugRenderMode.Off), ("WIRE", DebugRenderMode.Wireframe), ("OVERDRAW", DebugRenderMode.Overdraw),
                ("UNSHADED", DebugRenderMode.Unshaded), ("LIGHTING", DebugRenderMode.Lighting),
            }, DebugOptions.RenderMode, v => DebugOptions.RenderMode = v,
            "Godot's debug views of the 3D scene; Shift+F8 cycles them", debug: true);

        Section("FRAME RATE");
        var caps = new (string, int)[DebugOptions.FpsCaps.Length];
        for (var i = 0; i < caps.Length; i++) caps[i] = (DebugOptions.FpsCaps[i] == 0 ? "OFF" : DebugOptions.FpsCaps[i].ToString(), DebugOptions.FpsCaps[i]);
        Choice("FPS cap", caps, DebugOptions.FpsCap, v => DebugOptions.FpsCap = v,
            "Most frames a second, overlay or not; 30 or 60 keeps a laptop cooler", debug: true);
        var vsync = Row("VSync", $"{(GameSettings.VSync ? "ON: frames wait for the display (caps FPS)" : "OFF: no cap but the one above")} · set in General › Display");
        var go = Tile("GENERAL ›", false);
        go.ToggleMode = false;
        go.Pressed += () =>
        {
            _page = 0;
            Callable.From(Refill).CallDeferred();
        };
        vsync.AddChild(go);
    }

    private GridContainer Grid()
    {
        var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(22 * _s));
        grid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(5 * _s));
        _box.AddChild(grid);
        return grid;
    }

    /// <summary>A compact row for the two-column DEBUG grid: name + one-line hint, ON/OFF tile.</summary>
    private void DebugToggle(GridContainer grid, string name, string hint, bool value, System.Action<bool> set)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
        var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        text.AddChild(HudKit.Label(name, 15 * _s, HudKit.Ink, condensed: true, bold: true));
        var h = HudKit.Label(hint, 11 * _s, HudKit.Dim);
        h.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        h.CustomMinimumSize = new Vector2(205 * _s, 0);
        text.AddChild(h);
        row.AddChild(text);
        var b = Tile(value ? "ON" : "OFF", value);
        b.CustomMinimumSize = new Vector2(52 * _s, 28 * _s);
        b.Toggled += on =>
        {
            b.Text = on ? "ON" : "OFF";
            set(on);
            DebugChanged();
        };
        row.AddChild(b);
        grid.AddChild(row);
    }

    /// <summary>A debug switch changed: save, apply (the FPS cap), tell the overlay.</summary>
    private static void DebugChanged()
    {
        Changed();
        DebugOptions.NotifyChanged();
    }

    /// <summary>The page shown (0 general, 1 graphics, 2 debug); kept while the game runs so the card reopens where it was.</summary>
    private static int _page;

    /// <summary>GRAPHICS: the preset, one toggle per feature (the preset's values while Classic / Enhanced; touching one
    /// makes the mix Custom), the frame-time readout. Every change applies at once (GraphicsQuality.Changed) and is saved.</summary>
    private void FillGraphics()
    {
        Section("GRAPHICS");
        Choice("Preset", new[] { ("CLASSIC", GraphicsPreset.Classic), ("ENHANCED", GraphicsPreset.Enhanced), ("CUSTOM", GraphicsPreset.Custom) },
            GraphicsQuality.Preset, v => GraphicsQuality.SetPreset(v),
            "Classic: the look before the visual pass · Enhanced: everything on · Custom: your own mix", graphics: true);
        for (var i = 0; i < GraphicsQuality.Features.Length; i++)
        {
            var index = i;
            var (name, hint, _, _) = GraphicsQuality.Features[i];
            Toggle(name, hint, GraphicsQuality.FeatureShown(i), v => GraphicsQuality.SetFeature(index, v), graphics: true);
        }
        Toggle("Show frame time", "FPS and frame / GPU milliseconds top-left (always shown while F7 compares)", GraphicsQuality.ShowFps,
            v => GraphicsQuality.ShowFps = v);
        var note = GraphicsQuality.ForwardPlus
            ? "In the game F7 flips between Classic and these settings, with the frame time. Turn VSync off to see the real cost."
            : "This renderer has no global illumination, volumetric fog or FSR 2 (Forward+ only): those switches do nothing here.";
        if (_inGame && GraphicsQuality.CompareClassic) note = "F7 compare is on: the game shows Classic until F7 is pressed again. " + note;
        var n = HudKit.Label(note, 12 * _s, HudKit.Dim);
        n.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        n.CustomMinimumSize = new Vector2(400 * _s, 0);
        _box.AddChild(n);
    }

    private void FillGeneral()
    {
        var s = _s;
        Section("GAME");
        Toggle("Tutorial hints", "Guided cards in a campaign start and on the star-system picker (SKIP TUTORIAL turns this off)", GameSettings.TutorialHints,
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

    /// <summary>A graphics switch changed: save, tell the listeners (environment, terrain, decals), and redraw the page
    /// (the preset row and the other toggles follow), not inside the button's own signal.</summary>
    private void GraphicsChanged()
    {
        Changed();
        GraphicsQuality.NotifyChanged();
        Callable.From(Refill).CallDeferred();
    }

    private void Toggle(string name, string hint, bool value, System.Action<bool> set, bool graphics = false)
    {
        var row = Row(name, hint);
        var b = Tile(value ? "ON" : "OFF", value);
        b.Toggled += on =>
        {
            b.Text = on ? "ON" : "OFF";
            set(on);
            if (graphics) GraphicsChanged();
            else Changed();
        };
        row.AddChild(b);
    }

    private void Choice<T>(string name, (string Text, T Value)[] options, T value, System.Action<T> set, string hint, bool graphics = false, bool debug = false)
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
                if (graphics) GraphicsChanged();
                else if (debug) DebugChanged();
                else Changed();
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
