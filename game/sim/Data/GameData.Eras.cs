using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FinalFrontier.Sim.Data;

// Eras (data/eras.json) and the strategic tech tree (data/tech_tree.json): read and checked here, not played yet. The sim
// keeps them read-only so the data is validated with the rest (and travels in the release pack); see EraDefs.cs.
public sealed partial class GameData
{
    /// <summary>Starting eras (eras.json), in file order. Loaded and checked; nothing in the sim plays them yet.</summary>
    public IReadOnlyList<EraDef> Eras { get; private init; } = Array.Empty<EraDef>();

    /// <summary>The strategic tech tree (tech_tree.json). Loaded and checked; separate from the surface research (<see cref="Techs"/>).</summary>
    public TechTreeDef TechTree { get; private init; } = new(Array.Empty<string>(), Array.Empty<TreeTechDef>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FactionTechRule>());

    public EraDef? Era(string id) => Eras.FirstOrDefault(e => e.Id == id);

    private static List<string> StrList(JsonElement e, string property) =>
        e.TryGetProperty(property, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : new List<string>();

    private static List<EraDef> ReadEras(JsonElement root) =>
        root.GetProperty("eras").EnumerateArray().Select(e =>
        {
            var years = e.GetProperty("years");
            return new EraDef(Str(e, "id"), Str(e, "name"), years[0].GetInt32(), years[1].GetInt32(), Str(e, "stardate_format"),
                e.GetProperty("tech_cap_tier").GetInt32(), e.TryGetProperty("optional", out var o) && o.ValueKind == JsonValueKind.True,
                StrList(e, "playable"), StrList(e, "ai_only"), StrList(e, "events"));
        }).ToList();

    private static TechTreeDef ReadTechTree(JsonElement root)
    {
        var techs = root.GetProperty("techs").EnumerateArray().Select(t => new TreeTechDef(Str(t, "id"), Str(t, "name"), Str(t, "tree"),
            t.GetProperty("tier").GetInt32(), StrList(t, "prereqs"), StrList(t, "unlocks"), StrList(t, "faction_only"),
            t.TryGetProperty("requires_resource", out var rr) ? rr.GetString() ?? "" : "",
            t.TryGetProperty("forbidden", out var fb) && fb.ValueKind == JsonValueKind.True)).ToList();
        var rules = new List<FactionTechRule>();
        if (root.TryGetProperty("faction_rules", out var fr))
            foreach (var p in fr.EnumerateObject())
            {
                var research = !p.Value.TryGetProperty("research", out var r) || r.ValueKind != JsonValueKind.False;
                var resource = "";
                var perTier = 0;
                if (p.Value.TryGetProperty("combine_cost", out var cc))
                {
                    resource = cc.TryGetProperty("resource", out var cr) ? cr.GetString() ?? "" : "";
                    perTier = cc.TryGetProperty("per_tier", out var pt) ? pt.GetInt32() : 0;
                }
                rules.Add(new FactionTechRule(p.Name, research, resource, perTier));
            }
        return new TechTreeDef(StrList(root, "trees"), techs, StrList(root, "starting_modules"), StrList(root, "assimilated_modules"), rules);
    }

    /// <summary>
    /// eras.json against factions.json (they must agree: each era's factions exist, only "playable": true factions are
    /// playable, and a faction's eras list names exactly the eras that list it), the match era (orbit.era, maps.json era)
    /// and ship-class eras.
    /// </summary>
    private void ValidateEras(List<string> problems)
    {
        var eraIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in Eras)
            if (!eraIds.Add(e.Id)) problems.Add($"Era {e.Id}: listed more than once in eras.json.");
        var maxTier = TechTree.Techs.Count > 0 ? TechTree.Techs.Max(t => t.Tier) : int.MaxValue;
        foreach (var e in Eras)
        {
            var where = $"Era {e.Id}";
            if (e.YearTo < e.YearFrom) problems.Add($"{where}: years must run from the earlier to the later year.");
            if (e.TechCapTier < 1 || e.TechCapTier > maxTier) problems.Add($"{where}: tech_cap_tier {e.TechCapTier} must be 1..{maxTier} (the tech tree's tiers).");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in e.Playable.Concat(e.AiOnly))
            {
                if (!seen.Add(f)) problems.Add($"{where}: faction '{f}' is listed twice (playable and ai_only are exclusive).");
                if (!_factions.TryGetValue(f, out var def))
                {
                    problems.Add($"{where}: unknown faction '{f}' (not in factions.json; monsters and one-off threats go in events).");
                    continue;
                }
                if (!def.Eras.Contains(e.Id)) problems.Add($"{where} lists {f}, but factions.json {f} eras doesn't list {e.Id}.");
            }
            foreach (var f in e.Playable)
                if (_factions.TryGetValue(f, out var def) && !def.Playable)
                    problems.Add($"{where}: {f} is playable here, but factions.json marks it \"{def.Role}\"; only \"playable\": true factions can be played (put it in ai_only).");
        }
        foreach (var f in Factions)
        {
            foreach (var era in f.Eras)
            {
                var e = Eras.FirstOrDefault(x => x.Id == era);
                if (e == null) problems.Add($"Faction {f.Id}: unknown era '{era}'.");
                else if (!e.Playable.Contains(f.Id) && !e.AiOnly.Contains(f.Id)) problems.Add($"Faction {f.Id}: lists era {era}, but eras.json {era} has it in neither playable nor ai_only.");
            }
            if (f.Playable && Eras.Count > 0 && !Eras.Any(e => e.Playable.Contains(f.Id)))
                problems.Add($"Faction {f.Id}: \"playable\": true, but no era in eras.json lets anyone play it.");
        }

