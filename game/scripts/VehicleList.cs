using System;
using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// The compact list of the player's vehicles that comes up with the ping (owner 2026-11-17: "We need a way to find
/// vehicles on the surface"): one row each with its type, the name on its hull and what it is doing (driving, parked,
/// digging, disabled, docked …). A click on a row tells the driver to select that vehicle and fly the camera to it;
/// while the mouse is on a row the driver keeps that vehicle's beacon up. PIN keeps the list open after the ping fades,
/// × closes it. It sits bottom-left, just above the bottom panel (over the minimap it belongs with), scrolls past a few
/// rows, takes the mouse over itself and never the keyboard (no focus anywhere in it). Rows are kept and re-used; a
/// label's text is only set when it changed.
/// </summary>
public partial class VehicleList : PanelContainer
{
    /// <summary>One row's text, filled by the driver.</summary>
    public struct Row
    {
        public int Id;
        public string Kind, Name, State;
        public Color StateColour;
    }

    /// <summary>A row was clicked (the vehicle's unit id).</summary>
    public event Action<int>? Picked;
    public event Action? PinPressed;
    public event Action? ClosePressed;

    /// <summary>The vehicle whose row the mouse is on (0 = none).</summary>
    public int HoveredId { get; private set; }

    private sealed class RowUi
    {
        public int Id;
        public Button Button = null!;
        public Label Kind = null!, Name = null!, State = null!;
        public Color StateColour;
    }

    private const float BaseWidth = 286f, RowHeight = 22f, RowGap = 2f;

    private float _s;
    private bool _built;
    private Label _title = null!;
    private Button _pin = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _rowsBox = null!;
    private readonly List<RowUi> _rows = new();
    private int _shownRows = -1, _visibleRows = -1, _count = -1;
    private bool _pinned;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    public bool IsOverUi(Vector2 mouse) => Visible && GetGlobalRect().HasPoint(mouse);

    /// <summary>Shows the list with these rows, its bottom-left corner at <paramref name="bottomLeft"/>, at most
    /// <paramref name="maxRows"/> rows tall (more scroll).</summary>
    public void Present(List<Row> rows, bool pinned, Vector2 bottomLeft, int maxRows, Vector2 screen)
    {
        var s = HudKit.ScaleFor(screen);
        if (!_built || Mathf.Abs(s - _s) > 0.01f) Build(s);

        if (_count != rows.Count)
        {
            _count = rows.Count;
            _title.Text = rows.Count == 1 ? "VEHICLES  ·  1" : $"VEHICLES  ·  {rows.Count}";
        }
        if (_pinned != pinned)
        {
            _pinned = pinned;
            _pin.SetPressedNoSignal(pinned);
            _pin.Text = pinned ? "PINNED" : "PIN";
        }
        while (_rows.Count < rows.Count) _rows.Add(NewRow());
        var resized = false;
        for (var i = 0; i < _rows.Count; i++)
        {
            var ui = _rows[i];
            var show = i < rows.Count;
            if (ui.Button.Visible != show)
            {
                ui.Button.Visible = show;
                resized = true;
            }
            if (!show)
            {
                if (HoveredId == ui.Id && ui.Id != 0) HoveredId = 0;
                ui.Id = 0;
                continue;
            }
            var row = rows[i];
            if (ui.Id != row.Id)
            {
                if (HoveredId == ui.Id) HoveredId = ui.Button.IsHovered() ? row.Id : 0; // the row under the mouse changed vehicle
                ui.Id = row.Id;
            }
            if (ui.Kind.Text != row.Kind) ui.Kind.Text = row.Kind;
            if (ui.Name.Text != row.Name) ui.Name.Text = row.Name;
            if (ui.State.Text != row.State) ui.State.Text = row.State;
            if (ui.StateColour != row.StateColour)
            {
                ui.StateColour = row.StateColour;
                ui.State.AddThemeColorOverride("font_color", row.StateColour);
            }
        }
        var visibleRows = Mathf.Clamp(rows.Count, 1, Mathf.Max(maxRows, 1));
        if (visibleRows != _visibleRows || rows.Count != _shownRows) resized = true;
        _shownRows = rows.Count;
        if (resized)
        {
            _visibleRows = visibleRows;
            _scroll.CustomMinimumSize = new Vector2(0, visibleRows * (RowHeight + RowGap) * _s);
            ResetSize();
        }
        var size = GetCombinedMinimumSize();
        var at = new Vector2(bottomLeft.X, bottomLeft.Y - size.Y);
        if (Position != at) Position = at;
        if (!Visible) Visible = true;
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        HoveredId = 0;
    }

