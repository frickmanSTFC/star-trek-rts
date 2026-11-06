using System;
using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>A quick tile on the command card's build strip: one of the last three buildings placed.</summary>
public sealed record RecentTile(string Id, string Name, string Key, IReadOnlyList<(string ResourceId, string Text, bool Short)> Cost, bool Active);

/// <summary>The command card in build mode (drawer option C): the BUILD [B] button and the recent builds.</summary>
public sealed record BuildStripView(string Caption, IReadOnlyList<RecentTile> Recent, bool Enabled, bool DrawerOpen);

// The command card's build strip (owner 2026-11-06, build menu option C): in place of the old grid of build buttons, a
// BUILD [B] button that opens the slide-up drawer (BuildDrawer) and the last three builds as picture tiles. Also hooks
// for the overlays the driver owns (drawer, large card, placing strip), picture tiles for Vehicle Bay orders, and the
// tile under the mouse for the tooltip.
public partial class PlanetHud
{
    private readonly Dictionary<string, (VBoxContainer Box, HBoxContainer Top, TextureRect Icon, Label Name, Label Key)> _commandParts = new();
    private readonly Dictionary<string, TextureRect> _commandPictures = new();
    private Control? _buildStrip;
    private Button _buildButton = null!;
    private Label _buildButtonSub = null!;
    private readonly List<(Button Button, TextureRect Picture, Label Name, Label Key, HFlowContainer Cost, List<HudKit.CostChip> Chips)> _recentTiles = new();
    private readonly string?[] _recentIds = new string?[3];
    private int _recentThumbVersion = -1;

    /// <summary>The BUILD [B] button on the command card.</summary>
    public event Action? BuildButtonPressed;

    /// <summary>A recent tile was picked (its building id).</summary>
    public event Action<string>? RecentPicked;

    /// <summary>The command-card tile under the mouse (a recent build's id, or a command id), null = none.</summary>
    public string? HoveredCommand { get; private set; }

    /// <summary>Adds a driver-owned overlay (drawer, large card, strip) above the bottom panel but under F1 help and the tooltip.</summary>
    public void AddOverlay(Control overlay)
    {
        AddChild(overlay);
        MoveChild(overlay, _help.GetIndex());
    }

    /// <summary>Gives a command-card button a picture (a render from ModelThumbnails) in place of its line icon; null keeps the icon.</summary>
    public void SetCommandPicture(string id, Texture2D? picture)
    {
        if (picture == null || !_commandParts.TryGetValue(id, out var parts)) return;
        if (!_commandPictures.TryGetValue(id, out var rect))
        {
            // Picture on top with the number key over its corner; the name over the picture's foot; cost under it.
            rect = HudKit.Picture(new Vector2(80, 46));
            parts.Icon.Visible = false;
            parts.Key.Visible = false;
            parts.Box.AddChild(rect);
            parts.Box.MoveChild(rect, 0);
            parts.Box.AddThemeConstantOverride("separation", 2);
            rect.AddChild(BuildDrawer.Badge(parts.Key.Text.Trim('[', ']'), Dim, 1f, left: true));
            parts.Name.AddThemeFontSizeOverride("font_size", 12);
            parts.Name.AutowrapMode = TextServer.AutowrapMode.Off;
            parts.Name.ClipText = true;
            parts.Name.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            _commandPictures[id] = rect;
        }
        if (rect.Texture != picture) rect.Texture = picture;
    }

