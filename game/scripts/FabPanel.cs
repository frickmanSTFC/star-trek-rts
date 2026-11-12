using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FinalFrontier;

/// <summary>One input of a recipe or order: what (resource / item id, for its picture), its name, how many, short = red.</summary>
public sealed record FabCost(string ResourceId, string Name, int Amount, int Have, bool Short);

/// <summary>A recipe tile: item id, full name, number key ("7", "0", "" = none), tier colour, inputs, how many of it are
/// queued here, whether it can be made right now (false = greyed out, still clickable), and its hover text.</summary>
/// Picture = an <see cref="ItemArt.ForKey"/> key ("" = the item id; "vehicle:&lt;id&gt;" draws a 4:3 render across the
/// tile's top); Meta = a dim line under the cost chips ("20 s · speed 2 · survey 2").</summary>
public sealed record FabTileView(string Id, string Name, string Key, Color Colour, IReadOnlyList<FabCost> Cost, int Queued, bool CanMake, string Tip,
    string Picture = "", string Meta = "");

/// <summary>A queued order: its id (×), item, inputs, why it can't start (red; empty = it can), time ("0:15", "0:07 left"),
/// running progress (below 0 = not started), a tag ("keep in stock") and its picture key ("" = the item id).</summary>
public sealed record FabQueueView(int Id, string ItemId, string Name, Color Colour, IReadOnlyList<FabCost> Cost, string Reason, string Time, float Progress, string Tag,
    string Picture = "");

/// <summary>An Overview fact: caption above its value; Tone colours the value (null = ink).</summary>
public sealed record FabFactView(string Label, string Value, Color? Tone = null);

/// <summary>A tech tile on a Research Lab's Tech tree / Done tab: id (sent on click), name, picture key, Line = its first
/// effect or "needs …" (Red), Status at the right ("✓ DONE", "48%", "LOCKED", "80 PTS") in StatusColour; Done / Active
/// edge colours, Locked = greyed out (still clickable), Linked = draw the connector to the column on its left, Tag
/// (a small "RUINS" pill), Key = its number key, Tip = hover text.</summary>
public sealed record TechTileView(string Id, string Name, string Picture, string Line, bool Red, string Status, Color StatusColour,
    bool Done, bool Active, bool Locked, bool Linked, string Tag, string Key, string Tip);

/// <summary>One column of the tech tree: its caption and tiles.</summary>
public sealed record TechColumnView(string Title, IReadOnlyList<TechTileView> Techs);

/// <summary>
/// A selected building with a tabbed bottom panel (owner 2026-11-12 picked option 2, "Tabs", of
/// docs/mockups/machine-shop-panel, then approved docs/mockups/bay-lab-panel for the Vehicle Bay and Research Lab): on the
/// left the name, level, status line (with a progress bar when StatusProgress ≥ 0) and the buttons; on the right the
/// tabs. Kind "fab" (Machine Shop / core workshop: Overview, Queue · n, Recipes · n), "bay" (Overview, Queue · n,
/// Vehicles · n: the same queue rows and tiles), "lab" (Overview, Tech tree · n = Tree's columns, Done · n = Done).
/// Tabs = (id, label) with ids overview / queue / recipes / tree / done (null = the Machine Shop's three).
/// Shorts = every queued item that can't start with its full reason.
/// </summary>
public sealed record FabPanelView(
    string Title,
    string Subtitle,
    string IconId,
    string Status,
    Color StatusColour,
    IReadOnlyList<ActionView> Actions,
    IReadOnlyList<FabFactView> Facts,
    IReadOnlyList<(string Name, string Reason)> Shorts,
    IReadOnlyList<FabQueueView> Queue,
    string QueueNote,
    IReadOnlyList<FabTileView> Recipes,
    string RecipeNote,
    bool Workshop,
    string Kind = "fab",
    IReadOnlyList<(string Id, string Label)>? Tabs = null,
    IReadOnlyList<TechColumnView>? Tree = null,
    IReadOnlyList<TechTileView>? Done = null,
    float StatusProgress = -1f);

/// <summary>
/// The tabbed Machine Shop panel (see <see cref="FabPanelView"/>). It takes the bottom panel's centre and command card
/// (PlanetHud.ShowFabPanel); the HUD makes the bottom panel as tall as <see cref="NeededHeight"/>. Sizes come from
/// <see cref="HudKit.ScaleFor"/> (rebuilt when the screen or the UI scale changes). Nothing is clipped or cut mid-word:
/// captions sit above values, long text wraps between words, tile names are wrapped here (two lines at most, the font
/// shrinks a little rather than break a word), and each tab scrolls if it holds more than fits.
/// Recipe tiles: a left click opens the item's card, Shift+click queues 5, Ctrl+click queues 1, a right click removes
/// one queued order of it; tiles you can't make right now are dimmed and desaturated (still clickable).
/// The same panel shows a selected Vehicle Bay (vehicle tiles with their pictures, same clicks) and Research Lab (a
/// three-column tech tree and a Done tab of tech tiles; a click opens the tech's card): see <see cref="FabPanelView"/>.
/// </summary>
public partial class FabPanel : MarginContainer
{
    /// <summary>The tab that shows per panel kind (remembered for the session; Recipes / Vehicles / Tech tree at first).</summary>
    private static readonly Dictionary<string, string> LastTabs = new();

    private static string DefaultTab(string kind) => kind == "lab" ? "tree" : "recipes";

    private string LastTab
    {
        get => LastTabs.GetValueOrDefault(_kind, DefaultTab(_kind));
        set => LastTabs[_kind] = value;
    }

    /// <summary>A recipe / vehicle / tech tile: (id, mode) with mode 0 = open its card, 1 / 5 = queue that many, -1 = remove one.</summary>
    public event Action<string, int>? RecipePressed;
    public event Action<int>? CancelPressed;
    public event Action<string>? ActionPressed;
    public event Action? InventoryPressed;
    public event Action? WorkshopPressed;

    private float _width = 1000; // the room right of the minimap

    private float _s = 1;
    private Vector2 _builtFor;
    private float _builtScale;
    private float _rightW;
    private int _columns = 6;
    private float _tileW, _tileH, _bodyH;
    private float _nameSize;

    private int _shown;
    private string _kind = "fab";
    private bool _wideTiles;
    private float _metaSize;
    private int _metaLines;
    private float _picH;

