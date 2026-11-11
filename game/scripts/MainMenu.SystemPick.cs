using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Space;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// The campaign's star-system picker (docs/menu-contract.md A): a generated system (a new random seed each visit, REROLL
// for another) drawn top-down by SystemMap. Every body shows; the landable ones (usually 1-2) are ringed in amber and
// clickable for a card (class, gravity, habitability tier, map size, RECOMMENDED for the easiest); the others say why
// they can't be landed on. LAND HERE → GameSetup.StartCampaign → Planet.tscn, where PlanetDriver starts
// MatchSetup.CreateSystemGame on that seed and body (same data setup as here, so the same system).
// With tutorial hints on, three TutorialCards explain the screen (once per run, unless left half-way).
public partial class MainMenu
{
    private ulong _pickSeed;
    private GeneratedSystem? _system;
    private string? _picked;
    private string? _recommended;
    private SystemMap? _map;
    private VBoxContainer? _card;
    private Button? _landButton;

    private static bool _pickerTutorialDone;
    private TutorialCard? _tutorial;
    private int _tutorialStep = -1;
    private Rect2 _tutorialAnchor;

    /// <summary>A new system for the picker (on entering it and on REROLL).</summary>
    private void Roll()
    {
        _pickSeed = (((ulong)GD.Randi() << 32) | GD.Randi()) % 999_999UL + 1UL;
        _system = null;
        _picked = null;
        _recommended = null;
        if (Data is not { } data) return;
        try
        {
            _system = SystemGenerator.Generate(data, _pickSeed);
        }
        catch (System.Exception e)
        {
            GD.PushError($"Main menu: no star system for seed {_pickSeed}: {e.Message}");
            return;
        }
        _recommended = _system.Landable(data)
            .OrderBy(b => (int)Tier(data, b.ClassId))
            .ThenBy(b => Mathf.Abs(Gravity(data, b.ClassId) - 1f))
            .ThenBy(b => b.ClassId == "class_m" ? 0 : 1)
            .FirstOrDefault()?.Id;
        // With the tutorial on the player picks (that's step 2); otherwise start on the easiest world.
        if (!(GameSettings.TutorialHints && !_pickerTutorialDone)) _picked = _recommended;
    }

    private static HabitabilityTier Tier(GameData data, string classId)
    {
        try
        {
            return WorldHabitability.Assess(data, classId).Tier;
        }
        catch (System.Exception)
        {
            return HabitabilityTier.Impossible;
        }
    }

    private static float Gravity(GameData data, string classId) => data.EnvironmentOf(classId)?.GravityG.ToFloat() ?? 1f;

    private bool Landable(string? id) =>
        id != null && Data is { } data && _system?.Body(id) is { } b && data.IsLandableClass(b.ClassId);

