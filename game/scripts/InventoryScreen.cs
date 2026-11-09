using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FinalFrontier;

/// <summary>One item on the inventory screen (a part, spare kit or construction part).</summary>
public sealed record InventoryRow(
    string Id,
    string Name,
    string Code,
    string Tier,
    Color Colour,
    int Stock,
    int Target,
    int Queued,
    float Volume,
    string Tip);

/// <summary>What the inventory screen shows: the items, the storage split (everything else vs parts) and, when the
/// sim has no parts yet, why the list is empty.</summary>
public sealed record InventoryView(IReadOnlyList<InventoryRow> Rows, float Stored, float Capacity, float PartsSpace, string Empty);

/// <summary>
/// The inventory screen (key I, docs/mockups/parts part C; contract "UI"): a full-screen overlay like
/// <see cref="AchievementScreen"/> (dimmed world, click outside closes, the game runs on underneath) with every
/// parts-economy item: tier code chip, name, type, stock (amber below its target), keep-in-stock target with − / +,
/// queued at the fabricators, storage space each and in all; filters All / Parts / Spare kits / Construction (phase 4) /
/// Below target; a storage bar split ore + metals vs parts. Sizes scale from 1280×720. The driver sets
/// <see cref="Source"/> (read every refresh) and sends a StockTargetCommand on <see cref="TargetChanged"/>.
/// </summary>
public partial class InventoryScreen : Control
{
    /// <summary>A − / + press: the item and its new target (never below 0).</summary>
    public event Action<string, int>? TargetChanged;

    public Func<InventoryView?> Source { get; set; } = () => null;

    public bool IsOpen => Visible;

    private string _filter = "all";
    private string _builtKey = "";
    private double _refreshAt;
    private float _s = 1f;
    private InventoryView? _view;
    private readonly Dictionary<string, RowParts> _rows = new();
    private Label? _storageText;
    private Label? _storageSplit;
    private Control? _storageBar;
    private ColorRect? _bulkFill;
    private ColorRect? _partsFill;
    private Label? _count;

    private sealed record RowParts(Control Root, Label Stock, Label Target, Label Queued, Label Space, Button Minus);

    private static readonly (string Id, string Name)[] Filters =
    {
        ("all", "All"), ("part", "Parts"), ("spare", "Spare kits"), ("construction", "Construction · phase 4"), ("low", "Below target"),
    };

