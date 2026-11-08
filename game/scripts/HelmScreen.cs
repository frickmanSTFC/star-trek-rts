using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The helm station screen drawn over the orbit view (owner's mockups S1–S5, E10): a slim "elbow console" frame
/// (rounded corner bars, flat colour blocks, condensed caps) around a see-through viewscreen, a collapsible data
/// panel over the right of the view (sections + a slowly scrolling findings feed), a cursor readout, an object card
/// and the deploy info card. The 3D view renders full-window behind it; the viewscreen area ignores the mouse so the
/// driver's drag / zoom / click on the globe still work (use <see cref="IsOverUi"/> to skip clicks on the frame).
/// Everything scales with the viewport (1.0 at 1280×720). Setters are cheap to call every frame: controls are only
/// rebuilt when their content changes. Render-only: the driver pushes content in, button presses come back out.
/// </summary>
public partial class HelmScreen : Control
{
    // Palette (from the mockups). Public so the driver can colour sections, rows and readouts to match.
    public static readonly Color Peach = Color.FromHtml("#f2a65a");
    public static readonly Color Lavender = Color.FromHtml("#a9a3e0");
    public static readonly Color Sky = Color.FromHtml("#7fb0e8");
    public static readonly Color Rust = Color.FromHtml("#c8664a");
    public static readonly Color Cyan = Color.FromHtml("#5ff2e6");
    public static readonly Color Green = Color.FromHtml("#9be37a");
    public static readonly Color Red = Color.FromHtml("#e8645a");
    public static readonly Color Ink = Color.FromHtml("#f2e6d8");
    private static readonly Color Line = Color.FromHtml("#26324d");
    private static readonly Color Grey = Color.FromHtml("#3d404b");
    private static readonly Color GreyText = Color.FromHtml("#8d909a");
    private static readonly Color Black = new(0, 0, 0);

    // Red alert (E10).
    private static readonly Color AlertRed = Color.FromHtml("#e8645a");
    private static readonly Color AlertDark = Color.FromHtml("#7a1f18");
    private static readonly Color AlertPink = Color.FromHtml("#d9a49a");
    private static readonly Color AlertBrick = Color.FromHtml("#8c4a42");
    private static readonly Color AlertTitle = Color.FromHtml("#ff8a7a");
    private static readonly Color AlertInk = Color.FromHtml("#f6e2dc");
    private static readonly Color AlertLine = Color.FromHtml("#5a1c17");

    /// <summary>Frame sizes in 720p pixels. Open = S1 (data panel open); Slim = S5 (panel folded to side tabs).</summary>
    private readonly record struct Metrics(float Pad, float Gap, float Rail, float Top, float Bottom, float Side, float Radius,
        int TitleSize, int SubSize, int ButtonSize, int TextSize, int PillSize);

    private static readonly Metrics OpenFrame = new(10, 6, 92, 40, 44, 0, 24, 24, 15, 15, 14, 15);
    private static readonly Metrics SlimFrame = new(8, 5, 56, 32, 40, 30, 18, 20, 14, 13, 13, 14);
    private const float PanelWidth = 250;
    private const float CardWidth = 220;
    private const float InfoWidth = 300;

    private static readonly (OrbitDisplay Display, string Label)[] Displays =
    {
        (OrbitDisplay.Planet, "PLANET"), (OrbitDisplay.System, "SYSTEM"), (OrbitDisplay.Orbit, "ORBIT"), (OrbitDisplay.Atmosphere, "ATMOS"),
    };

    public event Action<string>? ActionPicked;
    public event Action<OrbitDisplay>? DisplayPicked;
    public event Action<bool>? PanelToggled;

    // ------------------------------------------------------------------ content (what the driver asked for)

    private string _title = "";
    private string _subtitle = "";
    private string _clock = "";
    private string _resources = "";
    private OrbitDisplay _display = OrbitDisplay.Planet;
    private List<HelmAction> _rail = new();
    private List<HelmAction> _actions = new();
    private string _panelHeader = "";
    private List<HelmSection> _sections = new();
    private List<string> _feed = new();
    private string _feedTitle = "FINDINGS";
    private bool _panelOpen = true;
    private bool _alert;

    private string? _readTitle;
    private string _readDetail = "";
    private Color _readColour = Peach;
    private Vector2 _readPos;

    private string? _cardTitle;
    private List<HelmRow> _cardRows = new();
    private List<HelmAction> _cardButtons = new();
    private Vector2 _cardPos;
    private Color _cardColour = Peach;

    private InfoView? _infoView;
    private Action? _onInfoAction;
    private Action? _onInfoClose;

    // ------------------------------------------------------------------ layout

    private float _s = 1;
    private Metrics _m = OpenFrame;
    private float _railWidth;
    private Rect2 _view;
    private Rect2 _panelRect;
    private Vector2 _builtSize;
    private bool _built;
    private bool _dirty;

    private SystemFont? _regularBase;
    private SystemFont? _boldBase;
    private FontVariation _regular = new();
    private FontVariation _bold = new();
    private float _fontScale = -1;

    // ------------------------------------------------------------------ nodes (rebuilt by Build)

    private readonly List<StyleBoxFlat> _pulse = new();
    private Label? _titleLabel;
    private Label? _subtitleLabel;
    private Label? _clockLabel;
    private Label? _resourcesLabel;
    private readonly List<HelmPill> _displayButtons = new();
    private VBoxContainer? _railBox;
    private HBoxContainer? _pillBox;
    private float _pillFit = 1f; // < 1 when the action strip is too wide for the screen: smaller pill text and padding
    private PanelContainer? _panel;
    private Label? _panelHeaderLabel;
    private VBoxContainer? _sectionsBox;
    private Control? _feedTitleBar;
    private Label? _feedTitleLabel;
    private Control? _feedArea;
    private Control? _feedClip;
    private VBoxContainer? _feedA;
    private VBoxContainer? _feedB;
    private float _feedOffset;
    private HelmTab? _dataTab;
    private HelmTab? _logTab;

    private PanelContainer? _readout;
    private StyleBoxFlat? _readStyle;
    private Label? _readTitleLabel;
    private Label? _readDetailLabel;
    private string? _readShownTitle;
    private string? _readShownDetail;
    private Color _readShownColour;

    private PanelContainer? _card;
    private StyleBoxFlat? _cardStyle;
    private VBoxContainer? _cardBox;

    private PanelContainer? _info;
    private TextureRect? _infoIcon;
    private Label? _infoTitle;
    private Label? _infoSubtitle;
    private Label? _infoCost;
    private Label? _infoBody;
    private Control? _infoRowsBox;
    private GridContainer? _infoRows;
    private Control? _infoProgress;
    private Bar? _infoBar;
    private Label? _infoProgressText;
    private HelmPill? _infoAction;
    private Label? _infoFooter;
    private InfoView? _infoShown;
    private IReadOnlyList<(string Label, string Value)>? _infoRowsShown;
    private string? _infoIconShown;

    // ================================================================== public API

    /// <summary>The viewscreen in viewport pixels: everything inside the frame (the open data panel sits over it).</summary>
    public Rect2 ViewRect => new(_view.Position + GlobalPosition, _view.Size);

    public bool PanelOpen => _panelOpen;

    /// <summary>Where floating windows may go, in viewport pixels: the viewscreen minus the open data panel.</summary>
    public Rect2 OpenRect
    {
        get
        {
            var area = OpenArea();
            return new Rect2(area.Position + GlobalPosition, area.Size);
        }
    }

    /// <summary>The frame's scale (1.0 at 1280×720).</summary>
    public float UiScale => _s;

    /// <summary>True over the frame, its buttons, the open data panel, the object card and the info card.</summary>
    public bool IsOverUi(Vector2 screenPos)
    {
        if (!_built || !IsVisibleInTree()) return false;
        if (!ViewRect.HasPoint(screenPos)) return true;
        if (_panel != null && _panel.GetGlobalRect().HasPoint(screenPos)) return true;
        if (_card != null && _card.Visible && _card.GetGlobalRect().HasPoint(screenPos)) return true;
        return _info != null && _info.Visible && _info.GetGlobalRect().HasPoint(screenPos);
    }

    public void SetTitle(string title, string subtitle)
    {
        title = Clean(title).ToUpperInvariant();
        subtitle = Clean(subtitle).ToUpperInvariant();
        if (title == _title && subtitle == _subtitle) return;
        _title = title;
        _subtitle = subtitle;
        if (_titleLabel != null) _titleLabel.Text = _title;
        if (_subtitleLabel != null) _subtitleLabel.Text = _subtitle;
    }

    public void SetClock(string text)
    {
        text = Clean(text);
        if (text == _clock) return;
        _clock = text;
        if (_clockLabel != null) _clockLabel.Text = _clock;
    }

    public void SetResources(string text)
    {
        text = Clean(text).ToUpperInvariant();
        if (text == _resources) return;
        _resources = text;
        if (_resourcesLabel != null) _resourcesLabel.Text = _resources;
    }

    /// <summary>Highlights the current display's top-bar button (peach; the others sky).</summary>
    public void SetDisplay(OrbitDisplay display)
    {
        if (display == _display) return;
        _display = display;
        ApplyDisplay();
    }

    /// <summary>The left rail buttons. Highlight = current (peach), otherwise sky; Colour overrides both.</summary>
    public void SetRail(IReadOnlyList<HelmAction> items)
    {
        if (SameList(items, _rail)) return;
        var countChanged = items.Count != _rail.Count;
        _rail = items.ToList();
        if (!_built) return;
        if (!_panelOpen && !Mathf.IsEqualApprox(MeasureRail(), _railWidth))
        {
            // The slim frame's rail fits its labels: a new width moves the viewscreen, so lay out again.
            ComputeLayout();
            _dirty = true;
            return;
        }
        if (countChanged) RebuildRail();
        else ApplyRail();
    }

    /// <summary>The action pills along the bottom strip (rounded ends on the first and last). Enabled = false greys one out.</summary>
    public void SetActions(IReadOnlyList<HelmAction> items)
    {
        if (SameList(items, _actions)) return;
        var countChanged = items.Count != _actions.Count;
        _actions = items.ToList();
        if (!_built) return;
        if (countChanged) RebuildActions();
        else ApplyActions();
    }

    /// <summary>The data panel's header and sections (a section with an empty title shows only its rows).</summary>
    public void SetPanel(string header, IReadOnlyList<HelmSection> sections)
    {
        header = Clean(header).ToUpperInvariant();
        if (header != _panelHeader)
        {
            _panelHeader = header;
            if (_panelHeaderLabel != null) _panelHeaderLabel.Text = _panelHeader;
        }
        if (SameSections(sections, _sections)) return;
        _sections = sections.Select(s => s with { Rows = s.Rows.ToList() }).ToList();
        RebuildSections();
    }

    /// <summary>The findings feed under the panel's sections; it scrolls slowly upward on a loop when it overflows.</summary>
    public void SetFeed(IReadOnlyList<string> lines) => SetFeed(lines, "FINDINGS");

    /// <summary>As <see cref="SetFeed(IReadOnlyList{string})"/>, with the feed's title bar text.</summary>
    public void SetFeed(IReadOnlyList<string> lines, string title)
    {
        title = Clean(title).ToUpperInvariant();
        if (title != _feedTitle)
        {
            _feedTitle = title;
            if (_feedTitleLabel != null) _feedTitleLabel.Text = _feedTitle;
        }
        if (lines.Count == _feed.Count && lines.Select(Clean).SequenceEqual(_feed)) return;
        _feed = lines.Select(Clean).ToList();
        RebuildFeed();
        ApplyTabs();
    }

    /// <summary>Opens the data panel (S1) or folds it to side tabs with the slimmer frame (S5). Doesn't raise PanelToggled.</summary>
    public void SetPanelOpen(bool open)
    {
        if (open == _panelOpen) return;
        _panelOpen = open;
        if (!_built) return;
        ComputeLayout();
        _dirty = true; // rebuilt in _Process, not inside the button signal that may have asked for it
    }

    /// <summary>A small readout box near a screen position (S5): title line + detail line, coloured left border. Title null hides it.</summary>
    public void ShowReadout(string? title, string detail, Color colour, Vector2 screenPos)
    {
        _readTitle = title == null ? null : Clean(title).ToUpperInvariant();
        _readDetail = Clean(detail);
        _readColour = colour;
        _readPos = screenPos;
        ApplyReadout();
    }

    /// <summary>A popup card next to an object (S3): coloured header, rows, up to 3 buttons. Title null hides it.</summary>
    public void ShowCard(string? title, IReadOnlyList<HelmRow> rows, IReadOnlyList<HelmAction> buttons, Vector2 screenPos, Color colour)
    {
        _cardPos = screenPos;
        if (title == null)
        {
            _cardTitle = null;
            if (_card != null) _card.Visible = false;
            return;
        }
        title = Clean(title).ToUpperInvariant();
        var buttonList = buttons.Count > 3 ? buttons.Take(3).ToList() : buttons;
        var changed = title != _cardTitle || colour != _cardColour || !SameList(rows, _cardRows) || !SameList(buttonList, _cardButtons);
        _cardTitle = title;
        if (changed)
        {
            _cardColour = colour;
            _cardRows = rows.ToList();
            _cardButtons = buttonList.ToList();
            RebuildCard();
        }
        if (_card != null)
        {
            _card.Visible = true;
            PlaceCard();
        }
    }

    /// <summary>Hooks for the info card's action button and its close button (same pattern as PlanetHud.SetInfoHandlers).</summary>
    public void SetInfoHandlers(Action onAction, Action onClose)
    {
        _onInfoAction = onAction;
        _onInfoClose = onClose;
    }

    /// <summary>The deploy info card (same content as PlanetHud.ShowInfo), bottom-right of the view above the action strip. Null hides it.</summary>
    public void ShowInfo(InfoView? view)
    {
        _infoView = view;
        ApplyInfo(false);
    }

    /// <summary>Red alert (E10): the frame turns red and the top elbows pulse.</summary>
    public void SetAlert(bool on)
    {
        if (on == _alert) return;
        _alert = on;
        if (_built) _dirty = true;
    }

    // ================================================================== Godot

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;

        // A condensed face if the system has one (Antonio is the mockup's; Bahnschrift ships with Windows 10+ and has a
        // condensed width; then common narrow faces). SystemFont falls back to the default font when none is found.
        string[] names = { "Antonio", "Bahnschrift", "Arial Narrow", "Roboto Condensed", "Liberation Sans Narrow", "DejaVu Sans Condensed", "Arial" };
        _regularBase = new SystemFont { FontNames = names, FontWeight = 500, FontStretch = 75 };
        _boldBase = new SystemFont { FontNames = names, FontWeight = 700, FontStretch = 75 };

        ComputeLayout();
        Build();
    }

    public override void _Process(double delta)
    {
        if (GetViewportRect().Size != _builtSize || _dirty)
        {
            ComputeLayout();
            Build();
        }

        if (_alert && _pulse.Count > 0)
        {
            var t = 0.5f + 0.5f * Mathf.Cos((float)(Time.GetTicksMsec() / 1000.0) * Mathf.Tau / 1.4f);
            var c = AlertDark.Lerp(AlertRed, t);
            foreach (var style in _pulse) style.BgColor = c;
        }

        ScrollFeed((float)delta);
        FitActions();
        if (_readout != null && _readout.Visible) PlaceReadout();
        if (_card != null && _card.Visible) PlaceCard();
        if (_info != null && _info.Visible) PlaceInfo();
    }

    // ================================================================== layout

    private int PillFont() => Mathf.Max(8, Mathf.RoundToInt(_m.PillSize * _s * _pillFit));

    /// <summary>Shrink the action pills (text and padding, down to 55%) while the strip runs off the right edge of the
    /// screen, and let them grow back when there is room again, so every action stays on screen.</summary>
    private void FitActions()
    {
        if (_pillBox == null || _actions.Count == 0 || !_pillBox.IsVisibleInTree()) return;
        var right = _pillBox.GetGlobalRect().End.X;
        var edge = GetViewportRect().Size.X - P(8);
        if (right > edge && _pillFit > 0.55f) _pillFit = Mathf.Max(0.55f, _pillFit - 0.05f);
        else if (right < edge - P(70) && _pillFit < 1f) _pillFit = Mathf.Min(1f, _pillFit + 0.05f);
        else return;
        ApplyActions();
    }

    private int P(float v) => Mathf.Max(1, Mathf.RoundToInt(v * _s));
    private int F(int v) => Mathf.Max(8, Mathf.RoundToInt(v * _s));

    /// <summary>Scale, fonts, frame metrics and the viewscreen rect for the current viewport and panel state.</summary>
    private void ComputeLayout()
    {
        var size = GetViewportRect().Size;
        _s = Mathf.Clamp(Mathf.Min(size.X / 1280f, size.Y / 720f), 0.5f, 4f);
        if (!Mathf.IsEqualApprox(_s, _fontScale))
        {
            _fontScale = _s;
            var spacing = Mathf.RoundToInt(0.6f * _s);
            _regular = new FontVariation { BaseFont = _regularBase, SpacingGlyph = spacing };
            _bold = new FontVariation { BaseFont = _boldBase, SpacingGlyph = spacing };
        }
        _m = _panelOpen ? OpenFrame : SlimFrame;
        _railWidth = _panelOpen ? Mathf.Round(_m.Rail * _s) : MeasureRail();

        var pad = Mathf.Round(_m.Pad * _s);
        var gap = Mathf.Round(_m.Gap * _s);
        var top = Mathf.Round(_m.Top * _s);
        var bottom = Mathf.Round(_m.Bottom * _s);
        var side = Mathf.Round(_m.Side * _s);
        var x0 = pad + _railWidth + gap;
        var y0 = pad + top + gap;
        var x1 = size.X - pad - (_panelOpen ? 0 : side + gap);
        var y1 = size.Y - pad - bottom - gap;
        _view = new Rect2(x0, y0, Mathf.Max(1, x1 - x0), Mathf.Max(1, y1 - y0));
        var panelW = Mathf.Round(PanelWidth * _s);
        _panelRect = new Rect2(_view.End.X - panelW, _view.Position.Y, panelW, _view.Size.Y);
    }

    /// <summary>The slim frame's rail width: the widest rail label, between 56 and 92 (720p) pixels.</summary>
    private float MeasureRail()
    {
        var size = F(SlimFrame.ButtonSize);
        var widest = 0f;
        foreach (var item in _rail) widest = Mathf.Max(widest, _bold.GetStringSize(Caption(item), HorizontalAlignment.Left, -1, size).X);
        return Mathf.Round(Mathf.Clamp(widest + 14 * _s, SlimFrame.Rail * _s, OpenFrame.Rail * _s));
    }

    /// <summary>Where cards and readouts may go: the viewscreen, minus the open data panel.</summary>
    private Rect2 OpenArea()
    {
        if (!_panelOpen) return _view;
        return new Rect2(_view.Position, new Vector2(Mathf.Max(1, _panelRect.Position.X - _view.Position.X), _view.Size.Y));
    }

    /// <summary>Builds every control from the stored content at the current scale and panel state.</summary>
    private void Build()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _pulse.Clear();
        _displayButtons.Clear();
        _railBox = null;
        _pillBox = null;
        _panel = null;
        _panelHeaderLabel = null;
        _sectionsBox = null;
        _feedTitleBar = null;
        _feedTitleLabel = null;
        _feedArea = null;
        _feedClip = null;
        _feedA = null;
        _feedB = null;
        _dataTab = null;
        _logTab = null;
        _resourcesLabel = null;

        var size = GetViewportRect().Size;
        _builtSize = size;
        _dirty = false;
        _built = true;
        Theme = new Theme { DefaultFont = _regular, DefaultFontSize = F(_m.TextSize) };

        var pad = Mathf.Round(_m.Pad * _s);
        var gap = Mathf.Round(_m.Gap * _s);
        var top = Mathf.Round(_m.Top * _s);
        var bottom = Mathf.Round(_m.Bottom * _s);
        var side = Mathf.Round(_m.Side * _s);
        var radius = P(_m.Radius);
        var x0 = _view.Position.X;
        var y0 = _view.Position.Y;
        var x1 = _view.End.X;
        var y1 = _view.End.Y;
        var bottomY = size.Y - pad - bottom;

        // Black frame around the viewscreen; it also stops the mouse there so the globe doesn't turn under the bars.
        AddBand(new Rect2(0, 0, size.X, y0));
        AddBand(new Rect2(0, y1, size.X, size.Y - y1));
        AddBand(new Rect2(0, y0, x0, y1 - y0));
        AddBand(new Rect2(x1, y0, size.X - x1, y1 - y0));

        var border = new StyleBoxFlat { DrawCenter = false, BorderColor = _alert ? AlertLine : Line };
        border.SetBorderWidthAll(1);
        var viewBorder = new Panel { MouseFilter = MouseFilterEnum.Ignore, Position = _view.Position, Size = _view.Size };
        viewBorder.AddThemeStyleboxOverride("panel", border);
        AddChild(viewBorder);

        // Top-left elbow (shows ALERT in red alert) and the top bar.
        var elbowStyle = Flat(_alert ? AlertRed : Peach, tl: radius);
        if (_alert) _pulse.Add(elbowStyle);
        elbowStyle.ContentMarginLeft = P(12);
        elbowStyle.ContentMarginBottom = P(3);
        var elbow = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Position = new Vector2(pad, pad), Size = new Vector2(_railWidth, top) };
        elbow.AddThemeStyleboxOverride("panel", elbowStyle);
        if (_alert)
        {
            var alertLabel = Text("ALERT", F(_m.ButtonSize), Black, true);
            alertLabel.VerticalAlignment = VerticalAlignment.Bottom;
            elbow.AddChild(alertLabel);
        }
        AddChild(elbow);

        var topBar = Row(new Rect2(x0, pad, x1 - x0, top), gap);
        var block = Block(_alert ? AlertRed : Peach, P(_panelOpen ? 30 : 20), out var blockStyle);
        if (_alert) _pulse.Add(blockStyle);
        topBar.AddChild(block);
        var titleMargin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
        titleMargin.AddThemeConstantOverride("margin_left", P(8));
        titleMargin.AddThemeConstantOverride("margin_right", P(8));
        var titleRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        titleRow.AddThemeConstantOverride("separation", P(14));
        _titleLabel = Text(_title, F(_m.TitleSize), _alert ? AlertTitle : Peach, true);
        _subtitleLabel = Text(_subtitle, F(_m.SubSize), Tone(Lavender));
        _subtitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _subtitleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        titleRow.AddChild(_titleLabel);
        titleRow.AddChild(_subtitleLabel);
        titleMargin.AddChild(titleRow);
        topBar.AddChild(titleMargin);
        foreach (var (display, label) in Displays)
        {
            var button = new HelmPill { Id = label };
            var captured = display;
            button.Pressed += () => DisplayPicked?.Invoke(captured);
            FontOf(button, F(_m.ButtonSize));
            _displayButtons.Add(button);
            topBar.AddChild(button);
        }
        ApplyDisplay();
        var clockStyle = Flat(Tone(Lavender), tr: _panelOpen ? P(20) : 0, br: _panelOpen ? P(20) : 0);
        var clock = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(P(_panelOpen ? 80 : 64), 0) };
        clock.AddThemeStyleboxOverride("panel", clockStyle);
        _clockLabel = Text(_clock, F(_m.TextSize), Black, true);
        _clockLabel.HorizontalAlignment = HorizontalAlignment.Center;
        clock.AddChild(_clockLabel);
        topBar.AddChild(clock);
        AddChild(topBar);

        // Left rail: buttons, then a filler bar.
        _railBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(pad, y0), Size = new Vector2(_railWidth, y1 - y0) };
        _railBox.AddThemeConstantOverride("separation", P(_panelOpen ? 5 : 4));
        AddChild(_railBox);
        RebuildRail();

        // Bottom-left elbow and the bottom strip: resources (open frame), filler, action pills.
        AddChild(Corner(new Rect2(pad, bottomY, _railWidth, bottom), _alert ? AlertBrick : Peach, bl: radius));
        var strip = Row(new Rect2(x0, bottomY, x1 - x0, bottom), gap);
        if (_panelOpen)
        {
            var resMargin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            resMargin.AddThemeConstantOverride("margin_left", P(10));
            resMargin.AddThemeConstantOverride("margin_right", P(10));
            _resourcesLabel = Text(_resources, F(_m.TextSize), Tone(Lavender));
            resMargin.AddChild(_resourcesLabel);
            strip.AddChild(resMargin);
        }
        var filler = Block(_alert ? AlertLine : Line, 0);
        filler.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        strip.AddChild(filler);
        _pillBox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _pillBox.AddThemeConstantOverride("separation", (int)gap);
        strip.AddChild(_pillBox);
        AddChild(strip);
        RebuildActions();

        if (_panelOpen) BuildPanel();
        else
        {
            // Slim frame: right column with elbows and the side tabs that reopen the panel; resources move into the view.
            var sx = x1 + gap;
            var pink = _alert ? AlertPink : Lavender;
            AddChild(Corner(new Rect2(sx, pad, side, top), pink, tr: P(_m.Radius)));
            AddChild(Corner(new Rect2(sx, bottomY, side, bottom), pink, br: P(_m.Radius)));
            var tabs = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(sx, y0), Size = new Vector2(side, y1 - y0) };
            tabs.AddThemeConstantOverride("separation", P(4));
            _dataTab = Tab(Tone(Rust), P(120));
            _logTab = Tab(Tone(Sky), P(100));
            tabs.AddChild(_dataTab);
            tabs.AddChild(_logTab);
            var tabFiller = Block(_alert ? AlertBrick : Lavender, 0);
            tabFiller.SizeFlagsVertical = SizeFlags.ExpandFill;
            tabs.AddChild(tabFiller);
            AddChild(tabs);
            ApplyTabs();

            _resourcesLabel = Text(_resources, F(13), Tone(Lavender));
            _resourcesLabel.Position = _view.Position + new Vector2(P(12), P(8));
            AddChild(_resourcesLabel);
        }

        BuildReadout();
        BuildCard();
        BuildInfo();
    }

    private void BuildPanel()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.6f),
            BorderColor = _alert ? AlertLine : Line,
            BorderWidthLeft = 1,
            ContentMarginLeft = P(10),
            ContentMarginRight = P(10),
            ContentMarginTop = P(8),
            ContentMarginBottom = P(10),
        };
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Position = _panelRect.Position, Size = _panelRect.Size, ClipContents = true };
        _panel.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", P(6));

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", P(4));
        var header = TitleBar(_panelHeader, Tone(Lavender), out _panelHeaderLabel);
        header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(header);
        var collapse = new HelmPill { Id = "collapse", TooltipText = "Fold the data panel" };
        FontOf(collapse, F(15));
        collapse.Set("›", Tone(Rust), false, 0, P(12), P(8), 0);
        collapse.CustomMinimumSize = new Vector2(P(26), 0);
        collapse.Pressed += () =>
        {
            SetPanelOpen(false);
            PanelToggled?.Invoke(false);
        };
        head.AddChild(collapse);
        box.AddChild(head);

        _sectionsBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _sectionsBox.AddThemeConstantOverride("separation", P(6));
        box.AddChild(_sectionsBox);

        _feedTitleBar = TitleBar(_feedTitle, Tone(Sky), out _feedTitleLabel);
        box.AddChild(_feedTitleBar);
        var feedStyle = new StyleBoxFlat { DrawCenter = false, BorderColor = Tone(Sky), BorderWidthLeft = P(4), ContentMarginLeft = P(8) };
        var feedArea = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
        feedArea.AddThemeStyleboxOverride("panel", feedStyle);
        _feedClip = new Control { MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        _feedA = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _feedB = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _feedA.AddThemeConstantOverride("separation", P(5));
        _feedB.AddThemeConstantOverride("separation", P(5));
        var innerW = _panelRect.Size.X - P(10) * 2 - P(4) - P(8);
        _feedA.Size = new Vector2(innerW, 0);
        _feedB.Size = new Vector2(innerW, 0);
        _feedClip.AddChild(_feedA);
        _feedClip.AddChild(_feedB);
        feedArea.AddChild(_feedClip);
        _feedArea = feedArea;
        box.AddChild(feedArea);

        _panel.AddChild(box);
        AddChild(_panel);
        RebuildSections();
        RebuildFeed();
    }

    // ================================================================== regions

    private void ApplyDisplay()
    {
        for (var i = 0; i < _displayButtons.Count; i++)
            _displayButtons[i].Set(Displays[i].Label, Tone(Displays[i].Display == _display ? Peach : Sky), false, 0, 0, P(14), 0);
    }

    private void RebuildRail()
    {
        if (_railBox == null) return;
        Clear(_railBox);
        foreach (var _ in _rail)
        {
            var pill = new HelmPill { Alignment = HorizontalAlignment.Right, ClipText = true };
            FontOf(pill, F(_m.ButtonSize));
            pill.Pressed += () => ActionPicked?.Invoke(pill.Id);
            _railBox.AddChild(pill);
        }
        var filler = Block(_alert ? AlertBrick : Lavender, 0);
        filler.SizeFlagsVertical = SizeFlags.ExpandFill;
        _railBox.AddChild(filler);
        ApplyRail();
    }

    private void ApplyRail()
    {
        if (_railBox == null) return;
        for (var i = 0; i < _rail.Count && i < _railBox.GetChildCount(); i++)
        {
            if (_railBox.GetChild(i) is not HelmPill pill) continue;
            var item = _rail[i];
            pill.Id = item.Id;
            pill.Set(Caption(item), Tone(item.Colour ?? (item.Highlight ? Peach : Sky)), !item.Enabled, 0, 0, P(_panelOpen ? 10 : 6), P(8));
        }
    }

    private void RebuildActions()
    {
        if (_pillBox == null) return;
        Clear(_pillBox);
        foreach (var _ in _actions)
        {
            var pill = new HelmPill();
            FontOf(pill, PillFont());
            pill.Pressed += () => ActionPicked?.Invoke(pill.Id);
            _pillBox.AddChild(pill);
        }
        ApplyActions();
    }

    private void ApplyActions()
    {
        if (_pillBox == null) return;
        var round = P(_m.Bottom / 2);
        for (var i = 0; i < _actions.Count && i < _pillBox.GetChildCount(); i++)
        {
            if (_pillBox.GetChild(i) is not HelmPill pill) continue;
            var item = _actions[i];
            pill.Id = item.Id;
            pill.AddThemeFontSizeOverride("font_size", PillFont());
            pill.Set(Caption(item), Tone(item.Colour ?? (item.Highlight ? Peach : Lavender)), !item.Enabled,
                i == 0 ? round : 0, i == _actions.Count - 1 ? round : 0, P((_panelOpen ? 14 : 12) * _pillFit), 0);
        }
    }

    private void RebuildSections()
    {
        if (_sectionsBox == null) return;
        Clear(_sectionsBox);
        foreach (var section in _sections)
        {
            var colour = Tone(section.Colour);
            if (section.Title.Length > 0) _sectionsBox.AddChild(TitleBar(Clean(section.Title).ToUpperInvariant(), colour, out _));
            if (section.Rows.Count == 0) continue;
            _sectionsBox.AddChild(RowsBlock(section.Rows, colour, P(84), F(14)));
        }
    }

    private void RebuildFeed()
    {
        if (_feedA == null || _feedB == null) return;
        Clear(_feedA);
        Clear(_feedB);
        foreach (var line in _feed)
        {
            _feedA.AddChild(FeedLine(line));
            _feedB.AddChild(FeedLine(line));
        }
        _feedA.ResetSize();
        _feedB.ResetSize();
        _feedOffset = 0;
        var show = _feed.Count > 0;
        if (_feedTitleBar != null) _feedTitleBar.Visible = show;
        if (_feedArea != null) _feedArea.Visible = show;
    }

    private Label FeedLine(string line)
    {
        var label = Text(line, F(14), Tone(Ink));
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.VerticalAlignment = VerticalAlignment.Top;
        return label;
    }

    /// <summary>Loops the feed upward (about 7 px/s at 720p) when it is taller than its area; pauses under the mouse.</summary>
    private void ScrollFeed(float delta)
    {
        if (_feedClip == null || _feedA == null || _feedB == null || !_feedClip.IsVisibleInTree()) return;
        var width = _feedClip.Size.X;
        if (width > 1 && !Mathf.IsEqualApprox(_feedA.Size.X, width))
        {
            _feedA.Size = new Vector2(width, 0);
            _feedB.Size = new Vector2(width, 0);
        }
        var loop = _feedA.Size.Y + P(5);
        if (_feedA.Size.Y <= _feedClip.Size.Y || loop <= 1)
        {
            _feedOffset = 0;
            _feedA.Position = Vector2.Zero;
            _feedB.Visible = false;
            return;
        }
        _feedB.Visible = true;
        if (!_feedClip.GetGlobalRect().HasPoint(GetGlobalMousePosition())) _feedOffset = (_feedOffset + 7f * _s * delta) % loop;
        _feedA.Position = new Vector2(0, -Mathf.Round(_feedOffset));
        _feedB.Position = new Vector2(0, Mathf.Round(loop - _feedOffset));
    }

    private void ApplyTabs()
    {
        _dataTab?.SetCaption("‹ DATA");
        _logTab?.SetCaption(_feed.Count > 0 ? $"‹ LOG {_feed.Count}" : "‹ LOG");
    }

    // ------------------------------------------------------------------ readout

    private void BuildReadout()
    {
        _readStyle = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.7f),
            BorderWidthLeft = P(4),
            ContentMarginLeft = P(10),
            ContentMarginRight = P(10),
            ContentMarginTop = P(4),
            ContentMarginBottom = P(4),
        };
        _readout = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _readout.AddThemeStyleboxOverride("panel", _readStyle);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        _readTitleLabel = Text("", F(14), Tone(Ink), true);
        _readDetailLabel = Text("", F(14), Tone(Peach));
        box.AddChild(_readTitleLabel);
        box.AddChild(_readDetailLabel);
        _readout.AddChild(box);
        AddChild(_readout);
        _readShownTitle = null;
        _readShownDetail = null;
        ApplyReadout();
    }

    private void ApplyReadout()
    {
        if (_readout == null || _readStyle == null || _readTitleLabel == null || _readDetailLabel == null) return;
        _readout.Visible = _readTitle != null;
        if (_readTitle == null) return;
        var resize = false;
        if (_readTitle != _readShownTitle || _readDetail != _readShownDetail)
        {
            _readShownTitle = _readTitle;
            _readShownDetail = _readDetail;
            _readTitleLabel.Text = _readTitle;
            _readDetailLabel.Text = _readDetail;
            _readDetailLabel.Visible = _readDetail.Length > 0;
            resize = true;
        }
        if (_readColour != _readShownColour || resize)
        {
            _readShownColour = _readColour;
            _readStyle.BorderColor = Tone(_readColour);
            _readDetailLabel.AddThemeColorOverride("font_color", Tone(_readColour));
        }
        if (resize) _readout.ResetSize();
        PlaceReadout();
    }

    private void PlaceReadout()
    {
        if (_readout == null) return;
        var free = OpenArea();
        var at = _readPos - GlobalPosition;
        var size = _readout.Size;
        var pos = new Vector2(at.X + P(22), at.Y - size.Y / 2);
        if (pos.X + size.X > free.End.X - P(4)) pos.X = at.X - P(22) - size.X;
        _readout.Position = ClampInto(pos, size, free);
    }

    // ------------------------------------------------------------------ object card

    private void BuildCard()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.7f),
            ContentMarginLeft = P(10),
            ContentMarginRight = P(10),
            ContentMarginTop = P(8),
            ContentMarginBottom = P(8),
        };
        style.SetBorderWidthAll(1);
        _cardStyle = style;
        _card = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _card.AddThemeStyleboxOverride("panel", style);
        _cardBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(P(CardWidth) - P(20), 0) };
        _cardBox.AddThemeConstantOverride("separation", P(4));
        _card.AddChild(_cardBox);
        AddChild(_card);
        RebuildCard();
        if (_cardTitle != null)
        {
            _card.Visible = true;
            PlaceCard();
        }
    }

    private void RebuildCard()
    {
        if (_card == null || _cardBox == null || _cardTitle == null) return;
        var colour = Tone(_cardColour);
        if (_cardStyle != null) _cardStyle.BorderColor = colour;
        Clear(_cardBox);
        _cardBox.AddChild(TitleBar(_cardTitle, colour, out _));
        foreach (var row in _cardRows)
        {
            var valueColour = Tone(row.ValueColour ?? Ink);
            if (row.Label.Length == 0)
            {
                var line = Text(row.Value, F(14), valueColour);
                line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                _cardBox.AddChild(line);
                continue;
            }
            var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            h.AddThemeConstantOverride("separation", P(6));
            var name = Text(Clean(row.Label).ToUpperInvariant(), F(14), colour);
            name.CustomMinimumSize = new Vector2(P(70), 0);
            name.VerticalAlignment = VerticalAlignment.Top;
            var value = Text(row.Value, F(14), valueColour);
            value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            h.AddChild(name);
            h.AddChild(value);
            _cardBox.AddChild(h);
        }
        if (_cardButtons.Count > 0)
        {
            var buttons = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            buttons.AddThemeConstantOverride("separation", P(4));
            for (var i = 0; i < _cardButtons.Count; i++)
            {
                var item = _cardButtons[i];
                var pill = new HelmPill { Id = item.Id };
                FontOf(pill, F(14));
                var bg = item.Colour ?? (item.Highlight || i == 0 ? _cardColour : Lavender);
                pill.Set(Caption(item), Tone(bg), !item.Enabled, i == 0 ? P(12) : 0, i == _cardButtons.Count - 1 ? P(12) : 0, P(10), P(3));
                pill.Pressed += () => ActionPicked?.Invoke(pill.Id);
                buttons.AddChild(pill);
            }
            _cardBox.AddChild(buttons);
        }
        _card.ResetSize();
    }

    private void PlaceCard()
    {
        if (_card == null) return;
        _card.Size = Vector2.Zero; // shrink to content (clamped to the minimum size)
        var free = OpenArea();
        var at = _cardPos - GlobalPosition;
        var size = _card.Size;
        var pos = new Vector2(at.X + P(24), at.Y - P(24));
        if (pos.X + size.X > free.End.X - P(8)) pos.X = at.X - P(24) - size.X;
        _card.Position = ClampInto(pos, size, free);
    }

    // ------------------------------------------------------------------ info card

    private void BuildInfo()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.78f),
            BorderColor = Tone(Peach),
            BorderWidthLeft = P(4),
            ContentMarginLeft = P(12),
            ContentMarginRight = P(12),
            ContentMarginTop = P(10),
            ContentMarginBottom = P(12),
        };
        _info = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _info.AddThemeStyleboxOverride("panel", style);
        var inner = P(InfoWidth) - P(12) * 2 - P(4);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(inner, 0) };
        box.AddThemeConstantOverride("separation", P(6));

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", P(6));
        _infoIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(P(26), P(26)),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        head.AddChild(_infoIcon);
        var titleBar = TitleBar("", Tone(Peach), out var title, F(16));
        _infoTitle = title;
        titleBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(titleBar);
        var close = new HelmPill { Id = "close", TooltipText = "Close (Esc)" };
        FontOf(close, F(15));
        close.Set("×", Tone(Rust), false, 0, P(12), P(8), 0);
        close.CustomMinimumSize = new Vector2(P(26), 0);
        close.Pressed += () => _onInfoClose?.Invoke();
        head.AddChild(close);
        box.AddChild(head);

        _infoSubtitle = Text("", F(13), Tone(Lavender));
        box.AddChild(_infoSubtitle);
        _infoCost = Text("", F(14), Tone(Ink));
        box.AddChild(_infoCost);
        _infoBody = Text("", F(14), Tone(Ink));
        _infoBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoBody.CustomMinimumSize = new Vector2(inner, 0);
        box.AddChild(_infoBody);

        var rowsStyle = new StyleBoxFlat { DrawCenter = false, BorderColor = Tone(Peach), BorderWidthLeft = P(4), ContentMarginLeft = P(8) };
        var rowsBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        rowsBox.AddThemeStyleboxOverride("panel", rowsStyle);
        _infoRows = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        _infoRows.AddThemeConstantOverride("h_separation", P(8));
        _infoRows.AddThemeConstantOverride("v_separation", P(3));
        rowsBox.AddChild(_infoRows);
        _infoRowsBox = rowsBox;
        box.AddChild(rowsBox);

        var prog = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        prog.AddThemeConstantOverride("separation", P(3));
        _infoBar = new Bar { Colour = Tone(Sky), CustomMinimumSize = new Vector2(0, P(8)) };
        prog.AddChild(_infoBar);
        _infoProgressText = Text("", F(13), Tone(Lavender));
        prog.AddChild(_infoProgressText);
        _infoProgress = prog;
        box.AddChild(prog);

        _infoAction = new HelmPill { Id = "info" };
        FontOf(_infoAction, F(15));
        _infoAction.CustomMinimumSize = new Vector2(0, P(32));
        _infoAction.Pressed += () => _onInfoAction?.Invoke();
        box.AddChild(_infoAction);
        _infoFooter = Text("", F(13), Tone(Lavender));
        _infoFooter.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoFooter.CustomMinimumSize = new Vector2(inner, 0);
        box.AddChild(_infoFooter);

        _info.AddChild(box);
        AddChild(_info);
        _infoShown = null;
        _infoRowsShown = null;
        _infoIconShown = null;
        ApplyInfo(true);
    }

    private void ApplyInfo(bool force)
    {
        if (_info == null || _infoTitle == null || _infoSubtitle == null || _infoCost == null || _infoBody == null || _infoRows == null ||
            _infoRowsBox == null || _infoProgress == null || _infoBar == null || _infoProgressText == null || _infoAction == null ||
            _infoFooter == null || _infoIcon == null) return;
        var v = _infoView;
        _info.Visible = v != null;
        if (v == null)
        {
            _infoShown = null;
            return;
        }
        if (!force && SameInfo(v, _infoShown)) return;
        _infoShown = v;

        if (v.IconId != _infoIconShown)
        {
            _infoIconShown = v.IconId;
            _infoIcon.Texture = v.IconId.Length > 0 ? Icons.Get(v.IconId, Tone(Peach)) : null;
            _infoIcon.Visible = v.IconId.Length > 0;
        }
        _infoTitle.Text = Clean(v.Title).ToUpperInvariant();
        _infoSubtitle.Text = Clean(v.Subtitle);
        _infoSubtitle.Visible = v.Subtitle.Length > 0;
        _infoCost.Text = Clean(v.Cost);
        _infoCost.Visible = v.Cost.Length > 0;
        _infoCost.AddThemeColorOverride("font_color", v.Affordable ? Tone(Ink) : Tone(Red));
        _infoBody.Text = Clean(v.Body);
        _infoBody.Visible = v.Body.Length > 0;

        if (_infoRowsShown == null || !SameList(v.Rows, _infoRowsShown))
        {
            _infoRowsShown = v.Rows.ToList();
            Clear(_infoRows);
            foreach (var (label, value) in v.Rows)
            {
                var name = Text(Clean(label).ToUpperInvariant(), F(13), Tone(Peach));
                name.CustomMinimumSize = new Vector2(P(80), 0);
                name.VerticalAlignment = VerticalAlignment.Top;
                var text = Text(value, F(13), Tone(Ink));
                text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                text.CustomMinimumSize = new Vector2(P(InfoWidth) - P(12) * 2 - P(4) - P(12) - P(80) - P(8), 0);
                _infoRows.AddChild(name);
                _infoRows.AddChild(text);
            }
        }
        _infoRowsBox.Visible = v.Rows.Count > 0;

        _infoProgress.Visible = v.Progress >= 0;
        _infoBar.Fraction = Mathf.Max(0, v.Progress);
        _infoProgressText.Text = Clean(v.ProgressText);
        _infoProgressText.Visible = v.ProgressText.Length > 0;

        _infoAction.Visible = v.ActionLabel.Length > 0;
        _infoAction.Disabled = !v.ActionEnabled;
        var round = P(16);
        _infoAction.Set(Clean(v.ActionLabel).ToUpperInvariant(), Tone(Peach), false, round, round, P(14), P(4));
        _infoFooter.Text = Clean(v.Footer);
        _infoFooter.Visible = v.Footer.Length > 0;
        PlaceInfo();
    }

    /// <summary>Bottom-right of the view, just above the action strip (left of the data panel when it's open).</summary>
    private void PlaceInfo()
    {
        if (_info == null) return;
        _info.Size = Vector2.Zero; // shrink to content (clamped to the minimum size)
        var free = OpenArea();
        var size = _info.Size;
        var pos = new Vector2(free.End.X - P(12) - size.X, free.End.Y - P(12) - size.Y);
        _info.Position = ClampInto(pos, size, free);
    }

    // ================================================================== helpers

    /// <summary>The red-alert version of a palette colour (E10); unchanged when the alert is off or the colour isn't in the palette.</summary>
    private Color Tone(Color c)
    {
        if (!_alert) return c;
        if (Near(c, Peach) || Near(c, Red)) return AlertRed;
        if (Near(c, Rust)) return AlertBrick;
        if (Near(c, Ink)) return AlertInk;
        if (Near(c, Sky) || Near(c, Lavender) || Near(c, Cyan) || Near(c, Green)) return AlertPink;
        return c;
    }

    private static bool Near(Color a, Color b) =>
        Mathf.Abs(a.R - b.R) < 0.02f && Mathf.Abs(a.G - b.G) < 0.02f && Mathf.Abs(a.B - b.B) < 0.02f;

    private static string Clean(string s) => s.Replace("\r", "");

    private static string Caption(HelmAction a)
    {
        var label = Clean(a.Label).ToUpperInvariant();
        return a.Key.Length > 0 ? $"{Clean(a.Key).ToUpperInvariant()} {label}" : label;
    }

    private static Vector2 ClampInto(Vector2 pos, Vector2 size, Rect2 area) => new(
        Mathf.Clamp(pos.X, area.Position.X + 4, Mathf.Max(area.Position.X + 4, area.End.X - size.X - 4)),
        Mathf.Clamp(pos.Y, area.Position.Y + 4, Mathf.Max(area.Position.Y + 4, area.End.Y - size.Y - 4)));

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static StyleBoxFlat Flat(Color bg, int tl = 0, int tr = 0, int br = 0, int bl = 0)
    {
        var style = new StyleBoxFlat { BgColor = bg, CornerRadiusTopLeft = tl, CornerRadiusTopRight = tr, CornerRadiusBottomRight = br, CornerRadiusBottomLeft = bl };
        return style;
    }

    private void AddBand(Rect2 rect)
    {
        if (rect.Size.X <= 0 || rect.Size.Y <= 0) return;
        AddChild(new ColorRect { Color = Black, MouseFilter = MouseFilterEnum.Stop, Position = rect.Position, Size = rect.Size });
    }

    /// <summary>A flat colour block (a cap or filler bar in a row or column); width 0 = sized by its container.</summary>
    private static Panel Block(Color colour, int width) => Block(colour, width, out _);

    private static Panel Block(Color colour, int width, out StyleBoxFlat style)
    {
        var panel = new Panel { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(width, 0) };
        style = Flat(colour);
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    /// <summary>A corner elbow: a flat block with one big rounded corner.</summary>
    private static Panel Corner(Rect2 rect, Color colour, int tl = 0, int tr = 0, int br = 0, int bl = 0)
    {
        var panel = new Panel { MouseFilter = MouseFilterEnum.Stop, Position = rect.Position, Size = rect.Size };
        panel.AddThemeStyleboxOverride("panel", Flat(colour, tl, tr, br, bl));
        return panel;
    }

    private static HBoxContainer Row(Rect2 rect, float gap)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Position = rect.Position, Size = rect.Size };
        row.AddThemeConstantOverride("separation", (int)gap);
        return row;
    }

    private Label Text(string text, int size, Color colour, bool bold = false)
    {
        var label = new Label { Text = Clean(text), MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        if (bold) label.AddThemeFontOverride("font", _bold);
        return label;
    }

    private void FontOf(Button button, int size)
    {
        button.AddThemeFontOverride("font", _bold);
        button.AddThemeFontSizeOverride("font_size", size);
    }

    /// <summary>A title bar: flat colour with black bold caps (panel header, section titles, card header).</summary>
    private PanelContainer TitleBar(string text, Color colour, out Label label, int size = 0)
    {
        var style = Flat(colour);
        style.ContentMarginLeft = P(8);
        style.ContentMarginRight = P(8);
        style.ContentMarginTop = P(3);
        style.ContentMarginBottom = P(3);
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("panel", style);
        label = Text(text, size > 0 ? size : F(15), Black, true);
        label.ClipText = true;
        bar.AddChild(label);
        return bar;
    }

    /// <summary>Label / value rows with a coloured left border (panel sections).</summary>
    private PanelContainer RowsBlock(IReadOnlyList<HelmRow> rows, Color colour, int labelWidth, int size)
    {
        var style = new StyleBoxFlat { DrawCenter = false, BorderColor = colour, BorderWidthLeft = P(4), ContentMarginLeft = P(8) };
        var block = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        block.AddThemeStyleboxOverride("panel", style);
        var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", P(6));
        grid.AddThemeConstantOverride("v_separation", P(3));
        foreach (var row in rows)
        {
            var name = Text(Clean(row.Label).ToUpperInvariant(), size, colour);
            name.CustomMinimumSize = new Vector2(labelWidth, 0);
            name.VerticalAlignment = VerticalAlignment.Top;
            var value = Text(row.Value, size, Tone(row.ValueColour ?? Ink));
            value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(name);
            grid.AddChild(value);
        }
        block.AddChild(grid);
        return block;
    }

    private HelmTab Tab(Color colour, int height)
    {
        var tab = new HelmTab { Id = "open", CustomMinimumSize = new Vector2(0, height), CaptionFont = _bold, CaptionSize = F(13) };
        tab.Set("", colour, false, 0, P(14), 0, 0);
        tab.Pressed += () =>
        {
            SetPanelOpen(true);
            PanelToggled?.Invoke(true);
        };
        return tab;
    }

    private static bool SameList<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        if (a.Count != b.Count) return false;
        var eq = EqualityComparer<T>.Default;
        for (var i = 0; i < a.Count; i++)
            if (!eq.Equals(a[i], b[i])) return false;
        return true;
    }

    private static bool SameSections(IReadOnlyList<HelmSection> a, IReadOnlyList<HelmSection> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].Title != b[i].Title || a[i].Colour != b[i].Colour || !SameList(a[i].Rows, b[i].Rows)) return false;
        return true;
    }

    private static bool SameInfo(InfoView a, InfoView? b) =>
        b != null && a.IconId == b.IconId && a.Title == b.Title && a.Subtitle == b.Subtitle && a.Cost == b.Cost &&
        a.Affordable == b.Affordable && a.Body == b.Body && a.Progress == b.Progress && a.ProgressText == b.ProgressText &&
        a.ActionLabel == b.ActionLabel && a.ActionEnabled == b.ActionEnabled && a.Footer == b.Footer && SameList(a.Rows, b.Rows);
}

