using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The world and map seed the next game starts with (owner 2026-11-08: "create a world chooser into the game").
/// Static, so it outlives the scene reload that starts the new game, and saved to user://game_setup.cfg so the choice
/// is kept between runs. PlanetDriver reads it in _Ready before its own [Export] World / Seed defaults.
/// </summary>
public static class GameSetup
{
    private const string File = "user://game_setup.cfg";
    private static bool _loaded;
    private static string? _world;
    private static ulong? _seed;

    public static string? World { get { Load(); return _world; } }
    public static ulong? Seed { get { Load(); return _seed; } }

    public static void Choose(string world, ulong seed)
    {
        _loaded = true;
        _world = world;
        _seed = seed;
        var cfg = new ConfigFile();
        cfg.SetValue("game", "world", world);
        cfg.SetValue("game", "seed", seed.ToString());
        cfg.Save(File);
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok) return;
        var w = cfg.GetValue("game", "world", "").AsString();
        if (w.Length > 0) _world = w;
        if (ulong.TryParse(cfg.GetValue("game", "seed", "").AsString(), out var s)) _seed = s;
    }
}

/// <summary>
/// The in-game world chooser: a WORLD button on the orbit screen (and F2) opens a panel listing every playable world of
/// the duel map (class, gravity, habitability tier, map size) and the map seed; START saves the choice (GameSetup) and
/// reloads the scene, so the new game begins in orbit over that world. Lives on its own CanvasLayer above the helm.
/// </summary>
public partial class WorldChooser : Control
{
    private GameData _data = null!;
    private string _current = "";
    private ulong _currentSeed;
    private Button _open = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _rows = null!;
    private LineEdit _seedEdit = null!;
    private Label _note = null!;
    private string _picked = "";
    private readonly Dictionary<string, Button> _worldButtons = new();

    /// <summary>Whether the orbit screen is showing (the WORLD button only shows there).</summary>
    public bool Available { get; set; }

    public bool IsOpen => _panel.Visible;

    public void Setup(GameData data, string currentWorld, ulong currentSeed)
    {
        _data = data;
        _current = currentWorld;
        _currentSeed = currentSeed;
        _picked = currentWorld;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _open = new Button { Text = "WORLD  ▾", FocusMode = FocusModeEnum.None, TooltipText = "Choose the world for a new game (F2)" };
        HudKit.StyleTile(_open, 6, 6);
        _open.AddThemeFontOverride("font", HudKit.Condensed(true));
        _open.AddThemeColorOverride("font_color", HudKit.Ink);
        _open.Pressed += Toggle;
        AddChild(_open);

        _panel = HudKit.PanelBox(HudKit.Panel, HudKit.Accent, 10, 18, 14);
        _panel.MouseFilter = MouseFilterEnum.Stop;
        _panel.Visible = false;
        AddChild(_panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(box);
        box.AddChild(HudKit.Label("CHOOSE A WORLD", 22, HudKit.Accent, condensed: true, bold: true));
        box.AddChild(HudKit.Label("Starts a new game in orbit over the world you pick. Your choice is kept for the next run.", 13, HudKit.Dim));
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 6);
        box.AddChild(_rows);
        foreach (var (id, name, cls) in Worlds())
        {
            var b = new Button { ToggleMode = true, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
            HudKit.StyleTile(b, 6, 8);
            b.AddThemeFontOverride("font", HudKit.Condensed());
            b.AddThemeColorOverride("font_color", HudKit.Ink);
            b.Text = Describe(name, cls);
            var pick = id;
            b.Pressed += () => Pick(pick);
            _rows.AddChild(b);
            _worldButtons[id] = b;
        }

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 8);
        seedRow.AddChild(HudKit.Label("MAP SEED", 15, HudKit.Dim, condensed: true, bold: true));
        _seedEdit = new LineEdit { Text = currentSeed.ToString(), CustomMinimumSize = new Vector2(140, 0), TooltipText = "The same seed always gives the same map" };
        seedRow.AddChild(_seedEdit);
        var random = new Button { Text = "RANDOM", FocusMode = FocusModeEnum.None };
        HudKit.StyleTile(random, 6, 6);
        random.Pressed += () => _seedEdit.Text = (GD.Randi() % 99999 + 1).ToString();
        seedRow.AddChild(random);
        box.AddChild(seedRow);

        _note = HudKit.Label("", 13, HudKit.Warn);
        box.AddChild(_note);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        var cancel = new Button { Text = "CANCEL", FocusMode = FocusModeEnum.None };
        HudKit.StyleTile(cancel, 6, 8);
        cancel.Pressed += Close;
        var start = new Button { Text = "START NEW GAME", FocusMode = FocusModeEnum.None };
        HudKit.StyleTile(start, 6, 8);
        start.AddThemeColorOverride("font_color", HudKit.Accent);
        start.Pressed += Start;
        buttons.AddChild(cancel);
        buttons.AddChild(start);
        box.AddChild(buttons);
        Pick(currentWorld);
    }

