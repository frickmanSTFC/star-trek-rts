using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FinalFrontier;

/// <summary>One input row of an item card: id, name, how many one order takes, how many you have, short (red), and
/// whether the shop can make it (then "Make or get" links to its card).</summary>
public sealed record ItemNeed(string Id, string Name, int Each, int Have, bool Short, bool Makeable);

/// <summary>
/// What the item card shows: the item (id, name, kind "Spare kit", hotkey, tier colour), Times ("Mk I 0:15 · Mk II
/// 0:10"), CanMake (green box) or not (red box with the missing items), the Needs / Each / You have table, the
/// description, StockLine ("In stock 2 · 1 in the queue · 2 storage each"), Problem = why it can't be queued at all
/// (queue full…; disables the queue buttons), Note = a dim extra line (the core workshop makes kits from materials).
/// </summary>
public sealed record ItemCardView(string Id, string Name, string Kind, string Key, Color Colour, string Times, bool CanMake,
    IReadOnlyList<ItemNeed> Needs, string Description, string StockLine, string Problem, string Note);

/// <summary>The tone of a card's status box: green (can do it / done), red (can't), orange (in progress).</summary>
public enum CardTone { Good, Bad, Busy }

/// <summary>A link in a card's status box: opens that card (Clickable) or is plain red text.</summary>
public sealed record CardLink(string Id, string Name, bool Clickable);

/// <summary>A card button: its label, the action id it sends (<see cref="ItemCard.ActionPressed"/>), enabled, primary
/// (accent edge) and hover text.</summary>
public sealed record CardButton(string Label, int Action, bool Enabled, bool Primary, string Tip = "");

/// <summary>
/// The generic full card (shared by the Machine Shop's items, the Vehicle Bay's vehicles and the Research Lab's techs):
/// PictureKey for <see cref="ItemArt.ForKey"/> (Wide = a 4:3 render, else a square item picture on a plate), name,
/// Kind (coloured line under the name), Times (dim line), the status box (Tone, text, then LinkCaption + Links), stat
/// tiles (value over caption), a table (TableHead / EACH / YOU HAVE) of Rows, a bullet list (BulletsHead), the
/// description, extra Notes, Buttons and a dim Hint at the bottom. Rebuilt only when something on it changed.
/// </summary>
public sealed record PanelCard(
    string Id,
    string PictureKey,
    bool Wide,
    string Name,
    string Kind,
    string Times,
    Color Colour,
    CardTone Tone,
    string StatusText,
    string LinkCaption,
    IReadOnlyList<CardLink> Links,
    IReadOnlyList<(string Value, string Label)> Stats,
    string TableHead,
    IReadOnlyList<ItemNeed> Rows,
    string BulletsHead,
    IReadOnlyList<string> Bullets,
    string Description,
    IReadOnlyList<(string Text, Color Colour)> Notes,
    IReadOnlyList<CardButton> Buttons,
    string Hint)
{
    /// <summary>Everything on the card in one string (the card rebuilds when it changes).</summary>
    public string Signature() =>
        $"{Id}|{PictureKey}|{Wide}|{Name}|{Kind}|{Times}|{Colour}|{Tone}|{StatusText}|{LinkCaption}|{string.Join(";", Links)}|" +
        $"{string.Join(";", Stats)}|{TableHead}|{string.Join(";", Rows)}|{BulletsHead}|{string.Join(";", Bullets)}|{Description}|" +
        $"{string.Join(";", Notes.Select(n => n.Text))}|{string.Join(";", Buttons)}|{Hint}";
}

