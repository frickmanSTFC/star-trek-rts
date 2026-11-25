using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Space jobs, phase 4 (docs/space-jobs-contract.md "Phase 4"; data maps/sites.json hidden_ship and alien_probe): the
// chain-only sites that only a mission places (place_space_site), and the mission tweaks of the older kinds: lifepods whose
// survivors need hold room, a derelict whose reactor is cold, a rogue asteroid aimed at a named body, a tow handed over at
// a body. Hidden ship: long-range scans from different spots near it are fixes that narrow a search area until it is found
// (Detected); it may run once (by its hash), then come alongside and inspect. Alien probe: follows the flagship of the
// player whose mission placed it; decode it (Science, progress kept), then answer it: a three-step reply event on the away
// table machinery with no one leaving the ship; a good answer softens that player's later first contacts (Contact.cs reads
// ProbeAnsweredWell). Randomness: the site's hash; the reply's outcome on sim.Rng. Hashed with the space sites.
public sealed partial class StarSystemState
{
    /// <summary>Work actions of the chain-only kinds.</summary>
    private static bool IsChainAction(string action) => action is "decode" or "answer" or "inspect";

    private static bool IsChainKind(SiteKind kind) => GameData.ChainOnlyKinds.Contains(kind);

    // ---------------------------------------------------------------- missions place them

    /// <summary>A mission puts a hidden ship in the system for the player to hunt: a record (still Unknown) so the scans
    /// that miss can say why, and the search area shows.</summary>
    internal void MissionHunt(int player, string siteId)
    {
        if (player < 0 || player >= _status.Count || _status[player] == null || Site(siteId) == null) return;
        RecordFor(player, siteId);
        Version++;
    }

    /// <summary>A mission-placed alien probe follows this player's flagship.</summary>
    internal void MissionFollow(int player, string siteId)
    {
        if (Site(siteId) is not { Kind: SiteKind.AlienProbe } site) return;
        site.Follow = player;
        Version++;
    }

    /// <summary>A mission's lifepods: survivors who don't join the crew need room in the hold (the small hold means trips).</summary>
    internal void MissionHoldRoom(string siteId)
    {
        if (Site(siteId) is not { Kind: SiteKind.Lifepods } site) return;
        site.HoldRoom = true;
        Version++;
    }

    /// <summary>A mission's derelict whose reactor is cold: coming alongside starts no failure timer.</summary>
    internal void MissionNoReactor(string siteId)
    {
        if (Site(siteId) is not { } site) return;
        site.NoReactor = true;
        Version++;
    }

    /// <summary>A mission's tow handed over in orbit of a body (the player tows the site and is there): the tow ends, the
    /// owners take it (its reactor is theirs now), it is worked out and leaves. False when it isn't so.</summary>
    internal bool HandOver(int player, string siteId)
    {
        if (Status(player) is not { } st || st.Tow != siteId || FlagshipOf(player)?.AtBody is not { } body || Site(siteId) is not { } site) return false;
        var r = RecordFor(player, siteId);
        st.Tow = "";
        st.TowTick = -1;
        r.Towed = true;
        r.Alongside = false;
        r.Delivered = body;
        r.ReactorTick = -1;
        Spend(player, site, $"handed over at {Body(body)?.Name ?? body}");
        site.ExpireTick = Now; // gone as soon as no ship holds it
        Version++;
        return true;
    }

    /// <summary>A mission moves a living civilization (missions' civ action): awareness by points (clamped 0..100), an
    /// incident with a chance in percent (rolled on sim.Rng), settle = culture-shock unrest and the trade wait end now.
    /// Returns a line for the ship log ("" when nothing happened).</summary>
    internal string MissionCiv(Simulation sim, int player, string bodyId, int awareness, int incidentChance, bool settle)
    {
        if (CivAt(bodyId) is not { Living: true } site) return "";
        var parts = new List<string>();
        if (awareness != 0)
        {
            ShiftAwareness(bodyId, awareness);
            parts.Add(awareness < 0 ? "they trust us more" : "they are more wary of us");
        }
        if (settle && Rel(player, bodyId) is { } rel)
        {
            if (rel.TradeAfterTick > sim.Tick) rel.TradeAfterTick = sim.Tick;
            if (rel.UnrestTradeUntilTick > sim.Tick) rel.UnrestTradeUntilTick = sim.Tick;
            parts.Add("the unrest settles");
            Version++;
        }
        if (incidentChance > 0 && sim.Rng.NextInt(100) < incidentChance && CivData != null)
        {
            AddIncident(sim, player, bodyId, CivData.Post.StandingChange);
            parts.Add("an incident");
        }
        if (parts.Count == 0) return "";
        var line = $"The {site.Species} of {Body(bodyId)?.Name ?? bodyId}: {string.Join(", ", parts)}";
        ShipLog(player, line, parts.Contains("an incident") ? "warn" : "good");
        return line;
    }

