using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>What the test panel needs from the planet driver.</summary>
public interface ITestHost
{
    Simulation Sim { get; }
    PlanetView View { get; }
    int LocalPlayer { get; }
    int InputDelayTicks { get; }

    /// <summary>The selected vehicle (first of a group) and the selected building (or the selected base's core).</summary>
    (SurfaceUnit? Unit, SurfaceStructure? Structure, int Count) Selection();

    /// <summary>The last map tile the mouse was over (-1 = none yet).</summary>
    int CursorTile { get; }

    string UnitName(SurfaceUnit u);
    string BaseName(int baseId);
    void Notify(string text, bool warn = false);

    /// <summary>The next left-click on the map picks a tile for <paramref name="onTile"/> (Esc or right-click cancels).</summary>
    void BeginPick(string prompt, Action<int> onTile);

    /// <summary>The repo's data/ folder (for reloading and saving the JSON).</summary>
    string DataDirectory { get; }
}

/// <summary>
/// The in-game test panel (F9, dev builds only; owner 2026-11-04: "allow for setup changes in the game for testing ...
/// allowing us to increase/decrease all values"; mockup claude.ai/artifact/SCdjcY4gKUYcE1QRqMqp14). Docked on the right
/// of the surface view; tabs SELECTED, RESOURCES, RESEARCH, MAP, DATA. Everything that changes the game goes through the
/// sim's Debug*Command family (validated, in replays and the state hash); dirt, fog-off and game speed are render-side
/// only. Each edit remembers how to undo itself (the inverse commands, built from the values before the edit): the footer
/// shows the last edit, Undo / Ctrl+Z reverts the most recent undoable one.
/// </summary>
public partial class TestPanel : PanelContainer
{
    public const int Width = 420;

    // Mockup colours.
    private static readonly Color Bg = new(0.043f, 0.059f, 0.102f, 0.96f);
    private static readonly Color Border = new(0.169f, 0.227f, 0.361f);
    private static readonly Color Rule = new(0.118f, 0.165f, 0.267f);
    private static readonly Color Ink = new(0.902f, 0.922f, 0.961f);
    private static readonly Color Dim = new(0.541f, 0.588f, 0.678f);
    private static readonly Color Soft = new(0.706f, 0.749f, 0.827f);
    private static readonly Color Accent = PlanetHud.Accent;
    private static readonly Color ButtonBg = new(0.078f, 0.11f, 0.188f);
    private static readonly Color FieldBg = new(0.027f, 0.039f, 0.071f);
    private static readonly Color FooterBg = new(0.039f, 0.055f, 0.094f);
    private static readonly Color Off = new(0.435f, 0.482f, 0.573f);

    private readonly record struct Tint(Color Bg, Color Text, Color Edge);

    private static readonly Tint Plain = new(ButtonBg, Ink, Border);
    private static readonly Tint Green = new(new Color(0.063f, 0.141f, 0.11f), new Color(0.561f, 0.827f, 0.69f), new Color(0.173f, 0.353f, 0.267f));
    private static readonly Tint Blue = new(new Color(0.067f, 0.125f, 0.227f), new Color(0.561f, 0.769f, 1f), new Color(0.173f, 0.29f, 0.455f));
    private static readonly Tint Yellow = new(new Color(0.165f, 0.141f, 0.071f), new Color(0.953f, 0.796f, 0.357f), new Color(0.361f, 0.298f, 0.118f));
    private static readonly Tint Red = new(new Color(0.173f, 0.086f, 0.094f), new Color(0.941f, 0.627f, 0.635f), new Color(0.416f, 0.18f, 0.196f));

    private static readonly string[] TabNames = { "SELECTED", "RESOURCES", "RESEARCH", "MAP", "DATA" };

    private ITestHost _host = null!;
    private Font _caps = null!;
    private Font _capsBold = null!;
    private readonly List<Button> _tabButtons = new();
    private VBoxContainer _content = null!;
    private Label _lastEditLabel = null!;
    private Button _undoButton = null!;
    private int _tab;
    private string _shape = "";
    private readonly List<Action> _refreshers = new();

    // Undo: newest last. Edits that can't be undone only show in the footer.
    private sealed record Edit(string Text, Func<IEnumerable<DebugCommand>>? Undo, Action? UndoLocal);

    private readonly List<Edit> _undo = new();
    private string _lastEdit = "none yet";

    /// <summary>Render-only edits (dirt, fog) or game-speed changes were made: the top bar shows TEST MODE.</summary>
    public bool LocalEdits { get; private set; }

    // Game speed (the driver's tick pacing, not sim state).
    public double Speed { get; private set; } = 1;
    public bool Paused { get; private set; }
    private int _stepsOwed;

    /// <summary>Ticks the driver should step right now, on top of the normal pacing (step one tick, skip a minute, an edit while paused).</summary>
    public int TakeSteps()
    {
        var n = _stepsOwed;
        _stepsOwed = 0;
        return n;
    }

    private Simulation Sim => _host.Sim;
    private GameData Data => _host.Sim.Data;
    private PlanetSurface Surface => _host.Sim.Surface!;
    private int Me => _host.LocalPlayer;

    public void Setup(ITestHost host)
    {
        _host = host;
        string[] names = { "Barlow Condensed", "Antonio", "Bahnschrift", "Arial Narrow", "Roboto Condensed", "Liberation Sans Narrow", "DejaVu Sans Condensed", "Arial" };
        _caps = new FontVariation { BaseFont = new SystemFont { FontNames = names, FontWeight = 600, FontStretch = 75 }, SpacingGlyph = 1 };
        _capsBold = new FontVariation { BaseFont = new SystemFont { FontNames = names, FontWeight = 700, FontStretch = 75 }, SpacingGlyph = 1 };
        Build();
    }

    // ---------------------------------------------------------------- frame

    private void Build()
    {
        Name = "TestPanel";
        MouseFilter = MouseFilterEnum.Stop;
        var style = new StyleBoxFlat { BgColor = Bg, BorderColor = Border };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(6);
        AddThemeStyleboxOverride("panel", style);
        // Docked right, between the top bar and the bottom panel (below the hint line).
        AnchorLeft = 1;
        AnchorRight = 1;
        AnchorTop = 0;
        AnchorBottom = 1;
        OffsetLeft = -Width - 16;
        OffsetRight = -16;
        OffsetTop = PlanetHud.TopHeight + 36;
        OffsetBottom = -PlanetHud.BottomHeight - 16;
        GrowHorizontal = GrowDirection.Begin; // if anything is wider than planned, grow left, not off the screen

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        column.AddThemeConstantOverride("separation", 0);
        AddChild(column);

        // Header.
        var head = Padded(14, 10, 14, 10);
        var headRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        headRow.AddThemeConstantOverride("separation", 10);
        headRow.AddChild(Text("TEST PANEL", 18, Ink, _capsBold));
        var key = Chip("F9", new Color(0, 0, 0, 0), Soft, new Color(0.227f, 0.29f, 0.431f));
        headRow.AddChild(key);
        var dev = Text("dev builds only", 12, Dim);
        dev.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headRow.AddChild(dev);
        var close = MakeButton("×", new Tint(new Color(0, 0, 0, 0), Soft, Border), () => Visible = false, 32, 30);
        close.TooltipText = "Close (F9)";
        headRow.AddChild(close);
        head.AddChild(headRow);
        column.AddChild(head);
        column.AddChild(RuleLine());

        // Tabs.
        var tabs = Padded(10, 8, 10, 0);
        var tabRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        tabRow.AddThemeConstantOverride("separation", 2);
        for (var i = 0; i < TabNames.Length; i++)
        {
            var index = i;
            // Shares the row by name length and clips, so a wide fallback font can't push the panel off screen.
            var b = new Button
            {
                Text = TabNames[i], FocusMode = FocusModeEnum.None, ToggleMode = true, CustomMinimumSize = new Vector2(0, 32), ClipText = true,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = TabNames[i].Length + 3,
            };
            b.AddThemeFontOverride("font", _caps);
            b.AddThemeFontSizeOverride("font_size", 14);
            b.Pressed += () => ShowTab(index);
            _tabButtons.Add(b);
            tabRow.AddChild(b);
        }
        tabs.AddChild(tabRow);
        column.AddChild(tabs);
        column.AddChild(RuleLine());

        // Content (scrolls when the window is short).
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = MouseFilterEnum.Pass,
        };
        var margin = Padded(14, 12, 14, 12);
        margin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        _content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(_content);
        scroll.AddChild(margin);
        column.AddChild(scroll);

        // Footer: the last edit and Undo.
        column.AddChild(RuleLine());
        var foot = new PanelContainer { MouseFilter = MouseFilterEnum.Pass };
        var footStyle = new StyleBoxFlat { BgColor = FooterBg, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 8 };
        footStyle.CornerRadiusBottomLeft = 6;
        footStyle.CornerRadiusBottomRight = 6;
        foot.AddThemeStyleboxOverride("panel", footStyle);
        var footRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        footRow.AddThemeConstantOverride("separation", 10);
        footRow.AddChild(Text("Last edit:", 13, Dim));
        _lastEditLabel = Text("", 13, new Color(0.769f, 0.808f, 0.878f));
        _lastEditLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _lastEditLabel.ClipText = true;
        _lastEditLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        footRow.AddChild(_lastEditLabel);
        _undoButton = MakeButton("Undo · Ctrl+Z", new Tint(new Color(0, 0, 0, 0), new Color(0.561f, 0.769f, 1f), new Color(0.173f, 0.29f, 0.455f)), Undo, 0, 30);
        footRow.AddChild(_undoButton);
        foot.AddChild(footRow);
        column.AddChild(foot);

