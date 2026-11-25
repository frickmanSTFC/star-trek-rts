using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Space jobs, phase 1 (docs/space-jobs-contract.md; data maps/ship_status.json away_team): the away-team event, shaped
// like first contact (three timed steps, the game never pauses, a step that runs out takes its first choice), then the
// team comes back aboard and the outcome is rolled once on sim.Rng, then each member rolls. Leaving the site recalls the
// team (the boarding fails, no reward). Hashed with the flagship's status.

/// <summary>One choice at an away-team step: Label, Hint, and how it moves the odds (percentage points per person for
/// injury and loss; find = chance of a good find).</summary>
public sealed record AwayChoice(string Label, string Hint, int Injury, int Loss, int Find, int Seconds);

/// <summary>A member of the team: Name ("Lt. Okonjo"), Role ("pilot"), Division id.</summary>
public sealed record AwayMember(string Name, string Role, string Division);

/// <summary>A running (or just finished) away-team event. Step 0-based; Picked = choice per finished step (-1 none);
/// odds now in percent; Outcome/Result once done.</summary>
public sealed record AwayTeamView(string SiteId, int Player, int Step, int Steps, string StepTitle, IReadOnlyList<AwayChoice> Choices,
    IReadOnlyList<int> Picked, long StepStartTick, long StepEndTick, IReadOnlyList<AwayMember> Team, int InjuryPct, int LossPct,
    int FindPct, bool Done, string? Outcome, string? Result);

public sealed partial class StarSystemState
{
    private const ulong AwaySalt = 0xA_3A7_0001UL;

    /// <summary>Seconds a finished event stays readable (AwayTeamOf) before it is cleared.</summary>
    public const int AwayResultSeconds = 30;

    private sealed class AwayRun
    {
        public string Site = "";
        public int Step;
        public long StepStart, StepEnd, ReturnTick = -1, ShowUntil = -1;
        public readonly List<int> Picked = new();
        public readonly List<(string Name, string Role, int Division)> Team = new();
        public bool Done;
        public string Outcome = "", Result = "";

        public void AddToHash(ref StateHasher h)
        {
            h.Add(Site);
            h.Add(Step);
            h.Add(StepStart);
            h.Add(StepEnd);
            h.Add(ReturnTick);
            h.Add(ShowUntil);
            h.Add(Picked.Count);
            foreach (var p in Picked) h.Add(p);
            h.Add(Team.Count);
            foreach (var (n, r, d) in Team)
            {
                h.Add(n);
                h.Add(r);
                h.Add(d);
            }
            h.Add(Done);
            h.Add(Outcome);
            h.Add(Result);
        }
    }

    // ---------------------------------------------------------------- the view

    /// <summary>The player's running away-team event (or the last one for a short while after it ends), or null.</summary>
    public AwayTeamView? AwayTeamOf(int player)
    {
        if (Status(player)?.Away is not { } run || AwayTableAt(run.Site) is not { } table) return null;
        var (injury, loss, find) = Odds(player, run, table);
        var steps = table.Steps.Count;
        var returning = run.Step >= steps;
        var choices = returning || run.Done ? new List<AwayChoice>()
            : table.Steps[run.Step].Choices.Select(c => new AwayChoice(c.Label, c.Hint, c.Injury, c.Loss, c.Find, c.Seconds)).ToList();
        var ids = DivisionIds();
        return new AwayTeamView(run.Site, player, run.Step, steps, run.Done ? "Back aboard" : returning ? "Coming back aboard" : table.Steps[run.Step].Title,
            choices, run.Picked.ToList(), run.StepStart, run.StepEnd,
            run.Team.Select(m => new AwayMember(m.Name, m.Role, m.Division >= 0 && m.Division < ids.Count ? ids[m.Division] : "")).ToList(),
            injury, loss, find, run.Done, run.Done ? run.Outcome : null, run.Done ? run.Result : null);
    }

    /// <summary>The people a team of this many per division would be (the names the next team gets), for the picker.</summary>
    public IReadOnlyList<AwayMember> AwayTeamPreview(int player, IReadOnlyList<int> crew)
    {
        var list = new List<AwayMember>();
        if (Status(player) is not { } st || crew == null) return list;
        var ids = DivisionIds();
        foreach (var (n, r, d) in PickTeam(player, st, crew)) list.Add(new AwayMember(n, r, d < ids.Count ? ids[d] : ""));
        return list;
    }

