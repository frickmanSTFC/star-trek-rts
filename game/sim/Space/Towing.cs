using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Space jobs, phase 2 (docs/space-jobs-contract.md "Phase 2"; data maps/sites.json: a kind's tow, tow.flight): the flagship
// takes a wreck in tow from alongside (the tractor must work), the wreck follows the ship (its position is the ship's every
// tick), flights take tow.flight longer, and the tow is dropped at a station (latinum) or a colony ship's colony body
// (research and duranium through storage), or released where it is. A tractor knocked out, the ship crippled or the wreck's
// reactor failing ends the tow. Hashed with the flagship's status (system games only).
public sealed partial class StarSystemState
{
    /// <summary>The player whose flagship has the site in tow (-1 = none).</summary>
    public int TowerOf(string? siteId)
    {
        if (siteId == null) return -1;
        for (var p = 0; p < _status.Count; p++)
            if (_status[p] is { } st && st.Tow.Length > 0 && st.Tow == siteId) return p;
        return -1;
    }

    /// <summary>The site the player's flagship has in tow, or null.</summary>
    public string? TowOf(int player) => Status(player) is { Tow.Length: > 0 } st ? st.Tow : null;

    /// <summary>A colony ship can drop tows (and dock) at its colony's body.</summary>
    private bool CanColonyDock(int player) => SurfaceBody != null && _sim?.CanColonize(player) == true && _sim.ShipChoiceOf(player) != null;

    private string? TowProblem(int player, SystemShip ship, ShipState st, SpaceSite site, SiteRecord r)
    {
        if (SiteKindOf(site)?.Tow == null) return "it can't be towed";
        if (r.Towed) return "towed in already";
        if (r.ReactorDone) return "its reactor failed: nothing left worth towing";
        if (st.Tow.Length > 0) return st.Tow == site.Id ? "in tow already" : $"towing {SiteNameFor(player, st.Tow)} already: drop or release it first";
        if (st.Escort.Length > 0) return $"escorting {SiteNameFor(player, st.Escort)}: stop the escort first";
        if (TowerOf(site.Id) >= 0) return "another ship has it in tow";
        if (!r.Alongside) return "come alongside first";
        if (IsDown(st, ShipSystem.Tractor)) return "the tractor is down: nothing can be towed";
        if (st.Away is { Done: false }) return "an away team is out";
        foreach (var s in _ships)
            if (s != ship && (s.AtSite == site.Id || s.ToSite == site.Id || s.FromSite == site.Id)) return "another ship is there or on its way";
        return null;
    }

    private void BeginTow(Simulation sim, int player, SpaceSite site)
    {
        var st = Status(player)!;
        st.Tow = site.Id;
        st.TowTick = sim.Tick;
        Version++;
        var f = SiteRules?.TowFlight ?? Fix64.One;
        ShipLog(player, $"{site.Name} taken in tow: flights {((f - Fix64.One) * Fix64.FromInt(100)).FloorToInt()}% slower", "good");
    }

    /// <summary>Lets the tow go where it is (it stays there; it can be taken in tow again).</summary>
    internal void ReleaseTow(int player, string why)
    {
        if (Status(player) is not { Tow.Length: > 0 } st) return;
        var name = Site(st.Tow)?.Name ?? "the tow";
        if (Record(player, st.Tow) is { } r) r.Alongside = false;
        st.Tow = "";
        st.TowTick = -1;
        Version++;
        ShipLog(player, $"{name} released: {why}", "warn");
    }

    /// <summary>Each tick, before the rest: every towed site sits where its ship is (a tow whose ship or site is gone ends).</summary>
    private void StepTows(Simulation sim)
    {
        for (var p = 0; p < _status.Count; p++)
        {
            if (_status[p] is not { Tow.Length: > 0 } st) continue;
            if (Site(st.Tow) is not { } site || FlagshipOf(p) is not { } ship)
            {
                st.Tow = "";
                st.TowTick = -1;
                Version++;
                continue;
            }
            site.Position = ShipPosition(ship, sim.Tick);
        }
    }

    private string? TowHoldProblem(int player, ShipState st, SystemShip ship, string action)
    {
        if (st.Tow.Length == 0 || Site(st.Tow) is not { } site) return "nothing in tow";
        switch (action)
        {
            case "drop":
            {
                var atStation = ship.AtSite is { } sid && Site(sid) is { Kind: SiteKind.Station };
                if (atStation) return null;
                if (CanColonyDock(player) && AtOwnColony(player, ship)) return null;
                return CanColonyDock(player) ? $"drop {site.Name} at a station or the colony's body" : $"drop {site.Name} at a station";
            }
            case "jettison":
                return null; // let it go
            case "sell":
                return $"drop {site.Name} at a station: it pays for its salvage";
        }
        return $"no hold action '{action}' for a tow";
    }

    private void ApplyTowHold(Simulation sim, int p, ShipState st, SystemShip ship, string action)
    {
        if (action == "jettison")
        {
            ReleaseTow(p, "cast off");
            return;
        }
        var site = Site(st.Tow)!;
        var def = SiteKindOf(site)!.Tow!;
        var r = RecordFor(p, site.Id);
        var atStation = ship.AtSite is { } sid && Site(sid) is { Kind: SiteKind.Station };
        st.Tow = "";
        st.TowTick = -1;
        r.Towed = true;
        r.Alongside = false;
        if (atStation)
        {
            var paid = GiveLatinum(sim, p, SystemGenerator.Int(SiteRoll(site, 600), def.LatinumMin, def.LatinumMax));
            AddTally(st, "tow:station", 1);
            ShipLog(p, $"{site.Name} handed over at {Site(ship.AtSite)!.Name} for salvage: {paid} latinum", "good");
        }
        else
        {
            var text = GiveSpaceResearch(sim, p, Fix64.FromInt(SystemGenerator.Int(SiteRoll(site, 601), def.ResearchMin, def.ResearchMax)));
            var duranium = 0L;
            if (sim.Surface is { } s && sim.Data.HasResource("duranium"))
                duranium = s.StoreTrade(sim, p, sim.Data.Resource("duranium").Index, Fix64.FromInt(SystemGenerator.Int(SiteRoll(site, 602), def.DuraniumMin, def.DuraniumMax))).FloorToInt();
            AddTally(st, "tow:colony", 1);
            ShipLog(p, $"{site.Name} broken up over the colony: {duranium} duranium{(text.Length > 0 ? $", {text}" : "")}", "good");
        }
        Spend(p, site, "towed in");
        site.ExpireTick = sim.Tick; // gone as soon as no ship holds it
        Version++;
    }
}
