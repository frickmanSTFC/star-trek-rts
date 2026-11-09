using System;
using Godot;

namespace FinalFrontier;

// The top bar's red KITS SHORT chip (owner 2026-11-09: "event line + HUD chip" when spare kits run out): shown while any
// of the local player's repairs waits for kits, with what is waiting in the tooltip; a click opens the inventory.
// Built on first use just left of the mission clock, like the trophy chip.
public partial class PlanetHud
{
    private PanelContainer? _kitsChip;
    private Label _kitsText = null!;

    /// <summary>The chip was clicked.</summary>
    public event Action? KitsShortPressed;

    /// <summary>Shows the chip ("KITS SHORT · 3"); null text hides it.</summary>
    public void ShowKitsShort(string? text, string tooltip)
    {
        if (_kitsChip == null)
        {
            if (text == null) return;
            var row = _clock.GetParent();
            var red = new Color(0.86f, 0.25f, 0.25f);
            _kitsChip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            _kitsChip.AddThemeStyleboxOverride("panel", HudKit.Box(new Color(0.18f, 0.05f, 0.07f), red, 5, 9, 3));
            _kitsText = MakeLabel("", 14, new Color(1f, 0.76f, 0.76f));
            _kitsText.AddThemeFontOverride("font", HudKit.Condensed(true));
            _kitsText.MouseFilter = Control.MouseFilterEnum.Ignore;
            _kitsChip.AddChild(_kitsText);
            _kitsChip.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    KitsShortPressed?.Invoke();
                    _kitsChip.AcceptEvent();
                }
            };
            row.AddChild(_kitsChip);
            row.MoveChild(_kitsChip, _clock.GetIndex());
            var gap = new Control { CustomMinimumSize = new Vector2(10, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddChild(gap);
            row.MoveChild(gap, _clock.GetIndex());
        }
        var show = text != null;
        if (_kitsChip.Visible != show) _kitsChip.Visible = show;
        if (!show) return;
        if (_kitsText.Text != text) _kitsText.Text = text;
        _kitsChip.TooltipText = tooltip;
    }
}
