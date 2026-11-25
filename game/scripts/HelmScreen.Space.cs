using Godot;

namespace FinalFrontier;

// The helm's hull + crew chip (space jobs, docs/space-jobs-contract.md "VIEW"; the design page's "HULL 78% · CREW 38/40"):
// a dark block in the top bar, left of the Standing chip and the display buttons: HULL, the percentage in the band's
// colour, a small bar with the band marks (25 / 50 / 75), then CREW on duty / at the start. Tooltip = the band and its
// effect, knocked-out systems, injured / lost crew. Shown only while the driver gives it text (a flagship with a status:
// StarSystemState.StatusOf). Clicking it raises ActionPicked("space:status") (the ship status screen). Rebuilt with the
// frame (Build), so the content lives in fields.
public partial class HelmScreen
{
    private string? _hullText;
    private string _crewText = "";
    private float _hullFraction = 1f;
    private Color _hullColour = Green;
    private string _hullTip = "";
    private PanelContainer? _hullChip;
    private Label? _hullValue;
    private Label? _crewValue;
    private CivMeter? _hullBar;

    private static readonly Color Panel2 = Color.FromHtml("#11141d");

    /// <summary>The hull chip: hull text ("78%", null hides the chip), 0..1 for the bar, the band's colour, crew text
    /// ("38/40") and the tooltip.</summary>
    public void SetHull(string? hull, float fraction, Color colour, string crew, string tooltip)
    {
        if (hull == _hullText && Mathf.IsEqualApprox(fraction, _hullFraction) && colour == _hullColour && crew == _crewText && tooltip == _hullTip) return;
        _hullText = hull;
        _hullFraction = Mathf.Clamp(fraction, 0f, 1f);
        _hullColour = colour;
        _crewText = crew;
        _hullTip = tooltip;
        ApplyHull();
    }

    private Control BuildHullChip()
    {
        var style = Flat(Panel2);
        style.ContentMarginLeft = P(10);
        style.ContentMarginRight = P(10);
        _hullChip = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        _hullChip.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", P(6));
        row.AddChild(Text("HULL", F(12), GreyText, true));
        _hullValue = Text("", F(_m.ButtonSize), Ink, true);
        row.AddChild(_hullValue);
        _hullBar = new CivMeter { Marks = new[] { 0.25f, 0.5f, 0.75f }, CustomMinimumSize = new Vector2(P(46), P(7)), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(_hullBar);
        row.AddChild(Text("CREW", F(12), GreyText, true));
        _crewValue = Text("", F(_m.ButtonSize), Ink, true);
        row.AddChild(_crewValue);
        _hullChip.AddChild(row);
        var chip = _hullChip;
        chip.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            chip.AcceptEvent();
            ActionPicked?.Invoke("space:status");
        };
        ApplyHull();
        return _hullChip;
    }

    private void ApplyHull()
    {
        if (_hullChip == null || _hullValue == null || _crewValue == null || _hullBar == null) return;
        _hullChip.Visible = _hullText != null;
        if (_hullText == null) return;
        _hullValue.Text = Clean(_hullText);
        _hullValue.AddThemeColorOverride("font_color", Tone(_hullColour));
        _crewValue.Text = Clean(_crewText);
        _hullBar.Colour = Tone(_hullColour);
        _hullBar.Fraction = _hullFraction;
        _hullChip.TooltipText = _hullTip;
    }
}
