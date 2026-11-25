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

    /// <summary>A pool's offers that were declined or lapsed come back (Locked, to be offered again when their trigger
    /// holds) once the chain's return_seconds have passed since they ended (0 = never).</summary>
    private void PoolOffersReturn(int p, PlayerBook book)
    {
        foreach (var run in book.Runs)
        {
            if (run.State != MissionState.Declined || run.End < 0 || Def(run) is not { } d || !InPool(d)) continue;
            if (Defs?.Chain(d.Chain) is not { ReturnSeconds: > 0 } chain || Now < run.End + (long)chain.ReturnSeconds * Simulation.TickRate) continue;
            Clear(p, run); // MissionBook.Debug.cs: back to a fresh Locked run
        }
    }

    /// <summary>Why an active pool job fails now, or null: the flagship is crippled, or the boarding of a site it is about
    /// was called off (the team recalled after the job started).</summary>
    private string? SpaceJobFailure(int p, MissionRun run)
    {
        if (Sys is not { } sys) return null;
        if (sys.IsCrippled(p)) return "the ship is crippled";
        foreach (var b in run.Bindings)
            if (b.Site != null && sys.AwayRecalledSince(p, b.Site, run.Start)) return "the away team was called back";
        // Phase 3: an event objective of the stage that can no longer come true (the rock struck, the comet left, the pods
        // ran out of air, the stranded ship was bait or lost, the storm dispersed).
        if (Def(run) is { } d && run.Stage >= 0 && run.Stage < d.Stages.Count)
        {
            var stage = d.Stages[run.Stage];
            for (var k = 0; k < stage.Objectives.Count && k < run.Done.Length; k++)
                if (!run.Done[k] && !stage.Objectives[k].Optional && EventLost(sys, p, run, stage.Objectives[k].Cond) is { } lost) return lost;
        }
        return null;
    }

    /// <summary>Why a main mission's stage can't be finished any more (phase 4): a required objective's space-site state
    /// was lost (StarSystemState.SiteStateLost); the stage then goes to its on_lost. Null otherwise (and always without a
    /// star system).</summary>
    private string? StageLost(int p, MissionRun run)
    {
        if (Sys is not { } sys || Def(run) is not { } d || run.Stage < 0 || run.Stage >= d.Stages.Count) return null;
        var stage = d.Stages[run.Stage];
        for (var k = 0; k < stage.Objectives.Count && k < run.Done.Length; k++)
            if (!run.Done[k] && !stage.Objectives[k].Optional && EventLost(sys, p, run, stage.Objectives[k].Cond) is { } lost) return lost;
        return null;
    }

    /// <summary>Why an event condition (space_site with an event state, alone or inside all) can't come true any more, or null.</summary>
    private static string? EventLost(StarSystemState sys, int p, MissionRun run, MissionCondDef c)
    {
        if (c.Kind == "all")
        {
            foreach (var child in c.Children)
                if (EventLost(sys, p, run, child) is { } why) return why;
            return null;
        }
        if (c.Kind != "space_site" || c.F("bind") != null || SiteRef(run, c.F("site")) is not { } id) return null;
        return c.F("state") is { } state && EventStates.Contains(state) ? sys.SiteStateLost(p, id, state, c.I("count", 1)) : null;
    }

    /// <summary>space_site states of the event sites (phase 3); the ones with a count compare it.</summary>
    private static readonly string[] EventStates = { "deflected", "sampled", "picked", "escorted", "recorded", "delivered" };

    /// <summary>The site a reference names (a site binding), or null.</summary>
    private static string? SiteRef(MissionRun? run, string? name) => name != null && run?.Find(name) is { Site: { } site } ? site : null;

    /// <summary>The space-job conditions, or null when the kind isn't one (then the space chains' run).</summary>
    private MissionEval? SpaceJobCheck(int p, MissionRun? run, int obj, MissionCondDef c, List<Binding>? bind)
    {
        if (c.Kind is not ("space_site" or "hull" or "crew" or "hold" or "space_tally")) return null;
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
                var state = c.F("state") ?? "identified";
                if (state is "picked" or "recorded") // counted: pods picked up, research recorded
                    return MissionEval.Count(sys.SiteWork(p, id) is { } w ? state == "picked" ? w.Picked : w.Recorded : 0, c.I("count", 1));
                if (state == "delivered") // phase 4: a mission's tow in orbit of that body (handed over by the hand_over action)
                    return MissionEval.Of(BodyRef(run, c.F("body")) is { } body && sys.SiteWork(p, id) is { } dw
                        && (dw.Delivered == body || (dw.Towing && sys.FlagshipOf(p)?.AtBody == body)));
                return MissionEval.Of(SiteState(sys, p, id, state));
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
                var n = st.Hold.Where(l => item == "survivors" ? l.Item.StartsWith(StarSystemState.SurvivorsPrefix, System.StringComparison.Ordinal)
                    : item == "ore" ? l.Item.StartsWith(StarSystemState.OrePrefix, System.StringComparison.Ordinal) : l.Item == item).Sum(l => l.Count);
                return MissionEval.Count(n, c.I("count", 1));
            }
            case "space_tally":
            {
                // What was sold / dropped / mined / towed in since the objective's stage began (the baseline).
                var since = run != null && obj >= 0 && obj < run.Base.Length ? run.Base[obj].FloorToInt() : 0;
                return MissionEval.Count(System.Math.Max(0, TallyOf(sys, p, c) - since), c.I("count", 1));
            }
        }
        return MissionEval.No;
    }

    /// <summary>A binding trigger's site: the lowest-numbered site of the kind(s) (and variant(s)) the player knows at least
    /// to the asked state, not worked out (unless spent: true), not bound by another of the player's offers or active jobs,
    /// and for a colony ship within its reach of the colony's body.</summary>
    private SpaceSite? TriggerSite(int p, MissionCondDef c)
    {
        if (Sys is not { } sys) return null;
        var kinds = new List<SiteKind>();
        foreach (var id in c.L("kinds").Append(c.F("kind") ?? ""))
            if (SpaceSiteRules.KindOf(id) is { } kind) kinds.Add(kind);
        if (kinds.Count == 0) return null;
        var want = c.F("state") == "identified" ? SiteKnowledge.Identified : SiteKnowledge.Detected;
        var variants = c.L("variants").ToList();
        if (c.F("variant") is { } one) variants.Add(one);
        var spentOk = c.F("spent") == "true"; // a worked-out wreck (tow it in) may be bound too
        foreach (var s in sys.Sites)
        {
            if (!kinds.Contains(s.Kind) || (variants.Count > 0 && !variants.Contains(s.Variant))) continue;
            var k = sys.SiteKnowledgeOf(p, s.Id);
            if (k < want || (k == SiteKnowledge.Spent && !spentOk)) continue;
            if (spentOk && (sys.SiteWork(p, s.Id) is { Towed: true } || sys.TowerOf(s.Id) is var tower and >= 0 && tower != p)) continue;
            if (k == SiteKnowledge.Spent && s.ExpireTick >= 0 && s.ExpireTick - Now < 60L * Simulation.TickRate) continue; // drifting off soon
            // Not held by another of the player's offers or running jobs (one that ended lets the site go: a boarded wreck can
            // still be towed in).
            if (Track(p).Runs.Any(r => r.State is MissionState.Offered or MissionState.Active && r.Bindings.Any(b => b.Site == s.Id))) continue;
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
            "mined" => w.MinedOut,
            "towing" => w.Towing,
            "towed" => w.Towed,
            "deflected" => w.Deflected,
            "sampled" => w.Sampled,
            "escorting" => w.Escorting,
            "escorted" => w.Rescued,
            "inspected" => w.Inspected,
            "decoded" => w.Decoded,
            "answered" => w.Answer.Length > 0,
            "answered_well" => w.AnsweredWell,
            _ => false,
        };
    }

    /// <summary>The space-job actions; false when the kind isn't one.</summary>
    private bool SpaceJobAct(int p, MissionRun run, MissionActionDef a)
    {
        if (a.Kind is not ("place_space_site" or "bind_site" or "damage" or "lose_crew" or "give_crew" or "hold_add" or "hand_over" or "civ")) return false;
        if (Sys is not { } sys || _sim == null) return true;
        switch (a.Kind)
        {
            case "place_space_site":
            {
                if (a.F("bind") is not { } name || SpaceSiteRules.KindOf(a.F("kind")) is not { } kind) return true;
                // Placed already (a stage entered again) unless again: true (a fresh site, e.g. after the last one was lost).
                if (a.F("again") != "true" && run.Find(name) is { Site: { } placed } && sys.Site(placed) != null) return true;
                var min = Fix64.FromInt(System.Math.Max(0, a.I("min", 0)));
                var max = Fix64.FromInt(System.Math.Max(1, a.I("max", 100)));
                var near = PlaceNear(p, run, a.F("near"), ref min, ref max);
                var site = sys.SpawnSite(kind, a.F("variant"), near, min, max, a.I("pods", 0), BodyRef(run, a.F("target")));
                if (site == null) return true;
                if (kind == SiteKind.HiddenShip) sys.MissionHunt(p, site.Id); // never detected: the player's scans find it
                else sys.MissionDetectSite(p, site.Id);
                if (kind == SiteKind.AlienProbe) sys.MissionFollow(p, site.Id);
                if (a.F("hold_room") == "true") sys.MissionHoldRoom(site.Id);
                if (a.F("cold_reactor") == "true") sys.MissionNoReactor(site.Id);
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
            case "hand_over":
                if (SiteRef(run, a.F("site")) is { } towed) sys.HandOver(p, towed);
                return true;
            case "civ":
                if (BodyRef(run, a.F("at")) is { } world)
                    sys.MissionCiv(_sim, p, world, a.I("awareness", 0), a.I("incident", 0), a.F("settle") == "true");
                return true;
        }
        return true;
    }

    /// <summary>Where a place_space_site goes round (phase 4 adds edge, belt and bindings): the flagship (ship, the default),
    /// the colony's body, the star with min / max moved past the outermost orbit (edge), the belt nearest the flagship (the
    /// outermost planet when there is none), or a body or site binding.</summary>
    private FixVec2 PlaceNear(int p, MissionRun run, string? near, ref Fix64 min, ref Fix64 max)
    {
        var sys = Sys!;
        var here = Flagship(p) is { } ship ? sys.ShipPosition(ship, Now) : FixVec2.Zero;
        switch (near)
        {
            case null:
            case "ship":
                return here;
            case "colony":
                return sys.Body(sys.SurfaceBody) is { } colony ? colony.Position : here;
            case "edge":
            {
                var edge = Fix64.Zero;
                foreach (var b in sys.Bodies)
                {
                    var d = (b.Parent != null && sys.Body(b.Parent) is { } parent ? parent.Position : b.Position).Length();
                    if (d > edge) edge = d;
                }
                min += edge;
                max += edge;
                return FixVec2.Zero;
            }
            case "belt":
            {
                BodyDef? best = null;
                var bestD = Fix64.Zero;
                foreach (var b in sys.Bodies)
                {
                    if (Data?.SystemGenerator is not { } g || !g.IsBelt(b.ClassId)) continue;
                    var d = FixVec2.Distance(b.Position, here);
                    if (best == null || d < bestD) (best, bestD) = (b, d);
                }
                if (best != null) return best.Position;
                foreach (var b in sys.Bodies) // no belt: the outermost planet
                    if (b.Parent == null && (best == null || b.Position.Length() > best.Position.Length())) best = b;
                return best?.Position ?? here;
            }
            default:
                if (BodyRef(run, near) is { } body && sys.Body(body) is { } bd) return bd.Position;
                if (SiteRef(run, near) is { } siteId && sys.Site(siteId) is { } s) return s.Position;
                return here;
        }
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
            case "hand_over":
            case "civ":
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

    /// <summary>A space_tally's count now: the sum over its keys of the player's tally (StarSystemState.Tally).</summary>
    private static int TallyOf(StarSystemState sys, int p, MissionCondDef c)
    {
        var n = 0;
        foreach (var k in c.L("keys")) n += sys.Tally(p, k);
        if (c.F("key") is { } key) n += sys.Tally(p, key);
        return n;
    }

    /// <summary>The baseline of a space-job condition when its stage begins (space_tally: the tally then).</summary>
    private Fix64 SpaceJobBaseline(int p, MissionCondDef c) =>
        c.Kind == "space_tally" && Sys is { } sys ? Fix64.FromInt(TallyOf(sys, p, c)) : Fix64.Zero;

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