/// <summary>
/// The full card of a Machine Shop item, a Vehicle Bay vehicle or a Research Lab tech (owner 2026-11-12: anything you
/// can't do right now is greyed out but still clickable to see its full card), modal over the map like the build
/// menu's <see cref="BuildingCard"/>: a dimmed backdrop (a click on it closes the card), Esc / × close, and no click
/// reaches the map while it is open. Big picture, name, kind, times, a green / red / orange status box whose links open
/// other cards, stat tiles, a table with what you have, bullets, the description and its buttons (see
/// <see cref="PanelCard"/>). The Machine Shop's item card is <see cref="Show(ItemCardView)"/>: QUEUE ONE [Enter] (or
/// "Queue anyway (waits for inputs)" when short) / QUEUE 5 [Shift+Enter] / CLOSE [Esc]. Sizes scale from 1280×720; the
/// body scrolls when it is taller than the screen (UI scale 125% at 720p), so nothing is cut off.
/// </summary>
public partial class ItemCard : Control
{
    /// <summary>A card button: its action (the Machine Shop's queue buttons send 1 / 5).</summary>
    public event Action<int>? ActionPressed;
    /// <summary>A link: open that card (an item id, or the panel's own link id such as "tech:x").</summary>
    public event Action<string>? LinkPressed;
    public event Action? CloseRequested;

    private string _signature = "";
    private Vector2 _builtFor;
    private float _builtScale;
    private ScrollContainer? _scroll;
    private Control? _body;
    private float _maxBody;
    private string _lastId = "";
    private int _settle;

    public bool IsOpen => Visible;

