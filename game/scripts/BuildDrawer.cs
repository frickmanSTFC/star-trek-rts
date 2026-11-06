using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Math;
using Godot;

namespace FinalFrontier;

/// <summary>One cost entry on a tile: resource id and index, the amount, and the chip text ("120 DUR").</summary>
public readonly record struct CostPart(string ResourceId, int Resource, Fix64 Amount, string Text);

/// <summary>A building tile in the drawer. Key = its number key ("" = none); Locked = research-gated (greyed, LockText = the tech).</summary>
public sealed record DrawerTile(string Id, string Name, string Key, bool Locked, string LockText, IReadOnlyList<CostPart> Cost);

/// <summary>A category column in the drawer.</summary>
public sealed record DrawerGroup(string Name, IReadOnlyList<DrawerTile> Tiles);

/// <summary>
/// The slide-up build drawer (owner 2026-11-06: command card option C of docs/mockups/build-card): B opens it over the
/// bottom of the map, above the HUD's bottom panel, with every buildable building as a picture tile (render from
/// <see cref="ModelThumbnails"/>, the line icon until it is ready), its name, cost chips (red when short), number key and
/// a lock badge when research-gated, grouped by category in columns. Clicking a tile raises <see cref="Picked"/>; the
/// driver opens the large card (or places at once with Shift). <see cref="HoveredId"/> feeds the HUD tooltip.
/// Sizes scale from 1280×720 (<see cref="HudKit.ScaleFor"/>); the content is rebuilt only when the list or the screen
/// size changes, and <see cref="Refresh"/> only touches what changed.
/// </summary>
public partial class BuildDrawer : PanelContainer
{
    public event Action<string>? Picked;

    /// <summary>The tile under the mouse (null = none).</summary>
    public string? HoveredId { get; private set; }

    /// <summary>Open (or opening): the driver's B / Esc state.</summary>
    public bool IsOpen { get; private set; }

    private float _shown; // 0 closed .. 1 open (slide)
    private float _s = 1f;
    private Vector2 _builtFor;
    private string _signature = "";
    private string _title = "", _keys = "";
    private IReadOnlyList<DrawerGroup> _groups = Array.Empty<DrawerGroup>();
    private int _thumbVersion = -1;
    private float _bottom = PlanetHud.BottomHeight;

    private sealed class Tile
    {
        public DrawerTile Data = null!;
        public Button Button = null!;
        public TextureRect Picture = null!;
        public Label Name = null!;
        public readonly List<(HudKit.CostChip Chip, CostPart Part)> Chips = new();
    }

    private readonly List<Tile> _tiles = new();
    private Label _titleLabel = null!;
    private Label _keysLabel = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    /// <summary>Bottom edge's distance from the screen bottom (the HUD's bottom panel height).</summary>
    public void SetBottom(float bottom) => _bottom = bottom;

    public void SetOpen(bool open)
    {
        IsOpen = open;
        if (!open) HoveredId = null;
    }

    /// <summary>True when the mouse is over the drawer (the world shouldn't react).</summary>
    public bool IsOverUi(Vector2 mouse) => Visible && _shown > 0.05f && GetGlobalRect().HasPoint(mouse);

    /// <summary>The tiles to show. Rebuilt when the signature (the driver's list of ids, keys and lock states) changes.</summary>
    public void SetContent(string signature, string title, string keys, IReadOnlyList<DrawerGroup> groups)
    {
        _title = title;
        _keys = keys;
        if (signature == _signature && _builtFor == ScreenSize()) return;
        _signature = signature;
        _groups = groups;
        Rebuild();
    }

    private Vector2 ScreenSize() => GetViewport()?.GetVisibleRect().Size ?? new Vector2(1600, 900);

