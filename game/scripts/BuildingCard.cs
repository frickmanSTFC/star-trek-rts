using System;
using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>A stat tile on the large card ("BUILD 30 s", "POWER −8"); Tone colours the value (null = ink).</summary>
public sealed record CardStat(string Label, string Value, Color? Tone = null);

/// <summary>A piece of a card section.</summary>
public abstract record CardBlock;

/// <summary>A paragraph (Dim = secondary text).</summary>
public sealed record CardText(string Text, bool Dim = false) : CardBlock;

/// <summary>A flow row: Lead, then In → Out with resource icons ("2 Duranium Ore → 1 Duranium"), then a dim Note.
/// Out empty = no arrow (e.g. "Burns 3 Deuterium a minute").</summary>
public sealed record CardFlow(string Lead, IReadOnlyList<(string ResourceId, string Text)> In, IReadOnlyList<(string ResourceId, string Text)> Out, string Note = "") : CardBlock;

/// <summary>A table: header captions, column shares of the width, rows of cells (cells may hold line breaks).</summary>
public sealed record CardTable(IReadOnlyList<string> Header, IReadOnlyList<float> Widths, IReadOnlyList<IReadOnlyList<string>> Rows) : CardBlock;

/// <summary>A row of chips with a colour swatch (terrain); Off chips are dimmed and marked ×.</summary>
public sealed record CardChips(IReadOnlyList<(string Text, Color Colour, bool On)> Chips) : CardBlock;

/// <summary>Label → text rows (TAKES / GIVES).</summary>
public sealed record CardPairs(IReadOnlyList<(string Label, string Text)> Pairs) : CardBlock;

/// <summary>A bulleted list.</summary>
public sealed record CardBullets(IReadOnlyList<string> Items) : CardBlock;

/// <summary>A captioned section of the card's right column.</summary>
public sealed record CardSection(string Caption, IReadOnlyList<CardBlock> Blocks);

/// <summary>
/// What the large card shows that only changes with the data, research or the base (the driver rebuilds it then):
/// ModelKey for the turntable ("refinery", "unit:mining_crawler"), IconId (fallback picture), title, level pips
/// (Pips boxes, PipsOn lit) with LevelLine ("Mk I → Mk II → Mk III"), SubLine ("Industry · key 2"), Badge on the
/// picture, stat tiles, the description, and the right column's sections.
/// </summary>
public sealed record CardView(string ModelKey, string IconId, string Title, int Pips, int PipsOn, string LevelLine, string SubLine, string Badge,
    IReadOnlyList<CardStat> Stats, string Description, IReadOnlyList<CardSection> Sections);

/// <summary>What the large card shows that changes every frame: cost chips (text with what you have, Short = red),
/// CostNote ("30 s to build"), a Warning box (empty = none), the action button and the hint line.</summary>
public sealed record CardLive(IReadOnlyList<(string ResourceId, string Text, bool Short)> Cost, string CostNote, string Warning,
    string ActionLabel, bool ActionEnabled, string Hint);

/// <summary>
/// The large centre card (owner 2026-11-06, the mockup's "Large centre card"): the first pick of a building, or of a
/// Vehicle Bay order, opens it centred over the dimmed world. Two columns: on the left the title, level line, a live
/// turning model (<see cref="ModelThumbnails.ShowHero"/>; drag to turn it), cost with what you have, stat tiles, the
/// description, a warning when you can't, PLACE / ORDER [Enter] and CLOSE [Esc]; on the right (scrolls) everything else
/// from the data. A click on the dimmed backdrop also closes it. The whole screen counts as UI while it is open.
/// Sizes scale from 1280×720.
/// </summary>
public partial class BuildingCard : Control
{
    public event Action? ActionPressed;
    public event Action? CloseRequested;

    private ModelThumbnails? _thumbs;
    private CardView? _view;
    private Vector2 _builtFor;
    private float _s = 1f;

