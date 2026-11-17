using System;
using Godot;

namespace FinalFrontier;

// The HUD's part of the vehicle ping (PlanetDriver.Ping.cs): the PING button on the survey map's caption row, a place
// under the whole HUD for the ping's drawing, and where the vehicle list may stand.
public partial class PlanetHud
{
    private Button? _pingButton;

    /// <summary>Adds a driver-owned layer under everything in the HUD (the ping's beacons and arrows: over the 3D view,
    /// under the bars, panels and cards).</summary>
    public void AddUnderlay(Control underlay)
    {
        AddChild(underlay);
        MoveChild(underlay, 0);
    }

    /// <summary>Puts a small PING button at the right of the survey map's caption ("SURVEY MAP … PING [Z]").</summary>
    public void AddPingButton(string keyName, Action pressed)
    {
        if (_pingButton != null) return;
        // The minimap's column: caption, map, legend (BuildBottom).
        if (Minimap.GetParent() is not Control column || column.GetChildCount() == 0 || column.GetChild(0) is not Label caption) return;
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.RemoveChild(caption);
        caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        caption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(caption);
        _pingButton = new Button
        {
            Text = $"PING [{keyName}]",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 18),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = $"Find your vehicles: a beacon over each one for a few seconds, arrows to the ones off screen, and the vehicle list ({keyName}; twice quickly keeps the list open)",
        };
        _pingButton.AddThemeFontOverride("font", HudKit.Condensed(true));
        _pingButton.AddThemeFontSizeOverride("font_size", 11);
        var edge = new Color(PingOverlay.PingColour, 0.55f);
        _pingButton.AddThemeStyleboxOverride("normal", HudKit.Box(new Color(0.04f, 0.09f, 0.13f), edge, 4, 7, 0));
        _pingButton.AddThemeStyleboxOverride("hover", HudKit.Box(new Color(0.07f, 0.15f, 0.2f), PingOverlay.PingColour, 4, 7, 0));
        _pingButton.AddThemeStyleboxOverride("pressed", HudKit.Box(new Color(0.1f, 0.22f, 0.28f), PingOverlay.PingColour, 4, 7, 0));
        _pingButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _pingButton.AddThemeColorOverride("font_color", PingOverlay.PingColour);
        _pingButton.AddThemeColorOverride("font_hover_color", Colors.White);
        _pingButton.AddThemeColorOverride("font_pressed_color", Colors.White);
        Sfx.Tag(_pingButton, "none"); // the ping has its own sound
        _pingButton.Pressed += pressed;
        row.AddChild(_pingButton);
        column.AddChild(row);
        column.MoveChild(row, 0);
    }

    /// <summary>The left edge for the vehicle list above the bottom panel: clear of the dig's finds list when that shows.</summary>
    public float PingListLeft => _finds is { Visible: true } finds ? finds.Position.X + finds.Size.X + 8f : 16f;
}