    // ---------------------------------------------------------------- hidden ship: fixes, search area, running

    /// <summary>The search area of a hidden ship the player hasn't found yet: centre and radius (map units), shrinking with
    /// each fix; null once it is found, or for any other site. The centre is off the ship by the hash, so it gives nothing away.</summary>
    public (FixVec2 Centre, Fix64 Radius)? HiddenSearchArea(int player, string siteId)
    {
        if (Site(siteId) is not { Kind: SiteKind.HiddenShip } site || SiteKindOf(site)?.Hidden is not { } hd) return null;
        if (SiteKnowledgeOf(player, siteId) >= SiteKnowledge.Detected) return null;
        var fixes = Record(player, siteId)?.Fixes.Count ?? 0;
        var left = System.Math.Max(1, hd.Fixes - fixes);
        var radius = hd.Area * Fix64.FromInt(left) / Fix64.FromInt(hd.Fixes);
        var centre = site.Position + Direction(SiteRoll(site, 1500 + fixes)) * (radius * Fix64.Half);
        return (centre, radius);
    }

    /// <summary>Fixes the player has on a hidden ship (0 for any other site), and how many find it.</summary>
    public (int Fixes, int Need) HiddenFixes(int player, string siteId) =>
        Site(siteId) is { Kind: SiteKind.HiddenShip } site && SiteKindOf(site)?.Hidden is { } hd ? (Record(player, siteId)?.Fixes.Count ?? 0, hd.Fixes) : (0, 0);

    /// <summary>A long-range scan from here: a fix on every hidden ship within fix_range the player hasn't found, if the spot
    /// is spacing away from their earlier fixes on it; enough fixes find it (and it may run).</summary>
    private void FixHidden(int player, FixVec2 at)
    {
        for (var i = 0; i < _sites.Count; i++)
        {
            var site = _sites[i];
            if (site.Kind != SiteKind.HiddenShip || SiteKindOf(site)?.Hidden is not { } hd || SiteKnowledgeOf(player, site.Id) != SiteKnowledge.Unknown) continue;
            var hunting = Record(player, site.Id) != null; // a mission named it to the player: the misses are logged too
            if (FixVec2.Distance(at, site.Position) > hd.FixRange)
            {
                if (hunting) ShipLog(player, "No fix on the ship running dark: it isn't within range of this spot", "warn");
                continue;
            }
            var r = RecordFor(player, site.Id);
            var tooClose = false;
            foreach (var f in r.Fixes)
                if (FixVec2.Distance(f, at) < hd.Spacing) tooClose = true;
            if (tooClose)
            {
                ShipLog(player, $"No new fix: scan from a spot at least {hd.Spacing.FloorToInt()} units from the last ones", "warn");
                continue;
            }
            r.Fixes.Add(at);
            Version++;
            if (r.Fixes.Count < hd.Fixes)
            {
                var area = HiddenSearchArea(player, site.Id);
                ShipLog(player, $"Fix {r.Fixes.Count} of {hd.Fixes} on a ship running dark: search area down to {area?.Radius.FloorToInt() ?? 0} units", "good");
                continue;
            }
            SetKnowledge(player, site.Id, SiteKnowledge.Detected);
            ShipLog(player, $"Fix {r.Fixes.Count} of {hd.Fixes}: found it, a contact running dark", "good");
            MaybeRun(site, hd);
        }
    }