    private PanelContainer _panel = null!;
    private TextureRect _hero = null!;
    private HFlowContainer _cost = null!;
    private readonly List<HudKit.CostChip> _costChips = new();
    private Label _costNote = null!;
    private PanelContainer _warning = null!;
    private Label _warningText = null!;
    private Button _action = null!;
    private Label _hint = null!;
    private CardLive? _live;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        GuiInput += e =>
        {
            // The dimmed backdrop: a left or right click outside the card closes it (the wheel does nothing).
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right }) CloseRequested?.Invoke();
        };
    }

    public void SetThumbnails(ModelThumbnails thumbs) => _thumbs = thumbs;

    /// <summary>Opens (or refills) the card. Content is rebuilt from the view; the turntable switches to its model.</summary>
    public void Open(CardView view)
    {
        _view = view;
        Visible = true;
        Build();
        _thumbs?.ShowHero(view.ModelKey);
        if (_live != null) SetLive(_live, force: true);
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        _view = null;
        _live = null;
        _thumbs?.HideHero();
    }

    public override void _Process(double delta)
    {
        if (Visible && _view != null && GetViewportRect().Size != _builtFor)
        {
            Build();
            if (_live != null) SetLive(_live, force: true);
        }
    }

    private void Build()
    {
        var view = _view!;
        var screen = GetViewportRect().Size;
        _builtFor = screen;
        _s = HudKit.ScaleFor(screen);
        var s = _s;
        HudKit.Clear(this);
        _costChips.Clear();

        var dim = new ColorRect { Color = new Color(0.005f, 0.01f, 0.025f, 0.62f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // the dimmed world behind the card

        var pad = 18 * s;
        var width = Mathf.Min(screen.X - 40 * s, 900 * s);
        var height = Mathf.Min(screen.Y - 36 * s, 690 * s);
        var inner = height - pad * 2;
        var gap = 22 * s;
        var leftW = Mathf.Round((width - pad * 2 - gap) * 0.47f);
        var rightW = Mathf.Round(width - pad * 2 - gap - leftW);

        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", HudKit.Box(HudKit.Panel, HudKit.Accent, 10 * s, pad, pad));
        var cols = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        cols.AddThemeConstantOverride("separation", Mathf.RoundToInt(gap));

        // ---------------- left column
        var leftScroll = Scroll(leftW, inner);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(leftW - 4 * s, 0), MouseFilter = MouseFilterEnum.Pass };
        left.AddThemeConstantOverride("separation", Mathf.RoundToInt(9 * s));

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", Mathf.RoundToInt(2 * s));
        names.AddChild(HudKit.Label(view.Title.ToUpperInvariant(), 27 * s, HudKit.Ink, bold: true));
        var sub = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        sub.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * s));
        if (view.Pips > 0)
        {
            var pips = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
            pips.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
            for (var i = 0; i < view.Pips; i++)
                pips.AddChild(new ColorRect { Color = i < view.PipsOn ? HudKit.Accent : HudKit.Line, CustomMinimumSize = new Vector2(16 * s, 4 * s), MouseFilter = MouseFilterEnum.Ignore });
            sub.AddChild(pips);
        }
        if (view.LevelLine.Length > 0) sub.AddChild(HudKit.Label(view.LevelLine, 12 * s, HudKit.Accent, condensed: true));
        if (view.SubLine.Length > 0) sub.AddChild(HudKit.Label("·  " + view.SubLine, 12 * s, HudKit.Dim, condensed: true));
        names.AddChild(sub);
        head.AddChild(names);
        var close = new Button { Text = "×", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(30 * s, 30 * s), SizeFlagsVertical = SizeFlags.ShrinkBegin, TooltipText = "Close (Esc)" };
        close.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(18 * s));
        HudKit.StyleTile(close, 6 * s);
        close.Pressed += () => CloseRequested?.Invoke();
        head.AddChild(close);
        left.AddChild(head);

        // The live model on a navy plate.
        var heroH = Mathf.Clamp(inner - 360 * s, 150 * s, 330 * s);
        var plate = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, heroH) };
        plate.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.035f, 0.055f, 0.1f), HudKit.Line, 8 * s));
        _hero = HudKit.Picture(new Vector2(0, heroH));
        _hero.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _hero.TextureFilter = TextureFilterEnum.Linear; // a ViewportTexture has no mipmaps
        _hero.MouseFilter = MouseFilterEnum.Stop;
        _hero.MouseDefaultCursorShape = CursorShape.Drag;
        _hero.Texture = _thumbs != null ? _thumbs.HeroTexture : Icons.Get(view.IconId, HudKit.Accent);
        _hero.GuiInput += e =>
        {
            if (e is InputEventMouseMotion m && (m.ButtonMask & MouseButtonMask.Left) != 0 && _thumbs != null) _thumbs.HeroDrag += m.Relative.X * 0.012f;
        };
        plate.AddChild(_hero);
        if (view.Badge.Length > 0)
        {
            var badge = BuildDrawer.Badge(view.Badge, HudKit.Good, s, left: true);
            _hero.AddChild(badge);
        }
        var drag = HudKit.Label("Drag to turn", 10 * s, HudKit.Dim, condensed: true);
        drag.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft, LayoutPresetMode.Minsize);
        drag.GrowVertical = GrowDirection.Begin;
        drag.OffsetLeft = 8 * s;
        drag.OffsetBottom = -6 * s;
        _hero.AddChild(drag);
        left.AddChild(plate);

        var costRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        costRow.AddThemeConstantOverride("separation", Mathf.RoundToInt(10 * s));
        _cost = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cost.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(12 * s));
        costRow.AddChild(_cost);
        _costNote = HudKit.Label("", 12 * s, HudKit.Dim, condensed: true);
        _costNote.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        costRow.AddChild(_costNote);
        left.AddChild(costRow);

        if (view.Stats.Count > 0)
        {
            var columns = view.Stats.Count <= 4 ? view.Stats.Count : (view.Stats.Count + 1) / 2;
            var stats = new GridContainer { Columns = Mathf.Max(1, columns), MouseFilter = MouseFilterEnum.Ignore };
            stats.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(6 * s));
            stats.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(6 * s));
            var tileW = (leftW - 8 * s - 6 * s * (columns - 1)) / columns;
            foreach (var st in view.Stats)
            {
                var t = HudKit.PanelBox(HudKit.Chip, new Color(0, 0, 0, 0), 6 * s, 8 * s, 5 * s);
                t.CustomMinimumSize = new Vector2(tileW, 0);
                var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                v.AddThemeConstantOverride("separation", 0);
                v.AddChild(HudKit.Label(st.Label.ToUpperInvariant(), 9.5f * s, HudKit.Dim, condensed: true));
                var value = HudKit.Label(st.Value, 15 * s, st.Tone ?? HudKit.Ink, bold: true);
                value.ClipText = true;
                v.AddChild(value);
                t.AddChild(v);
                stats.AddChild(t);
            }
            left.AddChild(stats);
        }

        left.AddChild(Wrapped(view.Description, 12.5f * s, HudKit.Ink, leftW - 6 * s));

        _warning = HudKit.PanelBox(new Color(0.16f, 0.09f, 0.03f, 0.85f), new Color(0.6f, 0.38f, 0.12f), 6 * s, 10 * s, 7 * s);
        _warningText = Wrapped("", 12 * s, HudKit.Warn, leftW - 30 * s);
        _warning.AddChild(_warningText);
        _warning.Visible = false;
        left.AddChild(_warning);

        var buttons = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        buttons.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * s));
        _action = HudKit.ActionButton("", true, s);
        _action.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _action.Pressed += () => ActionPressed?.Invoke();
        buttons.AddChild(_action);
        var closeButton = HudKit.ActionButton("CLOSE [ESC]", false, s);
        closeButton.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(closeButton);
        left.AddChild(buttons);
        _hint = Wrapped("", 11 * s, HudKit.Dim, leftW - 6 * s);
        left.AddChild(_hint);
        leftScroll.AddChild(left);
        cols.AddChild(leftScroll);

        // ---------------- right column
        var rightScroll = Scroll(rightW, inner);
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(rightW - 14 * s, 0), MouseFilter = MouseFilterEnum.Pass };
        right.AddThemeConstantOverride("separation", Mathf.RoundToInt(7 * s));
        var textW = rightW - 18 * s;
        var first = true;
        foreach (var section in view.Sections)
        {
            if (!first) right.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6 * s), MouseFilter = MouseFilterEnum.Ignore });
            first = false;
            right.AddChild(HudKit.Label(section.Caption.ToUpperInvariant(), 11.5f * s, HudKit.Accent, bold: true));
            foreach (var block in section.Blocks) right.AddChild(Block(block, textW, s));
        }
        rightScroll.AddChild(right);
        cols.AddChild(rightScroll);

        _panel.AddChild(cols);
        AddChild(_panel);
        _panel.ResetSize();
        var size = _panel.GetCombinedMinimumSize();
        _panel.Position = ((screen - size) * 0.5f).Floor();
    }

    private static ScrollContainer Scroll(float width, float height)
    {
        var sc = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(width, height),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Pass,
        };
        return sc;
    }

    private static Label Wrapped(string text, float size, Color colour, float width)
    {
        var l = HudKit.Label(text, size, colour);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(Mathf.Max(40, width), 0);
        return l;
    }

    private static Control Block(CardBlock block, float width, float s)
    {
        switch (block)
        {
            case CardText t:
                return Wrapped(t.Text.Replace("\r", ""), 12 * s, t.Dim ? HudKit.Dim : HudKit.Ink, width);
            case CardFlow f:
            {
                var p = HudKit.PanelBox(HudKit.Chip, new Color(0, 0, 0, 0), 6 * s, 9 * s, 5 * s);
                var row = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(width - 18 * s, 0) };
                row.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(7 * s));
                if (f.Lead.Length > 0) row.AddChild(HudKit.Label(f.Lead, 12.5f * s, HudKit.Dim, condensed: true));
                Side(row, f.In, s);
                if (f.Out.Count > 0)
                {
                    row.AddChild(HudKit.Label("→", 13 * s, HudKit.Accent, bold: true));
                    Side(row, f.Out, s);
                }
                if (f.Note.Length > 0) row.AddChild(HudKit.Label(f.Note, 11.5f * s, HudKit.Dim, condensed: true));
                p.AddChild(row);
                return p;
            }
            case CardTable tb:
            {
                var grid = new GridContainer { Columns = tb.Header.Count, MouseFilter = MouseFilterEnum.Ignore };
                grid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(8 * s));
                grid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(5 * s));
                var usable = width - 8 * s * (tb.Header.Count - 1);
                for (var i = 0; i < tb.Header.Count; i++)
                {
                    var h = HudKit.Label(tb.Header[i].ToUpperInvariant(), 9.5f * s, HudKit.Dim, condensed: true);
                    h.CustomMinimumSize = new Vector2(usable * tb.Widths[i], 0);
                    grid.AddChild(h);
                }
                foreach (var r in tb.Rows)
                    for (var i = 0; i < tb.Header.Count; i++)
                    {
                        var cell = Wrapped(i < r.Count ? r[i] : "", 11.5f * s, i == 0 ? HudKit.Accent : HudKit.Ink, usable * tb.Widths[i]);
                        if (i == 0) cell.AddThemeFontOverride("font", HudKit.Condensed(true));
                        grid.AddChild(cell);
                    }
                return grid;
            }
            case CardChips c:
            {
                var flow = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(width, 0) };
                flow.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(5 * s));
                flow.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(5 * s));
                foreach (var (text, colour, on) in c.Chips)
                {
                    var chip = HudKit.PanelBox(HudKit.Chip, new Color(0, 0, 0, 0), 5 * s, 7 * s, 2 * s);
                    var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                    row.AddThemeConstantOverride("separation", Mathf.RoundToInt(5 * s));
                    row.AddChild(new ColorRect { Color = colour, CustomMinimumSize = new Vector2(10 * s, 10 * s), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore });
                    row.AddChild(HudKit.Label(on ? text : text + " ×", 11.5f * s, on ? HudKit.Ink : HudKit.Dim, condensed: true));
                    chip.AddChild(row);
                    if (!on) chip.Modulate = new Color(1, 1, 1, 0.55f);
                    flow.AddChild(chip);
                }
                return flow;
            }
            case CardPairs pr:
            {
                var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
                grid.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(10 * s));
                grid.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(5 * s));
                var labelW = 62 * s;
                foreach (var (label, text) in pr.Pairs)
                {
                    var l = HudKit.Label(label.ToUpperInvariant(), 10 * s, HudKit.Dim, condensed: true);
                    l.CustomMinimumSize = new Vector2(labelW, 0);
                    l.SizeFlagsVertical = SizeFlags.ShrinkBegin;
                    grid.AddChild(l);
                    grid.AddChild(Wrapped(text.Replace("\r", ""), 12 * s, HudKit.Ink, width - labelW - 10 * s));
                }
                return grid;
            }
            case CardBullets b:
            {
                var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                list.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
                foreach (var item in b.Items) list.AddChild(Wrapped("•  " + item, 12 * s, HudKit.Ink, width));
                return list;
            }
        }
        return new Control();
    }

    private static void Side(HFlowContainer row, IReadOnlyList<(string ResourceId, string Text)> amounts, float s)
    {
        for (var i = 0; i < amounts.Count; i++)
        {
            if (i > 0) row.AddChild(HudKit.Label("+", 12 * s, HudKit.Dim));
            var chip = new HudKit.CostChip(amounts[i].ResourceId, amounts[i].Text, 12.5f * s);
            chip.Set(amounts[i].Text, false, HudKit.Ink);
            row.AddChild(chip.Root);
        }
    }

    /// <summary>The per-frame part: cost (with what you have), warning, action button and hint. Only changed labels are touched.</summary>
    public void SetLive(CardLive live, bool force = false)
    {
        _live = live;
        if (!Visible || _view == null) return;
        if (force || _costChips.Count != live.Cost.Count)
        {
            HudKit.Clear(_cost);
            _costChips.Clear();
            foreach (var c in live.Cost)
            {
                var chip = new HudKit.CostChip(c.ResourceId, c.Text, 13.5f * _s);
                _cost.AddChild(chip.Root);
                _costChips.Add(chip);
            }
        }
        for (var i = 0; i < live.Cost.Count; i++) _costChips[i].Set(live.Cost[i].Text, live.Cost[i].Short, HudKit.Ink);
        if (_costNote.Text != live.CostNote) _costNote.Text = live.CostNote;
        var warn = live.Warning.Length > 0;
        if (_warning.Visible != warn) _warning.Visible = warn;
        if (_warningText.Text != live.Warning) _warningText.Text = live.Warning;
        if (_action.Text != live.ActionLabel) _action.Text = live.ActionLabel;
        if (_action.Disabled == live.ActionEnabled) _action.Disabled = !live.ActionEnabled;
        if (_hint.Text != live.Hint) _hint.Text = live.Hint;
    }
}

