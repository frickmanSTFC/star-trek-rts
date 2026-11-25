using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Missions;

// Space jobs (docs/space-jobs-contract.md): the conditions and actions of missions about space sites and the flagship's
// status, and the side-job pool (rules.json chain "pool": true, data/missions/space_jobs.json) offered to every chain's
// players in a game with a star system. A colony ship (its chain isn't a space chain) is offered only sites within
// maps space_sites colony_reach_seconds of the colony's body. Randomness is the system's hash (never sim.Rng).
public sealed partial class MissionBook
{
    /// <summary>A mission of a side-job pool (offered to every chain in a game with a star system).</summary>
    private bool InPool(MissionDef d) => _sim?.System != null && Defs?.Chain(d.Chain) is { Pool: true };

    /// <summary>Why an active pool job fails now, or null: the flagship is crippled, or the boarding of a site it is about
    /// was called off (the team recalled after the job started).</summary>
    private string? SpaceJobFailure(int p, MissionRun run)
    {
        if (Sys is not { } sys) return null;
        if (sys.IsCrippled(p)) return "the ship is crippled";
        foreach (var b in run.Bindings)
            if (b.Site != null && sys.AwayRecalledSince(p, b.Site, run.Start)) return "the away team was called back";
        return null;
    }

    /// <summary>The site a reference names (a site binding), or null.</summary>
    private static string? SiteRef(MissionRun? run, string? name) => name != null && run?.Find(name) is { Site: { } site } ? site : null;

    /// <summary>The space-job conditions, or null when the kind isn't one (then the space chains' run).</summary>
    private MissionEval? SpaceJobCheck(int p, MissionRun? run, MissionCondDef c, List<Binding>? bind)
    {
        if (c.Kind is not ("space_site" or "hull" or "crew" or "hold")) return null;
        if (Sys is not { } sys) return MissionEval.No;
        switch (c.Kind)
        {
            case "space_site":
            {
                if (c.F("bind") is { } name)
                {
                    var site = TriggerSite(p, c);
                    if (site == null) return MissionEval.No;
                    bind?.Add(new Binding { Name = name, Site = site.Id, Text = site.Name });
                    return MissionEval.Yes;
                }
                if (SiteRef(run, c.F("site")) is not { } id) return MissionEval.No;
                return MissionEval.Of(SiteState(sys, p, id, c.F("state") ?? "identified"));
            }
            case "hull":
            {
                if (sys.StatusOf(p) is not { } st) return MissionEval.No;
                return MissionEval.Of(c.F("below") != null ? st.Hull < c.N("below", Fix64.Zero) : st.Hull >= c.N("at_least", Fix64.Zero));
            }
            case "crew":
            {
                if (sys.StatusOf(p) is not { } st) return MissionEval.No;
                var n = c.F("division") is { } div ? st.Crew.FirstOrDefault(x => x.Division == div)?.OnDuty ?? 0 : st.Crew.Sum(x => x.OnDuty);
                if (c.F("below") != null) return MissionEval.Of(n < c.I("below", 0));
                var need = c.I("at_least", 0);
                return new MissionEval(n >= need, Fix64.FromInt(n), need > 1 ? Fix64.FromInt(need) : Fix64.Zero);
            }
            case "hold":
            {
                if (sys.StatusOf(p) is not { } st || c.F("item") is not { } item) return MissionEval.No;
                var n = st.Hold.Where(l => item == "survivors" ? l.Item.StartsWith(StarSystemState.SurvivorsPrefix, System.StringComparison.Ordinal) : l.Item == item).Sum(l => l.Count);
                return MissionEval.Count(n, c.I("count", 1));
            }
        }
        return MissionEval.No;
    }

    /// <summary>A binding trigger's site: the lowest-numbered site of the kind (and variant) the player knows at least to
    /// the asked state, not worked out, not bound by another of the player's missions, and for a colony ship within its
    /// reach of the colony's body.</summary>
    private SpaceSite? TriggerSite(int p, MissionCondDef c)
    {
        if (Sys is not { } sys || SpaceSiteRules.KindOf(c.F("kind")) is not { } kind) return null;
        var want = c.F("state") == "identified" ? SiteKnowledge.Identified : SiteKnowledge.Detected;
        var variant = c.F("variant");
        foreach (var s in sys.Sites)
        {
            if (s.Kind != kind || (variant != null && s.Variant != variant)) continue;
            var k = sys.SiteKnowledgeOf(p, s.Id);
            if (k < want || k == SiteKnowledge.Spent) continue;
            if (Track(p).Runs.Any(r => r.State != MissionState.Locked && r.Bindings.Any(b => b.Site == s.Id))) continue;
            if (!SpaceChain && !InColonyReach(sys, p, s)) continue;
            return s;
        }
        return null;
    }

    /// <summary>A colony ship's side jobs stay within colony_reach_seconds of the colony's body.</summary>
    private bool InColonyReach(StarSystemState sys, int p, SpaceSite s)
    {
        if (sys.Body(sys.SurfaceBody) is not { } colony || Flagship(p) is not { } ship) return false;
        var reach = (long)(Data?.SpaceSites?.ColonyReachSeconds ?? 60) * Simulation.TickRate;
        return sys.TravelTicksFor(ship, colony.Position, s.Position) <= reach;
    }

