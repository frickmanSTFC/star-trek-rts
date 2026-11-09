using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// Civilizations phase 2 (data/civilizations/contact.json): parse + checks. Played by game/sim/Space/Contact.cs.
public sealed partial class GameData
{
    /// <summary>Reads contact.json's "contact" object (null when the document has none: no first contact in this data set).</summary>
    private static ContactRules? ReadContact(JsonElement root, Func<JsonElement, string, string, List<ResourceAmount>> amounts, CivReader r,
        List<string> problems)
    {
        if (!root.TryGetProperty("contact", out var c)) return null;
        if (c.ValueKind != JsonValueKind.Object)
        {
            problems.Add("Civilizations: contact.json \"contact\" must be an object.");
            return null;
        }
        const string w = "Civilizations contact";
        var steps = new List<ContactStepDef>();
        if (c.TryGetProperty("steps", out var ss) && ss.ValueKind == JsonValueKind.Array)
            foreach (var s in ss.EnumerateArray())
            {
                var sw = $"{w} step {(s.ValueKind == JsonValueKind.Object && s.TryGetProperty("id", out var sid) ? sid.ToString() : "?")}";
                var choices = new List<ContactChoiceDef>();
                if (s.ValueKind == JsonValueKind.Object && s.TryGetProperty("choices", out var cs) && cs.ValueKind == JsonValueKind.Array)
                    foreach (var x in cs.EnumerateArray())
                        choices.Add(new ContactChoiceDef(r.Str(x, "text", sw), r.Str(x, "hint", sw), r.Int(x, "shock", sw), r.Int(x, "research", sw),
                            r.Int(x, "standing_if_shock", sw), r.Int(x, "wait_minutes", sw)));
                else problems.Add($"{sw}: choices must be a list.");
                steps.Add(new ContactStepDef(r.Str(s, "id", sw), r.Str(s, "title", sw), choices));
            }
        else problems.Add($"{w}: steps must be a list.");

        var violation = r.Obj(c, "violation", w);
        var trade = r.Obj(c, "trade", w);
        var mult = r.Obj(trade, "stage_multiplier", $"{w} trade");
        var m = r.Obj(c, "membership", w);
        var membership = new MembershipRules(r.Int(m, "min_standing", $"{w} membership"), r.Int(m, "partner_minutes", $"{w} membership"),
            r.Int(m, "candidate_minutes", $"{w} membership"), r.Fix(m, "walkout_chance", $"{w} membership"), r.Fix(m, "walkout_income_cut", $"{w} membership"));

        var techs = new List<UniqueTechDef>();
        if (c.TryGetProperty("unique_techs", out var ts) && ts.ValueKind == JsonValueKind.Array)
            foreach (var t in ts.EnumerateArray())
            {
                var tw = $"{w} unique tech {(t.ValueKind == JsonValueKind.Object && t.TryGetProperty("id", out var tid) ? tid.ToString() : "?")}";
                var mods = new List<Modifier>();
                if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("modifiers", out var ms) && ms.ValueKind == JsonValueKind.Array)
                    foreach (var x in ms.EnumerateArray())
                    {
                        List<string> Ids(string p) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(p, out var a) && a.ValueKind == JsonValueKind.Array
                            ? a.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : "").ToList() : new List<string>();
                        var add = x.ValueKind == JsonValueKind.Object && x.TryGetProperty("add", out var ad) && ad.ValueKind == JsonValueKind.Number
                            ? Fix64.Parse(ad.GetRawText()) : Fix64.Zero;
                        mods.Add(new Modifier(r.Str(x, "stat", tw), Ids("buildings"), Ids("units"), add));
                    }
                else problems.Add($"{tw}: modifiers must be a list.");
                techs.Add(new UniqueTechDef(r.Str(t, "id", tw), r.Str(t, "name", tw), r.Str(t, "effect", tw), mods));
            }
        else problems.Add($"{w}: unique_techs must be a list.");

        var al = r.Obj(c, "ally", w);
        var byStage = r.Obj(al, "ships_by_stage", $"{w} ally");
        var classes = new List<AllyClassDef>();
        if (al.ValueKind == JsonValueKind.Object && al.TryGetProperty("classes", out var cl) && cl.ValueKind == JsonValueKind.Array)
            foreach (var x in cl.EnumerateArray())
                classes.Add(new AllyClassDef(r.Str(x, "id", $"{w} ally class"), r.Str(x, "name", $"{w} ally class"), r.Str(x, "role", $"{w} ally class"),
                    r.Int(x, "weight", $"{w} ally class")));
        else problems.Add($"{w}: ally.classes must be a list.");
        var ally = new AllyRules(r.Int(byStage, "trade_partner", $"{w} ally.ships_by_stage"), r.Int(byStage, "candidate", $"{w} ally.ships_by_stage"),
            r.Int(byStage, "allied", $"{w} ally.ships_by_stage"), classes, r.StringList(al, "names", $"{w} ally"), r.Fix(al, "travel_scale", $"{w} ally"),
            r.Int(al, "escort_delay_seconds", $"{w} ally"), r.Int(al, "scout_sweep_seconds", $"{w} ally"));

        var ag = r.Obj(c, "aggressive", w);
        var aggressive = new AggressiveRules(r.Int(ag, "from_awareness", $"{w} aggressive"), r.Int(ag, "shoot_from_level", $"{w} aggressive"),
            r.Fix(ag, "shoot_chance", $"{w} aggressive"), r.Int(ag, "shoot_down_standing", $"{w} aggressive"), r.Int(ag, "refuse_minutes", $"{w} aggressive"));
        var adv = r.Obj(c, "advance", w);
        var advance = new AdvanceRules(r.Fix(adv, "chance_per_min", $"{w} advance"), r.Fix(adv, "per_awareness", $"{w} advance"),
            r.Int(adv, "wreck_spike", $"{w} advance"), r.Fix(adv, "wreck_chance", $"{w} advance"));

        return new ContactRules(r.StringList(c, "factions", w), r.Int(c, "min_level", w), r.Bool(c, "needs_study", w), r.Int(c, "step_seconds", w),
            r.Int(c, "base_shock", w), r.Weights(c, "shock_by_temperament", w), r.Fix(c, "shock_per_awareness", w), r.Int(c, "pre_warp_shock", w),
            r.Int(c, "shock_min", w), r.Int(c, "shock_max", w), steps, r.Int(violation, "standing", $"{w} violation"), r.Int(c, "shock_wait_minutes", w),
            r.Int(c, "culture_shock_awareness", w), r.Int(c, "culture_shock_minutes", w), r.Int(c, "research_boost", w),
            trade.ValueKind == JsonValueKind.Object && trade.TryGetProperty("income_per_min", out _) ? amounts(trade, "income_per_min", $"{w} trade") : new List<ResourceAmount>(),
            r.Fix(mult, "trade_partner", $"{w} trade.stage_multiplier"), r.Fix(mult, "candidate", $"{w} trade.stage_multiplier"),
            r.Fix(mult, "allied", $"{w} trade.stage_multiplier"), membership, techs, ally, aggressive, r.Int(c, "refuse_after_incident_minutes", w), advance);
    }

    private void ValidateContact(CivilizationData civ, List<string> problems)
    {
        if (civ.Contact is not { } c) return;
        const string w = "Civilizations contact";
        void Share(Fix64 x, string name)
        {
            if (x < Fix64.Zero || x > Fix64.One) problems.Add($"{w}: {name} must be between 0 and 1.");
        }
        foreach (var f in c.Factions)
            if (!_factions.ContainsKey(f)) problems.Add($"{w}: factions: unknown faction '{f}'.");
        if (c.MinLevel < 1 || c.MinLevel > CivLevels) problems.Add($"{w}: min_level must be 1..{CivLevels}.");
        if (c.StepSeconds < 5 || c.StepSeconds > 600) problems.Add($"{w}: step_seconds must be 5..600.");
        if (c.ShockMin < 0 || c.ShockMax > 100 || c.ShockMin > c.ShockMax) problems.Add($"{w}: shock_min..shock_max must be within 0..100.");
        foreach (var t in c.ShockByTemperament)
            if (!TemperamentIds.Contains(t.Id)) problems.Add($"{w}: shock_by_temperament: unknown temperament '{t.Id}'.");
        if (c.ShockPerAwareness < Fix64.Zero || c.ShockPerAwareness > Fix64.One) problems.Add($"{w}: shock_per_awareness must be 0..1.");
        if (c.Steps.Count == 0 || c.Steps.Count > 6) problems.Add($"{w}: steps needs 1..6 steps.");
        foreach (var s in c.Steps)
        {
            if (s.Choices.Count < 2 || s.Choices.Count > 4) problems.Add($"{w} step {s.Id}: needs 2..4 choices.");
            foreach (var x in s.Choices)
            {
                if (x.Text.Length == 0) problems.Add($"{w} step {s.Id}: every choice needs a text.");
                if (x.Shock < -100 || x.Shock > 100) problems.Add($"{w} step {s.Id}: shock must be -100..100.");
                if (x.StandingIfShock > 0 || x.StandingIfShock < -100) problems.Add($"{w} step {s.Id}: standing_if_shock must be -100..0.");
                if (x.Research < -1000 || x.Research > 1000) problems.Add($"{w} step {s.Id}: research must be -1000..1000.");
            }
        }
        if (c.Steps.Select(s => s.Id).Distinct().Count() != c.Steps.Count) problems.Add($"{w}: step ids repeat.");
        if (c.ViolationStanding > 0 || c.ViolationStanding < -100) problems.Add($"{w}: violation.standing must be -100..0.");
        if (c.ShockWaitMinutes < 0 || c.CultureShockMinutes < 0 || c.CultureShockAwareness < 0 || c.CultureShockAwareness > 100)
            problems.Add($"{w}: shock_wait_minutes / culture_shock_minutes must not be negative, culture_shock_awareness 0..100.");
        if (c.ResearchBoost < 0) problems.Add($"{w}: research_boost must not be negative.");
        if (c.TradeIncome.Count == 0) problems.Add($"{w}: trade.income_per_min needs a resource.");
        if (c.TradeIncome.Any(x => x.Amount < Fix64.Zero || x.Amount > Fix64.FromInt(10000))) problems.Add($"{w}: trade.income_per_min must be 0..10000.");
        foreach (var (x, name) in new[] { (c.TradePartnerMultiplier, "trade_partner"), (c.CandidateMultiplier, "candidate"), (c.AlliedMultiplier, "allied") })
            if (x < Fix64.Zero || x > Fix64.FromInt(10)) problems.Add($"{w}: trade.stage_multiplier.{name} must be 0..10.");
        var m = c.Membership;
        if (m.MinStanding < 0 || m.MinStanding > 100) problems.Add($"{w}: membership.min_standing must be 0..100.");
        if (m.PartnerMinutes < 0 || m.CandidateMinutes < 0) problems.Add($"{w}: membership minutes must not be negative.");
        Share(m.WalkoutChance, "membership.walkout_chance");
        Share(m.WalkoutIncomeCut, "membership.walkout_income_cut");
        if (c.UniqueTechs.Count == 0) problems.Add($"{w}: unique_techs needs at least one tech.");
        foreach (var t in c.UniqueTechs)
        {
            if (t.Id.Length == 0 || t.Name.Length == 0) problems.Add($"{w}: every unique tech needs an id and a name.");
            if (_techs.ContainsKey(t.Id)) problems.Add($"{w} unique tech {t.Id}: the id is a surface tech's.");
            foreach (var x in t.Modifiers)
            {
                if (!Modifier.Stats.Contains(x.Stat)) problems.Add($"{w} unique tech {t.Id}: unknown modifier stat '{x.Stat}'.");
                foreach (var b in x.Buildings)
                    if (!_buildings.ContainsKey(b)) problems.Add($"{w} unique tech {t.Id}: unknown building '{b}'.");
                foreach (var u in x.Units)
                    if (!_surfaceUnits.ContainsKey(u)) problems.Add($"{w} unique tech {t.Id}: unknown unit '{u}'.");
                if (x.Add < Fix64.FromInt(-1) || x.Add > Fix64.FromInt(5)) problems.Add($"{w} unique tech {t.Id}: add must be -1..5.");
            }
        }
        if (c.UniqueTechs.Select(t => t.Id).Distinct().Count() != c.UniqueTechs.Count) problems.Add($"{w}: unique tech ids repeat.");
        var a = c.Ally;
        foreach (var (n, name) in new[] { (a.ShipsTradePartner, "trade_partner"), (a.ShipsCandidate, "candidate"), (a.ShipsAllied, "allied") })
            if (n < 0 || n > 6) problems.Add($"{w}: ally.ships_by_stage.{name} must be 0..6.");
        if (a.ShipsCandidate < a.ShipsTradePartner || a.ShipsAllied < a.ShipsCandidate) problems.Add($"{w}: ally.ships_by_stage must not fall from stage to stage.");
        if (a.Classes.Count == 0 || a.Classes.All(x => x.Weight <= 0)) problems.Add($"{w}: ally.classes needs a class with a positive weight.");
        if (a.Classes.Any(x => x.Id.Length == 0 || x.Name.Length == 0 || x.Weight < 0)) problems.Add($"{w}: every ally class needs an id, a name and a weight ≥ 0.");
        if (a.Names.Count == 0) problems.Add($"{w}: ally.names needs a name.");
        if (a.TravelScale < Fix64.Ratio(1, 2) || a.TravelScale > Fix64.FromInt(5)) problems.Add($"{w}: ally.travel_scale must be 0.5..5.");
        if (a.EscortDelaySeconds < 0 || a.EscortDelaySeconds > 120) problems.Add($"{w}: ally.escort_delay_seconds must be 0..120.");
        if (a.ScoutSweepSeconds < 1 || a.ScoutSweepSeconds > 600) problems.Add($"{w}: ally.scout_sweep_seconds must be 1..600.");
        var g = c.Aggressive;
        if (g.FromAwareness < 0 || g.FromAwareness > 100) problems.Add($"{w}: aggressive.from_awareness must be 0..100.");
        if (g.ShootFromLevel < 1 || g.ShootFromLevel > CivLevels + 1) problems.Add($"{w}: aggressive.shoot_from_level must be 1..{CivLevels + 1}.");
        Share(g.ShootChance, "aggressive.shoot_chance");
        if (g.ShootDownStanding > 0 || g.ShootDownStanding < -100) problems.Add($"{w}: aggressive.shoot_down_standing must be -100..0.");
        if (g.RefuseMinutes < 0 || c.RefuseAfterIncidentMinutes < 0) problems.Add($"{w}: refuse minutes must not be negative.");
        var d = c.Advance;
        Share(d.ChancePerMin, "advance.chance_per_min");
        if (d.PerAwareness < Fix64.Zero || d.PerAwareness > Fix64.Ratio(1, 100)) problems.Add($"{w}: advance.per_awareness must be 0..0.01.");
        if (d.WreckSpike < 1 || d.WreckSpike > 100) problems.Add($"{w}: advance.wreck_spike must be 1..100.");
        Share(d.WreckChance, "advance.wreck_chance");
    }
}