/// <summary>A flat helm button: one colour block, black bold caps, optional rounded ends. Greyed still clicks (the driver explains why).</summary>
public partial class HelmPill : Button
{
    public string Id = "";
    private string? _text;
    private Color _colour;
    private bool _greyed;
    private int _left = -1;
    private int _right = -1;
    private int _padX = -1;
    private int _padY = -1;

    public HelmPill()
    {
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>Restyles only when something changed.</summary>
    public void Set(string text, Color colour, bool greyed, int roundLeft, int roundRight, int padX, int padY)
    {
        if (text != _text)
        {
            _text = text;
            Text = text;
        }
        if (colour == _colour && greyed == _greyed && roundLeft == _left && roundRight == _right && padX == _padX && padY == _padY) return;
        _colour = colour;
        _greyed = greyed;
        _left = roundLeft;
        _right = roundRight;
        _padX = padX;
        _padY = padY;

        var bg = greyed ? new Color(0.24f, 0.25f, 0.29f) : colour;
        StyleBoxFlat Box(Color c) => new()
        {
            BgColor = c,
            CornerRadiusTopLeft = roundLeft,
            CornerRadiusBottomLeft = roundLeft,
            CornerRadiusTopRight = roundRight,
            CornerRadiusBottomRight = roundRight,
            ContentMarginLeft = padX,
            ContentMarginRight = padX,
            ContentMarginTop = padY,
            ContentMarginBottom = padY,
        };
        AddThemeStyleboxOverride("normal", Box(bg));
        AddThemeStyleboxOverride("hover", Box(bg.Lightened(0.18f)));
        AddThemeStyleboxOverride("pressed", Box(bg.Darkened(0.18f)));
        AddThemeStyleboxOverride("hover_pressed", Box(bg.Darkened(0.12f)));
        AddThemeStyleboxOverride("disabled", Box(new Color(0.24f, 0.25f, 0.29f)));
        var ink = greyed ? new Color(0.55f, 0.57f, 0.6f) : new Color(0, 0, 0);
        AddThemeColorOverride("font_color", ink);
        AddThemeColorOverride("font_hover_color", ink);
        AddThemeColorOverride("font_pressed_color", ink);
        AddThemeColorOverride("font_hover_pressed_color", ink);
        AddThemeColorOverride("font_focus_color", ink);
        AddThemeColorOverride("font_disabled_color", new Color(0.55f, 0.57f, 0.6f));
    }
}

/// <summary>A side tab with its caption written top to bottom (the slim frame's "‹ DATA" / "‹ LOG n").</summary>
public partial class HelmTab : HelmPill
{
    public Font? CaptionFont;
    public int CaptionSize = 13;
    private string _caption = "";

    public void SetCaption(string caption)
    {
        if (caption == _caption) return;
        _caption = caption;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_caption.Length == 0) return;
        var font = CaptionFont ?? GetThemeDefaultFont();
        var width = font.GetStringSize(_caption, HorizontalAlignment.Left, -1, CaptionSize).X;
        var ascent = font.GetAscent(CaptionSize);
        var descent = font.GetDescent(CaptionSize);
        DrawSetTransform(Size / 2, Mathf.Pi / 2);
        DrawString(font, new Vector2(-width / 2, (ascent - descent) / 2), _caption, HorizontalAlignment.Left, -1, CaptionSize, new Color(0, 0, 0));
        DrawSetTransform(Vector2.Zero);
    }
}
