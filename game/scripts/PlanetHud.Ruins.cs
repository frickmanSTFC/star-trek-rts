using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>One find in the Excavation Site's finds list.</summary>
public sealed record FindRow(string Kind, string Name, string Effect, string When, Color Colour);

// Civilizations phase 2 on the surface HUD (docs/civilizations-phase2-contract.md "View"): the sick-crew chip right of
// the base tabs (a released pathogen: CrewSickSeconds of the selected base), the finds list of the selected Excavation
// Site (a small panel above the bottom panel, bottom-left), and the trade-income line the driver adds to every resource
// chip's tooltip on the top bar (TradeTipLine).
public partial class PlanetHud
{
    private Button? _sickChip;
    private PanelContainer? _finds;
    private Label? _findsTitle;
    private VBoxContainer? _findsList;
    private string _findsKey = "";

    /// <summary>"Trade with natives: +3 Duranium a minute" for the top bar's resource tooltips ("" = none).</summary>
    public string TradeTipLine { get; set; } = "";

    private static readonly Color Sick = new(0.75f, 0.9f, 0.35f);

    /// <summary>The CREW SICK chip beside the base tabs (text null hides it). Call after ShowTools (it shares that row).</summary>
    public void ShowSickCrew(string? text, string tooltip)
    {
        if (_sickChip == null)
        {
            if (text == null) return;
            _sickChip = ToolChip("", "crew", "");
            _sickChip.AddThemeColorOverride("font_color", Sick);
            _sickChip.AddThemeColorOverride("font_hover_color", Sick);
            _tools.AddChild(_sickChip);
            _tools.MoveChild(_sickChip, 0);
        }
        var show = text != null;
        if (_sickChip.Visible != show) _sickChip.Visible = show;
        if (show)
        {
            if (_sickChip.Text != text) _sickChip.Text = text;
            _sickChip.TooltipText = tooltip;
            _toolsWanted = true;
        }
        UpdateTabRow();
    }

    /// <summary>The finds list beside the bottom panel (rows null or empty hides it). Newest first.</summary>
    public void ShowFinds(string? title, IReadOnlyList<FindRow>? rows)
    {
        if (title == null || rows == null || _orbitMode)
        {
            if (_finds != null && _finds.Visible) _finds.Visible = false;
            return;
        }
        if (_finds == null)
        {
            _finds = Panel(new Color(0.02f, 0.03f, 0.063f, 0.92f), Line, 8, 12, 8);
            _finds.MouseFilter = Control.MouseFilterEnum.Stop;
            _finds.CustomMinimumSize = new Vector2(330, 0);
            var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", 4);
            _findsTitle = MakeLabel("", 13, Accent);
            box.AddChild(_findsTitle);
            _findsList = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _findsList.AddThemeConstantOverride("separation", 3);
            box.AddChild(_findsList);
            _finds.AddChild(box);
            AddOverlay(_finds);
        }
        _finds.Visible = true;
        var key = title + "|" + string.Join("|", rows);
        if (key != _findsKey)
        {
            _findsKey = key;
            _findsTitle!.Text = title.ToUpperInvariant();
            foreach (var c in _findsList!.GetChildren()) c.QueueFree();
            if (rows.Count == 0) _findsList.AddChild(MakeLabel("Nothing yet: each dug layer turns something up.", 13, Dim));
            foreach (var r in rows)
            {
                var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddThemeConstantOverride("separation", 8);
                row.AddChild(new ColorRect { Color = r.Colour, CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
                var text = MakeLabel($"{r.Name}  ·  {r.Effect}", 13, Ink);
                text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                text.CustomMinimumSize = new Vector2(250, 0);
                text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(text);
                row.AddChild(MakeLabel(r.When, 12, Dim));
                _findsList.AddChild(row);
            }
            _finds.ResetSize();
        }
        var screen = GetViewport().GetVisibleRect().Size;
        var size = _finds.GetCombinedMinimumSize();
        _finds.Position = new Vector2(16, screen.Y - BottomHeight - size.Y - 12);
    }

    /// <summary>The finds list is under the mouse (added to BlocksMouse).</summary>
    private bool RuinsBlockMouse(Vector2 mouse) => _finds is { Visible: true } f && f.GetGlobalRect().HasPoint(mouse);
}