    /// <summary>Found, a hidden ship may run (once, by its hash): it moves run_distance away over run_seconds.</summary>
    private void MaybeRun(SpaceSite site, SiteHiddenDef hd)
    {
        if (site.PathStart >= 0 || (int)(SiteRoll(site, 1400) % 100) >= hd.RunChance || hd.RunDistance <= Fix64.Zero) return;
        var from = site.Position;
        var to = from + Direction(SiteRoll(site, 1401)) * hd.RunDistance;
        SetPath(site, from, to, Now, Now + (long)hd.RunSeconds * Simulation.TickRate);
        for (var p = 0; p < _status.Count; p++)
            if (SiteKnowledgeOf(p, site.Id) >= SiteKnowledge.Detected) ShipLog(p, $"The contact is running: it will stop in {hd.RunSeconds} s", "warn");
        Version++;
    }

    /// <summary>A hidden ship still running (it can't be come alongside until it stops).</summary>
    private bool Running(SpaceSite site) => site.Kind == SiteKind.HiddenShip && site.PathStart >= 0 && Now < site.PathEnd;

    // ---------------------------------------------------------------- the alien probe follows

    /// <summary>Each tick (after the tows): an alien probe moves toward its player's flagship until keep map units from it,
    /// and holds while the ship holds at it or once it has been answered.</summary>
    private void StepProbes(Simulation sim)
    {
        for (var i = 0; i < _sites.Count; i++)
        {
            var site = _sites[i];
            if (site.Kind != SiteKind.AlienProbe || site.Follow < 0 || SiteKindOf(site)?.Probe is not { } pr) continue;
            if (Record(site.Follow, site.Id) is { Answer.Length: > 0 }) continue;
            if (FlagshipOf(site.Follow) is not { } ship || ship.AtSite == site.Id) continue;
            var target = ShipPosition(ship, sim.Tick);
            var d = FixVec2.Distance(site.Position, target);
            if (d <= pr.Keep) continue;
            var step = Fix64.Min(pr.Speed / Fix64.FromInt(Simulation.TickRate), d - pr.Keep);
            site.Position += (target - site.Position).Normalized() * step;
        }
    }

    // ---------------------------------------------------------------- lifepods that need room

    /// <summary>The waiting pod with the least air (-1 = none).</summary>
    private int NextPod(SpaceSite site, SitePodsDef po)
    {
        var best = -1;
        var bestAir = long.MaxValue;
        for (var k = 0; k < site.PodCount && k < 31; k++)
        {
            if (((site.PodsTaken | site.PodsLost) & (1 << k)) != 0) continue;
            var air = PodAir(site, po, k);
            if (air < bestAir)
            {
                bestAir = air;
                best = k;
            }
        }
        return best;
    }

    /// <summary>Whether the next pod's survivors fit: on a mission's pods (hold_room) those who don't join the crew need room
    /// in the hold (survivors_per_container to a container, per division as the hold counts them).</summary>
    private bool PodFits(int player, ShipState st, SpaceSite site, SitePodsDef po)
    {
        if (!site.HoldRoom) return true;
        var best = NextPod(site, po);
        if (best < 0) return true;
        var n = PodSurvivors(site, po, best);
        var divisions = DivisionIds();
        var join = System.Math.Max(0, st.StartTotal - st.CrewTotal);
        var add = new int[divisions.Count];
        for (var j = 0; j < n && divisions.Count > 0; j++)
        {
            if (j < join) continue;
            add[(int)(SiteRoll(site, 1000 + best * 8 + j) % (ulong)divisions.Count)]++;
        }
        var more = 0;
        for (var d = 0; d < add.Length; d++)
        {
            if (add[d] == 0) continue;
            var item = SurvivorsPrefix + divisions[d];
            var have = Count(st, item);
            more += Containers(item, have + add[d]) - Containers(item, have);
        }
        return HoldUsed(player, st) + more <= st.HoldSize;
    }

    // ---------------------------------------------------------------- the card