    private static bool SiteState(StarSystemState sys, int p, string id, string state)
    {
        var k = sys.SiteKnowledgeOf(p, id);
        switch (state)
        {
            case "detected": return k >= SiteKnowledge.Detected;
            case "identified": return k >= SiteKnowledge.Identified;
            case "done": return k == SiteKnowledge.Spent;
            case "at": return sys.FlagshipOf(p)?.AtSite == id;
        }
        return sys.SiteWork(p, id) is { } w && state switch
        {
            "alongside" => w.Alongside,
            "powered" => w.Powered,
            "logs" => w.Logs,
            "boarded" => w.Boarded,
            "swept" => w.Swept,
            _ => false,
        };
    }

    /// <summary>The space-job actions; false when the kind isn't one.</summary>
    private bool SpaceJobAct(int p, MissionRun run, MissionActionDef a)
    {
        if (a.Kind is not ("place_space_site" or "bind_site" or "damage" or "lose_crew" or "give_crew" or "hold_add")) return false;
        if (Sys is not { } sys || _sim == null) return true;
        switch (a.Kind)
        {
            case "place_space_site":
            {
                if (a.F("bind") is not { } name || SpaceSiteRules.KindOf(a.F("kind")) is not { } kind) return true;
                if (run.Find(name) is { Site: { } placed } && sys.Site(placed) != null) return true; // placed already (a stage entered again)
                var near = a.F("near") == "colony" && sys.Body(sys.SurfaceBody) is { } colony ? colony.Position
                    : Flagship(p) is { } ship ? sys.ShipPosition(ship, Now) : FixVec2.Zero;
                var site = sys.SpawnSite(kind, a.F("variant"), near, Fix64.FromInt(System.Math.Max(0, a.I("min", 0))), Fix64.FromInt(System.Math.Max(1, a.I("max", 100))));
                if (site == null) return true;
                sys.MissionDetectSite(p, site.Id);
                run.Bind(new Binding { Name = name, Site = site.Id, Text = site.Name });
                return true;
            }
            case "bind_site":
            {
                if (a.F("as") is not { } name || SpaceSiteRules.KindOf(a.F("kind")) is not { } kind) return true;
                var known = sys.KnownSites(p).Where(s => s.Kind == kind && sys.SiteKnowledgeOf(p, s.Id) != SiteKnowledge.Spent).ToList();
                SpaceSite? pick = null;
                if (known.Count > 0)
                {
                    if (a.F("pick") == "random") pick = known[(int)(SpaceRoll(p, run.Id, name, 2) % (ulong)known.Count)];
                    else if (Flagship(p) is { } ship)
                    {
                        var at = sys.ShipPosition(ship, Now);
                        pick = known.OrderBy(s => FixVec2.Distance(s.Position, at)).ThenBy(s => s.Id, System.StringComparer.Ordinal).First();
                    }
                }
                run.Bind(new Binding { Name = name, Site = pick?.Id, Text = pick?.Name ?? "an unknown site" });
                return true;
            }
            case "damage":
                if (a.F("hull") != null) sys.Damage(_sim, p, a.N("hull", Fix64.Zero), Def(run)?.Title ?? "a mission");
                if (GameData.ShipSystemOf(a.F("system")) is { } system) sys.KnockOut(_sim, p, system, a.I("seconds", 60), Def(run)?.Title ?? "a mission");
                return true;
            case "lose_crew":
                sys.LoseCrew(p, Data!.FitOut?.DivisionIndex(a.F("division")) ?? -1, a.I("count", 1), Def(run)?.Title ?? "a mission");
                return true;
            case "give_crew":
                sys.GiveCrew(p, Data!.FitOut?.DivisionIndex(a.F("division")) ?? -1, a.I("count", 1), Def(run)?.Title ?? "a mission");
                return true;
            case "hold_add":
                if (a.F("item") is { } item) sys.MissionHoldAdd(p, item, a.I("count", 1));
                return true;
        }
        return true;
    }

    /// <summary>A space-job reward as paid ("" for none), or null when the kind isn't one.</summary>
    private string? SpaceJobReward(int p, MissionRun run, MissionActionDef a)
    {
        switch (a.Kind)
        {
            case "give_crew":
            case "hold_add":
            case "damage":
            case "lose_crew":
            case "place_space_site":
            case "bind_site":
                SpaceJobAct(p, run, a);
                return a.Kind switch
                {
                    "give_crew" => $"{a.I("count", 1)} crew join",
                    "hold_add" => $"{a.I("count", 1)} × {HoldItemText(a.F("item"))}",
                    _ => "",
                };
        }
        return null;
    }

    private string HoldItemText(string? item) =>
        item == StarSystemState.PatchKitItem ? "hull patch kit" : item == StarSystemState.MedicalItem ? "medical stores"
        : item != null && item.StartsWith(StarSystemState.SalvagePrefix, System.StringComparison.Ordinal)
            ? Data?.SpaceSites?.SalvageItem(item[StarSystemState.SalvagePrefix.Length..])?.Name ?? item : item ?? "";

    /// <summary>A site binding's placeholder: its name as the player knows it, or ".distance" = the flight there in seconds.</summary>
    private string SiteFill(int p, Binding b, string prop)
    {
        if (Sys is not { } sys || b.Site == null) return b.Text;
        if (prop == "distance") return sys.SiteFlightSeconds(p, b.Site) is var s and >= 0 ? s.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";
        return sys.Site(b.Site) != null ? sys.SiteNameFor(p, b.Site) : b.Text;
    }
}
