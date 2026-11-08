using System;
using Godot;

namespace FinalFrontier;

// The top bar's trophy chip (docs/mockups/achievements): "9 / 30" earned with a trophy icon and the F3 key, a tooltip,
// and a click that opens the achievements screen. Built on first use just left of the mission clock; hidden while
// there is no achievement data.
public partial class PlanetHud
{
    private PanelContainer? _trophyChip;
    private Label _trophyText = null!;
    private StyleBoxFlat _trophyNormal = null!, _trophyHover = null!;

    /// <summary>The chip was clicked.</summary>
    public event Action? TrophyPressed;

    /// <summary>Shows the chip with "9 / 30"; null text hides it.</summary>
    public void ShowTrophy(string? text, string tooltip)
    {
        if (_trophyChip == null)
        {
            if (text == null) return;
            var row = _clock.GetParent();
            _trophyNormal = HudKit.Box(HudKit.Chip, HudKit.BtnEdge, 5, 9, 3);
            _trophyHover = HudKit.Box(new Color(0.08f, 0.12f, 0.21f), HudKit.HoverEdge, 5, 9, 3);
            _trophyChip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            _trophyChip.AddThemeStyleboxOverride("panel", _trophyNormal);
            var box = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", 7);
            box.AddChild(new TextureRect
            {
                Texture = Medals.Line("trophy", Ink, 32),
                CustomMinimumSize = new Vector2(17, 17),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            _trophyText = MakeLabel("", 15, Ink);
            _trophyText.AddThemeFontOverride("font", HudKit.Condensed(true));
            box.AddChild(_trophyText);
            var key = MakeLabel("F3", 11, Dim);
            key.AddThemeFontOverride("font", Medals.Mono());
            key.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            box.AddChild(key);
            _trophyChip.AddChild(box);
            _trophyChip.MouseEntered += () => _trophyChip.AddThemeStyleboxOverride("panel", _trophyHover);
            _trophyChip.MouseExited += () => _trophyChip.AddThemeStyleboxOverride("panel", _trophyNormal);
            _trophyChip.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    TrophyPressed?.Invoke();
                    _trophyChip.AcceptEvent();
                }
            };
            row.AddChild(_trophyChip);
            row.MoveChild(_trophyChip, _clock.GetIndex()); // just before the clock (the sky chip, when built, goes between)
            var gap = new Control { CustomMinimumSize = new Vector2(10, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddChild(gap);
            row.MoveChild(gap, _clock.GetIndex());
        }
        var show = text != null;
        if (_trophyChip.Visible != show) _trophyChip.Visible = show;
        if (!show) return;
        if (_trophyText.Text != text) _trophyText.Text = text;
        _trophyChip.TooltipText = tooltip;
    }
}