    /// <summary>Why an away team can't go to the site now (or with that crew), or null.</summary>
    public string? AwayTeamProblem(int player, string siteId, IReadOnlyList<int>? crew)
    {
        if (Status(player) is not { } st || FlagshipOf(player) is not { } ship) return "no ship to send a team from";
        if (Site(siteId) is not { } site || SiteKnowledgeOf(player, siteId) == SiteKnowledge.Unknown) return "no such site";
        var def = SiteKindOf(site);
        if (def is not { AwayTable.Length: > 0 } || AwayTableAt(siteId) == null) return "nothing there to board";
        if (StarfleetHoldProblem(player) is { } held) return held;
        if (ship.AtSite != siteId) return $"the ship must be at {SiteNameFor(player, siteId)}";
        if (SiteKnowledgeOf(player, siteId) < SiteKnowledge.Identified) return "scan it first";
        if (SiteKnowledgeOf(player, siteId) == SiteKnowledge.Spent) return $"{site.Name} is worked out";
        if (st.Crippled) return "the ship is crippled";
        if (def.Hazardous && !HazardousOk(st)) return "the hull is critical: no hazardous jobs until it is repaired";
        var r = Record(player, siteId);
        if (r is not { Alongside: true }) return "come alongside first";
        if (r.Boarded) return "boarded already";
        if (st.Away is { Done: false }) return "an away team is out already";
        if (st.Work != null) return $"busy: {WorkText(st.Work.Action).ToLowerInvariant()}";
        var max = MaxAwayTeam(player);
        var min = StatusRules?.AwayMin ?? 2;
        if (max < min) return $"not enough crew on duty for a team of {min}";
        if (crew == null) return null;
        if (crew.Count != st.OnDuty.Length) return $"one number for each of the {st.OnDuty.Length} divisions";
        var total = 0;
        for (var i = 0; i < crew.Count; i++)
        {
            if (crew[i] < 0) return "a count can't be below 0";
            if (crew[i] > st.OnDuty[i]) return $"only {st.OnDuty[i]} of the {DivisionName(i)} crew are on duty";
            total += crew[i];
        }
        if (total < min) return $"a team needs at least {min}";
        if (total > max) return $"a team of {max} at most from this ship";
        return null;
    }

    /// <summary>Why the choice can't be picked now, or null.</summary>
    public string? AwayChoiceProblem(int player, string siteId, int choice)
    {
        if (Status(player)?.Away is not { Done: false } run || run.Site != siteId) return "no away team out there";
        if (AwayTableAt(siteId) is not { } table || run.Step >= table.Steps.Count) return "the team is coming back aboard";
        return choice < 0 || choice >= table.Steps[run.Step].Choices.Count ? "no such choice" : null;
    }

    /// <summary>The largest team the ship can send (half its fit crew, at most 6; the Armstrong 2).</summary>
    public int MaxAwayTeam(int player)
    {
        if (Status(player) is not { } st || StatusRules is not { } rules) return 0;
        var onDuty = st.OnDuty.Sum();
        var max = System.Math.Min(rules.AwayMax, (Fix64.FromInt(onDuty) * rules.AwayMaxShare).FloorToInt());
        foreach (var (ship, cap) in rules.AwayMaxByShip)
            if (ship == st.ShipKey) max = System.Math.Min(max, cap);
        return System.Math.Max(0, System.Math.Min(max, onDuty - 1)); // someone stays aboard
    }

    private AwayTableDef? AwayTableAt(string? siteId) =>
        Site(siteId) is { } site && SiteKindOf(site) is { AwayTable.Length: > 0 } def ? StatusRules?.Table(def.AwayTable) : null;

    /// <summary>The odds now (percent per person: injury, loss; find), from the base, a dark known derelict, the picked
    /// choices, the Defense members (defense_loss each) and the away_loss lever.</summary>
    private (int Injury, int Loss, int Find) Odds(int player, AwayRun run, AwayTableDef table)
    {
        var injury = table.Injury;
        var loss = table.Loss;
        var find = table.Find;
        if (Site(run.Site) is { Kind: SiteKind.KnownDerelict } && Record(player, run.Site) is not { Powered: true })
        {
            injury += table.UnpoweredInjury;
            loss += table.UnpoweredLoss;
            find += table.UnpoweredFind;
        }
        for (var k = 0; k < run.Picked.Count && k < table.Steps.Count; k++)
            if (run.Picked[k] >= 0 && run.Picked[k] < table.Steps[k].Choices.Count)
            {
                var c = table.Steps[k].Choices[run.Picked[k]];
                injury += c.Injury;
                loss += c.Loss;
                find += c.Find;
            }
        var defense = DivisionIndex("defense");
        loss -= (StatusRules?.DefenseLoss ?? 0) * run.Team.Count(m => m.Division == defense);
        var fit = Fit(player, FitOutRules.AwayLoss);
        if (fit != Fix64.One && loss > 0) loss = (Fix64.FromInt(loss) * fit + Fix64.Half).FloorToInt();
        return (System.Math.Clamp(injury, 0, 100), System.Math.Clamp(loss, 0, 100), System.Math.Clamp(find, 0, 100));
    }