    private Control BuildPickerPage()
    {
        var (root, column) = NewPage(0.42f, 40f);
        var data = Data;
        var sys = _system;
        if (data == null || sys == null)
        {
            Header(column, "DAWN OF WARP", "NO STAR SYSTEM", "The game data didn't load (see the Output panel).");
            column.AddChild(Spacer());
            var b = BottomBar(column);
            b.AddChild(Action("‹  BACK", false, () => ShowPage(Page.Era)));
            return root;
        }
        var planets = sys.Bodies.Count(x => x.Parent == null);
        var moons = sys.Bodies.Count - planets;
        var landable = sys.Landable(data).Count;
        Header(column, "DAWN OF WARP  ·  2063  ·  CHOOSE A STARTING POINT", $"{sys.Star.Name.ToUpperInvariant()} SYSTEM",
            $"Class {sys.Star.Class} star  ·  {planets} planets and belts  ·  {moons} moons  ·  {landable} landable  ·  seed {_pickSeed}");

        var main = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        main.AddThemeConstantOverride("separation", Mathf.RoundToInt(12 * _s));
        column.AddChild(main);

        var mapBox = HudKit.PanelBox(new Color(0.01f, 0.015f, 0.04f, 0.82f), HudKit.Line, 8 * _s, 0, 0);
        mapBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mapBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        main.AddChild(mapBox);
        _map = new SystemMap { Name = "SystemMap", Scale1 = _s };
        _map.Setup(data, sys, _recommended);
        _map.Selected = _picked;
        _map.BodyPicked += PickBody;
        mapBox.AddChild(_map);

        var side = HudKit.PanelBox(HudKit.Panel, HudKit.Line, 8 * _s, 16 * _s, 14 * _s);
        side.CustomMinimumSize = new Vector2(340 * _s, 0);
        main.AddChild(side);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        side.AddChild(scroll);
        _card = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        _card.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * _s));
        scroll.AddChild(_card);

        var bar = BottomBar(column);
        bar.AddChild(Action("‹  BACK", false, () => ShowPage(Page.Era)));
        bar.AddChild(Spacer());
        var hint = HudKit.Label("Click a world ringed in amber  ·  Esc: back", 13 * _s, HudKit.Dim);
        hint.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bar.AddChild(hint);
        var reroll = Action("REROLL", false, () =>
        {
            Roll();
            _dirty = true; // rebuilt next frame (this button goes with the page)
            if (_tutorialStep == 2 && !Landable(_picked)) ShowTutorialStep(1); // the new system needs a pick again
        }, 130f);
        reroll.TooltipText = "Chart another star system";
        bar.AddChild(reroll);
        _landButton = Action("LAND HERE  ›", true, Land, 190f);
        Sfx.Tag(_landButton, "confirm");
        bar.AddChild(_landButton);
        return root;
    }

    /// <summary>After the page is (re)built: the card, the LAND button, and the tutorial's first card.</summary>
    private void AfterPickerBuilt()
    {
        RefreshCard();
        if (GameSettings.TutorialHints && !_pickerTutorialDone && _system != null) StartPickerTutorial();
    }

    private void PickBody(string id)
    {
        _picked = id;
        if (_map != null) _map.Selected = id;
        _cardDirty = true; // next frame: the card's own buttons may be what was pressed
        if (_tutorialStep == 1 && Landable(id)) ShowTutorialStep(2);
    }

    private void Land()
    {
        if (!Landable(_picked)) return;
        CloseTutorial();
        GameSetup.StartCampaign(CampaignFaction, CampaignEra, _pickSeed, _picked!);
        GetTree().ChangeSceneToFile(GameSetup.PlanetScene);
    }

    private void LeavePicker()
    {
        CloseTutorial();
        _map = null;
        _card = null;
        _landButton = null;
    }

    // ---------------------------------------------------------------- the card

    private void RefreshCard()
    {
        if (_card == null || Data is not { } data || _system is not { } sys) return;
        HudKit.Clear(_card);
        if (_landButton != null)
        {
            _landButton.Disabled = !Landable(_picked);
            _landButton.TooltipText = _landButton.Disabled ? "Pick a world you can land on first" : $"Start the campaign in orbit of {sys.Body(_picked!)?.Name}";
        }
        var body = _picked != null ? sys.Body(_picked) : null;
        if (body == null)
        {
            Overview(data, sys);
            return;
        }

        var landable = data.IsLandableClass(body.ClassId);
        var recommended = body.Id == _recommended;
        _card.AddChild(HudKit.Label(body.Name.ToUpperInvariant(), 26 * _s, HudKit.Ink, condensed: true, bold: true));
        _card.AddChild(HudKit.Label(ClassTitle(data, body.ClassId), 14 * _s, landable ? HudKit.Accent : HudKit.Dim, condensed: true, bold: true));
        _card.AddChild(Tag(recommended ? "RECOMMENDED START" : landable ? "LANDABLE" : "NO LANDING",
            recommended ? HudKit.Good : landable ? HudKit.Sky : HudKit.Warn));

        var rows = new List<(string, string)>();
        if (landable)
        {
            rows.Add(("GRAVITY", $"{Gravity(data, body.ClassId):0.##} g"));
            rows.Add(("CREWS NEED", TierName(Tier(data, body.ClassId))));
            if (data.Surfaces.FirstOrDefault(x => x.ClassId == body.ClassId) is { } surface)
                rows.Add(("SURFACE MAP", $"{surface.Width} × {surface.Height} sectors"));
        }
        var entry = sys.Entry(body.Id);
        if (body.Parent != null) rows.Add(("ORBITS", sys.Body(body.Parent)?.Name ?? body.Parent));
        if (entry != null)
        {
            rows.Add(("ZONE", ZoneName(entry.Zone)));
            rows.Add(("FROM THE STAR", $"{entry.Climate.Au.ToFloat():0.##} AU"));
            if (!SystemPlot.IsRocks(body.ClassId) && entry.Climate.DayHours.ToFloat() > 0f)
                rows.Add(("DAY", $"{entry.Climate.DayHours.ToFloat():0.#} h{(entry.Climate.Retrograde ? ", turns backwards" : "")}"));
        }
        var moonCount = sys.Bodies.Count(x => x.Parent == body.Id);
        if (body.Parent == null && moonCount > 0) rows.Add(("MOONS", moonCount.ToString()));
        _card.AddChild(Rows(rows));

        var text = HudKit.Label(landable ? TierText(Tier(data, body.ClassId)) : WhyNot(data, body), 14 * _s, HudKit.Ink);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _card.AddChild(text);
        if (landable && !recommended && _recommended != null && sys.Body(_recommended) is { } easy)
        {
            var l = HudKit.Label($"Easier start: {easy.Name}.", 13 * _s, HudKit.Dim);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _card.AddChild(l);
        }
        var back = HudKit.ActionButton("ALL WORLDS", false, _s);
        back.Pressed += () =>
        {
            _picked = null;
            if (_map != null) _map.Selected = null;
            _cardDirty = true;
        };
        _card.AddChild(Spacer(false, 4 * _s));
        _card.AddChild(back);
    }

    /// <summary>Nothing picked: what to do, and a button per landable world.</summary>
    private void Overview(GameData data, GeneratedSystem sys)
    {
        _card!.AddChild(HudKit.Label("PICK A WORLD", 24 * _s, HudKit.Ink, condensed: true, bold: true));
        var intro = HudKit.Label("Worlds ringed in amber have ground a colony can stand on. Click any body on the plot to read about it.", 14 * _s, HudKit.Dim);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _card.AddChild(intro);
        foreach (var b in sys.Landable(data))
        {
            var button = new Button { FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left, ClipText = true };
            HudKit.StyleTile(button, 6 * _s, 8 * _s);
            button.AddThemeFontOverride("font", HudKit.Condensed(true));
            button.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(15 * _s));
            button.AddThemeColorOverride("font_color", b.Id == _recommended ? HudKit.Accent : HudKit.Ink);
            button.Text = $"{b.Name.ToUpperInvariant()}   {ShortClass(data, b.ClassId)} · {TierName(Tier(data, b.ClassId)).ToLowerInvariant()}{(b.Id == _recommended ? "   ★ RECOMMENDED" : "")}";
            var id = b.Id;
            button.Pressed += () => PickBody(id);
            _card.AddChild(button);
        }
        var others = sys.Bodies.Count - sys.Landable(data).Count;
        if (others > 0)
        {
            var l = HudKit.Label($"The other {others} bodies can't be settled yet; their cards say why.", 13 * _s, HudKit.Dim);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _card.AddChild(l);
        }
    }

    private Control Tag(string text, Color colour)
    {
        var box = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var chip = HudKit.PanelBox(new Color(colour, 0.14f), new Color(colour, 0.8f), 4 * _s, 8 * _s, 2 * _s);
        chip.AddChild(HudKit.Label(text, 13 * _s, colour, condensed: true, bold: true));
        box.AddChild(chip);
        return box;
    }

    private Control Rows(List<(string Key, string Value)> rows)
    {
        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(12 * _s));
        grid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(4 * _s));
        foreach (var (k, v) in rows)
        {
            grid.AddChild(HudKit.Label(k, 12 * _s, HudKit.Dim, condensed: true, bold: true));
            var value = HudKit.Label(v, 14 * _s, HudKit.Ink, condensed: true);
            value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.AddChild(value);
        }
        return grid;
    }

    private static string ClassTitle(GameData data, string classId) =>
        (data.PlanetClasses.FirstOrDefault(c => c.Id == classId)?.Name ?? classId.Replace('_', ' ')).ToUpperInvariant();

    private static string ShortClass(GameData data, string classId)
    {
        var name = data.PlanetClasses.FirstOrDefault(c => c.Id == classId)?.Name ?? classId.Replace('_', ' ');
        var dash = name.IndexOf(" —", System.StringComparison.Ordinal);
        return dash > 0 ? name[..dash] : name;
    }

    private static string ZoneName(string zone) => zone switch
    {
        "inner" => "inner system (hot)",
        "habitable" => "habitable zone",
        "outer" => "outer system (cold)",
        _ => zone,
    };

    private static string TierName(HabitabilityTier tier) => tier switch
    {
        HabitabilityTier.Open => "Open air",
        HabitabilityTier.Masks => "Breathing masks",
        HabitabilityTier.Suits => "Pressure suits",
        HabitabilityTier.Domes => "Domes",
        HabitabilityTier.Tech => "More technology",
        _ => "Uninhabitable",
    };

    private static string TierText(HabitabilityTier tier) => tier switch
    {
        HabitabilityTier.Open => "Breathable air and mild weather: crews work outside and buildings go anywhere. The easiest place to start.",
        HabitabilityTier.Masks => "Crews need breathing masks outside (trace gases or radiation); otherwise it builds like an open-air world.",
        HabitabilityTier.Suits => "Crews need pressure suits whenever they go outside.",
        HabitabilityTier.Domes => "The air can't be breathed: crewed buildings must stand inside the hub's containment field or a Pressure Dome, and need life support. A harder start, with slower industry.",
        HabitabilityTier.Tech => "Too hostile for a first colony without more technology.",
        _ => "Nothing could live here.",
    };

    private static string WhyNot(GameData data, BodyDef body)
    {
        if (SystemPlot.IsRocks(body.ClassId)) return "An asteroid belt: rocks and dust, nothing to set a colony on. Mining belts comes later.";
        if (body.ClassId is "class_j" or "class_t") return "A gas giant: no solid ground to land on. Its moons may be worth a look.";
        var landable = data.Surfaces.Select(s => ShortClass(data, s.ClassId)).Distinct().ToList();
        return $"No landing maps for {ShortClass(data, body.ClassId)} worlds yet: only {string.Join(" and ", landable)} worlds can be settled in this prototype.";
    }

    // ---------------------------------------------------------------- tutorial

    private TutorialStep TutorialStepFor(int step) => step switch
    {
        0 => new TutorialStep("menu_system", "A STAR SYSTEM",
            "Before you can build, you need somewhere to land. This is a newly charted star system: its star in the middle, planets on their orbits, moons beside them. Every visit charts a new one.",
            "1 / 3", _tutorialAnchor, "GOT IT"),
        1 => new TutorialStep("menu_landable", "FIND A WORLD YOU CAN LAND ON",
            "Worlds ringed in amber have ground a colony can stand on; usually there are only one or two. Gas giants, belts and most rocks can't be settled. Click a ringed world to read its card.",
            "2 / 3", _tutorialAnchor, null),
        _ => new TutorialStep("menu_land", "PICK YOUR STARTING POINT",
            "The card shows the world's class, its gravity and what your crews need to breathe. RECOMMENDED marks the easiest start. Press LAND HERE to begin, or REROLL for another system.",
            "3 / 3", _tutorialAnchor, "GOT IT"),
    };

    private bool TutorialOpen => _tutorial != null && GodotObject.IsInstanceValid(_tutorial);

    private void StartPickerTutorial()
    {
        if (TutorialOpen) return;
        _tutorialStep = 0;
        _tutorialAnchor = TutorialAnchor(0);
        _tutorial = TutorialCard.Open(_overlay, TutorialStepFor(0));
        _tutorial.Next += OnTutorialNext;
        _tutorial.SkipAll += OnTutorialSkip;
    }

    private void ShowTutorialStep(int step)
    {
        if (!TutorialOpen) return;
        _tutorialStep = step;
        _tutorialAnchor = TutorialAnchor(step);
        _tutorial!.Show(TutorialStepFor(step));
    }

    private void OnTutorialNext()
    {
        switch (_tutorialStep)
        {
            case 0:
                // Already picked a landable world (hints were turned on after the roll)? Then straight to the last card.
                ShowTutorialStep(Landable(_picked) ? 2 : 1);
                break;
            case 1:
                ShowTutorialStep(2);
                break;
            default:
                _pickerTutorialDone = true;
                CloseTutorial();
                Sfx.Ui("tutorial_done");
                break;
        }
    }

    private void OnTutorialSkip()
    {
        _pickerTutorialDone = true;
        GameSettings.SkipTutorials(); // owner 2026-11-11: a skip holds for every later game (Settings › Tutorial hints)
        CloseTutorial();
    }

    private void CloseTutorial()
    {
        if (TutorialOpen)
        {
            _tutorial!.Next -= OnTutorialNext;
            _tutorial.SkipAll -= OnTutorialSkip;
            _tutorial.Close();
        }
        _tutorial = null;
        _tutorialStep = -1;
    }

    /// <summary>What the current card points at, in viewport pixels: the plot, then the recommended (or first landable)
    /// world, then the LAND HERE button.</summary>
    private Rect2 TutorialAnchor(int step)
    {
        if (_map == null || !_map.IsInsideTree()) return new Rect2();
        switch (step)
        {
            case 0:
                return _map.GetGlobalRect();
            case 1:
                var target = _recommended ?? (Data is { } d ? _system?.Landable(d).FirstOrDefault()?.Id : null);
                return target != null && _map.BodyRect(target) is { } r ? r : _map.GetGlobalRect();
            default:
                return _landButton != null && _landButton.IsInsideTree() ? _landButton.GetGlobalRect() : new Rect2();
        }
    }

    /// <summary>Every frame on the picker: keep the tutorial card's arrow on its target (layout settles a frame after a
    /// rebuild; the window may be resized).</summary>
    private void UpdatePicker()
    {
        if (!TutorialOpen) return;
        var anchor = TutorialAnchor(_tutorialStep);
        if (anchor == _tutorialAnchor) return;
        _tutorialAnchor = anchor;
        _tutorial!.SetAnchor(anchor);
    }
}