    /// <summary>Shows the build strip in the command card (in place of the grid); ShowCommands brings the grid back.</summary>
    public void ShowBuildStrip(string title, BuildStripView v, ModelThumbnails? thumbs)
    {
        _buildStrip ??= MakeBuildStrip();
        _commandTitle.Text = title;
        if (_commandGrid.Visible) _commandGrid.Visible = false;
        if (!_buildStrip.Visible) _buildStrip.Visible = true;
        _buildButton.Disabled = !v.Enabled;
        _buildButton.SetPressedNoSignal(v.DrawerOpen);
        if (_buildButtonSub.Text != v.Caption) _buildButtonSub.Text = v.Caption;

        var pictures = thumbs != null && thumbs.Version != _recentThumbVersion;
        if (pictures) _recentThumbVersion = thumbs!.Version;
        for (var i = 0; i < _recentTiles.Count; i++)
        {
            var tile = _recentTiles[i];
            var show = i < v.Recent.Count;
            if (tile.Button.Visible != show) tile.Button.Visible = show;
            if (!show) continue;
            var r = v.Recent[i];
            if (_recentIds[i] != r.Id)
            {
                _recentIds[i] = r.Id;
                tile.Name.Text = r.Name;
                tile.Key.Text = r.Key;
                tile.Picture.Texture = Icons.Get(r.Id, Accent);
                tile.Picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                HudKit.Clear(tile.Cost);
                tile.Chips.Clear();
                foreach (var c in r.Cost)
                {
                    var chip = new HudKit.CostChip(c.ResourceId, c.Text, 11);
                    tile.Cost.AddChild(chip.Root);
                    tile.Chips.Add(chip);
                }
                pictures = thumbs != null;
            }
            if (pictures && thumbs!.Thumb(r.Id) is { } tex && tile.Picture.Texture != tex)
            {
                tile.Picture.Texture = tex;
                tile.Picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            }
            for (var c = 0; c < r.Cost.Count && c < tile.Chips.Count; c++) tile.Chips[c].Set(r.Cost[c].Text, r.Cost[c].Short, Dim);
            tile.Button.Disabled = !v.Enabled;
            tile.Button.SetPressedNoSignal(r.Active);
        }
    }

    private void HideBuildStrip()
    {
        if (_buildStrip != null && _buildStrip.Visible) _buildStrip.Visible = false;
        if (!_commandGrid.Visible) _commandGrid.Visible = true;
    }

    private Control MakeBuildStrip()
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 12);

        _buildButton = new Button { FocusMode = Control.FocusModeEnum.None, ToggleMode = true, CustomMinimumSize = new Vector2(150, 150), TooltipText = "Every building, with pictures (B)" };
        HudKit.StyleTile(_buildButton, 9);
        _buildButton.AddThemeStyleboxOverride("normal", HudKit.Box(new Color(0.2f, 0.14f, 0.04f), Accent, 9));
        var inner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        inner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 14);
        inner.AddThemeConstantOverride("separation", 2);
        inner.AddChild(HudKit.Label("BUILD", 34, Ink, bold: true));
        _buildButtonSub = HudKit.Label("", 12, Dim, condensed: true);
        _buildButtonSub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        inner.AddChild(_buildButtonSub);
        _buildButton.AddChild(inner);
        _buildButton.Pressed += () => BuildButtonPressed?.Invoke();
        _buildButton.MouseEntered += () => HoveredCommand = "build:drawer";
        _buildButton.MouseExited += () =>
        {
            if (HoveredCommand == "build:drawer") HoveredCommand = null;
        };
        row.AddChild(_buildButton);

        var recent = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        recent.AddThemeConstantOverride("separation", 4);
        recent.AddChild(HudKit.Label("RECENT", 11, Dim, condensed: true));
        var tiles = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        tiles.AddThemeConstantOverride("separation", 6);
        for (var i = 0; i < 3; i++)
        {
            var b = new Button { FocusMode = Control.FocusModeEnum.None, ToggleMode = true, CustomMinimumSize = new Vector2(100, 132), ClipContents = true, Visible = false };
            HudKit.StyleTile(b, 7);
            var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 5);
            box.AddThemeConstantOverride("separation", 3);
            var pic = HudKit.Picture(new Vector2(88, 74));
            var key = HudKit.Label("", 11, Dim, condensed: true);
            var plate = HudKit.PanelBox(new Color(0.02f, 0.03f, 0.07f, 0.8f), new Color(0, 0, 0, 0), 4, 4, 0);
            plate.AddChild(key);
            plate.Position = new Vector2(3, 3);
            pic.AddChild(plate);
            box.AddChild(pic);
            var name = HudKit.Label("", 12, Ink, bold: true);
            name.ClipText = true;
            box.AddChild(name);
            var cost = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            cost.AddThemeConstantOverride("h_separation", 6);
            cost.AddThemeConstantOverride("v_separation", 0);
            box.AddChild(cost);
            b.AddChild(box);
            var index = i;
            b.Pressed += () =>
            {
                if (_recentIds[index] is { } id) RecentPicked?.Invoke(id);
            };
            b.MouseEntered += () => HoveredCommand = _recentIds[index];
            b.MouseExited += () =>
            {
                if (HoveredCommand == _recentIds[index]) HoveredCommand = null;
            };
            tiles.AddChild(b);
            _recentTiles.Add((b, pic, name, key, cost, new List<HudKit.CostChip>()));
        }
        recent.AddChild(tiles);
        row.AddChild(recent);

        _commandGrid.GetParent().AddChild(row);
        return row;
    }
}