    /// <summary>Who goes: names and roles from the system's hash of the player and the team's number (the same picks
    /// always give the same people; names don't repeat within a team).</summary>
    private List<(string Name, string Role, int Division)> PickTeam(int player, ShipState st, IReadOnlyList<int> crew)
    {
        var list = new List<(string, string, int)>();
        if (StatusRules is not { } rules || rules.Names.Count == 0 || rules.Ranks.Count == 0) return list;
        var ids = DivisionIds();
        var serial = (long)player * 1_000_000 + st.TeamsSent * 64L;
        var start = (int)(Hash(AwaySalt, serial) % (ulong)rules.Names.Count);
        var k = 0;
        for (var d = 0; d < crew.Count && d < ids.Count; d++)
            for (var j = 0; j < crew[d] && j < 64; j++, k++)
            {
                var name = rules.Names[(start + k) % rules.Names.Count];
                var rank = rules.Ranks[(int)(Hash(AwaySalt, serial + 1 + k) % (ulong)rules.Ranks.Count)];
                var roles = rules.RolesOf(ids[d]);
                var role = roles.Count > 0 ? roles[(int)(Hash(AwaySalt ^ 0x77, serial + k) % (ulong)roles.Count)] : ids[d];
                list.Add(($"{rank} {name}", role, d));
            }
        return list;
    }

    // ---------------------------------------------------------------- commands

    private void ApplyAwayTeam(Simulation sim, AwayTeamCommand c)
    {
        var ship = FindShip(c.ShipId);
        if (ship == null || ship.Owner != c.Player || c.Player < 0 || FlagshipOf(c.Player) != ship)
        {
            sim.Refuse(c, ship == null ? "no such ship" : "not your flagship");
            return;
        }
        if (AwayTeamProblem(c.Player, c.SiteId, c.Crew) is { } problem)
        {
            sim.Refuse(c, problem);
            return;
        }
        var st = Status(c.Player)!;
        var table = AwayTableAt(c.SiteId)!;
        var run = new AwayRun { Site = c.SiteId, StepStart = sim.Tick, StepEnd = sim.Tick + (long)(StatusRules?.StepSeconds ?? 60) * Simulation.TickRate };
        run.Team.AddRange(PickTeam(c.Player, st, c.Crew));
        for (var k = 0; k < table.Steps.Count; k++) run.Picked.Add(-1);
        st.TeamsSent++;
        st.Away = run;
        Version++;
        ShipLog(c.Player, $"Away team of {run.Team.Count} crossing to {Site(c.SiteId)!.Name}", "");
    }

    private void ApplyAwayChoice(Simulation sim, AwayTeamChoiceCommand c)
    {
        if (AwayChoiceProblem(c.Player, c.SiteId, c.Choice) is { } problem)
        {
            sim.Refuse(c, problem);
            return;
        }
        var run = Status(c.Player)!.Away!;
        run.Picked[run.Step] = c.Choice;
        AdvanceAway(sim, run, AwayTableAt(run.Site)!);
    }

    private void AdvanceAway(Simulation sim, AwayRun run, AwayTableDef table)
    {
        run.Step++;
        run.StepStart = sim.Tick;
        Version++;
        if (run.Step < table.Steps.Count)
        {
            run.StepEnd = sim.Tick + (long)(StatusRules?.StepSeconds ?? 60) * Simulation.TickRate;
            return;
        }
        var seconds = StatusRules?.ReturnSeconds ?? 20;
        for (var k = 0; k < run.Picked.Count && k < table.Steps.Count; k++)
            if (run.Picked[k] >= 0 && run.Picked[k] < table.Steps[k].Choices.Count) seconds += table.Steps[k].Choices[run.Picked[k]].Seconds;
        run.ReturnTick = sim.Tick + System.Math.Max(5, seconds) * (long)Simulation.TickRate;
        run.StepEnd = run.ReturnTick;
    }

    // ---------------------------------------------------------------- ticks

