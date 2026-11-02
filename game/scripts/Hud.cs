using System.Collections.Generic;
using System.Linq;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Screen UI: resource bar across the top (stock and income per minute for every flow resource),
/// an info panel bottom-left for whatever is selected, and a status/help line bottom-right.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _resources = null!;
    private Label _info = null!;
    private PanelContainer _infoPanel = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        var top = Panel(out _resources, 17);
        top.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        AddChild(top);

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

    public void Show(Simulation sim, int localPlayer, Body? selectedBody, IReadOnlyCollection<int> selectedShips)
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

        var info = selectedBody != null ? DescribeBody(sim, selectedBody, localPlayer)
            : selectedShips.Count > 0 ? DescribeShips(sim, selectedShips)
            : "";
        _info.Text = info;
        _infoPanel.Visible = info.Length > 0;

        var seconds = sim.Tick / Simulation.TickRate;
        _status.Text =
            $"{sim.Map?.Name}  ·  {seconds / 60}:{seconds % 60:00}  ·  state {sim.StateHash():X16}\n" +
            "[Left-click] select ship or body  [Shift] add  [A] all ships  [Right-click] move\n" +
            "[WASD / arrows] pan  [Wheel] zoom  [H] home";
    }

    private static string DescribeBody(Simulation sim, Body body, int localPlayer)
    {
        var data = sim.Data;
        var cls = data.PlanetClass(body.ClassId);
        var sb = new StringBuilder();
        sb.AppendLine(body.Name);
        sb.AppendLine(cls.Name);
        sb.Append("Size ").Append(body.Size).Append("   Habitability ").Append(Whole(cls.Habitability * Fix64.FromInt(100))).AppendLine("%");
        if (body.ParentIndex >= 0) sb.Append("Moon of ").AppendLine(sim.Bodies[body.ParentIndex].Name);

        var deposits = data.Resources.Where(r => body.Deposits[r.Index] > Fix64.Zero)
            .Select(r => $"{r.Name} {Whole(body.Deposits[r.Index])}").ToList();
        sb.Append("Deposits: ").AppendLine(deposits.Count == 0 ? "none" : string.Join(" · ", deposits));

        var colony = body.ColonyId == 0 ? null : sim.FindColony(body.ColonyId);
        if (colony == null)
        {
            sb.Append("Unclaimed");
            return sb.ToString();
        }
        var owner = data.Faction(sim.Players[colony.Owner].FactionId).Name;
        sb.Append("Colony: ").Append(owner).AppendLine(colony.Owner == localPlayer ? " (you)" : "");
        foreach (var b in colony.Buildings)
        {
            var def = data.Building(b.DefId);
            sb.Append("  · ").Append(def.Name);
            if (def.OutputPerMinute.Count > 0)
                sb.Append("  ").Append(string.Join(", ", def.OutputPerMinute.Select(o => $"+{Whole(o.Amount)} {data.Resources[o.Resource].Name}"))).Append("/min");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string DescribeShips(Simulation sim, IReadOnlyCollection<int> ids)
    {
        var names = ids.Select(sim.FindShip).Where(s => s != null)
            .GroupBy(s => sim.Data.ShipClass(s!.ClassKey).Name)
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
        return $"{ids.Count} ship{(ids.Count == 1 ? "" : "s")} selected\n{string.Join(", ", names)}\nRight-click to move.";
    }

    private static string Whole(Fix64 v) => v.FloorToInt().ToString();

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
