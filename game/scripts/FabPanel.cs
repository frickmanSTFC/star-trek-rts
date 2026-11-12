using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FinalFrontier;

/// <summary>One input of a recipe or order: what (resource / item id, for its picture), its name, how many, short = red.</summary>
public sealed record FabCost(string ResourceId, string Name, int Amount, int Have, bool Short);

/// <summary>A recipe tile: item id, full name, number key ("7", "0", "" = none), tier colour, inputs, how many of it are
/// queued here, whether it can be made right now (false = greyed out, still clickable), and its hover text.</summary>
public sealed record FabTileView(string Id, string Name, string Key, Color Colour, IReadOnlyList<FabCost> Cost, int Queued, bool CanMake, string Tip);

/// <summary>A queued order: its id (×), item, inputs, why it can't start (red; empty = it can), time ("0:15", "0:07 left"),
/// running progress (below 0 = not started) and a tag ("keep in stock").</summary>
public sealed record FabQueueView(int Id, string ItemId, string Name, Color Colour, IReadOnlyList<FabCost> Cost, string Reason, string Time, float Progress, string Tag);

/// <summary>An Overview fact: caption above its value; Tone colours the value (null = ink).</summary>
public sealed record FabFactView(string Label, string Value, Color? Tone = null);

/// <summary>
/// A selected Machine Shop (or a colony core's workshop) as the bottom panel shows it (owner 2026-11-12 picked option 2,
/// "Tabs", of docs/mockups/machine-shop-panel): on the left the name, level, status line and the buttons; on the right
/// three tabs (Overview, Queue · n, Recipes · n). Shorts = every queued item that can't start with its full reason.
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
    bool Workshop);

/// <summary>
/// The tabbed Machine Shop panel (see <see cref="FabPanelView"/>). It takes the bottom panel's centre and command card
/// (PlanetHud.ShowFabPanel); the HUD makes the bottom panel as tall as <see cref="NeededHeight"/>. Sizes come from
/// <see cref="HudKit.ScaleFor"/> (rebuilt when the screen or the UI scale changes). Nothing is clipped or cut mid-word:
/// captions sit above values, long text wraps between words, tile names are wrapped here (two lines at most, the font
/// shrinks a little rather than break a word), and each tab scrolls if it holds more than fits.
/// Recipe tiles: a left click opens the item's card, Shift+click queues 5, Ctrl+click queues 1, a right click removes
/// one queued order of it; tiles you can't make right now are dimmed and desaturated (still clickable).
/// </summary>
public partial class FabPanel : MarginContainer
{
    /// <summary>The tab that shows (remembered for the session; Recipes at first).</summary>
    public static string LastTab = "recipes";