    private void StepAwayTeam(Simulation sim, int player, ShipState st)
    {
        if (st.Away is not { } run) return;
        if (run.Done)
        {
            if (sim.Tick >= run.ShowUntil)
            {
                st.Away = null;
                Version++;
            }
            return;
        }
        if (AwayTableAt(run.Site) is not { } table || FlagshipOf(player)?.AtSite != run.Site)
        {
            RecallAwayTeam(sim, player, "the ship is not alongside");
            return;
        }
        if (run.Step < table.Steps.Count)
        {
            if (sim.Tick < run.StepEnd) return;
            run.Picked[run.Step] = 0; // a step that runs out takes its first choice
            AdvanceAway(sim, run, table);
            return;
        }
        if (sim.Tick >= run.ReturnTick) Resolve(sim, player, st, run, table, -1);
    }

    /// <summary>The test panel ends the event now: steps not picked take their first choice; outcome index (-1 = rolled).</summary>
    private void FinishAwayTeam(Simulation sim, int player, int outcome)
    {
        if (Status(player) is not { Away: { Done: false } run } st || AwayTableAt(run.Site) is not { } table) return;
        for (var k = 0; k < run.Picked.Count; k++)
            if (run.Picked[k] < 0) run.Picked[k] = 0;
        run.Step = table.Steps.Count;
        Resolve(sim, player, st, run, table, outcome);
    }

    /// <summary>The team is back: the outcome (one roll, weights shifted by the choices), the find, then each member's
    /// loss (a medic turns medic_share of them into injuries) and injury; the site is boarded and worked out.</summary>
    private void Resolve(Simulation sim, int player, ShipState st, AwayRun run, AwayTableDef table, int forced)
    {
        var rules = StatusRules!;
        var site = Site(run.Site);
        var siteName = site?.Name ?? "the wreck";
        for (var k = 0; k < run.Picked.Count; k++)
            if (run.Picked[k] < 0) run.Picked[k] = 0;
        var (injury, loss, find) = Odds(player, run, table);
        var outcome = forced >= 0 && forced < table.Outcomes.Count ? table.Outcomes[forced] : RollOutcome(sim, run, table);
        var lines = new List<string> { outcome.Text };

        // The find: what the last step's choice brings back.
        var findPct = (Fix64.FromInt(find) * outcome.FindMult).FloorToInt();
        var last = table.Steps.Count > 0 ? table.Steps[^1].Choices[System.Math.Clamp(run.Picked[^1], 0, table.Steps[^1].Choices.Count - 1)] : null;
        if (last != null)
        {
            if (sim.Rng.NextInt(100) < findPct)
            {
                if (last.BringResearch && site != null)
                {
                    var text = GiveSpaceResearch(sim, player, Fix64.FromInt(SiteResearch(site)));
                    if (text.Length > 0) lines.Add(text);
                }
                if (last.BringSalvage.Length > 0)
                {
                    var item = SalvageItemId(last.BringSalvage);
                    var name = SiteRules?.SalvageItem(last.BringSalvage)?.Name ?? last.BringSalvage;
                    if (Fits(st, item, 1))
                    {
                        Add(st, item, 1);
                        lines.Add($"brought back: {name}");
                    }
                    else lines.Add($"{name} left behind: the hold is full");
                }
                if (last.BringSurvivorsMax > 0)
                {
                    if (sim.Rng.NextInt(100) < last.BringChance)
                    {
                        var n = sim.Rng.NextInt(last.BringSurvivorsMin, last.BringSurvivorsMax + 1);
                        var ids = DivisionIds();
                        for (var k = 0; k < n && ids.Count > 0; k++) TakeSurvivor(sim, player, ids[sim.Rng.NextInt(ids.Count)], siteName);
                        lines.Add(n == 0 ? "no one left alive" : $"{n} survivor{(n == 1 ? "" : "s")} brought aboard");
                    }
                    else lines.Add("the life reading was nothing");
                }
            }
            else lines.Add("came back empty-handed");
        }
        if (outcome.Research && site != null)
        {
            var text = GiveSpaceResearch(sim, player, Fix64.FromInt(SiteResearch(site)));
            if (text.Length > 0) lines.Add(text);
        }
        if (outcome.Hull > 0) Damage(sim, player, Fix64.FromInt(outcome.Hull), $"{outcome.Text.ToLowerInvariant()} at {siteName}");
        if (GameData.ShipSystemOf(outcome.System) is { } system)
            KnockOut(sim, player, system, sim.Rng.NextInt(rules.DownMin, rules.DownMax + 1), outcome.Text.ToLowerInvariant());

        // Each member.
        var medic = DivisionIndex(rules.MedicDivision);
        var hasMedic = medic >= 0 && run.Team.Any(m => m.Division == medic);
        var lossPct = (Fix64.FromInt(loss) * outcome.LossMult).FloorToInt();
        var medicPct = (rules.MedicShare * Fix64.FromInt(100)).FloorToInt();
        var fate = new int[run.Team.Count]; // 0 fine, 1 injured, 2 lost
        for (var m = 0; m < run.Team.Count; m++)
        {
            if (sim.Rng.NextInt(100) < lossPct) fate[m] = hasMedic && sim.Rng.NextInt(100) < medicPct ? 1 : 2;
            else if (sim.Rng.NextInt(100) < injury) fate[m] = 1;
        }
        for (var k = 0; k < outcome.Injure; k++)
        {
            var fine = Enumerable.Range(0, fate.Length).Where(m => fate[m] == 0).ToList();
            if (fine.Count == 0) break;
            fate[fine[sim.Rng.NextInt(fine.Count)]] = 1;
        }
        var quarantine = outcome.Quarantine;
        for (var m = 0; m < run.Team.Count; m++)
        {
            var (name, _, d) = run.Team[m];
            if (fate[m] == 2)
            {
                LoseCrew(player, d, 1, $"{name}, aboard {siteName}");
                lines.Add($"{name} lost");
            }
            else if (fate[m] == 1 || quarantine)
            {
                InjureCrew(sim, player, d, 1, quarantine && fate[m] != 1 ? $"{name} in quarantine" : $"{name}, aboard {siteName}", quarantine ? rules.QuarantineSeconds : -1);
                lines.Add(quarantine && fate[m] != 1 ? $"{name} in quarantine" : $"{name} injured");
            }
        }

        run.Done = true;
        run.Step = table.Steps.Count;
        run.Outcome = outcome.Id;
        run.Result = string.Join("; ", lines);
        run.ShowUntil = sim.Tick + (long)AwayResultSeconds * Simulation.TickRate;
        Version++;
        if (site != null)
        {
            RecordFor(player, site.Id).Boarded = true;
            ShipLog(player, $"Away team back from {siteName}: {run.Result}", fate.Contains(2) ? "bad" : fate.Contains(1) ? "warn" : "good");
            Spend(player, site, "boarded");
        }
    }

