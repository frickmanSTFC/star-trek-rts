using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using Godot;

namespace FinalFrontier;

/// <summary>One base as the bottom panel shows it.</summary>
public sealed record BaseView(
    string Name,
    string CoreName,
    string CoreId,
    int Level,
    string UpgradeLine,
    string UpgradeCost,
    bool UpgradeEnabled,
    float UpgradeProgress,
    float PowerUse,
    float PowerSupply,
    string Buildings,
    int QueueCapacity,
    IReadOnlyList<(string Name, float Progress)> Queue);

/// <summary>One button on the command card. Tag = a short note top-right (e.g. DONE, 40%); Disabled greys it out.</summary>
public sealed record CommandView(string Id, string Key, string Name, string Cost, bool Affordable, bool Active, string Tag = "", bool Disabled = false);

/// <summary>
/// The big info card for a building, tech or vehicle picked on the command card: header, cost line, description,
/// stat rows, optional progress, and either an action button (ActionLabel) or a footer hint. Progress below 0 hides the bar.
/// </summary>
public sealed record InfoView(
    string IconId,
    string Title,
    string Subtitle,
    string Cost,
    bool Affordable,
    string Body,
    IReadOnlyList<(string Label, string Value)> Rows,
    float Progress,
    string ProgressText,
    string ActionLabel,
    bool ActionEnabled,
    string Footer);

/// <summary>The orbit panel: the ship and planet, which scan is on (index into the scan names), its legend and the hovered tile.</summary>
public sealed record OrbitPanelView(string Ship, string Subtitle, int Scan, string Legend, string Hover);

/// <summary>A button in the building panel (upgrade, maintain, salvage...).</summary>
public sealed record ActionView(string Id, string Label, string Detail, bool Enabled);

/// <summary>
/// A selected building as the bottom panel shows it: identity and status, stat rows (label, value), a condition bar
/// with what maintenance is doing, a progress bar (construction, upgrade, assembly, research) and action buttons.
/// Condition / Progress below 0 hide those bars.
/// </summary>
public sealed record StructureView(
    string Title,
    string Subtitle,
    string IconId,
    string Status,
    Color StatusColour,
    IReadOnlyList<(string Label, string Value)> Rows,
    float Condition,
    string ConditionText,
    string MaintenanceText,
    float Progress,
    string ProgressLabel,
    string ProgressText,
    IReadOnlyList<ActionView> Actions,
    FabView? Fab = null); // a fabricator's queue in place of the condition / progress column (PlanetHud.Parts.cs)

/// <summary>
/// Planet-surface HUD (layout from the mockup): resource bar with icons across the top, event feed,
/// prompt banner, hover tooltip, and a bottom panel with the minimap, the selected base or object,
/// and the command card. Drivers push text and values in; buttons call back out.
/// </summary>
public partial class PlanetHud : CanvasLayer
{
    public const int BottomHeight = 236;
    public const int TopHeight = 60;

    private static readonly Color Ink = new(0.9f, 0.92f, 0.96f);
    private static readonly Color Dim = new(0.6f, 0.65f, 0.75f);
    private static readonly Color Line = new(0.15f, 0.19f, 0.29f);
    private static readonly Color Ground = new(0.02f, 0.03f, 0.063f, 0.95f);
    private static readonly Color Good = new(0.56f, 0.83f, 0.69f);
    private static readonly Color Warn = new(1f, 0.7f, 0.42f);
    public static readonly Color Accent = new(0.96f, 0.65f, 0.14f);

    public Minimap Minimap { get; } = new();

    private Control _topBar = null!;
    private Control _vignette = null!;
    private Control _bottom = null!;
    private Label _factionName = null!;
    private ColorRect? _factionSwatch;
    private Color _localColour = PlanetDriver.ColorOf(0);

    /// <summary>The local player's colour (top-bar swatch, base and building icons); the driver sets it at start.</summary>
    public Color LocalColour
    {
        get => _localColour;
        set
        {
            _localColour = value;
            if (_factionSwatch != null) _factionSwatch.Color = value;
        }
    }
    private Label _factionSub = null!;
    private readonly Dictionary<int, (Label Amount, Label Rate)> _chips = new();
    private Label _powerChip = null!;
    private Label _storageChip = null!;
    private Control _storageBox = null!;
    private Label _researchChip = null!;
    private Label _researchSub = null!;
    private PanelContainer _help = null!;

    // Info card (above the command card).
    private PanelContainer _info = null!;
    private TextureRect _infoIcon = null!;
    private Label _infoTitle = null!;
    private Label _infoSubtitle = null!;
    private Label _infoCost = null!;
    private Label _infoBody = null!;
    private GridContainer _infoRows = null!;
    private readonly List<(Label Name, Label Value)> _infoRowLabels = new();
    private Control _infoProgress = null!;
    private Bar _infoBar = null!;
    private Label _infoProgressText = null!;
    private Button _infoAction = null!;
    private Label _infoFooter = null!;
    private Action? _onInfoAction;
    private Action? _onInfoClose;
    private Label _clock = null!;
    private Label _hints = null!;
    private PanelContainer _bannerPanel = null!;
    private Label _banner = null!;
    private VBoxContainer _feed = null!;
    private readonly List<(Control Node, double Born)> _feedItems = new();
    private PanelContainer _tooltip = null!;
    private Label _tooltipText = null!;

    // Bottom centre: base tabs, then either the base view or a detail view.
    private HBoxContainer _tabs = null!;
    private readonly List<Button> _tabButtons = new();
    private Control _baseView = null!;
    private Control _detailView = null!;
    private TextureRect _baseIcon = null!;
    private Label _baseName = null!;
    private Label _coreName = null!;
    private Label _levelBadge = null!;
    private Label _upgradeLine = null!;
    private Button _upgradeButton = null!;
    private Bar _powerBar = null!;
    private Label _powerText = null!;
    private Control _upgradeRow = null!;
    private Bar _upgradeBar = null!;
    private Label _upgradeText = null!;
    private Label _buildingsText = null!;
    private Label _queueTitle = null!;
    private VBoxContainer _queueList = null!;
    private Label _detailTitle = null!;
    private Label _detailSubtitle = null!;
    private Label _detailBody = null!;

    // Building panel.
    private Control _structureView = null!;
    private TextureRect _structIcon = null!;
    private Label _structTitle = null!;
    private Label _structSub = null!;
    private Label _structStatus = null!;
    private readonly List<(Label Name, Label Value)> _structRows = new();
    private Control _conditionBox = null!;
    private Bar _conditionBar = null!;
    private Label _conditionText = null!;
    private Label _maintenanceText = null!;
    private Control _progressBox = null!;
    private Label _progressLabel = null!;
    private Bar _progressBar = null!;
    private Label _progressText = null!;
    private readonly List<Button> _actionButtons = new();
    private readonly List<string> _actionIds = new();
    private Action<string>? _onAction;

    // Orbit panel.
    private Control _orbitPanel = null!;
    private Label _orbitShip = null!;
    private Label _orbitSub = null!;
    private readonly List<Button> _scanButtons = new();
    private Label _orbitLegend = null!;
    private Label _orbitHover = null!;
    private Action<int>? _onScan;
    public static readonly string[] ScanNames = { "TERRAIN", "RESOURCES", "LIFEFORMS", "BIOLOGICS", "LANDING" };

