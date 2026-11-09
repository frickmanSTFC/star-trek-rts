using System;
using Godot;

namespace FinalFrontier;

// The top bar's Starfleet Standing chip (civilizations phase 1, docs/civilizations-contract.md "View"; owner: "Standing
// is a Federation-only top-bar chip"): "STANDING 64" with a swatch in the rung's colour, tooltip = the five-step ladder
// with the current step (CivText.StandingTooltip), a click opens the civilization dossier. Shown only for a Federation
// player in a Prime Directive era (the sim's StandingOf is non-null). Built on first use left of the TEST MODE tag.
public partial class PlanetHud
{
    private PanelContainer? _standingChip;
    private Label _standingText = null!;
    private ColorRect _standingSwatch = null!;
    private StyleBoxFlat _standingNormal = null!, _standingHover = null!;

    /// <summary>The Standing chip was clicked.</summary>
    public event Action? StandingPressed;

    /// <summary>Shows "STANDING 64" in the rung's colour with the ladder tooltip; null hides it.</summary>
    public void ShowStanding(int? standing, int rung, string tooltip)
    {
        if (_standingChip == null)
        {
            if (standing == null) return;
            var row = _testTag.GetParent();
            _standingNormal = HudKit.Box(HudKit.Chip, HudKit.BtnEdge, 5, 9, 3);
            _standingHover = HudKit.Box(new Color(0.08f, 0.12f, 0.21f), HudKit.HoverEdge, 5, 9, 3);
            _standingChip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            _standingChip.AddThemeStyleboxOverride("panel", _standingNormal);
            var box = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", 7);
            _standingSwatch = new ColorRect { CustomMinimumSize = new Vector2(6, 18), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddChild(_standingSwatch);
            _standingText = MakeLabel("", 15, Ink);
            _standingText.AddThemeFontOverride("font", HudKit.Condensed(true));
            box.AddChild(_standingText);
            _standingChip.AddChild(box);
            _standingChip.MouseEntered += () => _standingChip.AddThemeStyleboxOverride("panel", _standingHover);
            _standingChip.MouseExited += () => _standingChip.AddThemeStyleboxOverride("panel", _standingNormal);
            _standingChip.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
                StandingPressed?.Invoke();
                _standingChip.AcceptEvent();
            };
            row.AddChild(_standingChip);
            row.MoveChild(_standingChip, _testTag.GetIndex());
        }
        var show = standing != null;
        if (_standingChip.Visible != show) _standingChip.Visible = show;
        if (standing is not { } value) return;
        var colour = CivText.RungColour(rung);
        var text = $"STANDING {value}";
        if (_standingText.Text != text) _standingText.Text = text;
        _standingText.AddThemeColorOverride("font_color", rung == 0 ? Ink : colour);
        _standingSwatch.Color = colour;
        _standingChip.TooltipText = tooltip;
    }
}