        foreach (var c in ShipClasses)
            if (c.Era.Length > 0 && !eraIds.Contains(c.Era)) problems.Add($"Ship class {c.Key}: unknown era '{c.Era}'.");
        foreach (var m in Maps)
            if (!eraIds.Contains(m.Era)) problems.Add($"Map {m.Id}: unknown era '{m.Era}'.");
        if (Orbit is { } orbit)
        {
            if (!eraIds.Contains(orbit.Era)) problems.Add($"Orbit: unknown era '{orbit.Era}'.");
            foreach (var (era, _) in orbit.TransporterByEra)
                if (!eraIds.Contains(era)) problems.Add($"Orbit transporter by_era: unknown era '{era}'.");
            foreach (var e in Eras)
                if (orbit.TransporterByEra.All(x => x.Era != e.Id)) problems.Add($"Orbit transporter by_era: no level for era {e.Id}.");
            // One era per match: the base data's orbit era is the maps' era (an era layer sets orbit/era to its own).
            if (Setup.Era == null)
                foreach (var m in Maps)
                    if (m.Era != orbit.Era) problems.Add($"Map {m.Id} is set in era '{m.Era}', but surfaces.json orbit.era is '{orbit.Era}'; they must match.");
        }
    }

    /// <summary>tech_tree.json: ids unique, every reference resolves, no prerequisite loops, and the Borg rule (research: false) is there.</summary>
    private void ValidateTechTree(List<string> problems)
    {
        var tree = TechTree;
        var ids = new Dictionary<string, TreeTechDef>(StringComparer.Ordinal);
        foreach (var t in tree.Techs)
            if (!ids.TryAdd(t.Id, t)) problems.Add($"Tech tree: duplicate tech id '{t.Id}'.");
        foreach (var dup in tree.Trees.GroupBy(x => x).Where(g => g.Count() > 1))
            problems.Add($"Tech tree: tree '{dup.Key}' is listed twice.");
        foreach (var t in tree.Techs)
        {
            var where = $"Tech tree {t.Id}";
            if (!tree.Trees.Contains(t.Tree)) problems.Add($"{where}: unknown tree '{t.Tree}'.");
            if (t.Tier < 1) problems.Add($"{where}: tier must be at least 1.");
            foreach (var p in t.Prereqs)
                if (!ids.TryGetValue(p, out var req)) problems.Add($"{where}: unknown prerequisite '{p}'.");
                else if (req.Tier > t.Tier) problems.Add($"{where}: prerequisite '{p}' is tier {req.Tier}, above this tech's tier {t.Tier}.");
            foreach (var f in t.FactionOnly)
                if (!_factions.ContainsKey(f)) problems.Add($"{where}: faction_only names unknown faction '{f}'.");
            if (t.RequiresResource.Length > 0 && !_resources.ContainsKey(t.RequiresResource))
                problems.Add($"{where}: requires unknown resource '{t.RequiresResource}'.");
        }

        // Prerequisite loops (equal tiers could hide one): depth-first, colouring each tech once.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Loops(TreeTechDef t)
        {
            if (state.TryGetValue(t.Id, out var s)) return s == 1;
            state[t.Id] = 1;
            foreach (var p in t.Prereqs)
                if (ids.TryGetValue(p, out var req) && Loops(req)) return true;
            state[t.Id] = 2;
            return false;
        }
        foreach (var t in tree.Techs)
            if (!state.ContainsKey(t.Id) && Loops(t)) problems.Add($"Tech tree {t.Id}: its prerequisites loop back to it.");

        foreach (var r in tree.FactionRules)
        {
            if (!_factions.ContainsKey(r.Faction)) problems.Add($"Tech tree faction_rules: unknown faction '{r.Faction}'.");
            if (r.CombineResource.Length > 0 && !_resources.ContainsKey(r.CombineResource))
                problems.Add($"Tech tree faction_rules {r.Faction}: combine_cost names unknown resource '{r.CombineResource}'.");
            if (!r.Research && (r.CombineResource.Length == 0 || r.CombinePerTier < 1))
                problems.Add($"Tech tree faction_rules {r.Faction}: a faction that never researches needs a combine_cost (resource and per_tier).");
        }
        // Design rule: the Borg never research; they assimilate and combine.
        if (_factions.ContainsKey("borg") && !tree.FactionRules.Any(r => r.Faction == "borg" && !r.Research))
            problems.Add("Tech tree faction_rules: the Borg rule (\"borg\": { \"research\": false, … }) is missing; the Borg never research.");
    }
}