    // the lab's tech tree and Done tab
    private ScrollContainer _treeScroll = null!, _doneScroll = null!;
    private HBoxContainer _treeRow = null!;
    private GridContainer _doneGrid = null!;
    private readonly List<TechTile> _techTiles = new();
    private readonly List<TechTile> _doneTiles = new();
    private string _treeSignature = "";
    private Bar _statusBar = null!;

    /// <summary>Shown for a few frames since its last rebuild, so wrapped labels have their widths and its minimum size is real.</summary>
    public bool LaidOut => _shown > 2;

    /// <summary>The bottom panel's height this panel needs (border included).</summary>
    public float NeededHeight { get; private set; } = PlanetHud.BottomHeight;

    // left
    private TextureRect _icon = null!;
    private Label _title = null!, _sub = null!, _status = null!;
    private readonly List<Button> _actions = new();
    private readonly List<string> _actionIds = new();
    // header
    private readonly Dictionary<string, Button> _tabs = new();
    private Button _workshopChip = null!, _inventoryChip = null!;
    // bodies
    private ScrollContainer _overviewScroll = null!, _queueScroll = null!, _recipesScroll = null!;
    private GridContainer _facts = null!;
    private readonly List<(Label Caption, Label Value)> _factCells = new();
    private Label _shortCaption = null!;
    private RichTextLabel _shorts = null!;
    private VBoxContainer _queueList = null!;
    private Label _queueNote = null!;
    private readonly List<QueueRow> _queueRows = new();
    private GridContainer _grid = null!;
    private Label _recipeNote = null!;
    private readonly List<Tile> _tiles = new();
    private string _tileSignature = "";
    private string? _hoveredTile;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    /// <summary>Fills the panel (rebuilding the skeleton when the screen size or UI scale changed).</summary>
    /// <param name="width">The room right of the minimap (the screen width minus its frame's real width).</param>
    public void Show(FabPanelView v, Vector2 screen, float width)
    {
        var s = HudKit.ScaleFor(screen);
        var signature = string.Join(",", v.Recipes.Select(r => r.Id + ":" + r.Name + ":" + r.Cost.Count + ":" + r.Meta));
        var treeSignature = TreeSignature(v);
        if (screen != _builtFor || !Mathf.IsEqualApprox(width, _width) || !Mathf.IsEqualApprox(s, _builtScale) || _icon == null || v.Kind != _kind
            || string.Join(",", Tabs(v).Select(t => t.Id)) != string.Join(",", _tabs.Keys))
        {
            _width = width;
            _kind = v.Kind;
            Build(screen, s, v);
            _tileSignature = "";
            _treeSignature = "";
        }
        _shown = _icon == null || _shown > 1000 ? _shown : _shown + 1;
        if (signature != _tileSignature || treeSignature != _treeSignature)
        {
            _shown = 0;
            Measure(v);
            BuildTiles(v);
            BuildTree(v);
            _tileSignature = signature;
            _treeSignature = treeSignature;
        }
        Fill(v);
    }

    // ---------------------------------------------------------------- skeleton

    private void Build(Vector2 screen, float s, FabPanelView v)
    {
        _builtFor = screen;
        _builtScale = s;
        _s = s;
        HudKit.Clear(this);
        _actions.Clear();
        _actionIds.Clear();
        _tabs.Clear();
        _factCells.Clear();
        _queueRows.Clear();
        _tiles.Clear();
        _techTiles.Clear();
        _doneTiles.Clear();
        _wideTiles = v.Kind == "bay";

        foreach (var side in new[] { "left", "right" }) AddThemeConstantOverride($"margin_{side}", Mathf.RoundToInt(16 * s));
        AddThemeConstantOverride("margin_top", Mathf.RoundToInt(12 * s));
        AddThemeConstantOverride("margin_bottom", Mathf.RoundToInt(10 * s));

        var leftW = Mathf.Round(250 * s);
        var gap = Mathf.Round(18 * s);
        _rightW = Mathf.Max(200 * s, _width - 32 * s - leftW - gap);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(gap));
        AddChild(row);