    private static readonly Color Bulk = new(0.66f, 0.71f, 0.8f);

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        GuiInput += e =>
        {
            // the dimmed backdrop: a click outside the panel closes it
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right }) Close();
        };
    }

    public void Open()
    {
        Visible = true;
        _builtKey = "";
        _refreshAt = 0;
    }

    public void Close() => Visible = false;

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var now = Time.GetTicksMsec() / 1000.0;
        if (now < _refreshAt && _builtKey.Length > 0) return;
        _refreshAt = now + 0.2;
        _view = Source();
        var screen = GetViewportRect().Size;
        var rows = Shown(_view);
        // stock / target / queued change often: those labels are updated in place; the row set rebuilds the screen
        var key = $"{screen}|{_filter}|{_view == null}|{_view?.Empty}|{string.Join(",", rows.Select(r => r.Id))}";
        if (key != _builtKey)
        {
            _builtKey = key;
            Build(screen, rows);
        }
        Fill(rows);
    }

    private List<InventoryRow> Shown(InventoryView? v) =>
        v == null ? new List<InventoryRow>() : v.Rows.Where(r => _filter switch
        {
            "all" => true,
            "low" => r.Stock < r.Target,
            _ => r.Tier == _filter,
        }).ToList();

    // ---------------------------------------------------------------- build

    private void Build(Vector2 screen, List<InventoryRow> rows)
    {
        _s = HudKit.ScaleFor(screen);
        var s = _s;
        HudKit.Clear(this);
        _rows.Clear();
        _storageText = _storageSplit = null;
        _storageBar = null;
        _bulkFill = _partsFill = null;
        _count = null;

        var dim = new ColorRect { Color = new Color(0.005f, 0.01f, 0.025f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var width = Mathf.Min(screen.X - 40 * s, 1000 * s);
        var height = Mathf.Min(screen.Y - 36 * s, 620 * s);
        var pad = 22 * s;
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Size = new Vector2(width, height), Position = (screen - new Vector2(width, height)) / 2f };
        panel.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.03f, 0.047f, 0.09f, 0.985f), HudKit.Line, 10 * s, pad, pad * 0.9f));
        AddChild(panel);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", Mathf.RoundToInt(12 * s));
        panel.AddChild(column);

        column.AddChild(Header(s));
        column.AddChild(new ColorRect { Color = HudKit.Line, CustomMinimumSize = new Vector2(0, Mathf.Max(1, s)), MouseFilter = MouseFilterEnum.Ignore });

        if (_view == null || _view.Rows.Count == 0)
        {
            var none = HudKit.Label(_view?.Empty is { Length: > 0 } e ? e : "Parts economy not in this build yet", 16 * s, HudKit.Dim);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            none.SizeFlagsVertical = SizeFlags.ExpandFill;
            column.AddChild(none);
        }
        else
        {
            column.AddChild(FilterRow(s));
            column.AddChild(Table(s, rows));
        }

        var hint = HudKit.Label("Keep in stock: when stock + queued drops below the target, an order goes to a Machine Shop (spare kits: else a colony core's workshop)  ·  Esc or I closes",
            11 * s, HudKit.Dim);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(hint);
    }

    private Control Header(float s)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(18 * s));
        var titles = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        titles.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * s));
        titles.AddChild(HudKit.Label("INVENTORY", 30 * s, HudKit.Ink, condensed: true, bold: true));
        titles.AddChild(HudKit.Label("Parts, spare kits and construction parts across your bases", 13 * s, HudKit.Dim));
        row.AddChild(titles);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        // storage: used / space, a bar split ore + metals vs parts
        var storage = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        storage.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * s));
        top.AddChild(HudKit.Label("STORAGE", 11 * s, HudKit.Dim, condensed: true, bold: true));
        _storageText = HudKit.Label("", 17 * s, HudKit.Ink, condensed: true, bold: true);
        top.AddChild(_storageText);
        storage.AddChild(top);
        var bar = new Control { CustomMinimumSize = new Vector2(220 * s, 7 * s), MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        bar.AddChild(new ColorRect { Color = new Color(0.08f, 0.1f, 0.17f), MouseFilter = MouseFilterEnum.Ignore, Size = new Vector2(220 * s, 7 * s) });
        _bulkFill = new ColorRect { Color = Bulk, MouseFilter = MouseFilterEnum.Ignore, Size = new Vector2(0, 7 * s) };
        _partsFill = new ColorRect { Color = Icons.PartColour, MouseFilter = MouseFilterEnum.Ignore, Size = new Vector2(0, 7 * s) };
        bar.AddChild(_bulkFill);
        bar.AddChild(_partsFill);
        _storageBar = bar;
        storage.AddChild(bar);
        _storageSplit = HudKit.Label("", 11 * s, HudKit.Dim);
        storage.AddChild(_storageSplit);
        row.AddChild(storage);

        var close = new Button { Text = "×", FocusMode = FocusModeEnum.None, TooltipText = "Close (Esc / I)", SizeFlagsVertical = SizeFlags.ShrinkBegin };
        HudKit.StyleTile(close, 6 * s, 4 * s);
        close.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(18 * s));
        close.CustomMinimumSize = new Vector2(34 * s, 34 * s);
        close.Pressed += Close;
        row.AddChild(close);
        return row;
    }

    private Control FilterRow(float s)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
        row.AddChild(HudKit.Label("SHOW", 11 * s, HudKit.Dim, condensed: true, bold: true));
        foreach (var (id, name) in Filters)
        {
            var b = Pill(name, _filter == id, s);
            var pick = id;
            b.Pressed += () =>
            {
                _filter = pick;
                _builtKey = "";
                _refreshAt = 0;
            };
            row.AddChild(b);
        }
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _count = HudKit.Label("", 11 * s, HudKit.Dim);
        row.AddChild(_count);
        return row;
    }

    private static Button Pill(string text, bool on, float s)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        b.AddThemeFontOverride("font", HudKit.Condensed(true));
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(13 * s));
        var r = 6 * s;
        var normal = HudKit.Box(HudKit.Chip, HudKit.BtnEdge, r, 10 * s, 5 * s);
        var lit = HudKit.Box(new Color(0.12f, 0.087f, 0.027f), HudKit.Accent, r, 10 * s, 5 * s);
        b.AddThemeStyleboxOverride("normal", on ? lit : normal);
        b.AddThemeStyleboxOverride("hover", on ? lit : HudKit.Box(new Color(0.08f, 0.12f, 0.21f), HudKit.HoverEdge, r, 10 * s, 5 * s));
        b.AddThemeStyleboxOverride("pressed", lit);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", on ? HudKit.Accent : HudKit.Ink);
        b.AddThemeColorOverride("font_hover_color", on ? HudKit.Accent : HudKit.Ink);
        b.AddThemeColorOverride("font_pressed_color", HudKit.Accent);
        return b;
    }

    // columns: item, type, stock, keep (− n +), queued, space each, storage used
    private static readonly (string Title, float Width, bool Right)[] Columns =
    {
        ("ITEM", 0f, false), ("TYPE", 130f, false), ("STOCK", 70f, true), ("KEEP IN STOCK", 130f, true), ("QUEUED", 70f, true),
        ("SPACE EACH", 84f, true), ("STORAGE USED", 100f, true),
    };

    private Control Table(float s, List<InventoryRow> rows)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * s));
        foreach (var (title, w, right) in Columns)
        {
            var l = HudKit.Label(title, 11 * s, HudKit.Dim, condensed: true, bold: true);
            Fit(l, w, s, right);
            head.AddChild(l);
        }
        var headPad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        headPad.AddThemeConstantOverride("margin_left", Mathf.RoundToInt(10 * s));
        headPad.AddThemeConstantOverride("margin_right", Mathf.RoundToInt(24 * s)); // over the scroll bar
        headPad.AddChild(head);
        box.AddChild(headPad);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        box.AddChild(scroll);
        var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
        scroll.AddChild(list);
        if (rows.Count == 0)
            list.AddChild(HudKit.Label(_filter == "low" ? "Nothing is below its keep-in-stock target." : "Nothing of this kind.", 14 * s, HudKit.Dim));
        foreach (var r in rows) list.AddChild(Row(r, s));
        return box;
    }

    private static void Fit(Control c, float w, float s, bool right)
    {
        if (w <= 0) c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        else c.CustomMinimumSize = new Vector2(w * s, 0);
        if (c is Label l)
        {
            l.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            l.ClipText = true;
        }
    }

    private Control Row(InventoryRow r, float s)
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Pass, TooltipText = r.Tip };
        panel.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.045f, 0.065f, 0.12f), new Color(0, 0, 0, 0), 6 * s, 10 * s, 5 * s, 0));
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * s));
        panel.AddChild(row);

        var name = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        name.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * s));
        var chip = new HudKit.CodeChip(13 * s);
        chip.Set(r.Code, r.Colour);
        name.AddChild(chip.Root);
        var icon = HudKit.Picture(new Vector2(18 * s, 18 * s));
        icon.Texture = Icons.Get(r.Id, r.Colour);
        icon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        name.AddChild(icon);
        var title = HudKit.Label(r.Name, 15 * s, HudKit.Ink);
        title.ClipText = true;
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.AddChild(title);
        row.AddChild(name);

        var type = HudKit.Label(r.Tier switch { "part" => "Part", "spare" => "Spare kit", "construction" => "Construction · ph 4", _ => r.Tier }, 13 * s, HudKit.Dim);
        Fit(type, Columns[1].Width, s, false);
        row.AddChild(type);

        var stock = HudKit.Label("", 16 * s, HudKit.Ink, condensed: true, bold: true);
        Fit(stock, Columns[2].Width, s, true);
        row.AddChild(stock);

        var keep = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(Columns[3].Width * s, 0), Alignment = BoxContainer.AlignmentMode.End };
        keep.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
        var minus = Step("−", s, "Lower the keep-in-stock target");
        var target = HudKit.Label("", 15 * s, HudKit.Ink, condensed: true, bold: true);
        target.CustomMinimumSize = new Vector2(30 * s, 0);
        target.HorizontalAlignment = HorizontalAlignment.Center;
        var plus = Step("+", s, "Raise the keep-in-stock target");
        var id = r.Id;
        minus.Pressed += () => Nudge(id, -1);
        plus.Pressed += () => Nudge(id, +1);
        keep.AddChild(minus);
        keep.AddChild(target);
        keep.AddChild(plus);
        row.AddChild(keep);

        var queued = HudKit.Label("", 14 * s, HudKit.Ink);
        Fit(queued, Columns[4].Width, s, true);
        row.AddChild(queued);
        var each = HudKit.Label(Num(r.Volume), 14 * s, HudKit.Dim);
        Fit(each, Columns[5].Width, s, true);
        row.AddChild(each);
        var space = HudKit.Label("", 14 * s, HudKit.Ink);
        Fit(space, Columns[6].Width, s, true);
        row.AddChild(space);

        _rows[r.Id] = new RowParts(panel, stock, target, queued, space, minus);
        return panel;
    }

    private static Button Step(string text, float s, string tip)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, TooltipText = tip, CustomMinimumSize = new Vector2(26 * s, 24 * s) };
        HudKit.StyleTile(b, 5 * s, 2 * s);
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(15 * s));
        return b;
    }

    private void Nudge(string id, int by)
    {
        var row = _view?.Rows.FirstOrDefault(r => r.Id == id);
        if (row == null) return;
        var next = Mathf.Max(0, row.Target + by);
        if (next == row.Target) return;
        TargetChanged?.Invoke(id, next);
        _refreshAt = 0; // show the pending value next frame
    }

    // ---------------------------------------------------------------- fill (every refresh)

    private void Fill(List<InventoryRow> rows)
    {
        var v = _view;
        if (v != null && _storageText != null && _storageSplit != null && _storageBar != null && _bulkFill != null && _partsFill != null)
        {
            var cap = Mathf.Max(v.Capacity, 0f);
            var bulk = Mathf.Max(0f, v.Stored - v.PartsSpace);
            _storageText.Text = $"{Mathf.FloorToInt(v.Stored)} / {Mathf.RoundToInt(cap)}";
            _storageText.AddThemeColorOverride("font_color", cap > 0 && v.Stored >= cap - 0.5f ? HudKit.Warn : HudKit.Ink);
            _storageSplit.Text = $"ore + metals {Mathf.FloorToInt(bulk)}  ·  parts {Mathf.RoundToInt(v.PartsSpace)}";
            var w = _storageBar.CustomMinimumSize.X;
            var h = _storageBar.CustomMinimumSize.Y;
            var bulkW = cap > 0 ? Mathf.Min(1f, bulk / cap) * w : 0f;
            var partsW = cap > 0 ? Mathf.Min(1f - bulkW / w, v.PartsSpace / cap) * w : 0f;
            _bulkFill.Size = new Vector2(bulkW, h);
            _partsFill.Position = new Vector2(bulkW, 0);
            _partsFill.Size = new Vector2(Mathf.Max(0, partsW), h);
        }
        if (_count != null && v != null) _count.Text = $"{rows.Count} of {v.Rows.Count} items";
        foreach (var r in rows)
        {
            if (!_rows.TryGetValue(r.Id, out var p)) continue;
            p.Stock.Text = r.Stock.ToString();
            p.Stock.AddThemeColorOverride("font_color", r.Stock < r.Target ? HudKit.Warn : HudKit.Ink);
            p.Target.Text = r.Target > 0 ? r.Target.ToString() : "–";
            p.Target.AddThemeColorOverride("font_color", r.Target > 0 ? HudKit.Ink : HudKit.Dim);
            p.Minus.Disabled = r.Target <= 0;
            p.Queued.Text = r.Queued > 0 ? r.Queued.ToString() : "–";
            p.Queued.AddThemeColorOverride("font_color", r.Queued > 0 ? HudKit.Sky : HudKit.Dim);
            p.Space.Text = Num(r.Stock * r.Volume);
        }
    }

    private static string Num(float v) => Mathf.IsEqualApprox(v, Mathf.Round(v)) ? $"{Mathf.RoundToInt(v)}" : $"{v:0.#}";
}