    private AwayOutcomeDef RollOutcome(Simulation sim, AwayRun run, AwayTableDef table)
    {
        var weights = new int[table.Outcomes.Count];
        for (var i = 0; i < weights.Length; i++) weights[i] = table.Outcomes[i].Weight;
        for (var k = 0; k < run.Picked.Count && k < table.Steps.Count; k++)
            if (run.Picked[k] >= 0 && run.Picked[k] < table.Steps[k].Choices.Count)
                foreach (var (id, pts) in table.Steps[k].Choices[run.Picked[k]].Shift)
                    for (var i = 0; i < weights.Length; i++)
                        if (table.Outcomes[i].Id == id) weights[i] += pts;
        var total = 0;
        for (var i = 0; i < weights.Length; i++) total += System.Math.Max(0, weights[i]);
        if (total <= 0) return table.Outcomes[0];
        var roll = sim.Rng.NextInt(total);
        for (var i = 0; i < weights.Length; i++)
        {
            var w = System.Math.Max(0, weights[i]);
            if (roll < w) return table.Outcomes[i];
            roll -= w;
        }
        return table.Outcomes[^1];
    }

    /// <summary>The team is called back before it is done (the ship left, was crippled, the reactor blew): the boarding
    /// fails with no reward and no roll (injureAll: everyone comes back hurt).</summary>
    private void RecallAwayTeam(Simulation sim, int player, string why, bool injureAll = false)
    {
        if (Status(player) is not { Away: { Done: false } run }) return;
        run.Done = true;
        run.Outcome = "recalled";
        run.Result = $"Recalled: {why}";
        run.ShowUntil = sim.Tick + (long)AwayResultSeconds * Simulation.TickRate;
        if (Site(run.Site) != null)
        {
            var r = RecordFor(player, run.Site);
            r.Recalled = true;
            r.RecalledTick = sim.Tick;
        }
        if (injureAll)
            foreach (var (name, _, d) in run.Team) InjureCrew(sim, player, d, 1, $"{name}, caught aboard");
        ShipLog(player, $"Away team recalled: {why}", "warn");
        Version++;
    }

    /// <summary>The player's away team at that site was recalled (at or after the tick): a space job waiting for the
    /// boarding fails (MissionBook).</summary>
    public bool AwayRecalledSince(int player, string siteId, long tick) => Record(player, siteId) is { Recalled: true } r && r.RecalledTick >= tick;
}