    private void ChainRows(int player, SpaceSite site, SiteRecord r, List<SiteRow> rows)
    {
        var def = SiteKindOf(site);
        if (def?.Hidden is { } hd)
        {
            rows.Add(new SiteRow("FIXES", $"{r.Fixes.Count} of {hd.Fixes}: found", "good"));
            if (Running(site)) rows.Add(new SiteRow("RUNNING", $"stops in {SecondsUntil(site.PathEnd)} s", "warn"));
            rows.Add(new SiteRow("ALONGSIDE", r.Alongside ? "yes" : "no", r.Alongside ? "good" : ""));
            rows.Add(new SiteRow("INSPECTED", r.Inspected ? "yes" : $"no · {hd.InspectSeconds} s alongside", r.Inspected ? "good" : ""));
        }
        if (def?.Probe is { } pr)
        {
            rows.Add(new SiteRow("FOLLOWS", site.Follow == player ? "our ship" : site.Follow >= 0 ? "another ship" : "no one", site.Follow == player ? "warn" : ""));
            var need = DecodeNeed(player, pr);
            rows.Add(new SiteRow("DECODED", r.Decoded ? "yes" : $"{Percent(r.DecodeTicks, need)}% · Science, holding at it", r.Decoded ? "good" : ""));
            if (r.Answer.Length > 0)
                rows.Add(new SiteRow("ANSWER", pr.Good.Contains(r.Answer) ? "understood: first contact will go easier" : "sent, not understood", pr.Good.Contains(r.Answer) ? "good" : "warn"));
        }
    }

    /// <summary>Ticks of decoding the probe needs for the player (decode_seconds × the study_time lever).</summary>
    private long DecodeNeed(int player, SiteProbeDef pr) => System.Math.Max(1L, Scaled((long)pr.DecodeSeconds * Simulation.TickRate, Fit(player, FitOutRules.StudyTime)));

    private bool OffersChain(SpaceSite site, SiteRecord r, string action)
    {
        var def = SiteKindOf(site);
        return action switch
        {
            "inspect" => def?.Hidden != null && !r.Inspected,
            "decode" => def?.Probe != null && !r.Decoded,
            "answer" => def?.Probe != null && r.Decoded && r.Answer.Length == 0,
            _ => false,
        };
    }

    private static string ChainWorkLabel(string action) => action switch
    {
        "inspect" => "INSPECT HER",
        "decode" => "DECODE THE PATTERN",
        "answer" => "ANSWER IT",
        _ => action.ToUpperInvariant(),
    };

    private static string ChainWorkText(string action) => action switch
    {
        "inspect" => "Inspecting",
        "decode" => "Decoding the pattern",
        "answer" => "Answering",
        _ => action,
    };

    /// <summary>Why a chain action can't run (the common checks are done: at the site, identified, not crippled, not busy).</summary>
    private string? ChainWorkProblem(int player, ShipState st, SpaceSite site, SiteRecord r, string action)
    {
        var def = SiteKindOf(site);
        switch (action)
        {
            case "inspect":
                if (def?.Hidden == null) return "nothing to inspect";
                if (r.Inspected) return "inspected already";
                if (Running(site)) return "she is running: wait until she stops";
                return !r.Alongside ? "come alongside first" : null;
            case "decode":
                if (def?.Probe == null) return "nothing to decode";
                if (r.Decoded) return "decoded already";
                return OnDuty(st, "science") <= 0 ? "no Science crew on duty" : null;
            case "answer":
                if (def?.Probe is not { } pr) return "nothing to answer";
                if (!r.Decoded) return "decode the pattern first";
                if (r.Answer.Length > 0) return "answered already";
                if (st.Away is { Done: false }) return st.Away.Site == site.Id ? "answering it already" : "an away team is out";
                return StatusRules?.Table(pr.ReplyTable) == null ? "no way to answer it" : null;
        }
        return "no such work";
    }