    /// <summary>Every body of the duel map (then other maps) with a playable surface, by id, name and class; each id once.</summary>
    private IEnumerable<(string Id, string Name, string Class)> Worlds()
    {
        var seen = new HashSet<string>();
        foreach (var m in _data.Maps.OrderBy(m => m.Id == MatchSetup.MvpMap ? 0 : 1))
            foreach (var b in m.Bodies)
                if (seen.Add(b.Id) && MatchSetup.IsWorld(_data, b.Id))
                    yield return (b.Id, b.Name, b.ClassId);
    }

    private string Describe(string name, string cls)
    {
        var className = _data.PlanetClasses.FirstOrDefault(c => c.Id == cls)?.Name ?? cls;
        var env = _data.EnvironmentOf(cls);
        var tier = WorldHabitability.Assess(_data, cls).Tier;
        var surface = _data.Surfaces.FirstOrDefault(s => s.ClassId == cls);
        var size = surface != null ? $"{surface.Width} × {surface.Height}" : "";
        var g = env != null ? $"{env.GravityG.ToFloat():0.##} g" : "";
        var need = tier switch
        {
            HabitabilityTier.Open => "open air",
            HabitabilityTier.Masks => "masks",
            HabitabilityTier.Suits => "suits",
            HabitabilityTier.Domes => "domes needed",
            HabitabilityTier.Tech => "needs tech",
            _ => "uninhabitable",
        };
        return $"{name.ToUpperInvariant()}    {className} · {g} · {need} · {size} sectors";
    }

    private void Pick(string id)
    {
        _picked = id;
        foreach (var (key, b) in _worldButtons) b.ButtonPressed = key == id;
    }

    public void Toggle()
    {
        if (_panel.Visible) Close();
        else
        {
            _seedEdit.Text = _currentSeed.ToString();
            Pick(_current);
            _note.Text = "";
            _panel.Visible = true;
        }
    }

    private void Close() => _panel.Visible = false;

    private void Start()
    {
        if (!ulong.TryParse(_seedEdit.Text.Trim(), out var seed))
        {
            _note.Text = "The seed must be a whole number.";
            return;
        }
        if (!MatchSetup.IsWorld(_data, _picked))
        {
            _note.Text = "Pick a world.";
            return;
        }
        GameSetup.Choose(_picked, seed);
        GetTree().ReloadCurrentScene();
    }

    /// <summary>True when the mouse is over the button or the open panel (the driver doesn't pick tiles under it).</summary>
    public bool IsOverUi(Vector2 mouse) =>
        (_open.Visible && _open.GetGlobalRect().HasPoint(mouse)) || (_panel.Visible && _panel.GetGlobalRect().HasPoint(mouse));

    public override void _Process(double delta)
    {
        var screen = GetViewportRect().Size;
        var scale = HudKit.ScaleFor(screen);
        _open.Visible = Available || _panel.Visible;
        _open.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(15 * scale));
        // top of the helm's view area, left of centre (clear of the rail on the left and the panel on the right)
        _open.Position = new Vector2(150 * scale, 80 * scale);
        if (_panel.Visible)
        {
            var size = _panel.GetCombinedMinimumSize();
            _panel.Size = size;
            _panel.Position = (screen - size) / 2f;
        }
    }
}
