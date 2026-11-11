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
public enum GameMode { Debug, Campaign }

public static class GameSetup
{
    public const string MenuScene = "res://scenes/MainMenu.tscn";
    public const string PlanetScene = "res://scenes/Planet.tscn";

    private const string File = "user://game_setup.cfg";
    private static bool _loaded;
    private static string? _world;
    private static ulong? _seed;

    public static string? World { get { Load(); return _world; } }
    public static ulong? Seed { get { Load(); return _seed; } }

    // Main menu (docs/menu-contract.md, scripts/MainMenu*.cs). Debug = today's sandbox (the saved world/seed above, the
    // world chooser, the test panel); also what an F6 run of Planet.tscn gets. Campaign = the menu's pick: faction, era,
    // the system seed and the home body (PlanetDriver._Ready reads them; not saved).
    public static GameMode Mode { get; private set; } = GameMode.Debug;
    public static string Faction { get; private set; } = "federation";
    public static string? Era { get; private set; }
    public static ulong? CampaignSeed { get; private set; }
    public static string? HomeBody { get; private set; }

    /// <summary>Tutorial cards on: a campaign start with tutorial hints on in the settings.</summary>
    public static bool Tutorial => Mode == GameMode.Campaign && GameSettings.TutorialHints;

    /// <summary>The mission chain a campaign start turns on (docs/missions-contract.md C): the era's chain (Dawn of Warp's
    /// "First Light" is enabled by the era id), null in a DEBUG sandbox (the test panel's MISSIONS tab can switch them on).
    /// PlanetDriver.BeginMissions hands it to the sim before tick 0.</summary>
    public static string? MissionChain { get; private set; }

    /// <summary>The main menu's campaign start (not saved: chosen in the menu each time).</summary>
    public static void StartCampaign(string faction, string era, ulong seed, string homeBodyId)
    {
        Mode = GameMode.Campaign;
        Faction = faction;
        Era = era;
        CampaignSeed = seed;
        HomeBody = homeBodyId;
        MissionChain = era; // campaign start → missions on (the era's chain)
    }

    /// <summary>The main menu's DEBUG start: the sandbox as before.</summary>
    public static void StartDebug()
    {
        Mode = GameMode.Debug;
        Faction = "federation";
        Era = null;
        CampaignSeed = null;
        HomeBody = null;
        MissionChain = null;
    }

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
/// The in-game world chooser: a WORLD button on the orbit screen (and F2) opens a panel listing the landable bodies of
/// the star system the seed in the MAP SEED field generates (MatchSetup.LandableBodies: class, gravity, habitability
/// tier, map size; refreshed as the seed changes; Class M is rare). While that list is empty (the sim's generator isn't
/// in yet, or a seed with none) it falls back to the duel map's playable worlds. START saves the choice (GameSetup) and
/// reloads the scene; PlanetDriver then starts MatchSetup.CreateSystemGame on that home body. Own CanvasLayer above the helm.
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
    private Label _listNote = null!;
    private ulong? _listedSeed;
    private bool _systemList;
    private string _picked = "";
    private readonly Dictionary<string, Button> _worldButtons = new();

    /// <summary>Whether the orbit screen is showing (the WORLD button only shows there).</summary>
    public bool Available { get; set; }

    public bool IsOpen => _panel.Visible;

    /// <summary>The panel's ACHIEVEMENTS button (PlanetDriver.Achievements.cs opens the screen).</summary>
    public event System.Action? AchievementsRequested;

    /// <summary>The WORLD button in viewport pixels (empty while hidden).</summary>
    public Rect2 ButtonRect => _open.Visible ? _open.GetGlobalRect() : new Rect2();

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
        _listNote = HudKit.Label("", 13, HudKit.Dim);
        box.AddChild(_listNote);

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 8);
        seedRow.AddChild(HudKit.Label("MAP SEED", 15, HudKit.Dim, condensed: true, bold: true));
        _seedEdit = new LineEdit { Text = currentSeed.ToString(), CustomMinimumSize = new Vector2(140, 0), TooltipText = "The same seed always gives the same star system and map" };
        _seedEdit.TextChanged += _ => RefreshList();
        seedRow.AddChild(_seedEdit);
        var random = new Button { Text = "RANDOM", FocusMode = FocusModeEnum.None };
        HudKit.StyleTile(random, 6, 6);
        random.Pressed += () =>
        {
            _seedEdit.Text = (GD.Randi() % 99999 + 1).ToString(); // setting Text doesn't emit TextChanged
            RefreshList();
        };
        seedRow.AddChild(random);
        box.AddChild(seedRow);

        _note = HudKit.Label("", 13, HudKit.Warn);
        box.AddChild(_note);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        var achievements = new Button { Text = "ACHIEVEMENTS", FocusMode = FocusModeEnum.None, TooltipText = "Your achievements and the HANGAR (F3)" };
        HudKit.StyleTile(achievements, 6, 8);
        achievements.Pressed += () =>
        {
            Close();
            AchievementsRequested?.Invoke();
        };
        buttons.AddChild(achievements);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
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
        RefreshList();
        Pick(_worldButtons.ContainsKey(currentWorld) ? currentWorld : _picked);
    }

    /// <summary>Lists the worlds for the seed in the field (when it parses and changed): the seed's system's landable
    /// bodies, else the duel map's worlds. Keeps the pick if it is still listed, else picks the first.</summary>
    private void RefreshList()
    {
        if (!ulong.TryParse(_seedEdit.Text.Trim(), out var seed)) return;
        if (_listedSeed == seed) return;
        _listedSeed = seed;
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _worldButtons.Clear();
        var worlds = SystemWorlds(seed).ToList();
        _systemList = worlds.Count > 0;
        if (!_systemList) worlds = Worlds().ToList();
        foreach (var (id, name, cls) in worlds)
        {
            var b = new Button { ToggleMode = true, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
            HudKit.StyleTile(b, 6, 8);
            b.AddThemeFontOverride("font", HudKit.Condensed());
            b.AddThemeColorOverride("font_color", cls == "class_m" ? HudKit.Accent : HudKit.Ink);
            b.Text = Describe(name, cls);
            var pick = id;
            b.Pressed += () => Pick(pick);
            _rows.AddChild(b);
            _worldButtons[id] = b;
        }
        _listNote.Text = _systemList
            ? "Landable bodies of this seed's star system. Class M is rare: most systems have none."
            : "No generated star system for this seed yet: the duel map's worlds. Class M is rare: most systems have none.";
        Pick(_worldButtons.ContainsKey(_picked) ? _picked : worlds.Count > 0 ? worlds[0].Id : "");
    }

    /// <summary>The landable bodies of the system this seed generates (empty until the sim's generator is in).</summary>
    private List<(string Id, string Name, string Class)> SystemWorlds(ulong seed)
    {
        var list = new List<(string, string, string)>();
        try
        {
            foreach (var b in MatchSetup.LandableBodies(_data, seed)) list.Add((b.Id, b.Name, b.ClassId));
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"World chooser: no system for seed {seed}: {e.Message}");
            list.Clear();
        }
        return list;
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
        var parts = new List<string> { className };
        if (g.Length > 0) parts.Add(g);
        parts.Add(need);
        if (size.Length > 0) parts.Add($"{size} sectors");
        return $"{name.ToUpperInvariant()}    {string.Join(" · ", parts)}";
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
            RefreshList();
            Pick(_worldButtons.ContainsKey(_current) ? _current : _picked);
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
        RefreshList(); // the list matches the seed being started
        if (!_worldButtons.ContainsKey(_picked) || (!_systemList && !MatchSetup.IsWorld(_data, _picked)))
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