    private void StartChainWork(Simulation sim, int player, ShipState st, SpaceSite site, SiteRecord r, string action)
    {
        var def = SiteKindOf(site)!;
        switch (action)
        {
            case "inspect":
                st.Work = new WorkRun
                {
                    Site = site.Id, Action = action, Start = sim.Tick,
                    End = sim.Tick + System.Math.Max(1L, Scaled((long)def.Hidden!.InspectSeconds * Simulation.TickRate, Fit(player, FitOutRules.AlongsideTime))),
                };
                ShipLog(player, $"Boarding party inspecting {site.Name}", "");
                return;
            case "decode":
                st.Work = new WorkRun { Site = site.Id, Action = action, Start = sim.Tick, End = sim.Tick + System.Math.Max(1L, DecodeNeed(player, def.Probe!) - r.DecodeTicks) };
                ShipLog(player, $"Decoding the pattern from {site.Name}", "");
                return;
            case "answer":
            {
                var table = StatusRules!.Table(def.Probe!.ReplyTable)!;
                var run = new AwayRun { Site = site.Id, StepStart = sim.Tick, StepEnd = sim.Tick + (long)(StatusRules?.StepSeconds ?? 60) * Simulation.TickRate };
                for (var k = 0; k < table.Steps.Count; k++) run.Picked.Add(-1);
                st.Away = run; // no one leaves the ship: an away run with no team (AwayTeam.cs)
                ShipLog(player, $"Composing an answer to {site.Name}", "");
                return;
            }
        }
    }

    /// <summary>One tick of a chain action; false when the action isn't one (the caller carries on).</summary>
    private bool StepChainWork(Simulation sim, int player, ShipState st, SpaceSite site, SiteRecord r, WorkRun w)
    {
        if (!IsChainAction(w.Action)) return false;
        var def = SiteKindOf(site);
        if (w.Action == "decode")
        {
            if (def?.Probe is not { } pr || OnDuty(st, "science") <= 0)
            {
                st.Work = null;
                Version++;
                return true;
            }
            r.DecodeTicks++;
            if (r.DecodeTicks < DecodeNeed(player, pr)) return true;
            st.Work = null;
            r.Decoded = true;
            Version++;
            ShipLog(player, $"The pattern from {site.Name} is decoded: it is asking a question. Answer it.", "good");
            return true;
        }
        if (sim.Tick < w.End) return true;
        st.Work = null;
        Version++;
        if (w.Action == "inspect" && def?.Hidden is { } hd)
        {
            r.Inspected = true;
            var paid = GiveLatinum(sim, player, SystemGenerator.Int(SiteRoll(site, 1410), hd.LatinumMin, hd.LatinumMax));
            var text = GiveSpaceResearch(sim, player, Fix64.FromInt(SystemGenerator.Int(SiteRoll(site, 1411), hd.ResearchMin, hd.ResearchMax)));
            ShipLog(player, $"{site.Name} inspected: contraband aboard, {paid} latinum in fines{(text.Length > 0 ? $", {text}" : "")}", "good");
            Spend(player, site, "inspected");
        }
        return true;
    }

    /// <summary>The reply to a probe is back (AwayTeam.cs Resolve): its outcome is the answer; the probe is worked out.</summary>
    private void ProbeAnswered(int player, SpaceSite site, AwayOutcomeDef outcome)
    {
        var r = RecordFor(player, site.Id);
        r.Answer = outcome.Id;
        var good = SiteKindOf(site)?.Probe?.Good.Contains(outcome.Id) == true;
        Spend(player, site, good ? "it understood our answer" : "it heard our answer");
        if (site.Follow == player) site.Follow = -1;
        Version++;
    }

    /// <summary>The player gave a good answer to an alien probe: their first contacts have the probe's shock points (Contact.cs).</summary>
    public bool ProbeAnsweredWell(int player)
    {
        if (player < 0 || player >= _status.Count || _status[player] is not { } st) return false;
        foreach (var r in st.Records)
            if (r.Answer.Length > 0 && Site(r.Site) is var s && (s == null || s.Kind == SiteKind.AlienProbe)
                && SiteRules?.Kind(SiteKind.AlienProbe)?.Probe is { } pr && pr.Good.Contains(r.Answer))
                return true;
        return false;
    }

    /// <summary>Culture-shock points a good answer to a probe takes off (0 without one).</summary>
    internal int ProbeShockPoints(int player) =>
        ProbeAnsweredWell(player) && SiteRules?.Kind(SiteKind.AlienProbe)?.Probe is { } pr ? pr.Shock : 0;
}