/// <summary>What the placing strip shows: the building, its cost (Short = red), why the hovered tile won't do, the keys.</summary>
public sealed record StripView(string Id, string Title, IReadOnlyList<(string ResourceId, string Text, bool Short)> Cost, string Warning, string Hint);

/// <summary>
/// The compact placing strip (the mockup's "Placing strip"): while a building is being placed the large card shrinks to
/// this, above the command card at the right: thumbnail, PLACING NAME, cost, a red line when the hovered tile won't do,
/// and the keys. × stops placing; CARD [I] opens the large card again.
/// </summary>
public partial class PlacingStrip : PanelContainer
{
    public event Action? CloseRequested;
    public event Action? CardRequested;

    private float _s = -1f;
    private string _id = "";
    private TextureRect _picture = null!;
    private Label _title = null!;
    private HFlowContainer _cost = null!;
    private readonly List<HudKit.CostChip> _chips = new();
    private Label _warning = null!;
    private Label _hint = null!;
    private int _thumbVersion = -1;
    private float _bottom = PlanetHud.BottomHeight;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void SetBottom(float bottom) => _bottom = bottom;

    public bool IsOverUi(Vector2 mouse) => Visible && GetGlobalRect().HasPoint(mouse);

    /// <summary>Shows the strip (null hides it); thumbs supplies the picture.</summary>
    public void Display(StripView? v, ModelThumbnails? thumbs)
    {
        if (v == null)
        {
            if (Visible) Visible = false;
            return;
        }
        var screen = GetViewportRect().Size;
        var s = HudKit.ScaleFor(screen);
        if (!Mathf.IsEqualApprox(s, _s)) Build(s);
        if (v.Id != _id)
        {
            _id = v.Id;
            _thumbVersion = -1;
            _picture.Texture = Icons.Get(v.Id, HudKit.Accent);
            _picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        }
        if (thumbs != null && thumbs.Version != _thumbVersion)
        {
            _thumbVersion = thumbs.Version;
            if (thumbs.Thumb(v.Id) is { } tex)
            {
                _picture.Texture = tex;
                _picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            }
        }
        var title = v.Title.ToUpperInvariant();
        if (_title.Text != title) _title.Text = title;
        if (_chips.Count != v.Cost.Count)
        {
            HudKit.Clear(_cost);
            _chips.Clear();
            foreach (var c in v.Cost)
            {
                var chip = new HudKit.CostChip(c.ResourceId, c.Text, 11 * _s);
                _cost.AddChild(chip.Root);
                _chips.Add(chip);
            }
        }
        for (var i = 0; i < v.Cost.Count; i++) _chips[i].Set(v.Cost[i].Text, v.Cost[i].Short, HudKit.Dim);
        if (_warning.Text != v.Warning) _warning.Text = v.Warning;
        var warn = v.Warning.Length > 0;
        if (_warning.Visible != warn) _warning.Visible = warn;
        if (_hint.Text != v.Hint) _hint.Text = v.Hint;
        if (!Visible) Visible = true;

        ResetSize();
        var size = GetCombinedMinimumSize();
        Position = new Vector2(screen.X - size.X - 16, Mathf.Max(PlanetHud.TopHeight + 8, screen.Y - _bottom - 12 - size.Y));
    }