    /// <summary>A recipe tile: (item id, mode) with mode 0 = open its card, 1 / 5 = queue that many, -1 = remove one.</summary>
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
        var signature = string.Join(",", v.Recipes.Select(r => r.Id + ":" + r.Name + ":" + r.Cost.Count));
        if (screen != _builtFor || !Mathf.IsEqualApprox(width, _width) || !Mathf.IsEqualApprox(s, _builtScale) || _icon == null)
        {
            _width = width;
            Build(screen, s, v);
            _tileSignature = "";
        }
        _shown = _icon == null || _shown > 1000 ? _shown : _shown + 1;
        if (signature != _tileSignature)
        {
            _shown = 0;
            Measure(v);
            BuildTiles(v);
            _tileSignature = signature;
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
        foreach (var id in new[] { "overview", "queue", "recipes" })
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

        row.AddChild(right);
        Measure(v);
    }

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
        _columns = Mathf.Clamp(Mathf.FloorToInt((_rightW + gap) / (96 * s + gap)), 2, 7);
        var rows = Mathf.CeilToInt(n / (float)_columns);
        var width = _rightW - (rows > 2 ? 16 * s : 0) - 2; // room for the scroll bar when more than two rows
        _tileW = Mathf.Floor((width - (_columns - 1) * gap) / _columns);
        var pad = 6 * s;
        var inner = _tileW - pad * 2;
        // Tile names: the largest size (down to 75%) at which every name fits two lines without breaking a word.
        _nameSize = 13.5f * s;
        while (_nameSize > 10f * s && v.Recipes.Any(r => Wrap(r.Name, _nameSize, inner).Lines > 2)) _nameSize -= 0.5f;
        var chipRows = 1;
        foreach (var r in v.Recipes) chipRows = Mathf.Max(chipRows, ChipRows(r.Cost, inner));
        var chipH = ChipHeight();
        _tileH = Mathf.Ceil(pad * 2 + 36 * s + 3 * s + 2 * LineHeight(_nameSize) + 4 * s + chipRows * chipH + (chipRows - 1) * 3 * s + 4 * s);
        var shown = Mathf.Clamp(rows, 1, 2);
        _bodyH = Mathf.Ceil(shown * _tileH + (shown - 1) * gap + (v.RecipeNote.Length > 0 ? 18 * s : 0));
        var headerH = 30 * s + Mathf.Max(1, s) + 8 * s;
        var rightH = 22 * s + headerH + _bodyH;
        var leftH = 22 * s + 42 * s + 4 * s + 2 * 19 * s + 4 * (34 * s + 4 * s);
        NeededHeight = Mathf.Ceil(Mathf.Max(PlanetHud.BottomHeight, Mathf.Max(rightH, leftH) + 2));
        foreach (var sc in new[] { _overviewScroll, _queueScroll, _recipesScroll })
            if (sc != null) sc.CustomMinimumSize = new Vector2(0, Mathf.Max(_bodyH, leftH - 22 * s - headerH));
        if (_grid != null) _grid.Columns = _columns;
    }

    /// <summary>Greedy word wrap of a name at a font size: the text with line breaks, its line count (a word wider than
    /// the width counts as 99 lines, so it is never broken inside).</summary>
    private static (string Text, int Lines) Wrap(string name, float size, float width)
    {
        var font = HudKit.Condensed(true);
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
        public PanelContainer BadgeBox = null!;
        public HFlowContainer Cost = null!;
        public string CostSignature = "";
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
            var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            top.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
            t.Picture = new TextureRect
            {
                CustomMinimumSize = new Vector2(36, 36) * s, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.LinearWithMipmaps,
                MouseFilter = MouseFilterEnum.Ignore, Texture = ItemArt.PictureOrIcon(r.Id, r.Colour),
            };
            top.AddChild(t.Picture);
            top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
            var right = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            right.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
            t.Key = HudKit.Label(r.Key, 12 * s, HudKit.Dim, condensed: true);
            t.Key.HorizontalAlignment = HorizontalAlignment.Right;
            right.AddChild(t.Key);
            t.BadgeBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
            t.BadgeBox.AddThemeStyleboxOverride("panel", HudKit.Box(HudKit.Accent, HudKit.Accent, 9 * s, 5 * s, 0));
            t.Badge = HudKit.Label("", 12 * s, new Color(0.06f, 0.07f, 0.1f), condensed: true, bold: true);
            t.BadgeBox.AddChild(t.Badge);
            right.AddChild(t.BadgeBox);
            top.AddChild(right);
            box.AddChild(top);
            var (text, _) = Wrap(r.Name, _nameSize, _tileW - pad * 2);
            t.Name = HudKit.Label(text, _nameSize, HudKit.Ink, bold: true);
            t.Name.CustomMinimumSize = new Vector2(0, 2 * LineHeight(_nameSize));
            t.Name.VerticalAlignment = VerticalAlignment.Top;
            box.AddChild(t.Name);
            t.Cost = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
            t.Cost.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(3 * s));
            t.Cost.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(3 * s));
            box.AddChild(t.Cost);
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

    // ---------------------------------------------------------------- per frame

    private void Fill(FabPanelView v)
    {
        var s = _s;
        _icon.Texture = Icons.Get(v.IconId, HudKit.Sky);
        Set(_title, v.Title.ToUpperInvariant());
        Set(_sub, v.Subtitle);
        Set(_status, v.Status);
        _status.AddThemeColorOverride("font_color", v.StatusColour);
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
        SetTab("overview", "OVERVIEW");
        SetTab("queue", $"QUEUE · {v.Queue.Count}");
        SetTab("recipes", $"RECIPES · {v.Recipes.Count}");
        ShowTab();

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
        if (!_tabs.ContainsKey(LastTab)) LastTab = "recipes";
        foreach (var (id, tab) in _tabs) tab.SetPressedNoSignal(id == LastTab);
        if (_overviewScroll.Visible != (LastTab == "overview")) _overviewScroll.Visible = LastTab == "overview";
        if (_queueScroll.Visible != (LastTab == "queue")) _queueScroll.Visible = LastTab == "queue";
        if (_recipesScroll.Visible != (LastTab == "recipes")) _recipesScroll.Visible = LastTab == "recipes";
    }

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
            if (_item != q.ItemId)
            {
                _item = q.ItemId;
                _picture.Texture = ItemArt.PictureOrIcon(q.ItemId, q.Colour);
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
