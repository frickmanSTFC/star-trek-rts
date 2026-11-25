using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Space;
using Godot;

namespace FinalFrontier;

// Space jobs phase 2, view side (docs/space-jobs-contract.md "Phase 2"; render-only, commands only through SpaceOrders after
// the sim's own problem check): a neutral freighter's or tanker's ship card says it buys ore and, when our flagship is in
// orbit with it and has ore aboard, offers SELL ORE TO <NAME> ("sys:sellore:<ship id>": every ore line, through
// ShipHoldCommand "sell:<ship id>"). Ore and tow lines in the ship status hold come from StatusPage (PlanetDriver.Sites.cs).
public partial class PlanetDriver
{
    /// <summary>A ship card's ore-trade rows and button (only for a neutral ship of a trader role).</summary>
    private void OreTradeCard(SystemShip s, List<HelmRow> rows, List<HelmAction> buttons)
    {
        if (_sim.System is not { } sys || !s.IsNeutral || _data.SpaceSites is not { } rules || !rules.TraderRoles.Contains(s.Role)) return;
        var share = Mathf.RoundToInt(rules.TraderShare.ToFloat() * 100f);
        rows.Add(new HelmRow("BUYS", $"ore, at {share}% of the station's price", SystemPlot.SiteAmber));
        var ore = _status?.Hold.Where(l => l.Item.StartsWith(StarSystemState.OrePrefix, System.StringComparison.Ordinal)).ToList();
        if (ore is not { Count: > 0 }) return;
        var units = ore.Sum(l => l.Count);
        var value = ore.Sum(l => sys.OreValue(l.Item, l.Count, true));
        var problem = sys.BuysOre(LocalPlayer, s.Id) ? null : $"in orbit of the same body as the {s.Name} to trade";
        buttons.Add(new HelmAction($"sys:sellore:{s.Id}", "", $"SELL ORE TO {s.Name.ToUpperInvariant()}", problem == null,
            Colour: SystemPlot.SiteAmber, Tip: problem ?? $"{units} ore for about {value} latinum"));
    }

    /// <summary>"sys:sellore:&lt;ship&gt;": every ore line in the hold to that ship. True = handled.</summary>
    private bool OnOreSaleAction(string id, long when)
    {
        if (!id.StartsWith("sys:sellore:")) return false;
        if (!int.TryParse(id[12..], out var buyer) || _sim.System is not { } sys || Flagship is not { } f || _status == null) return true;
        var lines = _status.Hold.Where(l => l.Item.StartsWith(StarSystemState.OrePrefix, System.StringComparison.Ordinal)).ToList();
        string? problem = lines.Count == 0 ? "no ore in the hold" : null;
        var sold = 0;
        foreach (var l in lines)
        {
            problem = SpaceOrders.Hold(_sim, LocalPlayer, f.Id, $"sell:{buyer}", l.Item, l.Count, when);
            if (problem != null) break;
            sold += l.Count;
        }
        if (problem != null && sold == 0) _hud.PushEvent($"Can't do that: {problem}", Warn);
        else
        {
            Sfx.Ui("confirm");
            _hud.PushEvent($"Selling {sold} ore to the {sys.FindShip(buyer)?.Name ?? "freighter"}", HelmScreen.Cyan);
            _spacePollAt = -1;
        }
        return true;
    }
}
