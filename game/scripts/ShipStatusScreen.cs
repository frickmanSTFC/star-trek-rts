using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FinalFrontier;

/// <summary>A button on a status line: Id goes back to the driver (greyed still clicks: the driver says why).</summary>
public sealed record StatusButton(string Id, string Label, bool Enabled, string Tip, Color Colour);

/// <summary>One line of a status section: label, value, colour; Bar 0..1 draws a meter under it (Mark = a floor tick).</summary>
public sealed record StatusLine(string Label, string Value, Color Colour, float? Bar = null, float? Mark = null, IReadOnlyList<StatusButton>? Buttons = null);

/// <summary>A titled block of lines (Note = a grey line under them).</summary>
public sealed record StatusSection(string Title, Color Colour, IReadOnlyList<StatusLine> Lines, string? Note = null);

/// <summary>What the ship status screen shows (the driver builds it from StarSystemState.StatusOf).</summary>
public sealed record ShipStatusPage(string Title, string Subtitle, float Hull, Color HullColour, string HullText, string BandText,
    IReadOnlyList<string> BandLines, IReadOnlyList<StatusSection> Left, IReadOnlyList<StatusSection> Right);

/// <summary>
/// The ship status screen (space jobs phase 1, docs/space-jobs-contract.md "VIEW", the design page's screen 3), in the
/// helm's look: a window over the viewscreen's open area (not over the data panel), opened by STATUS on the helm rail,
/// the HULL chip on the helm's top bar, or U in orbit; Esc / U / × close it. Left: hull integrity (big percentage in the
/// band's colour, a bar with the 25 / 50 / 75 band marks and what each band does), repairs (field repair, patch kits,
/// dockyard, with their buttons) and the ship log. Right: the four systems with their timers (FIX with a patch kit),
/// crew by division (bar = on duty, red tick = the floor; injured / lost), the hold (SELL / DROP / JETTISON where the
/// sim allows) and hiring at a station. Rebuilt only when the page changes (the driver hands it a new page 4×/s).
/// </summary>
public partial class ShipStatusScreen : Control
{
    public event Action<string>? ActionPicked;
    public event Action? Closed;

    private PanelContainer? _panel;
    private ShipStatusPage? _page;
    private string _key = "";
    private Rect2 _area;
    private Vector2 _builtFor;
    private float _s = 1f;

    private static readonly Color Black = new(0, 0, 0);
    private static readonly Color Grey = new(0.55f, 0.57f, 0.62f);

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public bool IsOverUi(Vector2 mouse) => Visible && _panel != null && _panel.GetGlobalRect().HasPoint(mouse);