    // Command card.
    private Label _commandTitle = null!;
    private GridContainer _commandGrid = null!;
    private readonly Dictionary<string, (Button Button, Label Tag, Label Cost)> _commands = new();

    private Action<string>? _onBuild;
    private Action<int>? _onSelectBase;
    private Action? _onUpgrade;
    private List<int> _tabIds = new();

    public override void _Ready()
    {
        _vignette = Vignette();
        AddChild(_vignette);

        _topBar = BuildTopBar();
        AddChild(_topBar);

        _hints = MakeLabel("", 13, Dim);
        _hints.HorizontalAlignment = HorizontalAlignment.Right;
        _hints.AnchorLeft = 1;
        _hints.AnchorRight = 1;
        _hints.OffsetLeft = -1100;
        _hints.OffsetRight = -20;
        _hints.OffsetTop = TopHeight + 10;
        _hints.OffsetBottom = TopHeight + 30;
        AddChild(_hints);

        _feed = new VBoxContainer { Position = new Vector2(20, TopHeight + 16), MouseFilter = Control.MouseFilterEnum.Ignore };
        _feed.AddThemeConstantOverride("separation", 6);
        AddChild(_feed);

        _bannerPanel = Panel(Ground, Line, 8);
        _banner = MakeLabel("", 18, Ink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _bannerPanel.AddChild(_banner);
        _bannerPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.Minsize, 0);
        _bannerPanel.GrowHorizontal = Control.GrowDirection.Both;
        _bannerPanel.OffsetTop = TopHeight + 54;
        _bannerPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_bannerPanel);

        _bottom = BuildBottom();
        AddChild(_bottom);

        _help = BuildHelp();
        AddChild(_help);

        _info = BuildInfoCard();
        AddChild(_info);

