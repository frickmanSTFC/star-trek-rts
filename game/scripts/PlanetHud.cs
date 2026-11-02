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

/// <summary>One button on the command card.</summary>
public sealed record CommandView(string Id, string Key, string Name, string Cost, bool Affordable, bool Active);

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
    private Control _bottom = null!;
    private Label _factionName = null!;
    private Label _factionSub = null!;
    private readonly Dictionary<int, (Label Amount, Label Rate)> _chips = new();
    private Label _powerChip = null!;
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
        AddChild(Vignette());

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

        _tooltip = Panel(new Color(0.02f, 0.03f, 0.07f, 0.94f), Line, 8);
        _tooltipText = MakeLabel("", 14, Ink);
        _tooltip.AddChild(_tooltipText);
        _tooltip.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tooltip.Visible = false;
        AddChild(_tooltip);
    }

    public void Setup(IReadOnlyList<(string Id, string Name, string Key)> buildMenu, Action<string> onBuild, Action<int> onSelectBase, Action onUpgrade)
    {
        _onBuild = onBuild;
        _onSelectBase = onSelectBase;
        _onUpgrade = onUpgrade;
        foreach (var (id, name, key) in buildMenu)
        {
            var button = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(96, 86), ToggleMode = true };
            StyleButton(button, new Color(0.17f, 0.23f, 0.36f), new Color(0.06f, 0.1f, 0.19f));
            var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 7);
            var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            top.AddChild(Icon(id, 18));
            var keyLabel = MakeLabel(key, 12, Dim);
            keyLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            keyLabel.HorizontalAlignment = HorizontalAlignment.Right;
            top.AddChild(keyLabel);
            box.AddChild(top);
            var nameLabel = MakeLabel(name, 14, Ink);
            nameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            nameLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            box.AddChild(nameLabel);
            var cost = MakeLabel("", 12, Dim);
            cost.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(cost);
            var tag = MakeLabel("", 11, Warn);
            top.AddChild(tag);
            button.AddChild(box);
            var captured = id;
            button.Pressed += () => _onBuild?.Invoke(captured);
            _commandGrid.AddChild(button);
            _commands[id] = (button, tag, cost);
        }
    }

    /// <summary>True when the mouse is over the HUD (so the world shouldn't react to it).</summary>
    public bool BlocksMouse(Vector2 mouse) =>
        _topBar.GetGlobalRect().HasPoint(mouse) || _bottom.GetGlobalRect().HasPoint(mouse);

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

    public void ShowTop(Simulation sim, int player, string factionSub, float powerUse, float powerSupply, string clock)
    {
        var data = sim.Data;
        var p = sim.Players[player];
        _factionName.Text = data.Faction(p.FactionId).Name.ToUpperInvariant();
        _factionSub.Text = factionSub;
        var income = sim.IncomePerMinute(player);
        foreach (var r in data.HudResources)
        {
            if (!_chips.TryGetValue(r.Index, out var chip)) continue;
            chip.Amount.Text = Hud.Whole(p.Stock[r.Index]);
            var rate = income[r.Index];
            chip.Rate.Text = rate == Fix64.Zero ? r.Name.ToUpperInvariant() : $"{r.Name.ToUpperInvariant()} {Hud.PerMinute(rate)}";
            chip.Rate.AddThemeColorOverride("font_color", rate > Fix64.Zero ? Good : rate < Fix64.Zero ? Warn : Dim);
        }
        _powerChip.Text = $"{Mathf.RoundToInt(powerUse)} / {Mathf.RoundToInt(powerSupply)}";
        _powerChip.AddThemeColorOverride("font_color", powerUse > powerSupply ? Warn : Ink);
        _clock.Text = clock;
    }

    /// <summary>Called once the sim exists: one chip per HUD resource.</summary>
    public void SetResources(IReadOnlyList<(int Index, string Id, string Name)> resources)
    {
        var row = _topBar.GetNode<HBoxContainer>("Row");
        var insertAt = row.GetNode("ResourcesStart").GetIndex() + 1;
        foreach (var (index, id, name) in resources)
        {
            var chip = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            chip.AddThemeConstantOverride("separation", 8);
            chip.AddChild(IconTile(id));
            var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            text.AddThemeConstantOverride("separation", -2);
            var amount = MakeLabel("0", 19, Ink);
            var rate = MakeLabel(name.ToUpperInvariant(), 12, Dim);
            text.AddChild(amount);
            text.AddChild(rate);
            chip.AddChild(text);
            row.AddChild(chip);
            row.MoveChild(chip, insertAt++);
            _chips[index] = (amount, rate);
        }
    }

    public void ShowHints(string text) => _hints.Text = text;

    public void ShowBanner(string text)
    {
        _bannerPanel.Visible = text.Length > 0;
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
        _tooltipText.Text = text;
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
    }

    public void ShowBase(BaseView v)
    {
        _baseView.Visible = true;
        _detailView.Visible = false;
        _baseIcon.Texture = Icons.Get(v.CoreId, PlanetDriver.PlayerColors[0]);
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
        _detailView.Visible = true;
        _detailTitle.Text = title.ToUpperInvariant();
        _detailSubtitle.Text = subtitle;
        _detailBody.Text = body;
    }

    public void ShowCommands(string title, IReadOnlyList<CommandView> items, bool enabled)
    {
        _commandTitle.Text = title;
        foreach (var c in items)
        {
            if (!_commands.TryGetValue(c.Id, out var ui)) continue;
            ui.Button.SetPressedNoSignal(c.Active);
            ui.Button.Disabled = !enabled;
            ui.Cost.Text = c.Cost;
            ui.Cost.AddThemeColorOverride("font_color", c.Affordable ? Dim : Warn);
            ui.Tag.Text = c.Affordable ? "" : " SHORT";
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
        row.AddThemeConstantOverride("separation", 22);

        var faction = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        faction.AddThemeConstantOverride("separation", 10);
        faction.AddChild(new ColorRect { Color = PlanetDriver.PlayerColors[0], CustomMinimumSize = new Vector2(8, 28), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        var names = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", -2);
        _factionName = MakeLabel("", 17, Ink);
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
        _powerChip = MakeLabel("0 / 0", 19, Ink);
        powerText.AddChild(_powerChip);
        powerText.AddChild(MakeLabel("POWER USED / SUPPLY", 12, Dim));
        power.AddChild(powerText);
        row.AddChild(power);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
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
        var legend = MakeLabel("Squares: bases · dots: rovers · frame: view\nClick or drag to move the camera", 12, Dim);
        mapBox.AddChild(legend);
        row.AddChild(Framed(mapBox, 286));

        // Centre
        var centre = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.AddThemeConstantOverride("separation", 10);
        _tabs = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _tabs.AddThemeConstantOverride("separation", 6);
        centre.AddChild(_tabs);
        _baseView = BuildBaseView();
        centre.AddChild(_baseView);
        _detailView = BuildDetailView();
        centre.AddChild(_detailView);
        var centreMargin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "right" }) centreMargin.AddThemeConstantOverride($"margin_{side}", 20);
        centreMargin.AddThemeConstantOverride("margin_top", 12);
        centreMargin.AddThemeConstantOverride("margin_bottom", 12);
        centreMargin.AddChild(centre);
        row.AddChild(centreMargin);

        // Command card
        var card = Section(452);
        _commandTitle = Caption("BUILD");
        card.AddChild(_commandTitle);
        _commandGrid = new GridContainer { Columns = 4, MouseFilter = Control.MouseFilterEnum.Pass };
        _commandGrid.AddThemeConstantOverride("h_separation", 8);
        _commandGrid.AddThemeConstantOverride("v_separation", 8);
        card.AddChild(_commandGrid);
        row.AddChild(Framed(card, 452));

        panel.AddChild(row);
        return panel;
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
        tile.CustomMinimumSize = new Vector2(32, 32);
        tile.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        tile.AddChild(Icon(id, 20));
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
