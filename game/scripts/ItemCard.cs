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

/// <summary>
/// The full card of a Machine Shop item (owner 2026-11-12: anything you can't make right now is greyed out but still
/// clickable to see its full card), modal over the map like the build menu's <see cref="BuildingCard"/>: a dimmed
/// backdrop (a click on it closes the card), Esc / × close, and no click reaches the map while it is open. Big item
/// picture, name, kind, hotkey, times, a green "Can make now" or red "Can't make yet" box whose missing items are links
/// to their own cards, the inputs table, the description, stock, and QUEUE ONE [Enter] / QUEUE 5 [Shift+Enter] /
/// CLOSE [Esc]. The owner hasn't decided whether short items may be queued, so (as the sim does today: an order waits
/// for its inputs) the button then reads "Queue anyway (waits for inputs)". Sizes scale from 1280×720.
/// </summary>
public partial class ItemCard : Control
{
    /// <summary>Queue this many of the card's item.</summary>
    public event Action<int>? QueuePressed;
    /// <summary>A missing item's link: open that item's card.</summary>
    public event Action<string>? LinkPressed;
    public event Action? CloseRequested;

    private string _signature = "";
    private Vector2 _builtFor;
    private float _builtScale;

    public bool IsOpen => Visible;

    /// <summary>The item whose card is open (null = closed).</summary>
    public string? ItemId { get; private set; }

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        GuiInput += e =>
        {
            // The dimmed backdrop: a left or right click outside the card closes it.
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right })
            {
                AcceptEvent();
                CloseRequested?.Invoke();
            }
        };
    }

    /// <summary>Opens or refreshes the card (rebuilt only when something on it changed).</summary>
    public void Show(ItemCardView v)
    {
        var screen = GetViewportRect().Size;
        var s = HudKit.ScaleFor(screen);
        var sig = $"{v.Id}|{v.Name}|{v.Kind}|{v.Key}|{v.Times}|{v.CanMake}|{v.Description}|{v.StockLine}|{v.Problem}|{v.Note}|" +
                  string.Join(";", v.Needs.Select(n => $"{n.Id},{n.Each},{n.Have},{n.Short},{n.Makeable}"));
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

    private void Build(ItemCardView v, Vector2 screen, float s)
    {
        HudKit.Clear(this);
        var dim = new ColorRect { Color = new Color(0.005f, 0.01f, 0.025f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var width = Mathf.Min(screen.X - 40 * s, 640 * s);
        var pad = 18 * s;
        var inner = width - pad * 2;
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(width, 0) };
        var frame = HudKit.Box(HudKit.Panel, new Color(0.2f, 0.25f, 0.37f), 10 * s, pad, pad);
        frame.BorderWidthTop = Mathf.RoundToInt(4 * s);
        frame.BorderColor = v.Colour;
        frame.BorderWidthLeft = frame.BorderWidthRight = frame.BorderWidthBottom = Mathf.Max(1, Mathf.RoundToInt(s));
        panel.AddThemeStyleboxOverride("panel", frame);
        centre.AddChild(panel);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(inner, 0) };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(11 * s));
        panel.AddChild(box);

        // Header: big picture, name, kind · hotkey, times, ×.
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        head.AddThemeConstantOverride("separation", Mathf.RoundToInt(14 * s));
        var picSize = Mathf.Min(128 * s, (screen.Y - 420 * s) * 0.5f);
        picSize = Mathf.Max(picSize, 72 * s);
        var plate = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(picSize, picSize), SizeFlagsVertical = SizeFlags.ShrinkBegin };
        plate.AddThemeStyleboxOverride("panel", HudKit.Box(v.Colour.Darkened(0.78f), v.Colour.Darkened(0.4f), 12 * s));
        var pic = HudKit.Picture(new Vector2(picSize, picSize));
        pic.Texture = ItemArt.PictureOrIcon(v.Id, v.Colour);
        plate.AddChild(pic);
        head.AddChild(plate);
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        names.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
        var title = HudKit.Label(v.Name.ToUpperInvariant(), 27 * s, HudKit.Ink, bold: true);
        title.AutowrapMode = TextServer.AutowrapMode.Word;
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        names.AddChild(title);
        var kind = HudKit.Label(v.Kind + (v.Key.Length > 0 ? $"  ·  hotkey {v.Key}" : "  ·  no hotkey"), 14 * s, v.Colour, condensed: true);
        kind.AutowrapMode = TextServer.AutowrapMode.Word;
        names.AddChild(kind);
        var times = HudKit.Label(v.Times, 14 * s, HudKit.Dim);
        times.AutowrapMode = TextServer.AutowrapMode.Word;
        names.AddChild(times);
        head.AddChild(names);
        var close = new Button { Text = "×", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(32, 32) * s, SizeFlagsVertical = SizeFlags.ShrinkBegin, TooltipText = "Close (Esc)" };
        close.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(18 * s));
        HudKit.StyleTile(close, 6 * s);
        close.Pressed += () => CloseRequested?.Invoke();
        head.AddChild(close);
        box.AddChild(head);

        // Can make now / can't make yet (missing items link to their cards).
        var missing = v.Needs.Where(n => n.Short).ToList();
        var stat = new PanelContainer { MouseFilter = MouseFilterEnum.Pass };
        stat.AddThemeStyleboxOverride("panel", v.CanMake
            ? HudKit.Box(new Color(0.075f, 0.15f, 0.1f), new Color(0.56f, 0.84f, 0.48f, 0.5f), 7 * s, 11 * s, 8 * s)
            : HudKit.Box(new Color(0.17f, 0.08f, 0.08f), new Color(0.93f, 0.42f, 0.36f, 0.5f), 7 * s, 11 * s, 8 * s));
        var statBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        statBox.AddThemeConstantOverride("separation", Mathf.RoundToInt(4 * s));
        if (v.CanMake)
        {
            var yes = HudKit.Label("Can make now. It starts as soon as the shop is free.", 15 * s, new Color(0.78f, 0.95f, 0.74f));
            yes.AutowrapMode = TextServer.AutowrapMode.Word;
            statBox.AddChild(yes);
        }
        else
        {
            var lines = missing.Count > 0
                ? "Can't make yet: missing " + string.Join(" and ", missing.Select(n => $"{n.Each - n.Have} {n.Name}")) + "."
                : $"Can't queue it: {v.Problem}.";
            var no = HudKit.Label(lines, 15 * s, new Color(1f, 0.76f, 0.73f));
            no.AutowrapMode = TextServer.AutowrapMode.Word;
            statBox.AddChild(no);
            if (missing.Count > 0)
            {
                var flow = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass };
                flow.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(6 * s));
                flow.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(2 * s));
                flow.AddChild(HudKit.Label("Make or get:", 14 * s, new Color(1f, 0.76f, 0.73f)));
                for (var i = 0; i < missing.Count; i++)
                {
                    var n = missing[i];
                    var last = i == missing.Count - 1;
                    if (n.Makeable)
                    {
                        var link = new LinkButton { Text = n.Name + (last ? "" : ","), Underline = LinkButton.UnderlineMode.Always, FocusMode = FocusModeEnum.None, TooltipText = $"Open the {n.Name} card" };
                        link.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(14 * s));
                        link.AddThemeColorOverride("font_color", HudKit.Sky);
                        link.AddThemeColorOverride("font_hover_color", new Color(0.85f, 0.93f, 1f));
                        var id = n.Id;
                        link.Pressed += () => LinkPressed?.Invoke(id);
                        flow.AddChild(link);
                    }
                    else flow.AddChild(HudKit.Label($"{n.Name} (from a refinery or foundry){(last ? "" : ",")}", 14 * s, new Color(1f, 0.76f, 0.73f)));
                }
                statBox.AddChild(flow);
            }
        }
        stat.AddChild(statBox);
        box.AddChild(stat);

        // Needs / Each / You have.
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
        table.AddChild(Cell("NEEDS", HudKit.Dim, true, false));
        table.AddChild(Cell("EACH", HudKit.Dim, true, true));
        table.AddChild(Cell("YOU HAVE", HudKit.Dim, true, true));
        foreach (var n in v.Needs)
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

        if (v.Description.Length > 0)
        {
            var desc = HudKit.Label(v.Description, 15 * s, HudKit.Ink);
            desc.AutowrapMode = TextServer.AutowrapMode.Word;
            desc.CustomMinimumSize = new Vector2(inner, 0);
            box.AddChild(desc);
        }
        foreach (var (text, colour) in new[] { (v.StockLine, HudKit.Dim), (v.Note, HudKit.Dim), (v.CanMake || v.Problem.Length == 0 ? "" : $"Can't queue it: {v.Problem}", new Color(1f, 0.48f, 0.42f)) })
        {
            if (text.Length == 0) continue;
            var l = HudKit.Label(text, 13.5f * s, colour);
            l.AutowrapMode = TextServer.AutowrapMode.Word;
            l.CustomMinimumSize = new Vector2(inner, 0);
            box.AddChild(l);
        }

        // Buttons.
        var buttons = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass };
        buttons.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(8 * s));
        buttons.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(6 * s));
        var canQueue = v.Problem.Length == 0;
        var one = HudKit.ActionButton(v.CanMake ? "QUEUE ONE  [ENTER]" : "QUEUE ANYWAY (WAITS FOR INPUTS)  [ENTER]", true, s);
        one.Disabled = !canQueue;
        one.TooltipText = v.CanMake ? "Add one order to this shop's queue" : "The order waits in the queue until its inputs are in stock";
        one.Pressed += () => QueuePressed?.Invoke(1);
        buttons.AddChild(one);
        var five = HudKit.ActionButton("QUEUE 5  [SHIFT+ENTER]", false, s);
        five.Disabled = !canQueue;
        five.Pressed += () => QueuePressed?.Invoke(5);
        buttons.AddChild(five);
        var shut = HudKit.ActionButton("CLOSE  [ESC]", false, s);
        shut.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(shut);
        box.AddChild(buttons);
        var hint = HudKit.Label("On the recipe tiles: click = this card · Shift+click = queue 5 · Ctrl+click = queue 1 · right-click = remove one · number keys queue 1 (Shift 5)", 12 * s, HudKit.Dim);
        hint.AutowrapMode = TextServer.AutowrapMode.Word;
        hint.CustomMinimumSize = new Vector2(inner, 0);
        box.AddChild(hint);
    }
}
