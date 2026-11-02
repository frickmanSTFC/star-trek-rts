using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Screen UI shared by every view: resource bar across the top (stock and income per minute for every
/// flow resource), an info panel bottom-left, a prompt/help panel bottom-right. Views supply the text.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _resources = null!;
    private Label _info = null!;
    private PanelContainer _infoPanel = null!;
    private Label _status = null!;
    private Label _banner = null!;
    private PanelContainer _bannerPanel = null!;

    public override void _Ready()
    {
        var top = Panel(out _resources, 17);
        top.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        AddChild(top);

        _bannerPanel = Panel(out _banner, 20);
        _bannerPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.Minsize, 60);
        _bannerPanel.GrowHorizontal = Control.GrowDirection.Both;
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_bannerPanel);

        _infoPanel = Panel(out _info, 15);
        _infoPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.Minsize, 12);
        _infoPanel.GrowVertical = Control.GrowDirection.Begin;
        _infoPanel.CustomMinimumSize = new Vector2(380, 0);
        AddChild(_infoPanel);

        var status = Panel(out _status, 13);
        status.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 12);
        status.GrowVertical = Control.GrowDirection.Begin;
        status.GrowHorizontal = Control.GrowDirection.Begin;
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(status);
    }

    public void ShowResources(Simulation sim, int localPlayer)
    {
        var data = sim.Data;
        var player = sim.Players[localPlayer];
        var income = sim.IncomePerMinute(localPlayer);
        var bar = new StringBuilder();
        bar.Append(data.Faction(player.FactionId).Name).Append("     ");
        foreach (var r in data.FlowResources)
        {
            bar.Append(r.Name).Append(' ').Append(Whole(player.Stock[r.Index]));
            var perMin = income[r.Index];
            if (perMin != Fix64.Zero) bar.Append(" (+").Append(Whole(perMin)).Append("/min)");
            bar.Append("     ");
        }
        _resources.Text = bar.ToString().TrimEnd();
    }

    /// <summary>Big prompt at the top centre; empty hides it.</summary>
    public void ShowBanner(string text)
    {
        _banner.Text = text;
        _bannerPanel.Visible = text.Length > 0;
    }

    /// <summary>Details of whatever is selected or hovered; empty hides the panel.</summary>
    public void ShowInfo(string text)
    {
        _info.Text = text;
        _infoPanel.Visible = text.Length > 0;
    }

    public void ShowStatus(string text) => _status.Text = text;

    public static string Whole(Fix64 v) => v.FloorToInt().ToString();

    private static PanelContainer Panel(out Label label, int fontSize)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.05f, 0.1f, 0.82f),
            BorderColor = new Color(0.96f, 0.65f, 0.14f, 0.7f),
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(6);
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", style);
        label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.88f, 0.92f, 1f));
        panel.AddChild(label);
        return panel;
    }
}