    private void Build(float s)
    {
        _s = s;
        HudKit.Clear(this);
        _chips.Clear();
        _id = "";
        AddThemeStyleboxOverride("panel", HudKit.Box(HudKit.Panel, HudKit.Accent, 10 * s, 12 * s, 10 * s));
        var width = 376 * s;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(width - 24 * s, 0) };
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(12 * s));
        _picture = HudKit.Picture(new Vector2(84 * s, 64 * s));
        _picture.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(_picture);

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
        _title = HudKit.Label("", 17 * s, HudKit.Ink, bold: true);
        text.AddChild(_title);
        _cost = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        _cost.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(10 * s));
        text.AddChild(_cost);
        var textW = width - 24 * s - 84 * s - 12 * s * 2 - 64 * s;
        _warning = HudKit.Label("", 11.5f * s, HudKit.Warn);
        _warning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _warning.CustomMinimumSize = new Vector2(textW, 0);
        text.AddChild(_warning);
        _hint = HudKit.Label("", 10.5f * s, HudKit.Dim);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.CustomMinimumSize = new Vector2(textW, 0);
        text.AddChild(_hint);
        row.AddChild(text);

        var side = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        side.AddThemeConstantOverride("separation", Mathf.RoundToInt(6 * s));
        var close = new Button { Text = "×", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(64 * s, 26 * s), TooltipText = "Stop placing (Esc / right-click)" };
        close.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(16 * s));
        HudKit.StyleTile(close, 6 * s);
        close.Pressed += () => CloseRequested?.Invoke();
        side.AddChild(close);
        var card = new Button { Text = "CARD [I]", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(64 * s, 26 * s), TooltipText = "The building's card" };
        card.AddThemeFontOverride("font", HudKit.Condensed(true));
        card.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(11 * s));
        HudKit.StyleTile(card, 6 * s);
        card.Pressed += () => CardRequested?.Invoke();
        side.AddChild(card);
        row.AddChild(side);
        AddChild(row);
    }
}