    public void Open() => Visible = true;

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        Closed?.Invoke();
    }

    /// <summary>Every frame while open: the page and the area it may use (screen px). Null page = "no ship status".</summary>
    public void Show(ShipStatusPage? page, Rect2 area)
    {
        if (!Visible) return;
        var screen = GetViewportRect().Size;
        if (area.Size.X < 200 || area.Size.Y < 160) area = new Rect2(Vector2.Zero, screen);
        var key = page == null ? "none" : KeyOf(page);
        if (key != _key || screen != _builtFor || _panel == null || area != _area)
        {
            _key = key;
            _page = page;
            _builtFor = screen;
            _area = area;
            Rebuild(screen);
        }
        if (_panel != null)
        {
            var size = _panel.Size;
            _panel.Position = new Vector2(
                Mathf.Clamp(area.Position.X + (area.Size.X - size.X) / 2f, 4f, Mathf.Max(4f, screen.X - size.X - 4f)),
                Mathf.Clamp(area.Position.Y + P(8), 4f, Mathf.Max(4f, screen.Y - size.Y - 4f)));
        }
    }

    private static string KeyOf(ShipStatusPage p)
    {
        static string Sections(IReadOnlyList<StatusSection> list) => string.Join(";", list.Select(s =>
            s.Title + "|" + s.Note + "|" + string.Join(",", s.Lines.Select(l => $"{l.Label}={l.Value}/{l.Colour}/{l.Bar:0.00}/{l.Mark:0.00}/" +
                (l.Buttons == null ? "" : string.Join("+", l.Buttons.Select(b => $"{b.Id}:{b.Label}:{b.Enabled}:{b.Tip}")))))));
        return $"{p.Title}|{p.Subtitle}|{p.Hull:0.000}|{p.HullText}|{p.BandText}|{string.Join(",", p.BandLines)}|{Sections(p.Left)}#{Sections(p.Right)}";
    }

    private int P(float v) => Mathf.Max(1, Mathf.RoundToInt(v * _s));
    private int F(float v) => Mathf.Max(8, Mathf.RoundToInt(v * _s));

    private void Rebuild(Vector2 screen)
    {
        _panel?.QueueFree();
        _s = HudKit.ScaleFor(screen);
        var width = Mathf.Min(P(780), _area.Size.X - 16);
        var height = Mathf.Min(P(540), _area.Size.Y - 16);
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.93f), BorderColor = HelmScreen.Peach };
        style.SetBorderWidthAll(1);
        style.BorderWidthLeft = P(5);
        style.ContentMarginLeft = P(12);
        style.ContentMarginRight = P(12);
        style.ContentMarginTop = P(8);
        style.ContentMarginBottom = P(10);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(width, 0) };
        _panel.AddThemeStyleboxOverride("panel", style);
        AddChild(_panel);
        var root = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        root.AddThemeConstantOverride("separation", P(6));
        _panel.AddChild(root);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", P(6));
        var bar = Bar(HelmScreen.Peach, _page?.Title ?? "SHIP STATUS", F(17));
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(bar);
        var close = Pill("close", "×", HelmScreen.Lavender, true, Close, "Close (Esc / U)");
        Sfx.Tag(close, "back");
        head.AddChild(close);
        root.AddChild(head);

        if (_page == null)
        {
            var none = Text("No ship status yet: the flagship's hull, systems, crew and hold show here once the game has them.", F(14), Grey);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            root.AddChild(none);
            return;
        }
        root.AddChild(Text(_page.Subtitle, F(13), HelmScreen.Lavender));

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, Mathf.Max(P(160), height - P(70))),
            MouseFilter = MouseFilterEnum.Pass,
        };
        var columns = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", P(16));
        scroll.AddChild(columns);
        root.AddChild(scroll);
        var left = Column(columns);
        var right = Column(columns);

        // Hull: the big number, the bar with its bands, what the band does.
        left.AddChild(TitleBar("HULL INTEGRITY", _page.HullColour));
        var hullRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        hullRow.AddThemeConstantOverride("separation", P(10));
        hullRow.AddChild(Text(_page.HullText, F(34), _page.HullColour, true));
        var band = Text(_page.BandText, F(14), HelmScreen.Ink);
        band.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        band.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hullRow.AddChild(band);
        left.AddChild(hullRow);
        left.AddChild(new CivMeter { Colour = _page.HullColour, Fraction = _page.Hull, Marks = new[] { 0.25f, 0.5f, 0.75f }, CustomMinimumSize = new Vector2(0, P(10)) });
        var scale = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var w in new[] { "0 CRIPPLED", "25 CRITICAL", "50 DAMAGED", "75 WORN", "100" })
        {
            var l = Text(w, F(11), Grey);
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scale.AddChild(l);
        }
        left.AddChild(scale);
        foreach (var line in _page.BandLines)
        {
            var l = Text(line, F(12), Grey);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            left.AddChild(l);
        }
        foreach (var s in _page.Left) AddSection(left, s);
        foreach (var s in _page.Right) AddSection(right, s);
    }

    private VBoxContainer Column(HBoxContainer parent)
    {
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f };
        col.AddThemeConstantOverride("separation", P(5));
        parent.AddChild(col);
        return col;
    }

    private void AddSection(VBoxContainer column, StatusSection s)
    {
        column.AddChild(TitleBar(s.Title, s.Colour));
        foreach (var line in s.Lines)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            row.AddThemeConstantOverride("separation", P(6));
            if (line.Label.Length > 0)
            {
                var name = Text(line.Label.ToUpperInvariant(), F(13), s.Colour, true);
                name.CustomMinimumSize = new Vector2(P(96), 0);
                name.VerticalAlignment = VerticalAlignment.Top;
                row.AddChild(name);
            }
            var cell = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            cell.AddThemeConstantOverride("separation", P(2));
            var value = Text(line.Value, F(13), line.Colour);
            value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            cell.AddChild(value);
            if (line.Bar is { } fraction)
                cell.AddChild(new CivMeter
                {
                    Colour = line.Colour, Fraction = fraction, Marks = line.Mark is { } m ? new[] { m } : Array.Empty<float>(),
                    CustomMinimumSize = new Vector2(0, P(6)),
                });
            row.AddChild(cell);
            if (line.Buttons != null)
                foreach (var b in line.Buttons)
                {
                    var pill = Pill(b.Id, b.Label, b.Colour, b.Enabled, () => ActionPicked?.Invoke(b.Id), b.Tip);
                    pill.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                    row.AddChild(pill);
                }
            column.AddChild(row);
        }
        if (s.Note is { Length: > 0 } note)
        {
            var l = Text(note, F(12), Grey);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(l);
        }
    }

    // ------------------------------------------------------------------ bits (ContactPanel's look)

    private Label Text(string text, int size, Color colour, bool bold = false)
    {
        var label = new Label { Text = text.Replace("\r", ""), MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        label.AddThemeFontOverride("font", HudKit.Condensed(bold));
        return label;
    }

    private PanelContainer Bar(Color colour, string text, int size)
    {
        var style = new StyleBoxFlat { BgColor = colour, ContentMarginLeft = P(8), ContentMarginRight = P(8), ContentMarginTop = P(2), ContentMarginBottom = P(2) };
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("panel", style);
        var label = Text(text, size, Black, true);
        label.ClipText = true;
        bar.AddChild(label);
        return bar;
    }

    private PanelContainer TitleBar(string text, Color colour) => Bar(colour, text.ToUpperInvariant(), F(14));

    private HelmPill Pill(string id, string text, Color colour, bool enabled, Action onPressed, string tip)
    {
        var pill = new HelmPill { Id = id, TooltipText = tip };
        pill.AddThemeFontOverride("font", HudKit.Condensed(true));
        pill.AddThemeFontSizeOverride("font_size", F(13));
        pill.Set(text, colour, !enabled, P(9), P(9), P(8), P(2));
        pill.Pressed += onPressed;
        return pill;
    }
}