    private void Build(float s)
    {
        _s = s;
        _built = true;
        HudKit.Clear(this);
        _rows.Clear();
        _shownRows = _visibleRows = _count = -1;
        HoveredId = 0;
        AddThemeStyleboxOverride("panel", HudKit.Box(HudKit.Panel, HudKit.Line, 6f * s, 8f * s, 6f * s));
        CustomMinimumSize = new Vector2(BaseWidth * s, 0);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(5f * s));
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", Mathf.RoundToInt(6f * s));
        _title = HudKit.Label("VEHICLES", 13f * s, HudKit.Accent, condensed: true, bold: true);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.VerticalAlignment = VerticalAlignment.Center;
        head.AddChild(_title);
        _pinned = false;
        _pin = Chip("PIN", s, "Keep this list open after the ping fades (or press Z twice quickly)");
        _pin.ToggleMode = true;
        _pin.Pressed += () => PinPressed?.Invoke();
        head.AddChild(_pin);
        var close = Chip("×", s, "Close the list");
        close.Pressed += () => ClosePressed?.Invoke();
        head.AddChild(close);
        box.AddChild(head);

        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        _rowsBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rowsBox.AddThemeConstantOverride("separation", Mathf.RoundToInt(RowGap * s));
        _scroll.AddChild(_rowsBox);
        box.AddChild(_scroll);
        AddChild(box);
    }

    private static Button Chip(string text, float s, string tip)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, TooltipText = tip, CustomMinimumSize = new Vector2(22f * s, 20f * s) };
        b.AddThemeFontOverride("font", HudKit.Condensed(true));
        b.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(11f * s));
        HudKit.StyleTile(b, 4f * s, 5f * s);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            if (b.GetThemeStylebox(state) is StyleBoxFlat flat)
            {
                flat.ContentMarginTop = 0;
                flat.ContentMarginBottom = 0;
            }
        return b;
    }

    private RowUi NewRow()
    {
        var s = _s;
        var ui = new RowUi();
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, RowHeight * s), ClipContents = true };
        b.AddThemeStyleboxOverride("normal", HudKit.Box(new Color(0.04f, 0.06f, 0.11f), new Color(0, 0, 0, 0), 4f * s, 0, 0, 0));
        b.AddThemeStyleboxOverride("hover", HudKit.Box(new Color(0.08f, 0.13f, 0.25f), HudKit.HoverEdge, 4f * s));
        b.AddThemeStyleboxOverride("pressed", HudKit.Box(HudKit.Pressed, HudKit.Accent, 4f * s));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        line.OffsetLeft = 6f * s;
        line.OffsetRight = -6f * s;
        line.AddThemeConstantOverride("separation", Mathf.RoundToInt(6f * s));
        ui.Name = HudKit.Label("", 13f * s, HudKit.Ink, condensed: true, bold: true);
        ui.Name.CustomMinimumSize = new Vector2(84f * s, 0);
        ui.Name.ClipText = true;
        ui.Name.VerticalAlignment = VerticalAlignment.Center;
        ui.Name.SizeFlagsVertical = SizeFlags.Fill;
        ui.Kind = HudKit.Label("", 11f * s, HudKit.Dim, condensed: true);
        ui.Kind.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ui.Kind.ClipText = true;
        ui.Kind.VerticalAlignment = VerticalAlignment.Center;
        ui.Kind.SizeFlagsVertical = SizeFlags.Fill;
        ui.State = HudKit.Label("", 12f * s, HudKit.Dim, condensed: true);
        ui.State.HorizontalAlignment = HorizontalAlignment.Right;
        ui.State.VerticalAlignment = VerticalAlignment.Center;
        ui.State.SizeFlagsVertical = SizeFlags.Fill;
        ui.StateColour = HudKit.Dim;
        line.AddChild(ui.Name);
        line.AddChild(ui.Kind);
        line.AddChild(ui.State);
        b.AddChild(line);
        b.Pressed += () =>
        {
            if (ui.Id != 0) Picked?.Invoke(ui.Id);
        };
        b.MouseEntered += () => HoveredId = ui.Id;
        b.MouseExited += () =>
        {
            if (HoveredId == ui.Id) HoveredId = 0;
        };
        ui.Button = b;
        _rowsBox.AddChild(b);
        return ui;
    }
}