        // ---------------- left: name, level, status, buttons
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(leftW, 0), MouseFilter = MouseFilterEnum.Ignore };
        left.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * s));
        _icon = new TextureRect { CustomMinimumSize = new Vector2(40, 40) * s, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        head.AddChild(_icon);
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", 0);
        _title = HudKit.Label("", 21 * s, HudKit.Ink, bold: true);
        _title.AutowrapMode = TextServer.AutowrapMode.Word;
        _title.CustomMinimumSize = new Vector2(leftW - 50 * s, 0);
        _sub = HudKit.Label("", 13 * s, HudKit.Dim);
        _sub.AutowrapMode = TextServer.AutowrapMode.Word;
        _sub.CustomMinimumSize = new Vector2(leftW - 50 * s, 0);
        names.AddChild(_title);
        names.AddChild(_sub);
        head.AddChild(names);
        left.AddChild(head);
        _status = HudKit.Label("", 14 * s, HudKit.Accent);
        _status.AutowrapMode = TextServer.AutowrapMode.Word;
        _status.CustomMinimumSize = new Vector2(leftW, 0);
        left.AddChild(_status);
        _statusBar = new Bar { CustomMinimumSize = new Vector2(leftW, 6 * s), Visible = false };
        left.AddChild(_statusBar);
        for (var i = 0; i < 4; i++) // upgrade, maintain, pause, salvage / jury-rig
        {
            var b = new Button
            {
                FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(leftW, 28 * s), Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.Word, Visible = false,
            };
            b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(13 * s));
            HudKit.StyleTile(b, 5 * s, 0);
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
                if (b.GetThemeStylebox(state) is StyleBoxFlat f)
                {
                    f.ContentMarginLeft = f.ContentMarginRight = 9 * s;
                    f.ContentMarginTop = f.ContentMarginBottom = 3 * s;
                }
            var index = i;
            b.Pressed += () => ActionPressed?.Invoke(_actionIds[index]);
            _actions.Add(b);
            _actionIds.Add("");
            left.AddChild(b);
        }
        row.AddChild(left);

        // ---------------- right: tabs + bodies
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        right.AddThemeConstantOverride("separation", 0);
        var header = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        header.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * s));
        foreach (var (id, _) in Tabs(v))
        {
            var tab = new Button { ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30 * s) };
            tab.AddThemeFontOverride("font", HudKit.Condensed(true));
            tab.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(15 * s));
            StyleTab(tab, s);
            var captured = id;
            tab.Pressed += () =>
            {
                LastTab = captured;
                Sfx.Ui("click");
                ShowTab();
            };
            header.AddChild(tab);
            _tabs[id] = tab;
        }
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _workshopChip = Chip("WORKSHOP [K]", "machine_shop", "Back to the base panel (K)", s);
        _workshopChip.Pressed += () => WorkshopPressed?.Invoke();
        header.AddChild(_workshopChip);
        _inventoryChip = Chip("INVENTORY [I]", "parts", "Parts, spare kits and construction parts: stock, keep-in-stock targets, storage (I)", s);
        _inventoryChip.Pressed += () => InventoryPressed?.Invoke();
        header.AddChild(_inventoryChip);
        right.AddChild(header);
        right.AddChild(new ColorRect { Color = new Color(0.2f, 0.25f, 0.37f), CustomMinimumSize = new Vector2(0, Mathf.Max(1, s)), MouseFilter = MouseFilterEnum.Ignore });
        right.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8 * s), MouseFilter = MouseFilterEnum.Ignore });

        // Overview
        _overviewScroll = Scroll();
        var overview = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        overview.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
        _facts = new GridContainer { Columns = _rightW >= 600 * s ? 3 : 2, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        _facts.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(16 * s));
        _facts.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(7 * s));
        overview.AddChild(_facts);
        _shortCaption = HudKit.Label("SHORT", 12 * s, HudKit.Dim, condensed: true);
        overview.AddChild(_shortCaption);
        _shorts = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Word,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore,
        };
        _shorts.AddThemeFontSizeOverride("normal_font_size", Mathf.RoundToInt(14 * s));
        _shorts.AddThemeFontSizeOverride("bold_font_size", Mathf.RoundToInt(14 * s));
        _shorts.AddThemeColorOverride("default_color", HudKit.Ink);
        overview.AddChild(_shorts);
        _overviewScroll.AddChild(overview);
        right.AddChild(_overviewScroll);

        // Queue
        _queueScroll = Scroll();
        _queueList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        _queueList.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
        _queueNote = HudKit.Label("", 13 * s, HudKit.Dim);
        _queueNote.AutowrapMode = TextServer.AutowrapMode.Word;
        _queueNote.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _queueList.AddChild(_queueNote);
        _queueScroll.AddChild(_queueList);
        right.AddChild(_queueScroll);

        // Recipes
        _recipesScroll = Scroll();
        var recipes = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        recipes.AddThemeConstantOverride("separation", Mathf.RoundToInt(5 * s));
        _grid = new GridContainer { MouseFilter = MouseFilterEnum.Pass };
        _grid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(7 * s));
        _grid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(7 * s));
        recipes.AddChild(_grid);
        _recipeNote = HudKit.Label("", 12 * s, HudKit.Dim);
        _recipeNote.AutowrapMode = TextServer.AutowrapMode.Word;
        _recipeNote.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        recipes.AddChild(_recipeNote);
        _recipesScroll.AddChild(recipes);
        right.AddChild(_recipesScroll);

        // Tech tree (the lab): three columns side by side; Done: tech tiles in two columns.
        _treeScroll = Scroll();
        _treeRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        _treeRow.AddThemeConstantOverride("separation", 0);
        _treeScroll.AddChild(_treeRow);
        right.AddChild(_treeScroll);
        _doneScroll = Scroll();
        _doneGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        _doneGrid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(14 * s));
        _doneGrid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(7 * s));
        _doneScroll.AddChild(_doneGrid);
        right.AddChild(_doneScroll);

        row.AddChild(right);
        Measure(v);
    }

    /// <summary>The panel's tabs: the view's own, else the Machine Shop's three.</summary>
    private static IReadOnlyList<(string Id, string Label)> Tabs(FabPanelView v) =>
        v.Tabs ?? new[] { ("overview", "OVERVIEW"), ("queue", "QUEUE"), ("recipes", "RECIPES") };

    private ScrollContainer Scroll() => new()
    {
        HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        MouseFilter = MouseFilterEnum.Pass,
        FollowFocus = false,
    };

    private static void StyleTab(Button tab, float s)
    {
        StyleBoxFlat Box(Color edge, Color bg)
        {
            var b = HudKit.Box(bg, edge, 0, 14 * s, 6 * s, 0);
            b.BorderWidthBottom = Mathf.Max(2, Mathf.RoundToInt(3 * s));
            return b;
        }
        var none = new Color(0, 0, 0, 0);
        tab.AddThemeStyleboxOverride("normal", Box(none, none));
        tab.AddThemeStyleboxOverride("hover", Box(HudKit.HoverEdge, new Color(0.06f, 0.09f, 0.16f)));
        tab.AddThemeStyleboxOverride("pressed", Box(HudKit.Accent, none));
        tab.AddThemeStyleboxOverride("hover_pressed", Box(HudKit.Accent, new Color(0.06f, 0.09f, 0.16f)));
        tab.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        tab.AddThemeColorOverride("font_color", HudKit.Dim);
        tab.AddThemeColorOverride("font_hover_color", HudKit.Ink);
        tab.AddThemeColorOverride("font_pressed_color", HudKit.Ink);
        tab.AddThemeColorOverride("font_hover_pressed_color", HudKit.Ink);
    }

    private static Button Chip(string text, string icon, string tip, float s)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 26 * s), TooltipText = tip, Icon = Icons.Get(icon), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        b.AddThemeFontOverride("font", HudKit.Condensed(true));
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * s));
        b.AddThemeConstantOverride("icon_max_width", Mathf.RoundToInt(15 * s));
        HudKit.StyleTile(b, 5 * s, 6 * s);
        return b;
    }

    // ---------------------------------------------------------------- sizes

    // A label line: the font height plus the theme's line spacing (and a pixel), so two-line names never overrun the tile.
    private float LineHeight(float size) => HudKit.Condensed(true).GetHeight(Mathf.RoundToInt(size)) + 4;

    /// <summary>Columns, tile size, the tab bodies' height and the panel height, from the screen and the recipes.</summary>
    private void Measure(FabPanelView v)
    {
        var s = _s;
        var gap = Mathf.Round(7 * s);
        var n = Mathf.Max(1, v.Recipes.Count);
        // Vehicle tiles carry a 4:3 picture across the top, so they want a little more width.
        _columns = Mathf.Clamp(Mathf.FloorToInt((_rightW + gap) / ((_wideTiles ? 120 : 96) * s + gap)), 2, 7);
        var rows = Mathf.CeilToInt(n / (float)_columns);
        var width = _rightW - (rows > 2 ? 16 * s : 0) - 2; // room for the scroll bar when more than two rows
        _tileW = Mathf.Floor((width - (_columns - 1) * gap) / _columns);
        var pad = 6 * s;
        var inner = _tileW - pad * 2;
        // Tile names: the largest size (down to 75%) at which every name fits two lines without breaking a word.
        _nameSize = (_wideTiles ? 15f : 13.5f) * s;
        while (_nameSize > 10f * s && v.Recipes.Any(r => Wrap(r.Name, _nameSize, inner).Lines > 2)) _nameSize -= 0.5f;
        // The dim line under the chips (vehicles: time · speed · survey): every word kept, as many lines as it takes.
        _metaSize = 12f * s;
        _metaLines = 0;
        foreach (var r in v.Recipes)
            if (r.Meta.Length > 0) _metaLines = Mathf.Max(_metaLines, Mathf.Min(4, Wrap(r.Meta, _metaSize, inner, regular: true).Lines));
        var chipRows = 1;
        foreach (var r in v.Recipes) chipRows = Mathf.Max(chipRows, ChipRows(r.Cost, inner));
        var chipH = ChipHeight();
        _picH = _wideTiles ? Mathf.Floor(inner * 0.75f) : 36 * s;
        _tileH = Mathf.Ceil(pad * 2 + _picH + 3 * s + 2 * LineHeight(_nameSize) + 4 * s + chipRows * chipH + (chipRows - 1) * 3 * s + 4 * s
                            + (_metaLines > 0 ? _metaLines * LineHeight(_metaSize) + 3 * s : 0));
        var shown = Mathf.Clamp(rows, 1, 2);
        _bodyH = Mathf.Ceil(shown * _tileH + (shown - 1) * gap + (v.RecipeNote.Length > 0 ? 18 * s : 0));
        if (v.Tree != null) _bodyH = Mathf.Max(Mathf.Min(TreeHeight(v), 300 * s), 150 * s);
        var headerH = 30 * s + Mathf.Max(1, s) + 8 * s;
        var rightH = 22 * s + headerH + _bodyH;
        var leftH = 22 * s + 42 * s + 4 * s + 2 * 19 * s + 4 * (34 * s + 4 * s);
        NeededHeight = Mathf.Ceil(Mathf.Max(PlanetHud.BottomHeight, Mathf.Max(rightH, leftH) + 2));
        foreach (var sc in new[] { _overviewScroll, _queueScroll, _recipesScroll, _treeScroll, _doneScroll })
            if (sc != null) sc.CustomMinimumSize = new Vector2(0, Mathf.Max(_bodyH, leftH - 22 * s - headerH));
        if (_grid != null) _grid.Columns = _columns;
    }

    /// <summary>Greedy word wrap of a name at a font size: the text with line breaks, its line count (a word wider than
    /// the width counts as 99 lines, so it is never broken inside).</summary>
    private static (string Text, int Lines) Wrap(string name, float size, float width, bool regular = false)
    {
        var font = regular ? ThemeDB.FallbackFont : HudKit.Condensed(true);
        var px = Mathf.RoundToInt(size);
        float W(string t) => font.GetStringSize(t, HorizontalAlignment.Left, -1, px).X;
        var lines = new List<string>();
        var line = "";
        foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (W(word) > width) return (name, 99);
            var tryLine = line.Length == 0 ? word : line + " " + word;
            if (W(tryLine) <= width) line = tryLine;
            else
            {
                lines.Add(line);
                line = word;
            }
        }
        if (line.Length > 0) lines.Add(line);
        return (string.Join("\n", lines), lines.Count);
    }

    private float ChipFont => 12f * _s;
    private float ChipIcon => 15f * _s;
    private float ChipHeight() => Mathf.Ceil(Mathf.Max(ChipIcon, HudKit.Condensed().GetHeight(Mathf.RoundToInt(ChipFont))) + 3 * _s);

    private float ChipWidth(FabCost c) =>
        2 * 4 * _s + ChipIcon + 3 * _s + HudKit.Condensed().GetStringSize(c.Amount.ToString(), HorizontalAlignment.Left, -1, Mathf.RoundToInt(ChipFont)).X + 2;

    private int ChipRows(IReadOnlyList<FabCost> cost, float width)
    {
        var rows = 1;
        var x = 0f;
        foreach (var c in cost)
        {
            var w = ChipWidth(c);
            if (x > 0 && x + 3 * _s + w > width)
            {
                rows++;
                x = w;
            }
            else x += (x > 0 ? 3 * _s : 0) + w;
        }
        return rows;
    }

    // ---------------------------------------------------------------- tiles

    private sealed class Tile
    {
        public string Id = "";
        public Button Root = null!;
        public TextureRect Picture = null!;
        public Label Name = null!, Key = null!, Badge = null!;
        public Label? Meta;
        public PanelContainer BadgeBox = null!;
        public HFlowContainer Cost = null!;
        public string CostSignature = "";
        public string PictureKey = "";
        public bool HasPicture;
        public bool? CanMake;
    }

    private void BuildTiles(FabPanelView v)
    {
        HudKit.Clear(_grid);
        _tiles.Clear();
        _grid.Columns = _columns;
        var s = _s;
        var pad = 6 * s;
        foreach (var r in v.Recipes)
        {
            var t = new Tile { Id = r.Id };
            var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(_tileW, _tileH), MouseFilter = MouseFilterEnum.Stop };
            HudKit.StyleTile(b, 7 * s);
            var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
            box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, Mathf.RoundToInt(pad));
            var pictureKey = r.Picture.Length > 0 ? r.Picture : r.Id;
            t.Picture = new TextureRect
            {
                CustomMinimumSize = _wideTiles ? new Vector2(_tileW - pad * 2, _picH) : new Vector2(36, 36) * s, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = _wideTiles ? TextureRect.StretchModeEnum.KeepAspectCovered : TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps, ClipContents = _wideTiles,
                MouseFilter = MouseFilterEnum.Ignore, Texture = ItemArt.ForKey(pictureKey, r.Colour),
            };
            t.PictureKey = pictureKey;
            t.HasPicture = ItemArt.HasPicture(pictureKey);
            t.Key = HudKit.Label(r.Key, 12 * s, _wideTiles ? HudKit.Ink : HudKit.Dim, condensed: true);
            t.BadgeBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
            t.BadgeBox.AddThemeStyleboxOverride("panel", HudKit.Box(HudKit.Accent, HudKit.Accent, 9 * s, 5 * s, 0));
            t.Badge = HudKit.Label("", 12 * s, new Color(0.06f, 0.07f, 0.1f), condensed: true, bold: true);
            t.BadgeBox.AddChild(t.Badge);
            if (_wideTiles)
            {
                // The vehicle's picture across the top, its hotkey in the top-left corner and ×n in the top-right.
                var holder = new Control { CustomMinimumSize = t.Picture.CustomMinimumSize, MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
                holder.AddChild(t.Picture);
                t.Picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                var keyBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(4, 4) * s };
                keyBox.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.04f, 0.063f, 0.114f, 0.85f), HudKit.Line, 4 * s, 5 * s, 1 * s));
                keyBox.AddChild(t.Key);
                keyBox.Visible = r.Key.Length > 0;
                holder.AddChild(keyBox);
                holder.AddChild(t.BadgeBox);
                t.BadgeBox.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
                t.BadgeBox.GrowHorizontal = GrowDirection.Begin;
                t.BadgeBox.OffsetRight = -4 * s;
                t.BadgeBox.OffsetTop = 4 * s;
                box.AddChild(holder);
            }
            else
            {
                var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                top.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
                top.AddChild(t.Picture);
                top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
                var right = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                right.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
                t.Key.HorizontalAlignment = HorizontalAlignment.Right;
                right.AddChild(t.Key);
                right.AddChild(t.BadgeBox);
                top.AddChild(right);
                box.AddChild(top);
            }
            var (text, _) = Wrap(r.Name, _nameSize, _tileW - pad * 2);
            t.Name = HudKit.Label(text, _nameSize, HudKit.Ink, bold: true);
            t.Name.CustomMinimumSize = new Vector2(0, 2 * LineHeight(_nameSize));
            t.Name.VerticalAlignment = VerticalAlignment.Top;
            box.AddChild(t.Name);
            t.Cost = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
            t.Cost.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(3 * s));
            t.Cost.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(3 * s));
            box.AddChild(t.Cost);
            if (_metaLines > 0)
            {
                var (meta, lines) = Wrap(r.Meta, _metaSize, _tileW - pad * 2, regular: true);
                t.Meta = HudKit.Label(lines > 4 ? r.Meta : meta, _metaSize, HudKit.Dim);
                if (lines > 4) t.Meta.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                box.AddChild(t.Meta);
            }
            b.AddChild(box);
            var id = r.Id;
            b.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true } mb) return;
                if (mb.ButtonIndex == MouseButton.Left) RecipePressed?.Invoke(id, mb.ShiftPressed ? 5 : mb.CtrlPressed || mb.MetaPressed ? 1 : 0);
                else if (mb.ButtonIndex == MouseButton.Right) RecipePressed?.Invoke(id, -1);
                else return;
                b.AcceptEvent();
            };
            b.MouseEntered += () =>
            {
                _hoveredTile = id;
                Dim(t);
            };
            b.MouseExited += () =>
            {
                if (_hoveredTile == id) _hoveredTile = null;
                Dim(t);
            };
            t.Root = b;
            _grid.AddChild(b);
            _tiles.Add(t);
        }
    }

    private void Dim(Tile t)
    {
        var can = t.CanMake ?? true;
        t.Root.Modulate = can ? Colors.White : new Color(1, 1, 1, _hoveredTile == t.Id ? 0.75f : 0.52f);
        t.Picture.Material = can ? null : ItemArt.Greyed(0.65f);
    }

    /// <summary>One cost chip: the item's picture (or the resource icon) and a text ("2", or "2 Mechanism"); red when short.</summary>
    private PanelContainer CostChip(FabCost c, string text, float s)
    {
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", c.Short
            ? HudKit.Box(new Color(0.17f, 0.08f, 0.08f), new Color(0.93f, 0.42f, 0.36f, 0.6f), 4 * s, 4 * s, 1 * s)
            : HudKit.Box(new Color(0.09f, 0.13f, 0.22f), HudKit.Line, 4 * s, 4 * s, 1 * s));
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
        row.AddChild(new TextureRect
        {
            Texture = ItemArt.Picture(c.ResourceId) ?? Icons.Get(c.ResourceId), CustomMinimumSize = new Vector2(ChipIcon, ChipIcon),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore,
        });
        row.AddChild(HudKit.Label(text, ChipFont, c.Short ? new Color(1f, 0.7f, 0.66f) : HudKit.Ink, condensed: true));
        chip.AddChild(row);
        return chip;
    }

    // ---------------------------------------------------------------- the lab's tech tree

    private sealed class TechTile
    {
        public string Id = "";
        public PanelContainer Root = null!;
        public TextureRect Picture = null!;
        public Label Name = null!, Line = null!, Status = null!;
        public PanelContainer? TagBox;
        public ColorRect? Connector;
        public bool Hovered;
        public string Look = "";
        public bool? Locked;
    }

    private static string TreeSignature(FabPanelView v) =>
        v.Tree == null ? "" : string.Join("|", v.Tree.Select(c => c.Title + ":" + string.Join(",", c.Techs.Select(t => $"{t.Id}/{t.Name}/{t.Linked}/{t.Tag}/{t.Picture}"))))
            + "#" + string.Join(",", (v.Done ?? Array.Empty<TechTileView>()).Select(t => $"{t.Id}/{t.Name}/{t.Tag}"));

    private float TreeGap => Mathf.Round(16 * _s);
    private float TreeColumn => Mathf.Floor((_rightW - 16 * _s - 2 * TreeGap) / 3);
    private float TechPicW => Mathf.Round(56 * _s);
    private float TechPicH => Mathf.Round(42 * _s);

    /// <summary>The tallest column of the tree as laid out (an estimate from word-wrapping each tile's texts).</summary>
    private float TreeHeight(FabPanelView v)
    {
        if (v.Tree == null) return 0;
        var s = _s;
        var text = TreeColumn - 2 * 7 * s - TechPicW - 9 * s;
        var tallest = 0f;
        foreach (var column in v.Tree)
        {
            var h = LineHeight(12 * s) + 6 * s;
            foreach (var t in column.Techs)
            {
                var nameLines = Wrap(t.Name, 15 * s, text - (t.Tag.Length > 0 ? 44 * s : 0)).Lines;
                var lineLines = Wrap(t.Line, 12.5f * s, text, regular: true).Lines;
                var content = Mathf.Min(nameLines, 3) * LineHeight(15 * s) + Mathf.Min(lineLines, 4) * (LineHeight(12.5f * s) - 2) + LineHeight(12 * s) + 4 * s;
                h += Mathf.Max(TechPicH, content) + 2 * 6 * s + 7 * s;
            }
            tallest = Mathf.Max(tallest, h);
        }
        return Mathf.Ceil(tallest);
    }

    private void BuildTree(FabPanelView v)
    {
        HudKit.Clear(_treeRow);
        HudKit.Clear(_doneGrid);
        _techTiles.Clear();
        _doneTiles.Clear();
        if (v.Tree == null) return;
        var s = _s;
        var gap = TreeGap;
        var width = TreeColumn;
        for (var c = 0; c < v.Tree.Count; c++)
        {
            var column = v.Tree[c];
            var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(width + (c > 0 ? gap : 0), 0) };
            col.AddThemeConstantOverride("separation", Mathf.RoundToInt(7 * s));
            var caption = HudKit.Label(column.Title.ToUpperInvariant(), 12 * s, HudKit.Dim, condensed: true);
            caption.AutowrapMode = TextServer.AutowrapMode.Word;
            caption.CustomMinimumSize = new Vector2(width, 0);
            col.AddChild(Indented(caption, c > 0 ? gap : 0, null));
            foreach (var t in column.Techs)
            {
                var tile = MakeTechTile(t, width);
                ColorRect? line = null;
                if (c > 0 && t.Linked)
                    line = new ColorRect { Color = HudKit.HoverEdge, MouseFilter = MouseFilterEnum.Ignore };
                tile.Connector = line;
                col.AddChild(Indented(tile.Root, c > 0 ? gap : 0, line));
                _techTiles.Add(tile);
            }
            _treeRow.AddChild(col);
        }
        var doneW = Mathf.Floor((_rightW - 16 * s - 14 * s) / 2);
        foreach (var t in v.Done ?? Array.Empty<TechTileView>())
        {
            var tile = MakeTechTile(t, doneW);
            _doneGrid.AddChild(tile.Root);
            _doneTiles.Add(tile);
        }
    }

    /// <summary>A tree cell pushed right by the column gap; Line = the connector drawn across that gap at mid height.</summary>
    private static Control Indented(Control inner, float gap, ColorRect? line)
    {
        if (gap <= 0) return inner;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 0);
        var stub = new Control { CustomMinimumSize = new Vector2(gap, 0), MouseFilter = MouseFilterEnum.Ignore };
        if (line != null)
        {
            stub.AddChild(line);
            line.SetAnchorsAndOffsetsPreset(LayoutPreset.HcenterWide);
            line.OffsetTop = -1;
            line.OffsetBottom = 1;
            line.OffsetLeft = 0;
            line.OffsetRight = 0;
        }
        row.AddChild(stub);
        row.AddChild(inner);
        return row;
    }

    private TechTile MakeTechTile(TechTileView t, float width)
    {
        var s = _s;
        var tile = new TechTile { Id = t.Id };
        var root = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(width, 0), TooltipText = t.Tip };
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(9 * s));
        var plate = new Control { CustomMinimumSize = new Vector2(TechPicW, TechPicH), MouseFilter = MouseFilterEnum.Ignore, ClipContents = true, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        tile.Picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps, MouseFilter = MouseFilterEnum.Ignore,
            Texture = ItemArt.ForKey(t.Picture, HudKit.Good),
        };
        plate.AddChild(tile.Picture);
        tile.Picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.AddChild(plate);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        text.AddThemeConstantOverride("separation", Mathf.RoundToInt(1 * s));
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", Mathf.RoundToInt(5 * s));
        tile.Name = HudKit.Label(t.Name, 15 * s, HudKit.Ink, bold: true);
        tile.Name.AutowrapMode = TextServer.AutowrapMode.Word;
        tile.Name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(tile.Name);
        if (t.Tag.Length > 0)
        {
            tile.TagBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkBegin };
            tile.TagBox.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.09f, 0.13f, 0.22f), HudKit.Line, 8 * s, 5 * s, 0));
            tile.TagBox.AddChild(HudKit.Label(t.Tag.ToUpperInvariant(), 11 * s, HudKit.Dim, condensed: true));
            head.AddChild(tile.TagBox);
        }
        text.AddChild(head);
        tile.Line = HudKit.Label("", 12.5f * s, HudKit.Dim);
        tile.Line.AutowrapMode = TextServer.AutowrapMode.Word;
        tile.Line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AddChild(tile.Line);
        tile.Status = HudKit.Label("", 12 * s, HudKit.Dim, condensed: true, bold: true);
        tile.Status.AutowrapMode = TextServer.AutowrapMode.Word;
        tile.Status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AddChild(tile.Status);
        row.AddChild(text);
        root.AddChild(row);
        var id = t.Id;
        root.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true } mb) return;
            if (mb.ButtonIndex == MouseButton.Left) RecipePressed?.Invoke(id, mb.CtrlPressed || mb.MetaPressed ? 1 : 0);
            else if (mb.ButtonIndex != MouseButton.Right) return;
            root.AcceptEvent();
        };
        root.MouseEntered += () =>
        {
            tile.Hovered = true;
            tile.Look = "";
        };
        root.MouseExited += () =>
        {
            tile.Hovered = false;
            tile.Look = "";
        };
        return tile;
    }

    private void FillTree(FabPanelView v)
    {
        if (v.Tree == null) return;
        var i = 0;
        foreach (var column in v.Tree)
            foreach (var t in column.Techs)
                if (i < _techTiles.Count) FillTech(_techTiles[i++], t);
        var done = v.Done ?? Array.Empty<TechTileView>();
        for (var k = 0; k < done.Count && k < _doneTiles.Count; k++) FillTech(_doneTiles[k], done[k]);
    }

    private void FillTech(TechTile tile, TechTileView t)
    {
        var s = _s;
        Set(tile.Line, t.Line);
        tile.Line.AddThemeColorOverride("font_color", t.Red ? new Color(1f, 0.48f, 0.42f) : HudKit.Dim);
        Set(tile.Status, t.Key.Length > 0 ? $"{t.Status}   ·   KEY {t.Key}" : t.Status);
        tile.Status.AddThemeColorOverride("font_color", t.StatusColour);
        if (tile.Root.TooltipText != t.Tip) tile.Root.TooltipText = t.Tip;
        if (tile.Connector != null) tile.Connector.Color = t.Red ? new Color(0.45f, 0.25f, 0.25f) : new Color(0.56f, 0.84f, 0.48f, 0.7f);
        var look = $"{t.Done}{t.Active}{tile.Hovered}";
        if (look != tile.Look)
        {
            tile.Look = look;
            var edge = t.Active ? HudKit.Accent : t.Done ? new Color(0.56f, 0.84f, 0.48f, 0.55f) : tile.Hovered ? HudKit.HoverEdge : HudKit.Line;
            var bg = tile.Hovered ? new Color(0.08f, 0.13f, 0.25f) : new Color(0.047f, 0.075f, 0.13f);
            tile.Root.AddThemeStyleboxOverride("panel", HudKit.Box(bg, edge, 7 * s, 7 * s, 6 * s, t.Active ? 2 : 1));
        }
        // Locked: greyed out but still clickable for its card (brighter under the mouse).
        var dim = t.Locked ? new Color(1, 1, 1, tile.Hovered ? 0.75f : 0.52f) : Colors.White;
        if (tile.Root.Modulate != dim) tile.Root.Modulate = dim;
        if (tile.Locked != t.Locked)
        {
            tile.Locked = t.Locked;
            tile.Picture.Material = t.Locked ? ItemArt.Greyed(0.65f) : null;
        }
    }

    // ---------------------------------------------------------------- per frame

    private void Fill(FabPanelView v)
    {
        var s = _s;
        _icon.Texture = Icons.Get(v.IconId, HudKit.Sky);
        Set(_title, v.Title.ToUpperInvariant());
        Set(_sub, v.Subtitle);
        Set(_status, v.Status);
        _status.AddThemeColorOverride("font_color", v.StatusColour);
        var barOn = v.StatusProgress >= 0;
        if (_statusBar.Visible != barOn) _statusBar.Visible = barOn;
        if (barOn)
        {
            _statusBar.Fraction = v.StatusProgress;
            _statusBar.Colour = v.StatusColour;
        }
        for (var i = 0; i < _actions.Count; i++)
        {
            var b = _actions[i];
            var show = i < v.Actions.Count;
            if (b.Visible != show) b.Visible = show;
            if (!show) continue;
            var a = v.Actions[i];
            _actionIds[i] = a.Id;
            var text = a.Detail.Length > 0 ? $"{a.Label}   {a.Detail}" : a.Label;
            if (b.Text != text) b.Text = text;
            b.Disabled = !a.Enabled;
        }

        if (_workshopChip.Visible != v.Workshop) _workshopChip.Visible = v.Workshop;
        if (v.Tabs == null)
        {
            SetTab("overview", "OVERVIEW");
            SetTab("queue", $"QUEUE · {v.Queue.Count}");
            SetTab("recipes", $"RECIPES · {v.Recipes.Count}");
        }
        else
            foreach (var (id, label) in v.Tabs)
                if (_tabs.ContainsKey(id)) SetTab(id, label);
        ShowTab();
        FillTree(v);

        // Overview: captions above values, then every short order with its full reason.
        while (_factCells.Count < v.Facts.Count)
        {
            var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            cell.AddThemeConstantOverride("separation", Mathf.RoundToInt(1 * s));
            var caption = HudKit.Label("", 12 * s, HudKit.Dim, condensed: true);
            var value = HudKit.Label("", 14 * s, HudKit.Ink);
            value.AutowrapMode = TextServer.AutowrapMode.Word;
            value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            cell.AddChild(caption);
            cell.AddChild(value);
            _facts.AddChild(cell);
            _factCells.Add((caption, value));
        }
        for (var i = 0; i < _factCells.Count; i++)
        {
            var (caption, value) = _factCells[i];
            var show = i < v.Facts.Count;
            var cell = (Control)caption.GetParent();
            if (cell.Visible != show) cell.Visible = show;
            if (!show) continue;
            Set(caption, v.Facts[i].Label.ToUpperInvariant());
            Set(value, v.Facts[i].Value);
            value.AddThemeColorOverride("font_color", v.Facts[i].Tone ?? HudKit.Ink);
        }
        var anyShort = v.Shorts.Count > 0;
        if (_shortCaption.Visible != anyShort) _shortCaption.Visible = anyShort;
        if (_shorts.Visible != anyShort) _shorts.Visible = anyShort;
        if (anyShort)
        {
            var red = new Color(1f, 0.48f, 0.42f).ToHtml(false);
            var text = string.Join("\n", v.Shorts.Select(x => $"[b]{Esc(x.Name)}[/b]   [color=#{red}]{Esc(x.Reason)}[/color]"));
            _shortCaption.Text = $"SHORT · {v.Shorts.Count}";
            if (_shorts.Text != text) _shorts.Text = text;
        }

        // Queue
        while (_queueRows.Count < v.Queue.Count)
        {
            var row = new QueueRow(this, s);
            _queueList.AddChild(row.Root);
            _queueRows.Add(row);
        }
        for (var i = 0; i < _queueRows.Count; i++)
        {
            var show = i < v.Queue.Count;
            if (_queueRows[i].Root.Visible != show) _queueRows[i].Root.Visible = show;
            if (show) _queueRows[i].Fill(i + 1, v.Queue[i]);
        }
        if (_queueNote.GetIndex() != _queueList.GetChildCount() - 1) _queueList.MoveChild(_queueNote, _queueList.GetChildCount() - 1);
        Set(_queueNote, v.QueueNote);
        if (_queueNote.Visible != (v.QueueNote.Length > 0)) _queueNote.Visible = v.QueueNote.Length > 0;

        // Recipes
        for (var i = 0; i < _tiles.Count && i < v.Recipes.Count; i++)
        {
            var t = _tiles[i];
            var r = v.Recipes[i];
            Set(t.Key, r.Key);
            if (!t.HasPicture && t.PictureKey.StartsWith("vehicle:"))
            {
                var tex = ItemArt.ForKey(t.PictureKey, r.Colour); // the runtime render once it is ready
                if (t.Picture.Texture != tex) t.Picture.Texture = tex;
            }
            var queued = r.Queued > 0;
            if (t.BadgeBox.Visible != queued) t.BadgeBox.Visible = queued;
            if (queued) Set(t.Badge, $"×{r.Queued}");
            if (t.Root.TooltipText != r.Tip) t.Root.TooltipText = r.Tip;
            var costSig = string.Join(",", r.Cost.Select(c => $"{c.ResourceId}{c.Amount}{c.Short}"));
            if (costSig != t.CostSignature)
            {
                t.CostSignature = costSig;
                HudKit.Clear(t.Cost);
                foreach (var c in r.Cost) t.Cost.AddChild(CostChip(c, c.Amount.ToString(), s));
            }
            var edge = queued ? HudKit.Accent : HudKit.BtnEdge;
            if (t.Root.GetThemeStylebox("normal") is StyleBoxFlat normal && normal.BorderColor != edge) normal.BorderColor = edge;
            if (t.CanMake != r.CanMake)
            {
                t.CanMake = r.CanMake;
                Dim(t);
            }
        }
        Set(_recipeNote, v.RecipeNote);
        if (_recipeNote.Visible != (v.RecipeNote.Length > 0)) _recipeNote.Visible = v.RecipeNote.Length > 0;
    }

    private void SetTab(string id, string text)
    {
        var tab = _tabs[id];
        if (tab.Text != text) tab.Text = text;
    }

    private void ShowTab()
    {
        if (!_tabs.ContainsKey(LastTab)) LastTab = _tabs.ContainsKey(DefaultTab(_kind)) ? DefaultTab(_kind) : _tabs.Keys.First();
        var tab = LastTab;
        foreach (var (id, button) in _tabs) button.SetPressedNoSignal(id == tab);
        foreach (var (id, body) in new[] { ("overview", _overviewScroll), ("queue", _queueScroll), ("recipes", _recipesScroll), ("tree", _treeScroll), ("done", _doneScroll) })
            if (body.Visible != (tab == id)) body.Visible = tab == id;
    }

    /// <summary>Switches to a tab if the panel has it (the lab's tree opens when its research finishes).</summary>
    public static void SelectTab(string kind, string id) => LastTabs[kind] = id;

    private static void Set(Label l, string text)
    {
        text = text.Replace("\r", "");
        if (l.Text != text) l.Text = text;
    }

    private static string Esc(string text) => text.Replace("[", "[lb]");

    // ---------------------------------------------------------------- queue rows

    private sealed class QueueRow
    {
        public readonly PanelContainer Root;
        private readonly FabPanel _panel;
        private readonly float _s;
        private readonly TextureRect _picture;
        private readonly Label _name, _time, _reason, _tag;
        private readonly HFlowContainer _cost;
        private readonly Bar _bar;
        private string _costSignature = "", _item = "";
        private int _id;

        public QueueRow(FabPanel panel, float s)
        {
            _panel = panel;
            _s = s;
            Root = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            Root.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.047f, 0.075f, 0.13f), HudKit.Line, 6 * s, 8 * s, 4 * s));
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * s));
            _picture = new TextureRect
            {
                CustomMinimumSize = new Vector2(30, 30) * s, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.LinearWithMipmaps,
                MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            row.AddChild(_picture);
            var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            mid.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * s));
            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * s));
            _name = HudKit.Label("", 15 * s, HudKit.Ink, bold: true);
            _name.AutowrapMode = TextServer.AutowrapMode.Word;
            _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            line.AddChild(_name);
            _tag = HudKit.Label("", 12 * s, HudKit.Sky, condensed: true);
            _tag.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            line.AddChild(_tag);
            _time = HudKit.Label("", 13 * s, HudKit.Dim);
            _time.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            line.AddChild(_time);
            mid.AddChild(line);
            _cost = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
            _cost.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(4 * s));
            _cost.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(3 * s));
            mid.AddChild(_cost);
            _reason = HudKit.Label("", 13 * s, new Color(1f, 0.48f, 0.42f));
            _reason.AutowrapMode = TextServer.AutowrapMode.Word;
            _reason.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            mid.AddChild(_reason);
            _bar = new Bar { CustomMinimumSize = new Vector2(0, 6 * s) };
            mid.AddChild(_bar);
            row.AddChild(mid);
            var cancel = new Button { Text = "×", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(26, 26) * s, SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Remove this order (what it took comes back)" };
            cancel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(15 * s));
            HudKit.StyleTile(cancel, 5 * s);
            cancel.Pressed += () =>
            {
                if (_id != 0) _panel.CancelPressed?.Invoke(_id);
            };
            row.AddChild(cancel);
            Root.AddChild(row);
        }

        public void Fill(int number, FabQueueView q)
        {
            _id = q.Id;
            var key = q.Picture.Length > 0 ? q.Picture : q.ItemId;
            if (_item != key || (key.StartsWith("vehicle:") && !ItemArt.HasPicture(key)))
            {
                _item = key;
                _picture.Texture = ItemArt.ForKey(key, q.Colour);
                var wide = key.StartsWith("vehicle:"); // a 4:3 render: fill a wider frame
                _picture.StretchMode = wide ? TextureRect.StretchModeEnum.KeepAspectCovered : TextureRect.StretchModeEnum.KeepAspectCentered;
                _picture.ClipContents = wide;
                _picture.CustomMinimumSize = wide ? new Vector2(40, 30) * _s : new Vector2(30, 30) * _s;
            }
            Set(_name, $"{number}. {q.Name}");
            Set(_tag, q.Tag);
            Set(_time, q.Time);
            Set(_reason, q.Reason);
            if (_reason.Visible != (q.Reason.Length > 0)) _reason.Visible = q.Reason.Length > 0;
            var running = q.Progress >= 0;
            if (_bar.Visible != running) _bar.Visible = running;
            if (running)
            {
                _bar.Fraction = q.Progress;
                _bar.Colour = q.Colour;
            }
            var sig = string.Join(",", q.Cost.Select(c => $"{c.ResourceId}{c.Amount}{c.Short}"));
            if (sig != _costSignature)
            {
                _costSignature = sig;
                HudKit.Clear(_cost);
                foreach (var c in q.Cost) _cost.AddChild(_panel.CostChip(c, $"{c.Amount} {c.Name}", _s));
            }
        }
    }
}