    /// <summary>The card that is open (its Id; null = closed).</summary>
    public string? ItemId { get; private set; }

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        GuiInput += e =>
        {
            // The dimmed backdrop: a left or right click outside the card closes it; the wheel never reaches the map.
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right })
            {
                AcceptEvent();
                CloseRequested?.Invoke();
            }
            else if (e is InputEventMouseButton) AcceptEvent();
        };
    }

    public override void _Process(double delta)
    {
        // The body scrolls only when it is taller than the room on screen.
        if (!Visible || _scroll == null || _body == null) return;
        var want = Mathf.Min(_body.GetCombinedMinimumSize().Y, _maxBody);
        _settle++;
        if (!Mathf.IsEqualApprox(_scroll.CustomMinimumSize.Y, want)) _scroll.CustomMinimumSize = new Vector2(_scroll.CustomMinimumSize.X, want);
        else if (Modulate.A < 1 && _settle > 1) Modulate = Colors.White; // a new card shows once its body has its height (no jump)
        if (_settle > 5 && Modulate.A < 1) Modulate = Colors.White;
    }

    /// <summary>The Machine Shop's item card (as before the generic card existed).</summary>
    public void Show(ItemCardView v)
    {
        var missing = v.Needs.Where(n => n.Short).ToList();
        var status = v.CanMake ? "Can make now. It starts as soon as the shop is free."
            : missing.Count > 0 ? "Can't make yet: missing " + string.Join(" and ", missing.Select(n => $"{n.Each - n.Have} {n.Name}")) + "."
            : $"Can't queue it: {v.Problem}.";
        var links = missing.Select(n => n.Makeable ? new CardLink(n.Id, n.Name, true) : new CardLink(n.Id, $"{n.Name} (from a refinery or foundry)", false)).ToList();
        var notes = new List<(string, Color)>();
        foreach (var (text, colour) in new[] { (v.StockLine, HudKit.Dim), (v.Note, HudKit.Dim), (v.CanMake || v.Problem.Length == 0 ? "" : $"Can't queue it: {v.Problem}", new Color(1f, 0.48f, 0.42f)) })
            if (text.Length > 0) notes.Add((text, colour));
        var canQueue = v.Problem.Length == 0;
        var buttons = new List<CardButton>
        {
            new(v.CanMake ? "QUEUE ONE  [ENTER]" : "QUEUE ANYWAY (WAITS FOR INPUTS)  [ENTER]", 1, canQueue, true,
                v.CanMake ? "Add one order to this shop's queue" : "The order waits in the queue until its inputs are in stock"),
            new("QUEUE 5  [SHIFT+ENTER]", 5, canQueue, false),
        };
        Show(new PanelCard(v.Id, v.Id, false, v.Name, v.Kind + (v.Key.Length > 0 ? $"  ·  hotkey {v.Key}" : "  ·  no hotkey"), v.Times, v.Colour,
            v.CanMake ? CardTone.Good : CardTone.Bad, status, missing.Count > 0 ? "Make or get:" : "", links,
            Array.Empty<(string, string)>(), "NEEDS", v.Needs, "", Array.Empty<string>(), v.Description, notes, buttons,
            "On the recipe tiles: click = this card · Shift+click = queue 5 · Ctrl+click = queue 1 · right-click = remove one · number keys queue 1 (Shift 5)"));
    }

    /// <summary>Opens or refreshes the card (rebuilt only when something on it changed).</summary>
    public void Show(PanelCard v)
    {
        var screen = GetViewportRect().Size;
        var s = HudKit.ScaleFor(screen);
        var sig = v.Signature();
        if (!Visible)
        {
            Sfx.Ui("open");
            Visible = true;
        }
        ItemId = v.Id;
        if (sig == _signature && screen == _builtFor && Mathf.IsEqualApprox(s, _builtScale)) return;
        _signature = sig;
        _builtFor = screen;
        _builtScale = s;
        Build(v, screen, s);
    }

    public void Close()
    {
        if (!Visible) return;
        Sfx.Ui("close");
        Visible = false;
        ItemId = null;
        _signature = "";
    }

    private static Label Wrapped(string text, float size, Color colour, float width = 0)
    {
        var l = HudKit.Label(text, size, colour);
        l.AutowrapMode = TextServer.AutowrapMode.Word;
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (width > 0) l.CustomMinimumSize = new Vector2(width, 0);
        return l;
    }

    private void Build(PanelCard v, Vector2 screen, float s)
    {
        // The body starts at the last build's height when it is the same card (a refresh when the stock changes must not jump).
        var lastBody = _scroll != null && IsInstanceValid(_scroll) && v.Id == _lastId ? _scroll.CustomMinimumSize.Y : 0f;
        _lastId = v.Id;
        _settle = 0;
        if (lastBody <= 0) Modulate = new Color(1, 1, 1, 0);
        HudKit.Clear(this);
        var dim = new ColorRect { Color = new Color(0.005f, 0.01f, 0.025f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var width = Mathf.Min(screen.X - 40 * s, (v.Wide ? 680 : 640) * s);
        var pad = 18 * s;
        var inner = width - pad * 2;
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(width, 0) };
        var frame = HudKit.Box(HudKit.Panel, new Color(0.2f, 0.25f, 0.37f), 10 * s, pad, pad);
        frame.BorderWidthTop = Mathf.RoundToInt(4 * s);
        frame.BorderColor = v.Colour;
        frame.BorderWidthLeft = frame.BorderWidthRight = frame.BorderWidthBottom = Mathf.Max(1, Mathf.RoundToInt(s));
        panel.AddThemeStyleboxOverride("panel", frame);
        centre.AddChild(panel);

        var outer = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(inner, 0) };
        outer.AddThemeConstantOverride("separation", Mathf.RoundToInt(11 * s));
        panel.AddChild(outer);

        // Header (stays put): big picture, name, kind, times, ×.
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        head.AddThemeConstantOverride("separation", Mathf.RoundToInt(14 * s));
        var picH = Mathf.Max(Mathf.Min(128 * s, (screen.Y - 420 * s) * 0.5f), 72 * s);
        var picW = v.Wide ? Mathf.Round(picH * 4f / 3f) : picH;
        var plate = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(picW, picH), SizeFlagsVertical = SizeFlags.ShrinkBegin, ClipContents = true };
        plate.AddThemeStyleboxOverride("panel", HudKit.Box(v.Colour.Darkened(0.78f), v.Colour.Darkened(0.4f), (v.Wide ? 8 : 12) * s));
        var pic = HudKit.Picture(new Vector2(picW, picH));
        if (v.Wide) pic.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        pic.Texture = ItemArt.ForKey(v.PictureKey, v.Colour);
        plate.AddChild(pic);
        head.AddChild(plate);
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        names.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
        var title = HudKit.Label(v.Name.ToUpperInvariant(), 27 * s, HudKit.Ink, bold: true);
        title.AutowrapMode = TextServer.AutowrapMode.Word;
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        names.AddChild(title);
        var kind = HudKit.Label(v.Kind, 14 * s, v.Colour, condensed: true);
        kind.AutowrapMode = TextServer.AutowrapMode.Word;
        names.AddChild(kind);
        if (v.Times.Length > 0) names.AddChild(Wrapped(v.Times, 14 * s, HudKit.Dim));
        head.AddChild(names);
        var close = new Button { Text = "×", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(32, 32) * s, SizeFlagsVertical = SizeFlags.ShrinkBegin, TooltipText = "Close (Esc)" };
        close.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(18 * s));
        HudKit.StyleTile(close, 6 * s);
        close.Pressed += () => CloseRequested?.Invoke();
        head.AddChild(close);
        outer.AddChild(head);

        // The body scrolls if it is taller than the screen allows (sized in _Process).
        _maxBody = Mathf.Max(120 * s, screen.Y - 2 * pad - picH - 11 * s - 2 * 20 * s - 10 * s - 34 * s - 11 * s);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            MouseFilter = MouseFilterEnum.Pass, FollowFocus = false, CustomMinimumSize = new Vector2(inner, Mathf.Min(lastBody, _maxBody)),
        };
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(inner - 12 * s, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(11 * s));
        _scroll.AddChild(box);
        _body = box;
        var bodyW = inner - 12 * s;

        // Status box (links open other cards).
        var (bg, edge, ink) = v.Tone switch
        {
            CardTone.Good => (new Color(0.075f, 0.15f, 0.1f), new Color(0.56f, 0.84f, 0.48f, 0.5f), new Color(0.78f, 0.95f, 0.74f)),
            CardTone.Busy => (new Color(0.165f, 0.114f, 0.055f), new Color(0.94f, 0.64f, 0.29f, 0.6f), new Color(1f, 0.85f, 0.66f)),
            _ => (new Color(0.17f, 0.08f, 0.08f), new Color(0.93f, 0.42f, 0.36f, 0.5f), new Color(1f, 0.76f, 0.73f)),
        };
        var stat = new PanelContainer { MouseFilter = MouseFilterEnum.Pass };
        stat.AddThemeStyleboxOverride("panel", HudKit.Box(bg, edge, 7 * s, 11 * s, 8 * s));
        var statBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        statBox.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
        statBox.AddChild(Wrapped(v.StatusText, 15 * s, ink));
        if (v.Links.Count > 0)
        {
            var flow = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass };
            flow.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(6 * s));
            flow.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(2 * s));
            if (v.LinkCaption.Length > 0) flow.AddChild(HudKit.Label(v.LinkCaption, 14 * s, ink));
            for (var i = 0; i < v.Links.Count; i++)
            {
                var n = v.Links[i];
                var last = i == v.Links.Count - 1;
                if (n.Clickable)
                {
                    var link = new LinkButton { Text = n.Name + (last ? "" : ","), Underline = LinkButton.UnderlineMode.Always, FocusMode = FocusModeEnum.None, TooltipText = $"Open the {n.Name} card" };
                    link.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(14 * s));
                    link.AddThemeColorOverride("font_color", HudKit.Sky);
                    link.AddThemeColorOverride("font_hover_color", new Color(0.85f, 0.93f, 1f));
                    var id = n.Id;
                    link.Pressed += () => LinkPressed?.Invoke(id);
                    flow.AddChild(link);
                }
                else flow.AddChild(HudKit.Label(n.Name + (last ? "" : ","), 14 * s, ink));
            }
            statBox.AddChild(flow);
        }
        stat.AddChild(statBox);
        box.AddChild(stat);

        // Stat tiles: value over caption, as many columns as fit (each at least 120 px).
        if (v.Stats.Count > 0)
        {
            var columns = Mathf.Clamp(Mathf.FloorToInt((bodyW + 6 * s) / (120 * s + 6 * s)), 1, v.Stats.Count);
            var grid = new GridContainer { Columns = columns, MouseFilter = MouseFilterEnum.Ignore };
            grid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(6 * s));
            grid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(6 * s));
            var tileW = Mathf.Floor((bodyW - (columns - 1) * 6 * s) / columns);
            foreach (var (value, label) in v.Stats)
            {
                var t = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(tileW, 0) };
                t.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.067f, 0.106f, 0.19f), HudKit.Line, 6 * s, 8 * s, 6 * s));
                var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                col.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * s));
                var val = HudKit.Label(value, 17 * s, HudKit.Ink, bold: true);
                val.AutowrapMode = TextServer.AutowrapMode.Word;
                col.AddChild(val);
                var cap = HudKit.Label(label.ToUpperInvariant(), 11.5f * s, HudKit.Dim, condensed: true);
                cap.AutowrapMode = TextServer.AutowrapMode.Word;
                col.AddChild(cap);
                t.AddChild(col);
                grid.AddChild(t);
            }
            box.AddChild(grid);
        }

        // Table: TableHead / Each / You have.
        if (v.Rows.Count > 0)
        {
            var table = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
            table.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(18 * s));
            table.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(5 * s));
            Label Cell(string text, Color colour, bool head, bool right)
            {
                var l = head ? HudKit.Label(text, 12 * s, HudKit.Dim, condensed: true) : HudKit.Label(text, 15 * s, colour);
                l.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                if (!right) l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                l.CustomMinimumSize = new Vector2(right ? 80 * s : 0, 0);
                return l;
            }
            table.AddChild(Cell(v.TableHead, HudKit.Dim, true, false));
            table.AddChild(Cell("EACH", HudKit.Dim, true, true));
            table.AddChild(Cell("YOU HAVE", HudKit.Dim, true, true));
            foreach (var n in v.Rows)
            {
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
                row.AddChild(new TextureRect
                {
                    Texture = ItemArt.Picture(n.Id) ?? Icons.Get(n.Id), CustomMinimumSize = new Vector2(20, 20) * s,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TextureFilter = TextureFilterEnum.LinearWithMipmaps, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore,
                });
                var name = Cell(n.Name, HudKit.Ink, false, false);
                name.AutowrapMode = TextServer.AutowrapMode.Word;
                row.AddChild(name);
                table.AddChild(row);
                table.AddChild(Cell(n.Each.ToString(), HudKit.Ink, false, true));
                table.AddChild(Cell(n.Have.ToString(), n.Short ? new Color(1f, 0.48f, 0.42f) : HudKit.Good, false, true));
            }
            box.AddChild(table);
        }

        // Bullets ("What it does").
        if (v.Bullets.Count > 0)
        {
            var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            list.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
            if (v.BulletsHead.Length > 0) list.AddChild(HudKit.Label(v.BulletsHead.ToUpperInvariant(), 12 * s, HudKit.Dim, condensed: true));
            foreach (var b in v.Bullets)
            {
                var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                line.AddThemeConstantOverride("separation", Mathf.RoundToInt(7 * s));
                var dot = HudKit.Label("•", 15 * s, v.Colour);
                dot.SizeFlagsVertical = SizeFlags.ShrinkBegin;
                line.AddChild(dot);
                line.AddChild(Wrapped(b, 15 * s, HudKit.Ink));
                list.AddChild(line);
            }
            box.AddChild(list);
        }

        if (v.Description.Length > 0) box.AddChild(Wrapped(v.Description, 15 * s, HudKit.Ink, bodyW));
        foreach (var (text, colour) in v.Notes) box.AddChild(Wrapped(text, 13.5f * s, colour, bodyW));
        outer.AddChild(_scroll);

        // Buttons + hint (stay put under the body).
        var buttons = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass };
        buttons.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(8 * s));
        buttons.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(6 * s));
        foreach (var b in v.Buttons)
        {
            var button = HudKit.ActionButton(b.Label, b.Primary, s);
            button.Disabled = !b.Enabled;
            if (b.Tip.Length > 0) button.TooltipText = b.Tip;
            var action = b.Action;
            button.Pressed += () => ActionPressed?.Invoke(action);
            buttons.AddChild(button);
        }
        var shut = HudKit.ActionButton("CLOSE  [ESC]", false, s);
        shut.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(shut);
        outer.AddChild(buttons);
        if (v.Hint.Length > 0) outer.AddChild(Wrapped(v.Hint, 12 * s, HudKit.Dim, inner));
    }
}