/// <summary>
/// The picker's top-down plot of a generated system (2D, drawn in code): the star in the middle, a ring per orbit, belts
/// as rings of rubble, planets and moons as discs in their class colour (SystemPlot.ClassColour), lit from the star.
/// Landable bodies get a pulsing amber ring (the recommended one a second ring), the hovered one a light ring, the picked
/// one a cyan ring with ticks. Click picks (BodyPicked).
/// </summary>
public partial class SystemMap : Control
{
    private GameData? _data;
    private GeneratedSystem? _system;
    private string? _recommended;
    private string? _hovered;
    private double _time;

    /// <summary>The UI scale the page was built at (HudKit.ScaleFor).</summary>
    public float Scale1 { get; set; } = 1f;

    public string? Selected { get; set; }

    public event System.Action<string>? BodyPicked;

    private static readonly Color Amber = HudKit.Accent;

    public SystemMap()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        ClipContents = true;
        CustomMinimumSize = new Vector2(200, 200);
    }

    public void Setup(GameData data, GeneratedSystem system, string? recommended)
    {
        _data = data;
        _system = system;
        _recommended = recommended;
        QueueRedraw();
    }

    private bool IsLandable(BodyDef b) => _data != null && _data.IsLandableClass(b.ClassId);

    private float _scale = 1f;
    private Vector2 _centre;

    private void Layout()
    {
        _centre = Size / 2f;
        var maxR = 60f;
        if (_system != null)
            foreach (var b in _system.Bodies)
                maxR = Mathf.Max(maxR, new Vector2(b.Position.X.ToFloat(), b.Position.Y.ToFloat()).Length());
        var room = Mathf.Min(Size.X, Size.Y) / 2f - 34f * Scale1;
        _scale = Mathf.Max(0.05f, room / (maxR + 8f));
    }

    private Vector2 ScreenOf(BodyDef b) => _centre + new Vector2(b.Position.X.ToFloat(), -b.Position.Y.ToFloat()) * _scale;

    private float RadiusOf(BodyDef b) => (b.Parent == null ? 4f + b.Size * 1.7f : 2.5f + b.Size * 1.1f) * Scale1;

    /// <summary>A body's disc (with its rings' room) in viewport pixels, for the tutorial's arrow; null if not on the plot.</summary>
    public Rect2? BodyRect(string id)
    {
        if (_system?.Body(id) is not { } b) return null;
        Layout();
        var r = RadiusOf(b) + 8f * Scale1;
        var p = GetGlobalPosition() + ScreenOf(b);
        return new Rect2(p - new Vector2(r, r), new Vector2(r * 2f, r * 2f));
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (IsVisibleInTree()) QueueRedraw(); // the pulsing rings
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion m:
                var h = Hit(m.Position);
                if (h != _hovered)
                {
                    _hovered = h;
                    MouseDefaultCursorShape = h != null ? CursorShape.PointingHand : CursorShape.Arrow;
                    if (h != null) Sfx.Ui("hover");
                    TooltipText = h != null && _system?.Body(h) is { } hb ? hb.Name : "";
                }
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b:
                if (Hit(b.Position) is { } id)
                {
                    Selected = id;
                    BodyPicked?.Invoke(id);
                    Sfx.Ui("click");
                    AcceptEvent();
                }
                break;
        }
    }

    /// <summary>The body under a point (local pixels): the nearest within its disc plus a margin; moons win over their planet.</summary>
    private string? Hit(Vector2 local)
    {
        if (_system == null) return null;
        Layout();
        string? best = null;
        var bestD = float.MaxValue;
        foreach (var b in _system.Bodies)
        {
            var d = ScreenOf(b).DistanceTo(local);
            var reach = Mathf.Max(RadiusOf(b) + 6f * Scale1, 12f * Scale1);
            if (d <= reach && d < bestD)
            {
                bestD = d;
                best = b.Id;
            }
        }
        return best;
    }

    public override void _Draw()
    {
        if (_system == null) return;
        Layout();
        var s = Scale1;
        var font = HudKit.Condensed();
        var bold = HudKit.Condensed(true);
        var fontSize = Mathf.RoundToInt(12 * s);
        var pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 3f);

        // Orbits and belts.
        foreach (var b in _system.Bodies.Where(x => x.Parent == null))
        {
            var r = ScreenOf(b).DistanceTo(_centre);
            if (SystemPlot.IsRocks(b.ClassId))
            {
                // Rubble from a small fixed hash of the body id (same dots every frame, no allocation).
                var h = 2166136261u;
                foreach (var ch in b.Id) h = (h ^ ch) * 16777619u;
                float Next()
                {
                    h ^= h << 13;
                    h ^= h >> 17;
                    h ^= h << 5;
                    return (h & 0xffffff) / 16777216f;
                }
                for (var i = 0; i < 160; i++)
                {
                    var a = Next() * Mathf.Tau;
                    var rr = r + (Next() * 14f - 7f) * s;
                    DrawCircle(_centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr, (0.6f + Next()) * s, new Color(0.62f, 0.58f, 0.52f, 0.55f));
                }
            }
            else DrawArc(_centre, r, 0f, Mathf.Tau, 160, new Color(HudKit.Line, 0.95f), 1f, true);
        }
        foreach (var m in _system.Bodies.Where(x => x.Parent != null))
            if (_system.Body(m.Parent!) is { } parent)
                DrawArc(ScreenOf(parent), ScreenOf(m).DistanceTo(ScreenOf(parent)), 0f, Mathf.Tau, 64, new Color(HudKit.Line, 0.6f), 1f, true);

        // The star.
        var starColour = _system.Star.Class switch
        {
            "K" => new Color(1f, 0.74f, 0.44f),
            "M" => new Color(1f, 0.52f, 0.38f),
            "F" => new Color(0.94f, 0.96f, 1f),
            _ => new Color(1f, 0.92f, 0.68f),
        };
        for (var i = 5; i >= 1; i--) DrawCircle(_centre, (12f + i * 7f) * s, new Color(starColour, 0.05f));
        DrawCircle(_centre, 12f * s, starColour);
        DrawCircle(_centre, 7f * s, new Color(1f, 1f, 0.95f));

        // Bodies.
        foreach (var b in _system.Bodies)
        {
            var p = ScreenOf(b);
            var r = RadiusOf(b);
            var landable = IsLandable(b);
            var rocks = SystemPlot.IsRocks(b.ClassId);
            if (!rocks)
            {
                var colour = SystemPlot.ClassColour(b.ClassId);
                DrawCircle(p, r, colour.Darkened(0.62f));
                var toStar = (_centre - p).Normalized();
                if (toStar == Vector2.Zero) toStar = Vector2.Left;
                DrawCircle(p + toStar * r * 0.22f, r * 0.78f, colour);
                if (b.ClassId is "class_j" or "class_t") DrawLine(p + new Vector2(-r * 0.7f, r * 0.15f), p + new Vector2(r * 0.7f, r * 0.15f), colour.Darkened(0.25f), Mathf.Max(1f, r * 0.18f));
            }
            else DrawCircle(p, 3f * s, new Color(0.75f, 0.7f, 0.62f));

            if (landable)
            {
                DrawArc(p, r + (5f + 2f * pulse) * s, 0f, Mathf.Tau, 48, new Color(Amber, 0.55f + 0.45f * pulse), 2f * s, true);
                if (b.Id == _recommended) DrawArc(p, r + 11f * s, 0f, Mathf.Tau, 48, new Color(HudKit.Good, 0.6f), 1.2f * s, true);
            }
            if (b.Id == _hovered && b.Id != Selected) DrawArc(p, r + 4f * s, 0f, Mathf.Tau, 48, new Color(HudKit.Ink, 0.8f), 1.5f * s, true);
            if (b.Id == Selected)
            {
                var rr = r + 9f * s;
                DrawArc(p, rr, 0f, Mathf.Tau, 48, SystemPlot.Cyan, 2.2f * s, true);
                for (var k = 0; k < 4; k++)
                {
                    var d = Vector2.Right.Rotated(k * Mathf.Pi / 2f);
                    DrawLine(p + d * (rr + 2f * s), p + d * (rr + 8f * s), SystemPlot.Cyan, 2f * s);
                }
            }

            // Name under the disc (moons: only when hovered, picked or landable, so the plot stays readable).
            var showName = b.Parent == null || landable || b.Id == _hovered || b.Id == Selected;
            if (!showName) continue;
            var text = b.Name.ToUpperInvariant();
            var f = landable ? bold : font;
            var size = f.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
            var at = p + new Vector2(-size.X / 2f, r + (landable ? 18f : 6f) * s + size.Y * 0.75f);
            var ink = b.Id == Selected ? SystemPlot.Cyan : landable ? Amber : new Color(HudKit.Dim, 0.85f);
            DrawString(f, at + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, fontSize, new Color(0, 0, 0, 0.7f));
            DrawString(f, at, text, HorizontalAlignment.Left, -1, fontSize, ink);
            if (b.Id == _recommended)
            {
                var tag = "RECOMMENDED";
                var tagSize = Mathf.RoundToInt(10 * s);
                var ts = bold.GetStringSize(tag, HorizontalAlignment.Left, -1, tagSize);
                DrawString(bold, p + new Vector2(-ts.X / 2f, r + 18f * s + size.Y * 0.75f + ts.Y), tag, HorizontalAlignment.Left, -1, tagSize, HudKit.Good);
            }
        }
    }
}