    private void Rebuild()
    {
        var screen = ScreenSize();
        _builtFor = screen;
        _s = HudKit.ScaleFor(screen);
        var s = _s;
        HudKit.Clear(this);
        _tiles.Clear();
        HoveredId = null;
        _thumbVersion = -1;

        var style = HudKit.Box(new Color(0.02f, 0.03f, 0.07f, 0.96f), HudKit.Line, 10 * s, 14 * s, 10 * s);
        style.CornerRadiusBottomLeft = 0;
        style.CornerRadiusBottomRight = 0;
        style.BorderWidthBottom = 0;
        AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * s));
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _titleLabel = HudKit.Label(_title, 11 * s, HudKit.Dim, condensed: true);
        _titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_titleLabel);
        _keysLabel = HudKit.Label(_keys, 11 * s, HudKit.Dim, condensed: true);
        head.AddChild(_keysLabel);
        box.AddChild(head);

        var cols = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        cols.AddThemeConstantOverride("separation", Mathf.RoundToInt(12 * s));
        foreach (var g in _groups)
        {
            var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill | SizeFlags.Fill, SizeFlagsStretchRatio = 1, MouseFilter = MouseFilterEnum.Ignore };
            col.AddThemeConstantOverride("separation", Mathf.RoundToInt(5 * s));
            col.AddChild(HudKit.Label(g.Name.ToUpperInvariant(), 10.5f * s, HudKit.Dim, condensed: true));
            foreach (var t in g.Tiles) col.AddChild(MakeTile(t, s));
            cols.AddChild(col);
        }
        box.AddChild(cols);
        AddChild(box);
        ResetSize();
    }

    private Control MakeTile(DrawerTile t, float s)
    {
        var tile = new Tile { Data = t };
        var b = new Button { FocusMode = FocusModeEnum.None, ToggleMode = true, CustomMinimumSize = new Vector2(150 * s, 58 * s), ClipContents = true };
        HudKit.StyleTile(b, 7 * s);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * s));

        var pic = HudKit.Picture(new Vector2(78 * s, 58 * s));
        pic.Texture = Icons.Get(t.Id, HudKit.Accent);
        pic.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        if (t.Locked) pic.Modulate = new Color(1, 1, 1, 0.45f);
        if (t.Key.Length > 0) pic.AddChild(Badge(t.Key, HudKit.Dim, s, left: true));
        if (t.Locked) pic.AddChild(Badge("RESEARCH", HudKit.Sky, s, left: false));
        tile.Picture = pic;
        row.AddChild(pic);

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", Mathf.RoundToInt(3 * s));
        tile.Name = HudKit.Label(t.Name, 12.5f * s, t.Locked ? HudKit.Dim : HudKit.Ink, bold: true);
        tile.Name.ClipText = true;
        text.AddChild(tile.Name);
        var cost = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        cost.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(8 * s));
        cost.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(2 * s));
        if (t.Locked) cost.AddChild(HudKit.Label(t.LockText, 10.5f * s, HudKit.Sky, condensed: true));
        else
            foreach (var part in t.Cost)
            {
                var chip = new HudKit.CostChip(part.ResourceId, part.Text, 10.5f * s);
                cost.AddChild(chip.Root);
                tile.Chips.Add((chip, part));
            }
        text.AddChild(cost);
        row.AddChild(text);
        b.AddChild(row);

        var id = t.Id;
        b.Pressed += () => Picked?.Invoke(id);
        b.MouseEntered += () => HoveredId = id;
        b.MouseExited += () =>
        {
            if (HoveredId == id) HoveredId = null;
        };
        tile.Button = b;
        _tiles.Add(tile);
        return b;
    }

    /// <summary>A little label in a dark plate over a picture's top corner (the number key, or RESEARCH).</summary>
    public static Control Badge(string text, Color colour, float s, bool left)
    {
        var plate = HudKit.PanelBox(new Color(0.02f, 0.03f, 0.07f, 0.8f), new Color(0, 0, 0, 0), 4 * s, 4 * s, 0);
        plate.AddChild(HudKit.Label(text, 10 * s, colour, condensed: true));
        if (left)
        {
            plate.SetAnchorsPreset(LayoutPreset.TopLeft);
            plate.Position = new Vector2(3 * s, 3 * s);
        }
        else
        {
            plate.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.Minsize);
            plate.GrowHorizontal = GrowDirection.Begin;
            plate.OffsetRight = -3 * s;
            plate.OffsetTop = 3 * s;
        }
        return plate;
    }

    /// <summary>
    /// Once a frame: slide, place above the bottom panel, cost chips red when short (have(resource, amount) = enough),
    /// the active tile pressed, pictures in as they arrive. Enabled = false greys every tile (not landed yet).
    /// </summary>
    public void Refresh(float delta, Func<int, Fix64, bool> have, string? activeId, bool enabled, ModelThumbnails? thumbs)
    {
        _shown = Mathf.MoveToward(_shown, IsOpen ? 1f : 0f, delta / 0.14f);
        var visible = _shown > 0.001f;
        if (Visible != visible) Visible = visible;
        if (!visible) return;
        if (ScreenSize() != _builtFor) Rebuild();
        if (_titleLabel.Text != _title) _titleLabel.Text = _title;
        if (_keysLabel.Text != _keys) _keysLabel.Text = _keys;

        var screen = ScreenSize();
        var margin = Mathf.Round(10 * _s);
        var width = screen.X - margin * 2;
        if (!Mathf.IsEqualApprox(Size.X, width)) Size = new Vector2(width, 0);
        var height = GetCombinedMinimumSize().Y;
        var eased = 1f - (1f - _shown) * (1f - _shown);
        Position = new Vector2(margin, screen.Y - _bottom - height + (1f - eased) * 28f * _s);
        Modulate = new Color(1, 1, 1, eased);

        var refreshPictures = thumbs != null && thumbs.Version != _thumbVersion;
        if (refreshPictures) _thumbVersion = thumbs!.Version;
        foreach (var t in _tiles)
        {
            var active = t.Data.Id == activeId;
            if (t.Button.ButtonPressed != active) t.Button.SetPressedNoSignal(active);
            var disabled = !enabled;
            if (t.Button.Disabled != disabled) t.Button.Disabled = disabled;
            foreach (var (chip, part) in t.Chips) chip.Set(part.Text, !have(part.Resource, part.Amount), HudKit.Dim);
            // A new picture arrived (or the cache was dropped): ask again; Thumb queues a missing one the first time.
            if (refreshPictures && thumbs!.Thumb(t.Data.Id) is { } tex && t.Picture.Texture != tex)
            {
                t.Picture.Texture = tex;
                t.Picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            }
        }
    }
}