        ShowTab(0);
    }

    public override void _Process(double delta)
    {
        if (_host == null) return;
        // Deposit markers and fog redraw once the edit has run (also after Ctrl+Z with the panel closed).
        if (_redrawAtTick >= 0 && Sim.Tick > _redrawAtTick)
        {
            _redrawAtTick = -1;
            _host.View.Redraw();
        }
        if (!Visible) return;
        var shape = Shape();
        if (shape != _shape) RebuildTab();
        foreach (var r in _refreshers) r();
        _lastEditLabel.Text = _lastEdit;
        _lastEditLabel.TooltipText = _lastEdit;
        _undoButton.Disabled = _undo.Count == 0;
        _undoButton.TooltipText = _undo.Count == 0 ? "Nothing to undo" : $"Undo: {_undo[^1].Text}";
    }

    /// <summary>True when the mouse is over the panel (the map shouldn't react).</summary>
    public bool BlocksMouse(Vector2 mouse) => Visible && GetGlobalRect().HasPoint(mouse);

    private void ShowTab(int index)
    {
        _tab = index;
        for (var i = 0; i < _tabButtons.Count; i++) StyleTab(_tabButtons[i], i == index);
        RebuildTab();
    }

    /// <summary>What the current tab's layout depends on: when it changes, the tab is rebuilt (values refresh every frame).</summary>
    private string Shape()
    {
        switch (_tab)
        {
            case 0:
                var (unit, s, count) = _host.Selection();
                if (s != null)
                    return $"s{s.Id}|{s.DefId}|{s.State == StructureState.Complete}|{s.UpgradingLevel > 0 || s.UpgradingTo != null}|{s.Production.Count > 0}|{Surface.Players[s.Owner].ActiveTech >= 0}";
                return unit != null ? $"u{unit.Id}|{count}" : "none";
            case 3:
                return $"map|{Sim.Players.Count}";
            case 4:
                return $"data|{Identity(Data)}";
            default:
                return $"tab{_tab}";
        }
    }

    private static int Identity(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);

    private void RebuildTab()
    {
        _shape = Shape();
        _refreshers.Clear();
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
        switch (_tab)
        {
            case 0: BuildSelected(); break;
            case 1: BuildResources(); break;
            case 2: BuildResearch(); break;
            case 3: BuildMap(); break;
            case 4: BuildData(); break;
        }
    }

    // ---------------------------------------------------------------- edits and undo

    private long _redrawAtTick = -1;

    private void Schedule(DebugCommand c)
    {
        Sim.Schedule(Paused ? Sim.Tick : Sim.Tick + _host.InputDelayTicks, c);
        if (Paused) _stepsOwed = Math.Max(_stepsOwed, 1); // paused: step one tick so the edit shows
        _redrawAtTick = Sim.Tick + _host.InputDelayTicks + 1;
    }

    /// <summary>
    /// Checks and issues the commands as one edit. <paramref name="undo"/> builds the inverse commands (null = can't be
    /// undone). With <paramref name="checkFirstOnly"/> only the first command is checked up front (the rest depend on it).
    /// </summary>
    private bool Apply(string text, IReadOnlyList<DebugCommand> commands, Func<IEnumerable<DebugCommand>>? undo, bool checkFirstOnly = false)
    {
        if (commands.Count == 0) return false;
        foreach (var c in checkFirstOnly ? commands.Take(1) : commands)
        {
            var problem = Sim.DebugProblem(c);
            if (problem == null) continue;
            _host.Notify($"Test panel: {problem}", true);
            return false;
        }
        foreach (var c in commands) Schedule(c);
        _lastEdit = undo == null ? $"{text}  (no undo)" : text;
        if (undo != null) _undo.Add(new Edit(text, undo, null));
        return true;
    }

    private bool Apply(string text, DebugCommand command, params DebugCommand[] inverse) =>
        Apply(text, new[] { command }, () => inverse);

    /// <summary>A render-only edit (dirt, fog): done now, undone locally.</summary>
    private void Local(string text, Action undo)
    {
        LocalEdits = true;
        _lastEdit = text;
        _undo.Add(new Edit(text, null, undo));
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            _host.Notify("Test panel: nothing to undo");
            return;
        }
        var e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        e.UndoLocal?.Invoke();
        if (e.Undo != null)
            foreach (var c in e.Undo())
            {
                // The game may have moved on (the vehicle is gone, the building finished): say so rather than fail quietly.
                if (Sim.DebugProblem(c) is { } problem) _host.Notify($"Test panel: can't fully undo {e.Text}: {problem}", true);
                else Schedule(c);
            }
        _lastEdit = $"undid {e.Text}";
        _host.Notify($"Undid: {e.Text}");
    }

    private static Fix64 Fx(double v) => Fix64.FromDouble(v); // the input boundary: panel numbers become sim values here

    private static string N(Fix64 v) => Mathf.RoundToInt(v.ToFloat()).ToString(CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- SELECTED

    private void BuildSelected()
    {
        var (unit, s, count) = _host.Selection();
        if (s != null) BuildStructure(s);
        else if (unit != null) BuildUnit(unit, count);
        else
        {
            _content.AddChild(Text("Nothing selected", 20, Ink, _caps));
            _content.AddChild(Wrapped("Click a vehicle, a building or a base on the map to edit it here. The other tabs work on the whole game.", 13, Dim));
        }
    }

    private static (string Name, Color Colour) IntegrityBand(float v) => v switch
    {
        >= 85 => ("FINE", Green.Text),
        >= 55 => ("SCUFFED", Blue.Text),
        >= 25 => ("DAMAGED", Yellow.Text),
        _ => ("WRECKED", Red.Text),
    };

    private void BuildUnit(SurfaceUnit u, int count)
    {
        var id = u.Id;
        SurfaceUnit? Live() => Surface.FindUnit(id);
        var name = _host.UnitName(u);
        _content.AddChild(Text(name.ToUpperInvariant(), 20, Ink, _caps));
        var sub = Wrapped("", 13, Dim);
        _content.AddChild(sub);
        _refreshers.Add(() =>
        {
            if (Live() is not { } x) return;
            var doing = x.Broken ? "broken down" : x.Mode switch
            {
                UnitMode.Deployed => "mining",
                UnitMode.Deploying => "unfurling",
                UnitMode.Packing => "packing up",
                _ => x.Path.Count > 0 ? "driving" : "parked",
            };
            sub.Text = $"Unit {x.Id} · tile {x.Tile} · {Surface.Map.Terrain(x.Tile).Name.ToLowerInvariant()} · {doing}" + (count > 1 ? $" · 1 of {count} selected" : "");
        });

        // Integrity.
        var head = Row();
        head.AddChild(Caption("INTEGRITY"));
        var value = Text("", 22, Ink);
        value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(value);
        var band = Chip("", Blue.Text, Bg, null);
        head.AddChild(band);
        _content.AddChild(head);
        var slider = MakeSlider(0, 100, v => SetIntegrity(id, v));
        _content.AddChild(slider);
        _refreshers.Add(() =>
        {
            if (Live() is not { } x) return;
            var v = x.Integrity.ToFloat();
            value.Text = $"{Mathf.RoundToInt(v)}%";
            var (bandName, colour) = IntegrityBand(v);
            SetChip(band, bandName, colour);
            if (!slider.HasMeta("dragging")) slider.SetValueNoSignal(v);
        });
        var steps = Grid(6);
        foreach (var d in new[] { -25, -10, -1, 1, 10, 25 })
            steps.AddChild(MakeButton(d < 0 ? $"−{-d}" : $"+{d}", Plain, () =>
            {
                if (Live() is { } x) SetIntegrity(id, Mathf.Clamp(Mathf.RoundToInt(x.Integrity.ToFloat()) + d, 0, 100));
            }, 0, 32));
        _content.AddChild(steps);
        var presets = Grid(4);
        foreach (var (label, v, tint) in new[] { ("Fine · 100", 100, Green), ("Scuffed · 70", 70, Blue), ("Damaged · 40", 40, Yellow), ("Wrecked · 10", 10, Red) })
            presets.AddChild(MakeButton(label, tint, () => SetIntegrity(id, v), 0, 34, 12));
        _content.AddChild(presets);

        // State.
        _content.AddChild(Section("STATE"));
        var broken = MakeCheck("Broken down", on =>
        {
            if (Live() is { } x)
                Apply($"{CallSign(x)} {(on ? "broken down" : "fixed")}", new DebugUnitCommand(Me, id, DebugUnitField.Broken, on ? Fix64.One : Fix64.Zero),
                    new DebugUnitCommand(Me, id, DebugUnitField.Broken, x.Broken ? Fix64.One : Fix64.Zero));
        });
        _content.AddChild(broken);
        _refreshers.Add(() =>
        {
            if (Live() is { } x) broken.SetPressedNoSignal(x.Broken);
        });
        var tyre = MakeCheck("Flat tyre, front left", _ => { });
        tyre.Disabled = true;
        tyre.TooltipText = "Comes with rover damage (not built yet)";
        var tyreRow = Row();
        tyreRow.AddChild(tyre);
        tyreRow.AddChild(Text("(after rover damage)", 12, Off));
        _content.AddChild(tyreRow);

        // Look (render only).
        _content.AddChild(Section("LOOK  ·  render only"));
        var look = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Pass };
        look.AddThemeConstantOverride("h_separation", 8);
        look.AddThemeConstantOverride("v_separation", 6);
        foreach (var kind in new[] { "Dust", "Mud", "Snow" })
        {
            var k = kind;
            var label = Text(k, 14, Ink);
            label.CustomMinimumSize = new Vector2(52, 0);
            look.AddChild(label);
            var amount = Text("0", 14, Soft);
            var grime = MakeSlider(0, 100, v => SetGrime(id, k, v / 100f));
            grime.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            look.AddChild(grime);
            amount.CustomMinimumSize = new Vector2(36, 0);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            look.AddChild(amount);
            _refreshers.Add(() =>
            {
                if (_host.View.GrimeOf(id) is not { } g) return;
                var v = k switch { "Dust" => g.Dust, "Mud" => g.Mud, _ => g.Snow } * 100f;
                amount.Text = Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
                if (!grime.HasMeta("dragging")) grime.SetValueNoSignal(v);
            });
        }
        _content.AddChild(look);

        // Actions.
        _content.AddChild(RuleLine());
        var actions = Row(6);
        actions.AddChild(Grow(MakeButton("Teleport to tile…", Plain, () =>
        {
            if (Live() is not { } x) return;
            var from = x.Tile;
            _host.BeginPick($"Test panel: click a tile to put {CallSign(x)} there", tile =>
                Apply($"{CallSign(x)} teleported {from} → {tile}", new DebugMoveUnitCommand(Me, id, tile), new DebugMoveUnitCommand(Me, id, from)));
        }, 0, 36)));
        actions.AddChild(Grow(MakeButton("Clone unit", Plain, () =>
        {
            if (Live() is { } x) Spawn(x.Owner, x.DefId, x.Tile, $"cloned {CallSign(x)}");
        }, 0, 36)));
        actions.AddChild(Grow(MakeButton("Remove", Red, () =>
        {
            if (Live() is { } x) Apply($"removed {CallSign(x)}", new[] { new DebugRemoveUnitCommand(Me, id) }, null);
        }, 0, 36)));
        _content.AddChild(actions);
    }

    private static string CallSign(SurfaceUnit u)
    {
        var name = u.Name.Length == 0 ? u.DefId : char.ToUpperInvariant(u.Name[0]) + u.Name[1..].ToLowerInvariant();
        return u.Number.Length == 0 ? name : $"{name} {u.Number}";
    }

    private void SetIntegrity(int id, float v)
    {
        if (Surface.FindUnit(id) is not { } u) return;
        var was = u.Integrity;
        var to = Fx(Mathf.Clamp(Mathf.Round(v), 0, 100));
        if (to == was) return;
        Apply($"{CallSign(u)} integrity {N(was)} → {N(to)}", new DebugUnitCommand(Me, id, DebugUnitField.Integrity, to),
            new DebugUnitCommand(Me, id, DebugUnitField.Integrity, was));
    }

    private void SetGrime(int id, string kind, float v)
    {
        if (_host.View.GrimeOf(id) is not { } g || Surface.FindUnit(id) is not { } u) return;
        var (dust, mud, snow) = (g.Dust, g.Mud, g.Snow);
        g.Set(kind == "Dust" ? v : dust, kind == "Mud" ? v : mud, kind == "Snow" ? v : snow);
        var before = kind switch { "Dust" => dust, "Mud" => mud, _ => snow };
        Local($"{CallSign(u)} {kind.ToLowerInvariant()} {Mathf.RoundToInt(before * 100)} → {Mathf.RoundToInt(v * 100)}", () =>
        {
            if (_host.View.GrimeOf(id) is { } now) now.Set(dust, mud, snow);
        });
    }

    /// <summary>A new vehicle; undo takes away the newest one of that kind and owner made since.</summary>
    private void Spawn(int owner, string unitId, int tile, string text, bool newPlayer = false, string faction = "")
    {
        var after = Surface.Units.Count == 0 ? 0 : Surface.Units.Max(x => x.Id);
        var commands = new List<DebugCommand>();
        if (newPlayer) commands.Add(new DebugAddPlayerCommand(Me, faction));
        commands.Add(new DebugSpawnUnitCommand(Me, owner, unitId, tile));
        Apply(text, commands, () =>
        {
            var made = Surface.Units.Where(x => x.Id > after && x.Owner == owner && x.DefId == unitId).OrderBy(x => x.Id).LastOrDefault();
            return made == null ? Array.Empty<DebugCommand>() : new DebugCommand[] { new DebugRemoveUnitCommand(Me, made.Id) };
        }, checkFirstOnly: newPlayer);
    }

    private void BuildStructure(SurfaceStructure s)
    {
        var id = s.Id;
        SurfaceStructure? Live() => Surface.FindStructure(id);
        var def = Data.Building(s.DefId);
        var name = def.Name;
        var title = Text("", 20, Ink, _caps);
        _content.AddChild(title);
        var sub = Wrapped("", 13, Dim);
        _content.AddChild(sub);
        _refreshers.Add(() =>
        {
            if (Live() is not { } x) return;
            var d = Data.Building(x.DefId);
            title.Text = (d.Levels.Count > 0 ? $"{d.Name} · {Surface.LevelName(x)}" : d.Name).ToUpperInvariant();
            var state = x.State != StructureState.Complete ? "under construction" : x.Broken ? "broken down" : x.Status switch
            {
                StructureStatus.Working => "running",
                StructureStatus.LowPower => "low power",
                StructureStatus.NoInput => "idle",
                StructureStatus.NoUpkeep => "no upkeep",
                StructureStatus.StorageFull => "storage full",
                StructureStatus.Depleted => "deposit used up",
                _ => x.Status.ToString().ToLowerInvariant(),
            };
            var power = d.PowerUse > Fix64.Zero ? $" · {N(d.PowerUse)} power" : d.PowerSupply > Fix64.Zero ? $" · +{N(d.PowerSupply)} power" : "";
            var owner = x.Owner == Me ? "" : $" · player {x.Owner + 1}";
            sub.Text = $"{_host.BaseName(x.BaseId)} · tile {x.Tile} · {state}{power}{owner}";
        });

        if (def.Wears && s.State == StructureState.Complete) BuildCondition(id);
        else if (def.Wears)
        {
            _content.AddChild(Section("STATE"));
            _content.AddChild(StopWearCheck(id));
        }

        if (def.Levels.Count > 0 && s.State == StructureState.Complete)
        {
            _content.AddChild(Section("LEVEL"));
            var levels = Segmented(Enumerable.Range(1, def.MaxLevel).Select(l => l == 1 ? "Mk I" : def.Levels[l - 2].Name).ToArray(),
                () => Live()?.Level - 1 ?? 0,
                i =>
                {
                    if (Live() is not { } x || x.Level == i + 1) return;
                    Apply($"{name} level {x.Level} → {i + 1}", new DebugStructureCommand(Me, id, DebugStructureField.Level, Fix64.FromInt(i + 1)),
                        new DebugStructureCommand(Me, id, DebugStructureField.Level, Fix64.FromInt(x.Level)));
                });
            _content.AddChild(levels);
        }

        BuildWork(s);

        // Whole base.
        var baseId = s.BaseId;
        _content.AddChild(Section($"WHOLE BASE · {_host.BaseName(baseId).ToUpperInvariant()}"));
        var all = Grid(2);
        all.AddChild(MakeButton("Finish all construction", Plain, () =>
            Apply($"finished construction at {_host.BaseName(baseId)}", new[] { new DebugBaseCommand(Me, baseId, DebugBaseAction.FinishConstruction, Fix64.Zero) }, null), 0, 36));
        all.AddChild(MakeButton("Repair all buildings", Plain, () => BaseCondition(baseId, DebugBaseAction.RepairAll, Fix64.FromInt(100), "repaired every building"), 0, 36));
        all.AddChild(MakeButton("Wear all to 30%", Plain, () => BaseCondition(baseId, DebugBaseAction.SetCondition, Fix64.FromInt(30), "wore every building to 30%"), 0, 36));
        all.AddChild(MakeButton("Finish bay orders", Plain, () =>
            Apply($"finished Vehicle Bay orders at {_host.BaseName(baseId)}", new[] { new DebugBaseCommand(Me, baseId, DebugBaseAction.FinishOrders, Fix64.Zero) }, null), 0, 36));
        _content.AddChild(all);

        _content.AddChild(RuleLine());
        _content.AddChild(MakeButton("Remove (no refund)", Red, () =>
        {
            if (Live() is { } x) Apply($"removed {Data.Building(x.DefId).Name}", new[] { new DebugRemoveStructureCommand(Me, id) }, null);
        }, 0, 36));
    }

    private CheckBox StopWearCheck(int id)
    {
        var check = MakeCheck("Stop wear on this building", on =>
        {
            if (Surface.FindStructure(id) is { } x)
                Apply($"{Data.Building(x.DefId).Name} wear {(on ? "stopped" : "back on")}", new DebugStructureCommand(Me, id, DebugStructureField.NoWear, on ? Fix64.One : Fix64.Zero),
                    new DebugStructureCommand(Me, id, DebugStructureField.NoWear, x.NoWear ? Fix64.One : Fix64.Zero));
        });
        _refreshers.Add(() =>
        {
            if (Surface.FindStructure(id) is { } x) check.SetPressedNoSignal(x.NoWear);
        });
        return check;
    }

    private void BuildCondition(int id)
    {
        SurfaceStructure? Live() => Surface.FindStructure(id);
        var rules = Data.Maintenance;
        var head = Row();
        head.AddChild(Caption("CONDITION"));
        var value = Text("", 22, Ink);
        value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(value);
        var chip = Chip("", Green.Text, Bg, null);
        head.AddChild(chip);
        _content.AddChild(head);
        var slider = MakeSlider(0, 100, v => SetCondition(id, Fx(Mathf.Round(v)), null));
        _content.AddChild(slider);
        _refreshers.Add(() =>
        {
            if (Live() is not { } x) return;
            var v = x.Condition.ToFloat();
            value.Text = $"{Mathf.FloorToInt(v)}%";
            var (text, colour) = x.Broken ? ("BROKEN", Red.Text)
                : rules != null && x.Condition < rules.DeclineBelow ? ("WORN · SLOWED", Yellow.Text)
                : rules != null && x.Condition < rules.AutoBelow ? ("WORN", Blue.Text)
                : ("GOOD", Green.Text);
            SetChip(chip, text, colour);
            if (!slider.HasMeta("dragging")) slider.SetValueNoSignal(v);
        });
        var auto = rules != null ? Mathf.Max(0, Mathf.CeilToInt(rules.AutoBelow.ToFloat()) - 1) : 69;
        var presets = Grid(4);
        presets.AddChild(MakeButton("New · 100", Green, () => SetCondition(id, Fix64.FromInt(100), false), 0, 34, 12));
        presets.AddChild(MakeButton($"Auto-fix · {auto}", Blue, () => SetCondition(id, Fix64.FromInt(auto), null), 0, 34, 12));
        presets.AddChild(MakeButton("Worn · 30", Yellow, () => SetCondition(id, Fix64.FromInt(30), null), 0, 34, 12));
        presets.AddChild(MakeButton("Broken · 0", Red, () => SetCondition(id, Fix64.Zero, true), 0, 34, 12));
        _content.AddChild(presets);
        var broken = MakeCheck("Broken down", on =>
        {
            if (Live() is { } x)
                Apply($"{Data.Building(x.DefId).Name} {(on ? "broken down" : "fixed")}", new DebugStructureCommand(Me, id, DebugStructureField.Broken, on ? Fix64.One : Fix64.Zero),
                    new DebugStructureCommand(Me, id, DebugStructureField.Broken, x.Broken ? Fix64.One : Fix64.Zero));
        });
        _refreshers.Add(() =>
        {
            if (Live() is { } x) broken.SetPressedNoSignal(x.Broken);
        });
        _content.AddChild(broken);
        _content.AddChild(StopWearCheck(id));
    }

    /// <summary>Sets condition (and broken, when given) as one edit; undo restores both.</summary>
    private void SetCondition(int id, Fix64 to, bool? broken)
    {
        if (Surface.FindStructure(id) is not { } s) return;
        var commands = new List<DebugCommand> { new DebugStructureCommand(Me, id, DebugStructureField.Condition, to) };
        if (broken is { } b && b != s.Broken) commands.Add(new DebugStructureCommand(Me, id, DebugStructureField.Broken, b ? Fix64.One : Fix64.Zero));
        var inverse = new List<DebugCommand> { new DebugStructureCommand(Me, id, DebugStructureField.Condition, s.Condition) };
        if (broken is { } b2 && b2 != s.Broken) inverse.Add(new DebugStructureCommand(Me, id, DebugStructureField.Broken, s.Broken ? Fix64.One : Fix64.Zero));
        Apply($"{Data.Building(s.DefId).Name} condition {N(s.Condition)} → {N(to)}", commands, () => inverse);
    }

    /// <summary>Repair-all / wear-all: undo puts every building's condition and breakdown back.</summary>
    private void BaseCondition(int baseId, DebugBaseAction action, Fix64 value, string text)
    {
        var inverse = new List<DebugCommand>();
        foreach (var x in Surface.StructuresOf(baseId).Where(x => x.State == StructureState.Complete && Data.Building(x.DefId).Wears))
        {
            inverse.Add(new DebugStructureCommand(Me, x.Id, DebugStructureField.Condition, x.Condition));
            if (x.Broken) inverse.Add(new DebugStructureCommand(Me, x.Id, DebugStructureField.Broken, Fix64.One));
        }
        Apply($"{text} at {_host.BaseName(baseId)}", new[] { new DebugBaseCommand(Me, baseId, action, value) }, () => inverse);
    }

    /// <summary>Work in progress: construction, upgrade, bay order or research, with "finish now"; converters' input and work speed.</summary>
    private void BuildWork(SurfaceStructure s)
    {
        var id = s.Id;
        SurfaceStructure? Live() => Surface.FindStructure(id);
        var def = Data.Building(s.DefId);
        _content.AddChild(Section("WORK IN PROGRESS"));

        void Progress(string what, Func<SurfaceStructure, float> fraction, string finishLabel, Action<SurfaceStructure> finish, Action<SurfaceStructure, float>? set)
        {
            var row = Row();
            var label = Text(what, 14, Ink);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.ClipText = true;
            row.AddChild(label);
            var pct = Text("", 14, Soft);
            row.AddChild(pct);
            _content.AddChild(row);
            var bar = new Bar { Colour = new Color(0.561f, 0.769f, 1f), CustomMinimumSize = new Vector2(0, 8) };
            _content.AddChild(bar);
            _refreshers.Add(() =>
            {
                if (Live() is not { } x) return;
                var f = fraction(x);
                bar.Fraction = f;
                pct.Text = $"{Mathf.RoundToInt(f * 100)}%";
            });
            var buttons = Row(6);
            if (set != null)
                foreach (var share in new[] { 0f, 0.5f, 0.9f })
                    buttons.AddChild(MakeButton($"{Mathf.RoundToInt(share * 100)}%", Plain, () =>
                    {
                        if (Live() is { } x) set(x, share);
                    }, 52, 36));
            buttons.AddChild(Grow(MakeButton(finishLabel, Plain, () =>
            {
                if (Live() is { } x) finish(x);
            }, 0, 36)));
            _content.AddChild(buttons);
        }

        var any = false;
        if (s.State != StructureState.Complete)
        {
            any = true;
            Progress("Construction", x => Surface.BuildFraction(x).ToFloat(), "Finish now",
                x => Apply($"finished building {def.Name}", new[] { new DebugStructureCommand(Me, id, DebugStructureField.BuildProgress, Fix64.One) }, null),
                (x, f) => Apply($"{def.Name} construction {Mathf.RoundToInt(Surface.BuildFraction(x).ToFloat() * 100)}% → {Mathf.RoundToInt(f * 100)}%",
                    new DebugStructureCommand(Me, id, DebugStructureField.BuildProgress, Fx(f)),
                    new DebugStructureCommand(Me, id, DebugStructureField.BuildProgress, Surface.BuildFraction(x))));
        }
        else if (s.UpgradingLevel > 0 || s.UpgradingTo != null)
        {
            any = true;
            var to = s.UpgradingTo != null ? Data.Building(s.UpgradingTo).Name : def.Levels[s.UpgradingLevel - 2].Name;
            Progress($"Upgrade to {to}", x => Surface.UpgradeFraction(x).ToFloat(), "Finish upgrade now",
                x => Apply($"finished {def.Name} upgrade", new[] { new DebugStructureCommand(Me, id, DebugStructureField.UpgradeProgress, Fix64.One) }, null),
                (x, f) => Apply($"{def.Name} upgrade {Mathf.RoundToInt(Surface.UpgradeFraction(x).ToFloat() * 100)}% → {Mathf.RoundToInt(f * 100)}%",
                    new DebugStructureCommand(Me, id, DebugStructureField.UpgradeProgress, Fx(f)),
                    new DebugStructureCommand(Me, id, DebugStructureField.UpgradeProgress, Surface.UpgradeFraction(x))));
        }
        if (s.State == StructureState.Complete && s.Production.Count > 0)
        {
            any = true;
            var unitName = Data.GroundVehicleNames.GetValueOrDefault(s.Production[0], s.Production[0]);
            Progress($"Assembling {unitName}", x => Surface.ProductionFraction(x).ToFloat(), "Roll it out now",
                x => Apply($"rolled out a {unitName}", new[] { new DebugStructureCommand(Me, id, DebugStructureField.ProductionProgress, Fix64.One) }, null),
                (x, f) => Apply($"{unitName} assembly {Mathf.RoundToInt(Surface.ProductionFraction(x).ToFloat() * 100)}% → {Mathf.RoundToInt(f * 100)}%",
                    new DebugStructureCommand(Me, id, DebugStructureField.ProductionProgress, Fx(f)),
                    new DebugStructureCommand(Me, id, DebugStructureField.ProductionProgress, Surface.ProductionFraction(x))));
        }
        var player = Surface.Players[s.Owner];
        if (s.State == StructureState.Complete && def.IsLab && player.ActiveTech >= 0)
        {
            any = true;
            var tech = Data.Techs[player.ActiveTech];
            var owner = s.Owner;
            Progress($"Research: {tech.Name}", _ => (Surface.Players[owner].ResearchProgress[tech.Index] / tech.Cost).ToFloat(), "Finish research now",
                _ => Apply($"researched {tech.Name}", new DebugTechCommand(Me, owner, tech.Id, true, tech.Cost),
                    new DebugTechCommand(Me, owner, tech.Id, false, Surface.Players[owner].ResearchProgress[tech.Index])),
                (_, f) =>
                {
                    var was = Surface.Players[owner].ResearchProgress[tech.Index];
                    Apply($"{tech.Name} research {Mathf.RoundToInt((was / tech.Cost).ToFloat() * 100)}% → {Mathf.RoundToInt(f * 100)}%",
                        new DebugTechCommand(Me, owner, tech.Id, false, tech.Cost * Fx(f)), new DebugTechCommand(Me, owner, tech.Id, false, was));
                });
        }
        if (def.IsConverter && s.State == StructureState.Complete)
        {
            any = true;
            foreach (var r in def.Recipes)
                _content.AddChild(Text($"Recipe: {Amounts(r.In)} → {Amounts(r.Out)}  (runs continuously)", 14, Ink));
            var inputs = def.Recipes.SelectMany(r => r.In).Select(a => a.Resource).Distinct().ToList();
            var label = string.Join(", ", inputs.Select(i => Data.Resources[i].Name.ToLowerInvariant()));
            _content.AddChild(MakeButton($"Fill input ({label})", Plain, () => FillInput(s.Owner, inputs, label), 0, 36));
        }
        if (!any) _content.AddChild(Text(s.State == StructureState.Complete ? "Nothing in progress." : "", 13, Dim));

        if (s.State == StructureState.Complete)
        {
            var speedRow = Row();
            var speedLabel = Text("Work speed", 14, Ink);
            speedLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            speedRow.AddChild(speedLabel);
            _content.AddChild(speedRow);
            int[] speeds = { 1, 2, 5, 10 };
            _content.AddChild(Segmented(speeds.Select(v => v == 1 ? "×1 (normal)" : $"×{v}").ToArray(),
                () => Array.IndexOf(speeds, Live() is { } x ? Mathf.RoundToInt(x.WorkSpeed.ToFloat()) : 1),
                i =>
                {
                    if (Live() is not { } x) return;
                    Apply($"{Data.Building(x.DefId).Name} work speed ×{N(x.WorkSpeed)} → ×{speeds[i]}",
                        new DebugStructureCommand(Me, id, DebugStructureField.WorkSpeed, Fix64.FromInt(speeds[i])),
                        new DebugStructureCommand(Me, id, DebugStructureField.WorkSpeed, x.WorkSpeed));
                }));
        }
    }

    private string Amounts(IReadOnlyList<ResourceAmount> list) =>
        string.Join(" + ", list.Select(a => $"{N(a.Amount)} {Data.Resources[a.Resource].Name.ToLowerInvariant()}"));

    /// <summary>Adds the converter's inputs, sharing out the free storage (1000 each with no storage cap).</summary>
    private void FillInput(int owner, IReadOnlyList<int> inputs, string label)
    {
        var stock = Sim.Players[owner].Stock;
        var free = Surface.FreeStorage(Sim, owner);
        var each = Surface.Players[owner].NoStorageCap ? Fix64.FromInt(1000) : free / Math.Max(1, inputs.Count);
        if (each <= Fix64.Zero)
        {
            _host.Notify("Test panel: storage is full (turn on No storage cap on the RESOURCES tab)", true);
            return;
        }
        var values = inputs.Select(r => new ResourceAmount(r, Fix64.Min(Simulation.DebugMaxAmount, stock[r] + each))).ToList();
        var before = inputs.Select(r => new ResourceAmount(r, stock[r])).ToList();
        Apply($"filled input ({label}) +{N(each)} each", new DebugStockCommand(Me, owner, values), new DebugStockCommand(Me, owner, before));
    }

    // ---------------------------------------------------------------- RESOURCES

    private int _step = 100;

    /// <summary>Resources worth editing on the surface: the top bar's, crew and latinum, and anything a surface building, vehicle or the landing uses.</summary>
    private List<ResourceDef> SurfaceResources()
    {
        var used = new HashSet<int>();
        foreach (var r in Data.HudResources) used.Add(r.Index);
        foreach (var r in Data.StorageExempt) used.Add(r.Index);
        foreach (var a in Data.Landing.StartStock) used.Add(a.Resource);
        foreach (var id in Data.BuildMenu)
        {
            var b = Data.Building(id);
            foreach (var a in b.Cost.Concat(b.OutputPerMinute).Concat(b.UpkeepPerMinute).Concat(b.Recipes.SelectMany(r => r.In.Concat(r.Out))).Concat(b.Levels.SelectMany(l => l.Cost)))
                used.Add(a.Resource);
        }
        foreach (var u in Data.SurfaceUnits)
            foreach (var a in u.Cost) used.Add(a.Resource);
        return Data.Resources.Where(r => used.Contains(r.Index) && r.Id != Data.PowerResource).ToList();
    }

    private void BuildResources()
    {
        var faction = Data.Faction(Sim.Players[Me].FactionId).Name;
        var head = Row();
        var caption = Caption($"STOCKPILE · {(faction.Length > 16 ? Sim.Players[Me].FactionId : faction).ToUpperInvariant()}");
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(caption);
        int[] steps = { 1, 10, 100, 1000 };
        var stepper = Segmented(steps.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray(), () => Array.IndexOf(steps, _step), i => _step = steps[i], 40, 28);
        head.AddChild(stepper);
        _content.AddChild(head);

        var resources = SurfaceResources();
        var grid = new GridContainer { Columns = 4, MouseFilter = MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var r in resources)
        {
            var index = r.Index;
            var name = Text(r.Name, 14, Ink);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            grid.AddChild(name);
            grid.AddChild(MakeButton("−", Plain, () => SetStock(index, Sim.Players[Me].Stock[index] - Fix64.FromInt(_step)), 36, 30));
            var field = MakeField(text =>
            {
                if (ParseNumber(text) is { } v) SetStock(index, Fx(Math.Clamp(v, 0, 1e9)));
                else _host.Notify($"Test panel: '{text}' isn't a number", true);
            });
            field.CustomMinimumSize = new Vector2(84, 30);
            grid.AddChild(field);
            grid.AddChild(MakeButton("+", Plain, () => SetStock(index, Sim.Players[Me].Stock[index] + Fix64.FromInt(_step)), 36, 30));
            _refreshers.Add(() =>
            {
                if (!field.HasFocus()) field.Text = Hud.Whole(Sim.Players[Me].Stock[index]);
            });
        }
        _content.AddChild(grid);

        var bulk = Grid(3);
        bulk.AddChild(MakeButton("Fill storage", Plain, FillStorage, 0, 36));
        bulk.AddChild(MakeButton("Everything ×10", Plain, () => SetStocks("everything ×10", resources.Select(r =>
        {
            var v = Sim.Players[Me].Stock[r.Index];
            return (r.Index, v > Simulation.DebugMaxAmount / 10 ? Simulation.DebugMaxAmount : v * 10); // ×10 of a huge stock would overflow
        }).ToList()), 0, 36));
        bulk.AddChild(MakeButton("Zero all", Red, () => SetStocks("zeroed every resource", resources.Select(r => (r.Index, Fix64.Zero)).ToList()), 0, 36));
        _content.AddChild(bulk);

        _content.AddChild(Section("LIMITS"));
        foreach (var (flag, label) in new[]
                 {
                     (DebugFlag.NoStorageCap, "No storage cap"), (DebugFlag.UnlimitedPower, "Unlimited power"),
                     (DebugFlag.FreeBuilding, "Free building (no cost)"), (DebugFlag.InstantBuild, "Instant construction"),
                 })
        {
            var f = flag;
            var check = MakeCheck(label, on =>
                Apply($"{label.ToLowerInvariant()} {(on ? "on" : "off")}", new DebugFlagCommand(Me, Me, f, on), new DebugFlagCommand(Me, Me, f, !on)));
            _refreshers.Add(() => check.SetPressedNoSignal(Flag(f)));
            _content.AddChild(check);
        }

        _content.AddChild(Section("GAME SPEED  ·  this computer only"));
        double[] speeds = { 0.25, 0.5, 1, 2, 4, 8 };
        _content.AddChild(Segmented(new[] { "¼×", "½×", "1×", "2×", "4×", "8×" }, () => Array.IndexOf(speeds, Speed), i =>
        {
            Speed = speeds[i];
            if (Speed != 1) LocalEdits = true;
        }));
        var pacing = Row(6);
        var pause = MakeButton("Pause · Space", Plain, TogglePause, 0, 36);
        _refreshers.Add(() => pause.Text = Paused ? "Resume · Space" : "Pause · Space");
        pacing.AddChild(Grow(pause));
        pacing.AddChild(Grow(MakeButton("Step one tick", Plain, () =>
        {
            Paused = true;
            LocalEdits = true;
            _stepsOwed += 1;
        }, 0, 36)));
        pacing.AddChild(Grow(MakeButton("Skip 1 minute", Plain, () =>
        {
            LocalEdits = true;
            _stepsOwed += Simulation.TicksPerMinute;
            _lastEdit = "skipped 1 minute  (no undo)";
        }, 0, 36)));
        _content.AddChild(pacing);
    }

    public void TogglePause()
    {
        Paused = !Paused;
        LocalEdits = true;
        _host.Notify(Paused ? "Paused (test panel)" : "Running");
    }

    private bool Flag(DebugFlag f)
    {
        var p = Surface.Players[Me];
        return f switch
        {
            DebugFlag.NoStorageCap => p.NoStorageCap,
            DebugFlag.UnlimitedPower => p.UnlimitedPower,
            DebugFlag.FreeBuilding => p.FreeBuilding,
            _ => p.InstantBuild,
        };
    }

    private static double? ParseNumber(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v) && !double.IsInfinity(v) ? v : null;

    private void SetStock(int resource, Fix64 to) =>
        SetStocks($"{Data.Resources[resource].Name} {Hud.Whole(Sim.Players[Me].Stock[resource])} → {Hud.Whole(Fix64.Clamp(to, Fix64.Zero, Simulation.DebugMaxAmount))}", new List<(int, Fix64)> { (resource, to) });

    private void SetStocks(string text, IReadOnlyList<(int Resource, Fix64 Amount)> values)
    {
        var stock = Sim.Players[Me].Stock;
        var to = values.Select(v => new ResourceAmount(v.Resource, Fix64.Clamp(v.Amount, Fix64.Zero, Simulation.DebugMaxAmount))).ToList();
        var before = values.Select(v => new ResourceAmount(v.Resource, stock[v.Resource])).ToList();
        Apply(text, new DebugStockCommand(Me, Me, to), new DebugStockCommand(Me, Me, before));
    }

    /// <summary>Shares the free storage out between the top bar's stored resources.</summary>
    private void FillStorage()
    {
        if (Surface.Players[Me].NoStorageCap)
        {
            _host.Notify("Test panel: storage has no cap right now, so there's nothing to fill", true);
            return;
        }
        var free = Surface.FreeStorage(Sim, Me);
        var stored = Data.HudResources.Where(r => Data.IsStored(r.Index)).ToList();
        if (free <= Fix64.Zero || stored.Count == 0)
        {
            _host.Notify("Test panel: storage is already full");
            return;
        }
        var each = free / stored.Count;
        SetStocks($"filled storage (+{N(free)})", stored.Select(r => (r.Index, Sim.Players[Me].Stock[r.Index] + each)).ToList());
    }

    // ---------------------------------------------------------------- RESEARCH

    private void BuildResearch()
    {
        var bulk = Grid(2);
        bulk.AddChild(MakeButton("Complete all", Plain, () => SetTechs("completed all research", Data.Techs, true), 0, 36));
        bulk.AddChild(MakeButton("Forget all", Red, () => SetTechs("forgot all research", Data.Techs, false), 0, 36));
        _content.AddChild(bulk);
        var grid = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var t in Data.Techs)
        {
            var tech = t;
            var name = Text(t.Name, 14, Ink);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.TooltipText = t.Effect;
            name.MouseFilter = MouseFilterEnum.Pass;
            grid.AddChild(name);
            var state = Text("", 14, Soft);
            state.CustomMinimumSize = new Vector2(70, 0);
            state.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(state);
            var button = MakeButton("", Plain, () =>
            {
                var p = Surface.Players[Me];
                SetTechs(p.Known[tech.Index] ? $"forgot {tech.Name}" : $"researched {tech.Name}", new[] { tech }, !p.Known[tech.Index]);
            }, 92, 30);
            grid.AddChild(button);
            _refreshers.Add(() =>
            {
                var p = Surface.Players[Me];
                var known = p.Known[tech.Index];
                var share = (p.ResearchProgress[tech.Index] / tech.Cost).ToFloat();
                state.Text = known ? "Known" : share > 0 ? $"{Mathf.RoundToInt(share * 100)}%" : "—";
                state.AddThemeColorOverride("font_color", known ? Green.Text : p.ActiveTech == tech.Index ? Accent : Soft);
                button.Text = known ? "Forget" : "Complete";
            });
        }
        _content.AddChild(grid);
        _content.AddChild(Wrapped("Complete marks a tech researched (its bonuses apply at once); Forget takes it away and clears its progress.", 12, Dim));
    }

    private void SetTechs(string text, IEnumerable<TechDef> techs, bool known)
    {
        var p = Surface.Players[Me];
        var list = techs.ToList();
        var commands = list.Select(t => (DebugCommand)new DebugTechCommand(Me, Me, t.Id, known, known ? t.Cost : Fix64.Zero)).ToList();
        var inverse = list.Select(t => (DebugCommand)new DebugTechCommand(Me, Me, t.Id, p.Known[t.Index], p.ResearchProgress[t.Index])).ToList();
        Apply(text, commands, () => inverse);
    }

    // ---------------------------------------------------------------- MAP

    private int _placeUnit;
    private int _placeOwner;
    private int _placeBuilding;

    private void BuildMap()
    {
        _content.AddChild(Section("MAP"));
        var fog = MakeCheck("Fog off (see the whole planet)  ·  render only", on =>
        {
            _host.View.FogOff = on;
            Local($"fog {(on ? "off" : "on")}", () => _host.View.FogOff = !on);
        });
        _refreshers.Add(() => fog.SetPressedNoSignal(_host.View.FogOff));
        _content.AddChild(fog);
        _content.AddChild(MakeButton("Survey every deposit", Plain, () =>
            Apply("surveyed the whole planet", new[] { new DebugSurveyAllCommand(Me, Me) }, null), 0, 36));

        // Deposit under the cursor.
        _content.AddChild(Section("DEPOSIT UNDER THE CURSOR"));
        var where = Wrapped("", 14, Ink);
        _content.AddChild(where);
        var amountRow = Row(6);
        var amount = MakeField(text =>
        {
            var tile = _host.CursorTile;
            if (ParseNumber(text) is not { } v) _host.Notify($"Test panel: '{text}' isn't a number", true);
            else if (tile >= 0)
            {
                var was = Surface.Map.DepositAmount[tile];
                Apply($"deposit at tile {tile} {Hud.Whole(was)} → {Mathf.RoundToInt((float)v)}", new DebugDepositCommand(Me, tile, Fx(Math.Clamp(v, 0, 1e9))),
                    new DebugDepositCommand(Me, tile, was));
            }
        });
        amount.PlaceholderText = "amount";
        amount.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        amountRow.AddChild(amount);
        amountRow.AddChild(MakeButton("Set", Plain, () => amount.EmitSignal(LineEdit.SignalName.TextSubmitted, amount.Text), 60, 30));
        _content.AddChild(amountRow);
        var lastTile = -2;
        _refreshers.Add(() =>
        {
            var tile = _host.CursorTile;
            var map = Surface.Map;
            if (tile < 0)
            {
                where.Text = "Hover a tile on the map first.";
                return;
            }
            var r = map.DepositResource[tile];
            where.Text = r < 0 ? $"Tile {tile}: no deposit" : $"Tile {tile}: {Data.Resources[r].Name} {Hud.Whole(map.DepositAmount[tile])}" + (Surface.Players[Me].Surveyed[tile] ? "" : " (not surveyed)");
            if (tile != lastTile && !amount.HasFocus())
            {
                amount.Text = r < 0 ? "" : Hud.Whole(map.DepositAmount[tile]);
                lastTile = tile;
            }
            amount.Editable = r >= 0;
        });

        // Place a vehicle.
        _content.AddChild(Section("PLACE A VEHICLE"));
        var units = Data.SurfaceUnits.Where(u => Data.GroundVehicleNames.ContainsKey(u.Id) && u.Speed > Fix64.Zero).ToList();
        var unitPick = MakeOption(units.Select(u => Data.GroundVehicleNames[u.Id]).ToArray(), _placeUnit, i => _placeUnit = i);
        var owners = Sim.Players.Select((p, i) => i == Me ? $"{Data.Faction(p.FactionId).Name} (you)" : $"{Data.Faction(p.FactionId).Name} (player {i + 1})").ToList();
        var canAdd = Sim.Players.Count < 8 && Data.Factions.Any(f => f.Id == "cardassian");
        if (canAdd) owners.Add("Cardassian (new player)");
        _placeOwner = Math.Min(_placeOwner, owners.Count - 1);
        var ownerPick = MakeOption(owners.ToArray(), _placeOwner, i => _placeOwner = i);
        var unitGrid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Pass };
        unitGrid.AddThemeConstantOverride("h_separation", 8);
        unitGrid.AddThemeConstantOverride("v_separation", 6);
        unitGrid.AddChild(Text("Vehicle", 14, Ink));
        unitGrid.AddChild(Grow(unitPick));
        unitGrid.AddChild(Text("Owner", 14, Ink));
        unitGrid.AddChild(Grow(ownerPick));
        _content.AddChild(unitGrid);
        _content.AddChild(MakeButton("Place at cursor…", Plain, () =>
        {
            if (units.Count == 0) return;
            var def = units[Math.Clamp(_placeUnit, 0, units.Count - 1)];
            var newPlayer = canAdd && _placeOwner == owners.Count - 1;
            var owner = newPlayer ? Sim.Players.Count : _placeOwner;
            _host.BeginPick($"Test panel: click a tile to place a {Data.GroundVehicleNames[def.Id]}", tile =>
                Spawn(owner, def.Id, tile, $"placed a {Data.GroundVehicleNames[def.Id]}" + (owner == Me ? "" : $" for player {owner + 1}"), newPlayer, "cardassian"));
        }, 0, 36));

        // Place a building.
        _content.AddChild(Section("PLACE A BUILDING (FREE, FINISHED)"));
        var buildings = Data.BuildMenu.Concat(Data.Buildings.Where(b => b.IsCore).Select(b => b.Id)).Distinct().ToList();
        var buildingPick = MakeOption(buildings.Select(b => Data.Building(b).Name).ToArray(), _placeBuilding, i => _placeBuilding = i);
        _content.AddChild(buildingPick);
        _content.AddChild(MakeButton("Place building at cursor…", Plain, () =>
        {
            var id = buildings[Math.Clamp(_placeBuilding, 0, buildings.Count - 1)];
            var newPlayer = canAdd && _placeOwner == owners.Count - 1;
            var owner = newPlayer ? Sim.Players.Count : _placeOwner;
            _host.BeginPick($"Test panel: click a tile to place a {Data.Building(id).Name}", tile =>
            {
                var commands = new List<DebugCommand>();
                if (newPlayer) commands.Add(new DebugAddPlayerCommand(Me, "cardassian"));
                commands.Add(new DebugPlaceBuildingCommand(Me, owner, id, tile, 0));
                Apply($"placed a {Data.Building(id).Name}" + (owner == Me ? "" : $" for player {owner + 1}"), commands, () =>
                    Surface.StructureAt(tile) is { } s && s.DefId == id ? new DebugCommand[] { new DebugRemoveStructureCommand(Me, s.Id) } : Array.Empty<DebugCommand>(),
                    checkFirstOnly: newPlayer);
            });
        }, 0, 36));
        _content.AddChild(Wrapped("The owner above is used for buildings too. Anything but a Colony Hub must go inside that player's build range.", 12, Dim));
    }

    // ---------------------------------------------------------------- DATA

    private string _search = "";
    private readonly Dictionary<string, (DataNumber Number, string Value)> _pending = new();
    private (GameData Data, IReadOnlyList<DataNumber> Numbers)? _numbers;

    private IReadOnlyList<DataNumber> Numbers()
    {
        if (_numbers is not { } n || !ReferenceEquals(n.Data, Data)) _numbers = (Data, DataTuning.Numbers(Data));
        return _numbers.Value.Numbers;
    }

    private void BuildData()
    {
        _content.AddChild(Section("DATA TUNING"));
        var search = MakeField(_ => { });
        search.PlaceholderText = "Search data values (e.g. crawler chance, refinery wear)";
        search.Alignment = HorizontalAlignment.Left;
        search.Text = _search;
        _content.AddChild(search);
        var results = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        results.AddThemeConstantOverride("separation", 4);
        _content.AddChild(results);
        var status = Wrapped("", 13, Soft);
        _content.AddChild(status);

        void Fill()
        {
            foreach (var child in results.GetChildren())
            {
                results.RemoveChild(child);
                child.QueueFree();
            }
            var words = _search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                results.AddChild(Wrapped($"{Numbers().Count} numbers in {GameData.FileNames.Count} files. Type words from a path to find one.", 12, Dim));
                return;
            }
            var hits = Numbers().Where(n => words.All(w => (n.File + " " + n.Label).ToLowerInvariant().Contains(w))).ToList();
            foreach (var n in hits.Take(40)) results.AddChild(DataRow(n));
            if (hits.Count == 0) results.AddChild(Text("No match.", 13, Dim));
            else if (hits.Count > 40) results.AddChild(Text($"… and {hits.Count - 40} more: add words to narrow it", 12, Dim));
        }
        search.TextChanged += text =>
        {
            _search = text;
            Fill();
        };
        Fill();

        _refreshers.Add(() =>
        {
            if (_pending.Count == 0)
            {
                status.Text = DiffersFromDisk()
                    ? "This game's data differs from the files on disk (Save to JSON writes it)."
                    : "No changes waiting.";
                return;
            }
            var first = _pending.Values.First();
            status.Text = _pending.Count == 1
                ? $"1 value changed in data/{first.Number.File} (was {first.Number.Value})"
                : $"{_pending.Count} values changed in {string.Join(", ", _pending.Values.Select(p => p.Number.File).Distinct())}";
        });

        var buttons = Grid(3);
        buttons.AddChild(MakeButton("Apply to this game", Plain, ApplyPending, 0, 36, 12));
        buttons.AddChild(MakeButton("Reload data files", Plain, ReloadFromDisk, 0, 36, 12));
        buttons.AddChild(MakeButton("Save to JSON", Plain, SaveToDisk, 0, 36, 12));
        _content.AddChild(buttons);
        _content.AddChild(Wrapped("Applies to the running game. Map generation, landing and other set-up numbers only show in a new game. " +
                                  "Only numbers can change here; ids and lists need a restart.", 12, Dim));
    }

    private Control DataRow(DataNumber n)
    {
        var key = $"{n.File}|{n.Path}";
        var row = Row(8);
        var label = Text($"{n.Label}", 12, Ink);
        label.TooltipText = $"data/{n.File}: {n.Path}";
        label.MouseFilter = MouseFilterEnum.Pass;
        label.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
        LineEdit field = null!;
        field = MakeField(text =>
        {
            var value = text.Trim();
            if (value == n.Value)
            {
                _pending.Remove(key);
                MarkPending(field, false);
                return;
            }
            var problem = DataTuning.ValueProblem(n.Value, value);
            if (problem != null)
            {
                _host.Notify($"Test panel: {n.Label}: {problem}", true);
                field.Text = _pending.TryGetValue(key, out var p) ? p.Value : n.Value;
                return;
            }
            _pending[key] = (n, value);
            MarkPending(field, true);
        });
        field.CustomMinimumSize = new Vector2(90, 30);
        field.Text = _pending.TryGetValue(key, out var pending) ? pending.Value : n.Value;
        MarkPending(field, _pending.ContainsKey(key));
        row.AddChild(field);
        return row;
    }

    private static void MarkPending(LineEdit field, bool pending)
    {
        var s = FieldStyle(pending ? Accent : Border);
        field.AddThemeStyleboxOverride("normal", s);
        field.AddThemeStyleboxOverride("focus", FieldStyle(Accent));
    }

    private void ApplyPending()
    {
        if (_pending.Count == 0)
        {
            _host.Notify("Test panel: change a value first (type it and press Enter)");
            return;
        }
        var edits = _pending.Values.Select(p => new DataEdit(p.Number.File, p.Number.Path, p.Value)).ToList();
        var inverse = _pending.Values.Select(p => new DataEdit(p.Number.File, p.Number.Path, p.Number.Value)).ToList();
        var text = _pending.Count == 1 ? $"{_pending.Values.First().Number.Label} {_pending.Values.First().Number.Value} → {_pending.Values.First().Value}" : $"{_pending.Count} data values";
        if (Apply(text, new DebugDataCommand(Me, edits), new DebugDataCommand(Me, inverse))) _pending.Clear();
    }

    private string? DiskText(string file)
    {
        var path = Path.Combine(_host.DataDirectory, file);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private ulong _diskCheckedAt;
    private bool _differs;

    /// <summary>Whether this game's data differs from the files on disk (checked every two seconds, not every frame).</summary>
    private bool DiffersFromDisk()
    {
        var now = Time.GetTicksMsec();
        if (_diskCheckedAt != 0 && now - _diskCheckedAt < 2000) return _differs;
        _diskCheckedAt = now;
        _differs = GameData.FileNames.Any(f => Data.Sources.TryGetValue(f, out var text) && DiskText(f) is { } disk && disk != text);
        return _differs;
    }

    private void ReloadFromDisk()
    {
        var changed = new List<DataFile>();
        var before = new List<DataFile>();
        foreach (var f in GameData.FileNames)
        {
            var disk = DiskText(f);
            if (disk == null || !Data.Sources.TryGetValue(f, out var now) || disk == now) continue;
            changed.Add(new DataFile(f, disk));
            before.Add(new DataFile(f, now));
        }
        if (changed.Count == 0)
        {
            _host.Notify("Test panel: the data files on disk match this game");
            return;
        }
        if (Apply($"reloaded {string.Join(", ", changed.Select(c => c.File))}", new DebugReloadDataCommand(Me, changed), new DebugReloadDataCommand(Me, before)))
            _pending.Clear();
    }

    private void SaveToDisk()
    {
        if (_pending.Count > 0)
        {
            _host.Notify("Test panel: apply the changed values first, then save", true);
            return;
        }
        var saved = new List<string>();
        foreach (var f in GameData.FileNames)
        {
            if (!Data.Sources.TryGetValue(f, out var text) || DiskText(f) == text) continue;
            try
            {
                File.WriteAllText(Path.Combine(_host.DataDirectory, f), text);
                saved.Add(f);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _host.Notify($"Test panel: couldn't write {f}: {e.Message}", true);
            }
        }
        _host.Notify(saved.Count == 0 ? "Test panel: nothing to save, the files already match" : $"Saved data/{string.Join(", data/", saved)}");
        _diskCheckedAt = 0;
        if (saved.Count > 0) _lastEdit = $"saved {string.Join(", ", saved)}  (files on disk)";
    }

    // ---------------------------------------------------------------- widgets

    private static MarginContainer Padded(int left, int top, int right, int bottom)
    {
        var m = new MarginContainer { MouseFilter = MouseFilterEnum.Pass };
        m.AddThemeConstantOverride("margin_left", left);
        m.AddThemeConstantOverride("margin_top", top);
        m.AddThemeConstantOverride("margin_right", right);
        m.AddThemeConstantOverride("margin_bottom", bottom);
        return m;
    }

    private static Label Text(string text, int size, Color colour, Font? font = null)
    {
        var label = PlanetHud.MakeLabel(text, size, colour);
        if (font != null) label.AddThemeFontOverride("font", font);
        return label;
    }

    private static Label Wrapped(string text, int size, Color colour)
    {
        var label = Text(text, size, colour);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(Width - 60, 0);
        return label;
    }

    private Label Caption(string text) => Text(text, 13, Soft, _caps);

    /// <summary>A caption with a rule above it (the mockup's section breaks).</summary>
    private Control Section(string text)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(RuleLine());
        box.AddChild(Caption(text));
        return box;
    }

    private static ColorRect RuleLine() => new() { Color = Rule, CustomMinimumSize = new Vector2(0, 1), MouseFilter = MouseFilterEnum.Ignore };

    private static HBoxContainer Row(int separation = 8)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", separation);
        return row;
    }

    private static GridContainer Grid(int columns)
    {
        var grid = new GridContainer { Columns = columns, MouseFilter = MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        // Equal columns across the panel: every cell expands (fires as the cells enter the tree with the grid).
        grid.ChildEnteredTree += node =>
        {
            if (node is Control c && c.GetParent() == grid) c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        };
        return grid;
    }

    private static T Grow<T>(T c) where T : Control
    {
        c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return c;
    }

    private PanelContainer Chip(string text, Color bg, Color ink, Color? edge)
    {
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var style = new StyleBoxFlat { BgColor = bg, ContentMarginLeft = 7, ContentMarginRight = 7, ContentMarginTop = 1, ContentMarginBottom = 1 };
        style.SetCornerRadiusAll(edge == null ? 9 : 4);
        if (edge is { } e)
        {
            style.BorderColor = e;
            style.SetBorderWidthAll(1);
        }
        chip.AddThemeStyleboxOverride("panel", style);
        var label = Text(text, 12, ink, _caps);
        label.Name = "Text";
        chip.AddChild(label);
        return chip;
    }

    private static void SetChip(PanelContainer chip, string text, Color bg)
    {
        if (chip.GetThemeStylebox("panel") is StyleBoxFlat s) s.BgColor = bg;
        chip.GetNode<Label>("Text").Text = text;
    }

    private static StyleBoxFlat Box(Color bg, Color edge, int radius = 4)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = edge, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 2, ContentMarginBottom = 2 };
        s.SetBorderWidthAll(1);
        s.SetCornerRadiusAll(radius);
        return s;
    }

    private static Button MakeButton(string text, Tint tint, Action onPressed, int width, int height, int fontSize = 13)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(width, height), ClipText = true };
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeStyleboxOverride("normal", Box(tint.Bg, tint.Edge));
        b.AddThemeStyleboxOverride("hover", Box(tint.Bg.Lightened(0.08f), tint.Edge.Lightened(0.25f)));
        b.AddThemeStyleboxOverride("pressed", Box(tint.Bg.Darkened(0.2f), Accent));
        b.AddThemeStyleboxOverride("disabled", Box(tint.Bg.Darkened(0.3f), Rule));
        b.AddThemeColorOverride("font_color", tint.Text);
        b.AddThemeColorOverride("font_hover_color", tint.Text.Lightened(0.15f));
        b.AddThemeColorOverride("font_pressed_color", tint.Text);
        b.AddThemeColorOverride("font_disabled_color", Off);
        b.Pressed += onPressed;
        return b;
    }

    private void StyleTab(Button b, bool active)
    {
        var normal = new StyleBoxFlat { BgColor = active ? new Color(0.102f, 0.145f, 0.251f) : new Color(0, 0, 0, 0), ContentMarginLeft = 4, ContentMarginRight = 4 };
        if (active)
        {
            normal.BorderColor = Accent;
            normal.BorderWidthBottom = 2;
        }
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" }) b.AddThemeStyleboxOverride(state, normal);
        var ink = active ? Accent : Soft;
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) b.AddThemeColorOverride(c, ink);
        b.SetPressedNoSignal(active);
    }

    /// <summary>A row of joined toggle buttons; the selected one is orange.</summary>
    private Control Segmented(string[] options, Func<int> selected, Action<int> onPick, int width = 0, int height = 34)
    {
        var grid = new GridContainer { Columns = options.Length, MouseFilter = MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 0);
        var buttons = new List<Button>();
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var b = new Button { Text = options[i], FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(width, height), ClipText = true };
            if (width == 0) b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Pressed += () => onPick(index);
            buttons.Add(b);
            grid.AddChild(b);
        }
        if (width == 0) grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var last = -2;
        void Paint()
        {
            var at = selected();
            if (at == last) return;
            last = at;
            for (var i = 0; i < buttons.Count; i++)
            {
                var on = i == at;
                var style = Box(on ? Accent : new Color(0, 0, 0, 0), Border, 0);
                foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" }) buttons[i].AddThemeStyleboxOverride(state, style);
                var ink = on ? Bg with { A = 1 } : Soft;
                foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) buttons[i].AddThemeColorOverride(c, ink);
            }
        }
        Paint();
        _refreshers.Add(Paint);
        return grid;
    }

    private static CheckBox MakeCheck(string text, Action<bool> onToggled)
    {
        var check = new CheckBox { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 28) };
        check.AddThemeFontSizeOverride("font_size", 14);
        check.AddThemeColorOverride("font_color", Ink);
        check.AddThemeColorOverride("font_hover_color", Ink);
        check.AddThemeColorOverride("font_pressed_color", Ink);
        check.AddThemeColorOverride("font_hover_pressed_color", Ink);
        check.AddThemeColorOverride("font_disabled_color", Off);
        check.Toggled += on => onToggled(on);
        return check;
    }

    /// <summary>0..100 slider (orange). Dragging edits once, on release; the value follows the game otherwise.</summary>
    private static HSlider MakeSlider(double min, double max, Action<float> onCommit)
    {
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = 1, Scrollable = false, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 22) };
        slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var track = new StyleBoxFlat { BgColor = new Color(0.078f, 0.11f, 0.188f), ContentMarginTop = 3, ContentMarginBottom = 3 };
        track.SetCornerRadiusAll(3);
        var fill = new StyleBoxFlat { BgColor = Accent, ContentMarginTop = 3, ContentMarginBottom = 3 };
        fill.SetCornerRadiusAll(3);
        slider.AddThemeStyleboxOverride("slider", track);
        slider.AddThemeStyleboxOverride("grabber_area", fill);
        slider.AddThemeStyleboxOverride("grabber_area_highlight", fill);
        slider.DragStarted += () => slider.SetMeta("dragging", true);
        slider.DragEnded += changed =>
        {
            slider.RemoveMeta("dragging");
            if (changed) onCommit((float)slider.Value);
        };
        return slider;
    }

    private static StyleBoxFlat FieldStyle(Color edge)
    {
        var s = Box(FieldBg, edge);
        s.ContentMarginLeft = 8;
        s.ContentMarginRight = 8;
        return s;
    }

    /// <summary>A text field: Enter submits and gives the keyboard back to the game.</summary>
    private static LineEdit MakeField(Action<string> onSubmit)
    {
        var field = new LineEdit { Alignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(0, 30) };
        field.AddThemeFontSizeOverride("font_size", 14);
        field.AddThemeColorOverride("font_color", Ink);
        field.AddThemeStyleboxOverride("normal", FieldStyle(Border));
        field.AddThemeStyleboxOverride("focus", FieldStyle(Accent));
        field.TextSubmitted += text =>
        {
            onSubmit(text);
            field.ReleaseFocus();
        };
        return field;
    }

    private static OptionButton MakeOption(string[] items, int selected, Action<int> onPick)
    {
        var option = new OptionButton { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 32) };
        option.AddThemeFontSizeOverride("font_size", 14);
        option.AddThemeStyleboxOverride("normal", FieldStyle(Border));
        option.AddThemeStyleboxOverride("hover", FieldStyle(Border.Lightened(0.2f)));
        option.AddThemeStyleboxOverride("pressed", FieldStyle(Accent));
        for (var i = 0; i < items.Length; i++) option.AddItem(items[i], i);
        if (items.Length > 0) option.Selected = Math.Clamp(selected, 0, items.Length - 1);
        option.ItemSelected += index => onPick((int)index);
        return option;
    }
}