        _tooltip = Panel(new Color(0.02f, 0.03f, 0.07f, 0.94f), Line, 8);
        _tooltipText = MakeLabel("", 14, Ink);
        _tooltip.AddChild(_tooltipText);
        _tooltip.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tooltip.Visible = false;
        AddChild(_tooltip);
    }

    public void Setup(IReadOnlyList<(string Id, string Name, string Key)> buildMenu, Action<string> onBuild, Action<int> onSelectBase, Action onUpgrade, Action<string> onAction)
    {
        _onAction = onAction;
        _onBuild = onBuild;
        _onSelectBase = onSelectBase;
        _onUpgrade = onUpgrade;
        foreach (var (id, name, key) in buildMenu) AddCommandButton(id, name, key);
    }

    /// <summary>One command-card button (hidden until ShowCommands lists it). Also used for tiles made on demand (EnsureCommand).</summary>
    private void AddCommandButton(string id, string name, string key)
    {
        var button = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(CommandWidth, 88), ToggleMode = true };
        StyleButton(button, new Color(0.17f, 0.23f, 0.36f), new Color(0.06f, 0.1f, 0.19f));
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 7);
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var icon = Icon(id, 18);
        top.AddChild(icon);
        var keyLabel = MakeLabel(key, 12, Dim);
        keyLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        keyLabel.HorizontalAlignment = HorizontalAlignment.Right;
        top.AddChild(keyLabel);
        box.AddChild(top);
        var nameLabel = MakeLabel(name, 13, Ink);
        nameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        nameLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        box.AddChild(nameLabel);
        // One line, clipped: wrapped costs pushed the second row off the bottom of the screen.
        var cost = MakeLabel("", 11, Dim);
        cost.ClipText = true;
        box.AddChild(cost);
        var tag = MakeLabel("", 11, Warn);
        top.AddChild(tag);
        button.AddChild(box);
        var captured = id;
        button.Pressed += () => _onBuild?.Invoke(captured);
        _commandGrid.AddChild(button);
        _commands[id] = (button, tag, cost);
        _commandParts[id] = (box, top, icon, nameLabel, keyLabel); // picture tiles (PlanetHud.BuildStrip.cs)
        button.MouseEntered += () => HoveredCommand = captured;
        button.MouseExited += () =>
        {
            if (HoveredCommand == captured) HoveredCommand = null;
        };
    }

    /// <summary>
    /// In orbit the helm screen (HelmScreen) takes over: hide the top bar, bottom panel, hints, banner, vignette and
    /// the info card here. The event feed and the F1 help stay (they draw over the helm).
    /// </summary>
    public void SetOrbitMode(bool orbit)
    {
        _topBar.Visible = !orbit;
        _bottom.Visible = !orbit;
        _hints.Visible = !orbit;
        _bannerPanel.Visible = !orbit && _banner.Text.Length > 0;
        _vignette.Visible = !orbit;
        _feed.Position = new Vector2(orbit ? 150 : 20, orbit ? 84 : TopHeight + 16);
        if (orbit)
        {
            _info.Visible = false;
            _tooltip.Visible = false;
        }
        _orbitMode = orbit;
    }

    private bool _orbitMode;

    /// <summary>True when the mouse is over the HUD (so the world shouldn't react to it).</summary>
    public bool BlocksMouse(Vector2 mouse) =>
        (_topBar.Visible && _topBar.GetGlobalRect().HasPoint(mouse)) || (_bottom.Visible && _bottom.GetGlobalRect().HasPoint(mouse)) ||
        (_info.Visible && _info.GetGlobalRect().HasPoint(mouse)) || (_help.Visible && _help.GetGlobalRect().HasPoint(mouse)) || RuinsBlockMouse(mouse);

    /// <summary>Hooks for the info card's action button and its close button.</summary>
    public void SetInfoHandlers(Action onAction, Action onClose)
    {
        _onInfoAction = onAction;
        _onInfoClose = onClose;
    }

    private const int InfoWidth = 470;

    /// <summary>Shows the info card filled from v, or hides it (null). It sits above the command card, sized to its content.</summary>
    public void ShowInfo(InfoView? v)
    {
        _info.Visible = v != null && !_orbitMode;
        if (v == null) return;
        _infoIcon.Texture = Icons.Get(v.IconId, Accent);
        _infoTitle.Text = v.Title.ToUpperInvariant();
        _infoSubtitle.Text = v.Subtitle;
        _infoCost.Text = v.Cost;
        _infoCost.AddThemeColorOverride("font_color", v.Affordable ? Ink : Warn);
        _infoBody.Text = v.Body.Replace("\r", "");
        while (_infoRowLabels.Count < v.Rows.Count)
        {
            var name = MakeLabel("", 12, Dim);
            name.CustomMinimumSize = new Vector2(92, 0);
            var value = MakeLabel("", 13, Ink);
            value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            value.CustomMinimumSize = new Vector2(InfoWidth - 92 - 12 - 44, 0);
            _infoRows.AddChild(name);
            _infoRows.AddChild(value);
            _infoRowLabels.Add((name, value));
        }
        for (var i = 0; i < _infoRowLabels.Count; i++)
        {
            var show = i < v.Rows.Count;
            _infoRowLabels[i].Name.Visible = show;
            _infoRowLabels[i].Value.Visible = show;
            if (!show) continue;
            _infoRowLabels[i].Name.Text = v.Rows[i].Label.ToUpperInvariant();
            _infoRowLabels[i].Value.Text = v.Rows[i].Value;
        }
        _infoProgress.Visible = v.Progress >= 0;
        _infoBar.Fraction = Mathf.Max(0, v.Progress);
        _infoProgressText.Text = v.ProgressText;
        _infoAction.Visible = v.ActionLabel.Length > 0;
        _infoAction.Text = v.ActionLabel;
        _infoAction.Disabled = !v.ActionEnabled;
        _infoFooter.Visible = v.Footer.Length > 0;
        _infoFooter.Text = v.Footer;

        // Fit to the content and keep it just above the bottom panel, at the right edge.
        _info.ResetSize();
        var size = _info.GetCombinedMinimumSize();
        var screen = GetViewport().GetVisibleRect().Size;
        _info.Position = new Vector2(screen.X - size.X - 16, Mathf.Max(TopHeight + 8, screen.Y - BottomHeight - 12 - size.Y));
    }

    private PanelContainer BuildInfoCard()
    {
        var panel = Panel(new Color(0.02f, 0.03f, 0.07f, 0.97f), Accent, 10, 18, 14);
        panel.Visible = false;
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(InfoWidth - 36, 0) };
        box.AddThemeConstantOverride("separation", 8);

        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        head.AddThemeConstantOverride("separation", 12);
        _infoIcon = new TextureRect { CustomMinimumSize = new Vector2(44, 44), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddChild(_infoIcon);
        var names = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", 0);
        _infoTitle = MakeLabel("", 20, Ink);
        _infoSubtitle = MakeLabel("", 13, Accent);
        names.AddChild(_infoTitle);
        names.AddChild(_infoSubtitle);
        head.AddChild(names);
        var close = new Button { Text = "×", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, TooltipText = "Close (Esc)" };
        StyleButton(close, Line, new Color(0.04f, 0.06f, 0.11f));
        close.Pressed += () => _onInfoClose?.Invoke();
        head.AddChild(close);
        box.AddChild(head);

        _infoCost = MakeLabel("", 14, Ink);
        box.AddChild(_infoCost);
        box.AddChild(new ColorRect { Color = Line, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });
        _infoBody = MakeLabel("", 14, Ink);
        _infoBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoBody.CustomMinimumSize = new Vector2(InfoWidth - 36, 0);
        box.AddChild(_infoBody);
        _infoRows = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        _infoRows.AddThemeConstantOverride("h_separation", 12);
        _infoRows.AddThemeConstantOverride("v_separation", 5);
        box.AddChild(_infoRows);

        var prog = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        prog.AddThemeConstantOverride("separation", 4);
        _infoBar = new Bar { Colour = new Color(0.55f, 0.88f, 1f), CustomMinimumSize = new Vector2(0, 8) };
        prog.AddChild(_infoBar);
        _infoProgressText = MakeLabel("", 13, Dim);
        prog.AddChild(_infoProgressText);
        _infoProgress = prog;
        box.AddChild(prog);

        _infoAction = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) };
        _infoAction.AddThemeFontSizeOverride("font_size", 15);
        StyleButton(_infoAction, Accent, new Color(0.12f, 0.09f, 0.03f));
        _infoAction.Pressed += () => _onInfoAction?.Invoke();
        box.AddChild(_infoAction);
        _infoFooter = MakeLabel("", 13, Dim);
        _infoFooter.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoFooter.CustomMinimumSize = new Vector2(InfoWidth - 36, 0);
        box.AddChild(_infoFooter);

        panel.AddChild(box);
        return panel;
    }

    public override void _Process(double delta)
    {
        var now = Time.GetTicksMsec() / 1000.0;
        for (var i = _feedItems.Count - 1; i >= 0; i--)
        {
            var age = now - _feedItems[i].Born;
            if (age > 9)
            {
                _feedItems[i].Node.QueueFree();
                _feedItems.RemoveAt(i);
            }
            else if (age > 7) _feedItems[i].Node.Modulate = new Color(1, 1, 1, (float)(9 - age) / 2f);
        }
    }

    // ---------------------------------------------------------------- top

    public void ShowTop(Simulation sim, int player, string factionSub, float powerUse, float powerSupply, float stored, float capacity,
        string research, string researchSub, string clock)
    {
        var data = sim.Data;
        var p = sim.Players[player];
        var faction = data.Faction(p.FactionId).Name;
        // Long names ("United Federation of Planets") take a third of the bar: use the short id instead.
        _factionName.Text = (faction.Length > 16 ? p.FactionId.Replace('_', ' ') : faction).ToUpperInvariant();
        _factionSub.Text = factionSub;
        var income = sim.IncomePerMinute(player);
        foreach (var r in data.HudResources)
        {
            if (!_chips.TryGetValue(r.Index, out var chip)) continue;
            chip.Amount.Text = Hud.Whole(p.Stock[r.Index]);
            var rate = income[r.Index];
            var perMin = Mathf.RoundToInt(rate.ToFloat());
            chip.Rate.Text = perMin == 0 ? ShortName(r.Name) : $"{ShortName(r.Name)} {(perMin > 0 ? "+" : "−")}{Mathf.Abs(perMin)}";
            chip.Rate.AddThemeColorOverride("font_color", perMin > 0 ? Good : perMin < 0 ? Warn : Dim);
            if (chip.Amount.GetParent()?.GetParent() is Control box)
                box.TooltipText = $"{r.Name}: {Hud.Whole(p.Stock[r.Index])}" + (perMin == 0 ? "" : $"  ({(perMin > 0 ? "+" : "−")}{Mathf.Abs(perMin)} a minute)")
                    + (TradeTipLine.Length > 0 ? "\n" + TradeTipLine : ""); // trade partners' income (PlanetHud.Ruins.cs)
        }
        _researchChip.Text = research;
        _researchSub.Text = researchSub;
        _powerChip.Text = $"{Mathf.RoundToInt(powerUse)} / {Mathf.RoundToInt(powerSupply)}";
        _powerChip.AddThemeColorOverride("font_color", powerUse > powerSupply ? Warn : Ink);
        // capacity < 0 = hidden (the first outpost is still assembling, so there is no storage to show yet); the
        // chip keeps its place in the bar so nothing shifts when it comes back.
        _storageBox.Modulate = capacity < 0 ? Colors.Transparent : Colors.White;
        _storageBox.MouseFilter = capacity < 0 ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Pass;
        _storageChip.Text = $"{Mathf.FloorToInt(stored)} / {Mathf.RoundToInt(Mathf.Max(capacity, 0))}";
        _storageChip.AddThemeColorOverride("font_color", stored >= capacity - 0.5f ? Warn : stored > capacity * 0.85f ? Accent : Ink);
        _clock.Text = clock;
    }

    /// <summary>Called once the sim exists: one chip per HUD resource.</summary>
    public void SetResources(IReadOnlyList<(int Index, string Id, string Name)> resources)
    {
        var row = _topBar.GetNode<HBoxContainer>("Row");
        var insertAt = row.GetNode("ResourcesStart").GetIndex() + 1;
        foreach (var (index, id, name) in resources)
        {
            // Pass (not Ignore) so hovering the chip shows its tooltip: full name, stock and rate.
            var chip = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass, TooltipText = name };
            chip.AddThemeConstantOverride("separation", 6);
            chip.AddChild(IconTile(id));
            var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            text.AddThemeConstantOverride("separation", -3);
            var amount = MakeLabel("0", 17, Ink);
            var rate = MakeLabel(ShortName(name), 11, Dim);
            text.AddChild(amount);
            text.AddChild(rate);
            chip.AddChild(text);
            row.AddChild(chip);
            row.MoveChild(chip, insertAt++);
            _chips[index] = (amount, rate);
        }
    }

    /// <summary>Top-bar label: "Duranium Ore" → "DUR ORE" so the bar fits; one-word names stay whole.</summary>
    private static string ShortName(string name)
    {
        var space = name.IndexOf(' ');
        return (space > 3 ? name[..3] + name[space..] : name).ToUpperInvariant();
    }

    public void ShowHints(string text) => _hints.Text = text;

    private PanelContainer _testTag = null!;

    /// <summary>The orange TEST MODE tag in the top bar (the test panel has changed the game).</summary>
    public void SetTestMode(bool on)
    {
        if (_testTag.Visible != on) _testTag.Visible = on;
    }

    public bool HelpVisible => _help.Visible;

    public void ToggleHelp() => _help.Visible = !_help.Visible;

    /// <summary>The full controls list (F1), in two columns.</summary>
    private PanelContainer BuildHelp()
    {
        var panel = Panel(new Color(0.02f, 0.03f, 0.07f, 0.96f), Accent, 10, 22, 16);
        panel.Visible = false;
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(MakeLabel("CONTROLS", 18, Accent));
        var cols = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        cols.AddThemeConstantOverride("separation", 36);
        (string Key, string What)[][] groups =
        {
            new[]
            {
                ("Left-click", "select a building, vehicle, base or tile"), ("Shift+click", "add vehicles to the selection"), ("A", "select all vehicles"),
                ("Right-click", "drive there (a crawler on a deposit mines it)"), ("F", "crawler: unfurl / pack up / send for repair (in orbit: outpost video feed)"), ("V", "rovers: wipers auto / on / off"), ("L", "vehicles: headlights auto / on / off"),
                ("R", "send damaged vehicles to the repair pad"), ("T", "rovers: change a flat tyre (uses a spare)"),
                ("I", "inventory: parts, spare kits, construction parts, keep-in-stock targets"),
                ("J", "jury-rig the selected building or vehicles: no kits, capped, wears faster"),
                ("1–9, 0", "Machine Shop / workshop: queue a recipe (Shift +5); Backspace cancels the last order"),
                ("K", "selected base: its colony core's workshop (spare kits), K again: the base"),
                ("F / 1", "construction rover: found an outpost (pick a site 8+ tiles from your bases)"),
                ("Tab", "next base (in orbit: next scan)"), ("Esc", "stop placing / close the card / close the drawer / back to the base"),
                ("O", "orbit view and back"), ("Orbit", "drag or WASD turn the planet, wheel zoom, 1–9 deploy, click to send"),
                ("Sky (top bar)", "day, season, local time and weather at the view's centre; hover for wind and ground"),
                ("Ruins", "rust diamond on the minimap: an Excavation Site on the hex digs it, layer by layer"),
                ("Crew sick", "chip by the base tabs: a dig pathogen slows the crew; vehicles can carry it"),
            },
            new[]
            {
                ("B", "build drawer: every building with its picture, cost and key"),
                ("1–9", "a building's card (again or Enter: place it; Shift+key: place at once); a bay's / lab's card when one is selected"),
                ("Enter", "the card's button: place the building, order the vehicle, start the research"),
                ("I (placing)", "while placing: the building's card again"), ("R / Shift+R", "rotate while placing"),
                ("U", "upgrade the selected building or base"), ("M", "maintain the selected building"), ("P", "pause / resume the selected building (no input, no power)"), ("X", "cancel a build or upgrade / salvage (an unfinished founded hub: the rover comes back)"),
                ("Backspace", "cancel the bay's / shop's last order"), ("WASD, middle-drag", "pan"), ("Q / E", "turn the view"), ("Wheel", "zoom"), ("H", "fly home"), ("F1", "this list"),
                ("F9", "test panel (dev builds): edit anything for testing"), ("Ctrl+Z", "undo the last test-panel edit"), ("Space", "pause / resume while the test panel is open"),
                ("F9 › SKY", "force time of day, season, weather and wind (look only)"),
                ("F3", "achievements, and the HANGAR for the cosmetics they unlock"),
                ("C", "civilization dossier: level ladder, awareness, study, Starfleet Standing (Esc closes)"),
                ("Hidden post", "orbiting a native world: HIDDEN POST on the strip or its card studies them ×4, may be found"),
                ("Standing (top bar)", "Starfleet Standing (Prime Directive eras); hover for the ladder, click for the dossier"),
                ("First contact", "FIRST CONTACT on a living world's card (below L5: click twice); pick before each step ends"),
                ("Membership", "dossier: Contact → Trade partner → Candidate → Allied world; trade in the top bar tooltips"),
                ("Ally ships", "green chevrons on the plot: ESCORT follows the flagship, SCOUT + click a body charts it"),
                ("F9 › CIV", "spawn / edit a civilization, Standing, relations, ruins on the colony map"),
            },
        };
        foreach (var g in groups)
        {
            var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
            grid.AddThemeConstantOverride("h_separation", 14);
            grid.AddThemeConstantOverride("v_separation", 4);
            foreach (var (key, what) in g)
            {
                grid.AddChild(MakeLabel(key, 14, Accent));
                grid.AddChild(MakeLabel(what, 14, Ink));
            }
            cols.AddChild(grid);
        }
        box.AddChild(cols);
        box.AddChild(MakeLabel("Press F1 to close", 12, Dim));
        panel.AddChild(box);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize, 0);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        return panel;
    }

    public void ShowBanner(string text)
    {
        _bannerPanel.Visible = text.Length > 0 && !_orbitMode;
        if (_banner.Text == text) return;
        _banner.Text = text;
        // Shrink to the new text and re-centre.
        _bannerPanel.ResetSize();
        _bannerPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.Minsize, 0);
        _bannerPanel.OffsetTop = TopHeight + 54;
        _bannerPanel.OffsetBottom = TopHeight + 54 + _bannerPanel.GetCombinedMinimumSize().Y;
    }

    /// <summary>Adds a line to the event feed (newest at the bottom; old ones fade out).</summary>
    public void PushEvent(string text, Color dot)
    {
        var item = Panel(new Color(0.02f, 0.03f, 0.063f, 0.88f), Line, 8);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        var marker = new ColorRect { Color = dot, CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddChild(marker);
        row.AddChild(MakeLabel(text, 14, Ink));
        item.AddChild(row);
        _feed.AddChild(item);
        _feedItems.Add((item, Time.GetTicksMsec() / 1000.0));
        while (_feedItems.Count > 4)
        {
            _feedItems[0].Node.QueueFree();
            _feedItems.RemoveAt(0);
        }
    }

    /// <summary>Small panel next to the mouse; empty text hides it.</summary>
    public void ShowTooltip(string text, Vector2 mouse)
    {
        _tooltip.Visible = text.Length > 0;
        if (!_tooltip.Visible) return;
        _tooltipText.Text = text.Replace("\r", ""); // AppendLine writes \r\n on Windows; Godot shows \r as a blank line
        if (_tooltipRich != null && _tooltipRich.Visible) _tooltipRich.Visible = false; // ShowRichTooltip (PlanetHud.Parts.cs)
        if (!_tooltipText.Visible) _tooltipText.Visible = true;
        PlaceTooltip(mouse);
    }

    private void PlaceTooltip(Vector2 mouse)
    {
        _tooltip.ResetSize();
        var screen = GetViewport().GetVisibleRect().Size;
        var size = _tooltip.GetCombinedMinimumSize();
        var pos = mouse + new Vector2(20, 20);
        if (pos.X + size.X > screen.X - 8) pos.X = mouse.X - size.X - 12;
        if (pos.Y + size.Y > screen.Y - BottomHeight - 8) pos.Y = mouse.Y - size.Y - 12;
        _tooltip.Position = pos;
    }

    // ---------------------------------------------------------------- bottom centre

    public void ShowBaseTabs(IReadOnlyList<(int Id, string Name)> bases, int selected)
    {
        var ids = new List<int>();
        foreach (var b in bases) ids.Add(b.Id);
        if (!ids.SequenceEqual(_tabIds))
        {
            foreach (var b in _tabButtons) b.QueueFree();
            _tabButtons.Clear();
            foreach (var (id, name) in bases)
            {
                var button = new Button { Text = name.ToUpperInvariant(), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34), ToggleMode = true };
                button.AddThemeFontSizeOverride("font_size", 14);
                StyleButton(button, Line, new Color(0.04f, 0.07f, 0.13f));
                var captured = id;
                button.Pressed += () => _onSelectBase?.Invoke(captured);
                _tabs.AddChild(button);
                _tabButtons.Add(button);
            }
            _tabIds = ids;
        }
        for (var i = 0; i < _tabButtons.Count; i++) _tabButtons[i].SetPressedNoSignal(_tabIds[i] == selected);
        _tabs.Visible = bases.Count > 0;
        UpdateTabRow();
    }

    public void ShowBase(BaseView v)
    {
        _baseView.Visible = true;
        _detailView.Visible = false;
        _structureView.Visible = false;
        _orbitPanel.Visible = false;
        _baseIcon.Texture = Icons.Get(v.CoreId, LocalColour);
        _baseName.Text = v.Name.ToUpperInvariant();
        _coreName.Text = v.CoreName;
        _levelBadge.Text = $" LEVEL {v.Level} ";
        _upgradeLine.Text = v.UpgradeLine;
        _upgradeButton.Visible = v.UpgradeCost.Length > 0 && v.UpgradeProgress < 0;
        _upgradeButton.Text = $"UPGRADE [U]    {v.UpgradeCost}";
        _upgradeButton.Disabled = !v.UpgradeEnabled;

        _powerBar.Fraction = v.PowerSupply <= 0 ? (v.PowerUse > 0 ? 1 : 0) : Mathf.Min(1f, v.PowerUse / v.PowerSupply);
        _powerBar.Colour = v.PowerUse > v.PowerSupply ? Warn : Accent;
        var spare = v.PowerSupply - v.PowerUse;
        _powerText.Text = $"{Mathf.RoundToInt(v.PowerUse)} used / {Mathf.RoundToInt(v.PowerSupply)}  ({(spare >= 0 ? "+" : "−")}{Mathf.RoundToInt(Mathf.Abs(spare))})";
        _upgradeRow.Visible = v.UpgradeProgress >= 0;
        _upgradeBar.Fraction = Mathf.Max(0, v.UpgradeProgress);
        _upgradeText.Text = $"{Mathf.RoundToInt(Mathf.Max(0, v.UpgradeProgress) * 100)}%  [X] cancel";
        _buildingsText.Text = v.Buildings;

        _queueTitle.Text = $"BUILD QUEUE · {v.QueueCapacity} AT A TIME";
        var rows = _queueList.GetChildren();
        while (rows.Count < Mathf.Min(v.Queue.Count, 3))
        {
            _queueList.AddChild(new QueueRow());
            rows = _queueList.GetChildren();
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var row = (QueueRow)rows[i];
            row.Visible = i < v.Queue.Count;
            if (row.Visible) row.Show(v.Queue[i].Name, v.Queue[i].Progress);
        }
        if (v.Queue.Count > 3) _queueTitle.Text += $"  (+{v.Queue.Count - 3} more)";
        if (v.Queue.Count == 0) _queueTitle.Text += "\nNothing queued: pick a building from the card";
    }

    /// <summary>Building, rovers, tile or orbit info in place of the base view.</summary>
    public void ShowDetail(string title, string subtitle, string body)
    {
        _baseView.Visible = false;
        _structureView.Visible = false;
        _orbitPanel.Visible = false;
        _detailView.Visible = true;
        _detailTitle.Text = title.ToUpperInvariant();
        _detailSubtitle.Text = subtitle;
        _detailBody.Text = body.Replace("\r", "");
    }

    /// <summary>The selected building: stats, condition, progress and its buttons.</summary>
    public void ShowStructure(StructureView v)
    {
        _baseView.Visible = false;
        _detailView.Visible = false;
        _orbitPanel.Visible = false;
        _structureView.Visible = true;
        _structIcon.Texture = Icons.Get(v.IconId, LocalColour);
        _structTitle.Text = v.Title.ToUpperInvariant();
        _structSub.Text = v.Subtitle;
        _structStatus.Text = v.Status;
        _structStatus.AddThemeColorOverride("font_color", v.StatusColour);

        for (var i = 0; i < _structRows.Count; i++)
        {
            var show = i < v.Rows.Count;
            _structRows[i].Name.Visible = show;
            _structRows[i].Value.Visible = show;
            if (!show) continue;
            _structRows[i].Name.Text = v.Rows[i].Label.ToUpperInvariant();
            _structRows[i].Value.Text = v.Rows[i].Value;
        }

        ShowFab(v.Fab); // PlanetHud.Parts.cs: a Machine Shop's / core workshop's queue
        _conditionBox.Visible = v.Condition >= 0 && v.Fab == null;
        if (v.Condition >= 0)
        {
            _conditionBar.Fraction = v.Condition / 100f;
            _conditionBar.Colour = v.Condition >= 70 ? Good : v.Condition >= 50 ? Accent : Warn;
            _conditionText.Text = v.ConditionText;
            _maintenanceText.Text = v.MaintenanceText;
        }
        _progressBox.Visible = v.Progress >= 0 && v.Fab == null;
        if (v.Progress >= 0)
        {
            _progressLabel.Text = v.ProgressLabel.ToUpperInvariant();
            _progressBar.Fraction = v.Progress;
            _progressText.Text = v.ProgressText;
        }

        for (var i = 0; i < _actionButtons.Count; i++)
        {
            var b = _actionButtons[i];
            b.Visible = i < v.Actions.Count;
            if (!b.Visible) continue;
            var a = v.Actions[i];
            _actionIds[i] = a.Id;
            b.Text = a.Detail.Length > 0 ? $"{a.Label}   {a.Detail}" : a.Label;
            b.Disabled = !a.Enabled;
        }
    }

    /// <param name="columns">Tiles per row: 5 (100 px) as always; more makes them narrower in the same card width (a
    /// Machine Shop's 12 recipes fit two rows of 6).</param>
    public void ShowCommands(string title, IReadOnlyList<CommandView> items, bool enabled, int columns = CommandColumns)
    {
        HideBuildStrip(); // the build menu is the strip + drawer now (PlanetHud.BuildStrip.cs)
        _commandTitle.Text = title;
        columns = Mathf.Max(CommandColumns, columns);
        if (_commandGrid.Columns != columns) _commandGrid.Columns = columns;
        var tileWidth = (CommandColumns * CommandWidth - (columns - CommandColumns) * 5) / columns;
        foreach (var c in items)
        {
            if (!_commands.TryGetValue(c.Id, out var ui)) continue;
            if (!Mathf.IsEqualApprox(ui.Button.CustomMinimumSize.X, tileWidth)) ui.Button.CustomMinimumSize = new Vector2(tileWidth, ui.Button.CustomMinimumSize.Y);
            ui.Button.SetPressedNoSignal(c.Active);
            ui.Button.Disabled = !enabled || c.Disabled;
            ui.Cost.Text = c.Cost;
            ui.Cost.AddThemeColorOverride("font_color", c.Affordable ? Dim : Warn); // red cost = can't afford it yet
            ui.Tag.Text = c.Tag;
        }
        // Only the listed commands show (the build menu, or a Vehicle Bay's orders).
        foreach (var (id, ui) in _commands)
        {
            var show = false;
            foreach (var c in items)
                if (c.Id == id) show = true;
            if (ui.Button.Visible != show) ui.Button.Visible = show;
        }
    }

    // ---------------------------------------------------------------- construction

    private Control BuildTopBar()
    {
        var bar = Panel(new Color(0.02f, 0.03f, 0.063f, 0.95f), Line, 0, 20, 6);
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        bar.CustomMinimumSize = new Vector2(0, TopHeight);
        bar.MouseFilter = Control.MouseFilterEnum.Stop;
        var row = new HBoxContainer { Name = "Row", MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);

        var faction = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        faction.AddThemeConstantOverride("separation", 10);
        faction.AddChild(_factionSwatch = new ColorRect { Color = LocalColour, CustomMinimumSize = new Vector2(8, 28), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        var names = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", -2);
        _factionName = MakeLabel("", 16, Ink);
        _factionSub = MakeLabel("", 12, Dim);
        names.AddChild(_factionName);
        names.AddChild(_factionSub);
        faction.AddChild(names);
        row.AddChild(faction);
        row.AddChild(new VSeparator { Name = "ResourcesStart", MouseFilter = Control.MouseFilterEnum.Ignore });

        row.AddChild(new VSeparator { MouseFilter = Control.MouseFilterEnum.Ignore });
        var power = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        power.AddThemeConstantOverride("separation", 8);
        power.AddChild(IconTile("energy"));
        var powerText = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        powerText.AddThemeConstantOverride("separation", -2);
        _powerChip = MakeLabel("0 / 0", 17, Ink);
        powerText.AddChild(_powerChip);
        powerText.AddChild(MakeLabel("POWER", 11, Dim));
        power.MouseFilter = Control.MouseFilterEnum.Pass;
        power.TooltipText = "Power used / supplied across your bases";
        power.AddChild(powerText);
        row.AddChild(power);

        var storage = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _storageBox = storage;
        storage.AddThemeConstantOverride("separation", 8);
        storage.AddChild(IconTile("storage"));
        var storageText = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        storageText.AddThemeConstantOverride("separation", -2);
        _storageChip = MakeLabel("0 / 0", 17, Ink);
        storageText.AddChild(_storageChip);
        storageText.AddChild(MakeLabel("STORAGE", 11, Dim));
        storage.MouseFilter = Control.MouseFilterEnum.Pass;
        storage.TooltipText = "Storage used / space (ores, metals, fuel, strategics; crew and latinum take none)";
        storage.AddChild(storageText);
        row.AddChild(storage);

        var research = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass, TooltipText = "Research: build a Research Lab, select it and pick a tech" };
        research.AddThemeConstantOverride("separation", 8);
        research.AddChild(IconTile("research"));
        var researchText = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(190, 0) };
        researchText.AddThemeConstantOverride("separation", -3);
        _researchChip = MakeLabel("—", 17, Ink);
        _researchSub = MakeLabel("RESEARCH", 11, Dim);
        _researchSub.ClipText = true;
        _researchSub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; // "PREVENTIVE MAINTEN…" rather than a hard cut
        researchText.AddChild(_researchChip);
        researchText.AddChild(_researchSub);
        research.AddChild(researchText);
        row.AddChild(research);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        // Shown once the test panel (F9) has changed anything in this game.
        _testTag = Panel(Accent, Accent, 10, 9, 2);
        _testTag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _testTag.MouseFilter = Control.MouseFilterEnum.Pass;
        _testTag.TooltipText = "The test panel (F9) has changed this game";
        _testTag.AddChild(MakeLabel("TEST MODE", 12, new Color(0.043f, 0.059f, 0.102f)));
        _testTag.Visible = false;
        row.AddChild(_testTag);
        _clock = MakeLabel("", 15, Dim);
        row.AddChild(_clock);
        bar.AddChild(row);
        return bar;
    }

    private Control BuildBottom()
    {
        var panel = Panel(Ground, Accent, 0, 0, 0, topBorder: 2);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        panel.OffsetTop = -BottomHeight;
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 0);

        // Minimap
        var mapBox = Section(286);
        mapBox.AddChild(Caption("SURVEY MAP"));
        mapBox.AddChild(Minimap);
        var legend = MakeLabel("Squares: bases · dots: vehicles · frame: view\nClick or drag to move the camera", 12, Dim);
        mapBox.AddChild(legend);
        row.AddChild(Framed(mapBox, 286));

        // Centre
        var centre = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.AddThemeConstantOverride("separation", 10);
        _tabs = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _tabs.AddThemeConstantOverride("separation", 6);
        centre.AddChild(BuildTabRow(_tabs)); // base tabs + WORKSHOP / INVENTORY chips on the right (PlanetHud.Parts.cs)
        _baseView = BuildBaseView();
        centre.AddChild(_baseView);
        _detailView = BuildDetailView();
        centre.AddChild(_detailView);
        _structureView = BuildStructureView();
        centre.AddChild(_structureView);
        _orbitPanel = BuildOrbitPanel();
        centre.AddChild(_orbitPanel);
        var centreMargin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "right" }) centreMargin.AddThemeConstantOverride($"margin_{side}", 20);
        centreMargin.AddThemeConstantOverride("margin_top", 12);
        centreMargin.AddThemeConstantOverride("margin_bottom", 12);
        centreMargin.AddChild(centre);
        row.AddChild(centreMargin);

        // Command card
        var width = CommandColumns * CommandWidth + (CommandColumns - 1) * 5;
        var card = Section(width + 32);
        _commandTitle = Caption("BUILD");
        card.AddChild(_commandTitle);
        _commandGrid = new GridContainer { Columns = CommandColumns, MouseFilter = Control.MouseFilterEnum.Pass };
        _commandGrid.AddThemeConstantOverride("h_separation", 5);
        _commandGrid.AddThemeConstantOverride("v_separation", 5);
        card.AddChild(_commandGrid);
        row.AddChild(Framed(card, width + 32));

        panel.AddChild(row);
        return panel;
    }

    public void SetScanHandler(Action<int> onScan) => _onScan = onScan;

    /// <summary>The orbit view's panel in place of the base / building views.</summary>
    public void ShowOrbit(OrbitPanelView v)
    {
        _baseView.Visible = false;
        _detailView.Visible = false;
        _structureView.Visible = false;
        _orbitPanel.Visible = true;
        _orbitShip.Text = v.Ship.ToUpperInvariant();
        _orbitSub.Text = v.Subtitle;
        for (var i = 0; i < _scanButtons.Count; i++) _scanButtons[i].SetPressedNoSignal(i == v.Scan);
        _orbitLegend.Text = v.Legend;
        _orbitHover.Text = v.Hover.Replace("\r", "");
    }

    private Control BuildOrbitPanel()
    {
        var grid = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass, Visible = false };
        grid.AddThemeConstantOverride("separation", 20);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0), MouseFilter = Control.MouseFilterEnum.Pass };
        left.AddThemeConstantOverride("separation", 6);
        _orbitShip = MakeLabel("", 19, Ink);
        _orbitSub = MakeLabel("", 13, Accent);
        left.AddChild(_orbitShip);
        left.AddChild(_orbitSub);
        left.AddChild(Caption("SCAN  [Tab]"));
        var scans = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        scans.AddThemeConstantOverride("h_separation", 5);
        scans.AddThemeConstantOverride("v_separation", 5);
        for (var i = 0; i < ScanNames.Length; i++)
        {
            var b = new Button { Text = ScanNames[i], ToggleMode = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) };
            b.AddThemeFontSizeOverride("font_size", 12);
            StyleButton(b, Line, new Color(0.06f, 0.1f, 0.19f));
            var index = i;
            b.Pressed += () => _onScan?.Invoke(index);
            scans.AddChild(b);
            _scanButtons.Add(b);
        }
        left.AddChild(scans);
        _orbitLegend = MakeLabel("", 12, Dim);
        _orbitLegend.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _orbitLegend.CustomMinimumSize = new Vector2(330, 0);
        left.AddChild(_orbitLegend);
        grid.AddChild(left);
        _orbitHover = MakeLabel("", 13, Ink);
        _orbitHover.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _orbitHover.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        grid.AddChild(_orbitHover);
        return grid;
    }

    private const int CommandColumns = 5;
    private const int CommandWidth = 100;

    /// <summary>
    /// The building panel: [icon, name, level, status, buttons] [stat rows] [condition and maintenance, progress].
    /// </summary>
    private Control BuildStructureView()
    {
        var grid = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass, Visible = false };
        grid.AddThemeConstantOverride("separation", 18);

        var id = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0), MouseFilter = Control.MouseFilterEnum.Pass };
        id.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 10);
        _structIcon = new TextureRect { CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddChild(_structIcon);
        var names = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", 0);
        _structTitle = MakeLabel("", 19, Ink);
        _structSub = MakeLabel("", 13, Dim);
        names.AddChild(_structTitle);
        names.AddChild(_structSub);
        head.AddChild(names);
        id.AddChild(head);
        _structStatus = MakeLabel("", 14, Accent);
        _structStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _structStatus.CustomMinimumSize = new Vector2(220, 0);
        id.AddChild(_structStatus);
        for (var i = 0; i < 4; i++) // upgrade, maintain, pause, salvage
        {
            var b = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 26), Alignment = HorizontalAlignment.Left, ClipText = true };
            b.AddThemeFontSizeOverride("font_size", 13);
            StyleButton(b, Line, new Color(0.06f, 0.1f, 0.19f));
            var index = i;
            b.Pressed += () => _onAction?.Invoke(_actionIds[index]);
            _actionButtons.Add(b);
            _actionIds.Add("");
            id.AddChild(b);
        }
        grid.AddChild(id);

        var rows = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("h_separation", 12);
        rows.AddThemeConstantOverride("v_separation", 5);
        for (var i = 0; i < 6; i++)
        {
            var name = MakeLabel("", 12, Dim);
            name.CustomMinimumSize = new Vector2(80, 0);
            var value = MakeLabel("", 14, Ink);
            value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            value.ClipText = true;
            rows.AddChild(name);
            rows.AddChild(value);
            _structRows.Add((name, value));
        }
        grid.AddChild(rows);

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        side.AddThemeConstantOverride("separation", 4);
        var cond = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        cond.AddThemeConstantOverride("separation", 4);
        cond.AddChild(Caption("CONDITION"));
        _conditionBar = new Bar { Colour = Good, CustomMinimumSize = new Vector2(0, 10) };
        cond.AddChild(_conditionBar);
        _conditionText = MakeLabel("", 13, Ink);
        cond.AddChild(_conditionText);
        _maintenanceText = MakeLabel("", 12, Dim);
        _maintenanceText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _maintenanceText.CustomMinimumSize = new Vector2(210, 0);
        cond.AddChild(_maintenanceText);
        _conditionBox = cond;
        side.AddChild(cond);
        var prog = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        prog.AddThemeConstantOverride("separation", 4);
        _progressLabel = Caption("");
        prog.AddChild(_progressLabel);
        _progressBar = new Bar { Colour = new Color(0.31f, 0.71f, 1f), CustomMinimumSize = new Vector2(0, 8) };
        prog.AddChild(_progressBar);
        _progressText = MakeLabel("", 13, Ink);
        _progressText.ClipText = true;
        prog.AddChild(_progressText);
        _progressBox = prog;
        side.AddChild(prog);
        side.AddChild(BuildFabBox()); // PlanetHud.Parts.cs, hidden unless a fabricator is shown
        grid.AddChild(side);
        return grid;
    }

    private Control BuildBaseView()
    {
        var grid = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("separation", 22);

        // Identity and level
        var id = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0), MouseFilter = Control.MouseFilterEnum.Pass };
        id.AddThemeConstantOverride("separation", 6);
        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 12);
        _baseIcon = new TextureRect { CustomMinimumSize = new Vector2(52, 52), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddChild(_baseIcon);
        var names = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", 2);
        _baseName = MakeLabel("", 21, Ink);
        names.AddChild(_baseName);
        var sub = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        sub.AddThemeConstantOverride("separation", 8);
        _coreName = MakeLabel("", 14, Dim);
        sub.AddChild(_coreName);
        _levelBadge = MakeLabel("", 12, new Color(0.01f, 0.01f, 0.03f));
        var badgeStyle = new StyleBoxFlat { BgColor = Accent };
        badgeStyle.SetCornerRadiusAll(4);
        _levelBadge.AddThemeStyleboxOverride("normal", badgeStyle);
        sub.AddChild(_levelBadge);
        names.AddChild(sub);
        head.AddChild(names);
        id.AddChild(head);
        _upgradeLine = MakeLabel("", 13, Dim);
        _upgradeLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _upgradeLine.CustomMinimumSize = new Vector2(250, 0);
        id.AddChild(_upgradeLine);
        _upgradeButton = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(250, 44) };
        _upgradeButton.AddThemeFontSizeOverride("font_size", 14);
        StyleButton(_upgradeButton, Line, new Color(0.06f, 0.1f, 0.19f));
        _upgradeButton.Pressed += () => _onUpgrade?.Invoke();
        id.AddChild(_upgradeButton);
        grid.AddChild(id);

        // Vital stats
        var stats = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        stats.AddThemeConstantOverride("separation", 10);
        stats.AddChild(StatRow("POWER", Accent, out _powerBar, out _powerText));
        _upgradeRow = StatRow("UPGRADING", new Color(0.62f, 0.82f, 1f), out _upgradeBar, out _upgradeText);
        _upgradeBar.Colour = new Color(0.31f, 0.71f, 1f);
        stats.AddChild(_upgradeRow);
        _buildingsText = MakeLabel("", 13, Ink);
        _buildingsText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stats.AddChild(_buildingsText);
        grid.AddChild(stats);

        // Build queue
        var queue = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        queue.AddThemeConstantOverride("separation", 6);
        _queueTitle = Caption("BUILD QUEUE");
        queue.AddChild(_queueTitle);
        _queueList = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _queueList.AddThemeConstantOverride("separation", 6);
        queue.AddChild(_queueList);
        grid.AddChild(queue);
        return grid;
    }

    private Control BuildDetailView()
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        box.AddThemeConstantOverride("separation", 4);
        _detailTitle = MakeLabel("", 21, Ink);
        _detailSubtitle = MakeLabel("", 14, Accent);
        _detailBody = MakeLabel("", 14, Ink);
        _detailBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_detailTitle);
        box.AddChild(_detailSubtitle);
        box.AddChild(_detailBody);
        return box;
    }

    private static Control StatRow(string name, Color colour, out Bar bar, out Label value)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        var label = MakeLabel(name, 13, colour);
        label.CustomMinimumSize = new Vector2(92, 0);
        row.AddChild(label);
        bar = new Bar { Colour = colour, CustomMinimumSize = new Vector2(80, 12), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(bar);
        value = MakeLabel("", 14, Ink);
        value.CustomMinimumSize = new Vector2(150, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);
        return row;
    }

    private static VBoxContainer Section(int width)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(width - 32, 0), MouseFilter = Control.MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 8);
        return box;
    }

    /// <summary>A fixed-width column with padding and a divider line on its inner side.</summary>
    private static Control Framed(Control content, int width)
    {
        var margin = new MarginContainer { CustomMinimumSize = new Vector2(width, 0), MouseFilter = Control.MouseFilterEnum.Pass };
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        margin.AddChild(content);
        return margin;
    }

    private static Label Caption(string text) => MakeLabel(text, 13, Dim);

    private static TextureRect Icon(string id, int size) => new()
    {
        Texture = Icons.Get(id),
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static Control IconTile(string id)
    {
        var colour = Icons.ResourceColors.TryGetValue(id, out var c) ? c : Ink;
        var tile = Panel(new Color(colour.R, colour.G, colour.B, 0.16f), new Color(0, 0, 0, 0), 8, 6, 6);
        tile.CustomMinimumSize = new Vector2(28, 28);
        tile.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        tile.AddChild(Icon(id, 18));
        return tile;
    }

    private static TextureRect Vignette()
    {
        var gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.55f, 1f },
            Colors = new[] { new Color(0.008f, 0.012f, 0.03f, 0f), new Color(0.008f, 0.012f, 0.03f, 0f), new Color(0.008f, 0.012f, 0.03f, 0.85f) },
        };
        var texture = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.45f),
            FillTo = new Vector2(1.08f, 0.45f),
            Width = 512,
            Height = 288,
        };
        var rect = new TextureRect { Texture = texture, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    public static Label MakeLabel(string text, int size, Color colour)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }

    public static PanelContainer Panel(Color bg, Color border, int radius, int padX = 14, int padY = 8, int topBorder = -1)
    {
        var style = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = padX,
            ContentMarginRight = padX,
            ContentMarginTop = padY,
            ContentMarginBottom = padY,
        };
        if (topBorder >= 0) style.BorderWidthTop = topBorder;
        else style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(radius);
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static void StyleButton(Button button, Color border, Color bg)
    {
        StyleBoxFlat Box(Color b, Color fill)
        {
            var s = new StyleBoxFlat { BgColor = fill, BorderColor = b, ContentMarginLeft = 10, ContentMarginRight = 10 };
            s.SetBorderWidthAll(1);
            s.SetCornerRadiusAll(8);
            return s;
        }
        button.AddThemeStyleboxOverride("normal", Box(border, bg));
        button.AddThemeStyleboxOverride("hover", Box(new Color(0.3f, 0.4f, 0.6f), bg.Lightened(0.08f)));
        button.AddThemeStyleboxOverride("pressed", Box(Accent, new Color(0.24f, 0.17f, 0.05f)));
        button.AddThemeStyleboxOverride("hover_pressed", Box(Accent, new Color(0.28f, 0.2f, 0.06f)));
        button.AddThemeStyleboxOverride("disabled", Box(new Color(0.11f, 0.14f, 0.2f), new Color(0.04f, 0.05f, 0.09f)));
        button.AddThemeColorOverride("font_color", Ink);
        button.AddThemeColorOverride("font_disabled_color", Dim);
    }
}

