using Godot;

namespace FinalFrontier;

// The helm's Starfleet Standing chip (civilizations phase 1, docs/civilizations-contract.md "View"): a small block in
// the top bar, left of the display buttons, in the colour of the Standing's rung ("STANDING 64"), tooltip = the five-step
// ladder with the current step. Shown only while the driver gives it text (Federation in a Prime Directive era).
// Clicking it raises ActionPicked("civ:dossier"). Rebuilt with the frame (Build), so the content lives in fields.
public partial class HelmScreen
{
    private string? _standingText;
    private Color _standingColour = Green;
    private string _standingTip = "";
    private PanelContainer? _standingChip;
    private Label? _standingLabel;
    private StyleBoxFlat? _standingStyle;

    /// <summary>The Standing chip: text ("STANDING 64"), the rung's colour and the ladder tooltip; null text hides it.</summary>
    public void SetStanding(string? text, Color colour, string tooltip)
    {
        text = text == null ? null : Clean(text).ToUpperInvariant();
        if (text == _standingText && colour == _standingColour && tooltip == _standingTip) return;
        _standingText = text;
        _standingColour = colour;
        _standingTip = tooltip;
        ApplyStanding();
    }

    private Control BuildStandingChip()
    {
        _standingStyle = Flat(Tone(_standingColour));
        _standingStyle.ContentMarginLeft = P(10);
        _standingStyle.ContentMarginRight = P(10);
        _standingChip = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        _standingChip.AddThemeStyleboxOverride("panel", _standingStyle);
        _standingLabel = Text("", F(_m.ButtonSize), Black, true);
        _standingLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _standingChip.AddChild(_standingLabel);
        var chip = _standingChip;
        chip.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            chip.AcceptEvent();
            ActionPicked?.Invoke("civ:dossier");
        };
        ApplyStanding();
        return _standingChip;
    }

    private void ApplyStanding()
    {
        if (_standingChip == null || _standingLabel == null || _standingStyle == null) return;
        _standingChip.Visible = _standingText != null;
        if (_standingText == null) return;
        _standingLabel.Text = _standingText;
        _standingStyle.BgColor = Tone(_standingColour);
        _standingChip.TooltipText = _standingTip;
    }
}