/// <summary>A flat progress bar: background track and a fill (0..1).</summary>
public partial class Bar : Control
{
    private float _fraction;
    private Color _colour = PlanetHud.Accent;

    public float Fraction
    {
        get => _fraction;
        set
        {
            _fraction = Mathf.Clamp(value, 0, 1);
            QueueRedraw();
        }
    }

    public Color Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            QueueRedraw();
        }
    }

    public Bar() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.08f, 0.1f, 0.17f));
        if (_fraction > 0) DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * _fraction, Size.Y)), _colour);
    }
}

/// <summary>One entry in the base's build queue: name, state and a progress bar while building.</summary>
public partial class QueueRow : PanelContainer
{
    private readonly Label _name;
    private readonly Label _state;
    private readonly Bar _bar;

    public QueueRow()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.1f, 0.19f),
            BorderColor = new Color(0.17f, 0.29f, 0.47f),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(8);
        AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _name = PlanetHud.MakeLabel("", 14, new Color(0.9f, 0.92f, 0.96f));
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _state = PlanetHud.MakeLabel("", 14, new Color(0.62f, 0.82f, 1f));
        top.AddChild(_name);
        top.AddChild(_state);
        box.AddChild(top);
        _bar = new Bar { Colour = new Color(0.31f, 0.71f, 1f), CustomMinimumSize = new Vector2(0, 6) };
        box.AddChild(_bar);
        AddChild(box);
    }

    /// <param name="progress">0..1 while building; negative = waiting in the queue.</param>
    public void Show(string name, float progress)
    {
        _name.Text = name;
        _state.Text = progress < 0 ? "queued" : $"{Mathf.RoundToInt(progress * 100)}%";
        _bar.Visible = progress >= 0;
        _bar.Fraction = Mathf.Max(0, progress);
    }
}
