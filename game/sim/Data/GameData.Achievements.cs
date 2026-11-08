using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// Achievements (data/achievements/): parse + checks. The tracker is game/sim/Core/Achievements.cs; the names it knows
// (milestones, events and their fields, measures, conditions, counters) are listed here so Validate() can check the data.
public sealed partial class GameData
{
    /// <summary>The trigger kinds and the fields each may have (a list field holds strings).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> AchievementTriggerFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["milestone"] = new[] { "milestone", "before_seconds", "without" },
        ["built"] = new[] { "building", "count" },
        ["count"] = new[] { "measure", "at_least", "all" },
        ["share"] = new[] { "measure", "at_least" },
        ["unit_counter"] = new[] { "counter", "at_least", "units" },
        ["event"] = new[] { "event" }, // + the event's own fields (AchievementEvents)
        ["streak"] = new[] { "condition", "seconds", "from_start" },
        ["set"] = new[] { "measure", "contains" },
        ["view_count"] = new[] { "measure", "at_least", "all" },
        ["all_of"] = new[] { "rules" },
        ["any_of"] = new[] { "rules" },
    };

    /// <summary>Milestones by name (plus built:&lt;building id&gt; and unit:&lt;unit id&gt;), as the balance runner means them.</summary>
    public static readonly IReadOnlyList<string> AchievementMilestones = new[] { "uplink", "landed", "first_metal", "first_parasteel", "second_base", "research_all" };

    /// <summary>Events the sim reports, with their fields: "at_least" (the event's number must reach the trigger's),
    /// "at_most" (must not exceed it) or "equals" (text).</summary>
    public static readonly IReadOnlyDictionary<string, (string Field, string Compare)[]> AchievementEvents = new Dictionary<string, (string, string)[]>(StringComparer.Ordinal)
    {
        ["orbit_farthest"] = Array.Empty<(string, string)>(),
        ["charted_class"] = new[] { ("class", "equals") },
        ["deposit_found"] = new[] { ("amount", "at_least") },
        ["founded"] = Array.Empty<(string, string)>(),
        ["deposit_reopened"] = Array.Empty<(string, string)>(),
        ["tyre_changed_field"] = Array.Empty<(string, string)>(),
        ["limped_home"] = Array.Empty<(string, string)>(),
        ["crawler_explosion"] = Array.Empty<(string, string)>(),
        ["domed_crewed_working"] = Array.Empty<(string, string)>(),
        ["life_support_saved"] = Array.Empty<(string, string)>(),
        ["order_relayed"] = new[] { ("delay_seconds", "at_least") },
        ["sos_answered"] = new[] { ("within_seconds", "at_most") },
    };

    /// <summary>Measures per trigger kind, and whether "all" works for it.</summary>
    public static readonly IReadOnlyDictionary<string, (string Kind, bool All)> AchievementMeasures = new Dictionary<string, (string, bool)>(StringComparer.Ordinal)
    {
        ["bodies_charted"] = ("count", true),
        ["system_satellites"] = ("count", false),
        ["units"] = ("count", false),
        ["techs_known"] = ("count", true),
        ["surveyed"] = ("share", false),
        ["weather_seen"] = ("set", false),
        ["ships_inspected"] = ("view_count", true),
    };

    public static readonly IReadOnlyList<string> AchievementConditions = new[] { "no_power_short", "flagship_home" };
    public static readonly IReadOnlyList<string> AchievementCounters = new[] { "tiles_driven" };

    /// <summary>The line icons the screen draws (the mockup's set).</summary>
    public static readonly IReadOnlyList<string> AchievementIcons = new[]
    {
        "uplink", "ingot", "alloy", "crown", "capitol", "bolt", "chart", "satellite", "route", "planet", "hail", "hex", "gem", "base",
        "drill", "rover", "tyre", "wrench", "fleet", "blast", "dome", "lungs", "weight", "storm", "signal", "sos", "bay", "box", "flask", "anchor",
    };

    public static readonly IReadOnlyList<string> CosmeticKinds = new[] { "rover_name", "hull_number", "paint", "finish", "dropship_name", "beam_colour", "dome_tint" };
    public static readonly IReadOnlyList<string> CosmeticFinishes = new[] { "brushed_gold", "chrome", "blue_white", "gunmetal", "pearl", "rescue_white" };
    public static readonly IReadOnlyList<string> CosmeticPatterns = new[] { "solid", "band", "chevrons", "stripes" };

    /// <summary>Longest streak or milestone time a trigger may ask for (4 hours of play).</summary>
    public const int AchievementMaxSeconds = 14400;

    private static readonly Regex SnakeId = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex HexColour = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);

    private static AchievementData ReadAchievements(JsonElement root, List<string> problems)
    {
        string S(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        var categories = root.TryGetProperty("categories", out var cs)
            ? cs.EnumerateArray().Select(c => new AchievementCategory(S(c, "id"), S(c, "name"),
                c.TryGetProperty("order", out var o) && o.TryGetInt32(out var oi) ? oi : 0, S(c, "icon"))).ToList()
            : new List<AchievementCategory>();
        var tiers = root.TryGetProperty("tiers", out var ts)
            ? ts.EnumerateArray().Select(t => new AchievementTier(S(t, "id"), S(t, "name"), S(t, "colour"),
                t.TryGetProperty("points", out var p) && p.TryGetInt32(out var pi) ? pi : 0)).ToList()
            : new List<AchievementTier>();
        var cosmetics = root.TryGetProperty("cosmetics", out var cos)
            ? cos.EnumerateArray().Select(c => new CosmeticDef(S(c, "id"), S(c, "kind"), S(c, "name"),
                c.TryGetProperty("value", out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()) : "",
                S(c, "colour2"), S(c, "pattern"))).ToList()
            : new List<CosmeticDef>();
        var list = new List<AchievementDef>();
        if (root.TryGetProperty("achievements", out var arr))
            foreach (var a in arr.EnumerateArray())
            {
                var id = S(a, "id");
                if (!a.TryGetProperty("trigger", out var tr) || tr.ValueKind != JsonValueKind.Object)
                {
                    problems.Add($"Achievement {id}: needs a trigger.");
                    continue;
                }
                AchievementWorlds? worlds = null;
                if (a.TryGetProperty("worlds", out var w) && w.ValueKind == JsonValueKind.Object)
                    worlds = new AchievementWorlds(
                        w.TryGetProperty("classes", out var wc) ? wc.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>(),
                        Decimal(w, "min_gravity_g", $"Achievement {id}", problems),
                        Decimal(w, "max_gravity_g", $"Achievement {id}", problems),
                        w.TryGetProperty("tier", out var wt) ? wt.GetString() : null);
                list.Add(new AchievementDef(id, S(a, "name"), S(a, "description"), S(a, "category"), S(a, "tier"), S(a, "icon"),
                    a.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True,
                    a.TryGetProperty("progress", out var pr) && pr.ValueKind == JsonValueKind.True,
                    S(a, "reward"), ReadTrigger(tr), worlds));
            }
        return new AchievementData(categories, tiers, cosmetics, list);
    }

    private static decimal? Decimal(JsonElement e, string property, string where, List<string> problems)
    {
        if (!e.TryGetProperty(property, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && decimal.TryParse(v.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        problems.Add($"{where}: {property} must be a number.");
        return null;
    }

    /// <summary>A trigger object: "kind", "rules" (sub-triggers), string lists (Fields holds them joined with commas, List the
    /// items), and scalars as text (numbers as written, booleans "true"/"false").</summary>
    private static AchievementTrigger ReadTrigger(JsonElement t)
    {
        var kind = "";
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = new List<string>();
        var rules = new List<AchievementTrigger>();
        foreach (var p in t.EnumerateObject())
        {
            if (p.Name == "kind")
            {
                kind = p.Value.GetString() ?? "";
                continue;
            }
            switch (p.Value.ValueKind)
            {
                case JsonValueKind.Array when p.Name == "rules":
                    fields[p.Name] = "";
                    foreach (var r in p.Value.EnumerateArray())
                        if (r.ValueKind == JsonValueKind.Object) rules.Add(ReadTrigger(r));
                    break;
                case JsonValueKind.Array:
                    var l = p.Value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : x.GetRawText()).ToList();
                    items.AddRange(l);
                    fields[p.Name] = string.Join(",", l);
                    break;
                case JsonValueKind.String:
                    fields[p.Name] = p.Value.GetString()!;
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    fields[p.Name] = p.Value.ValueKind == JsonValueKind.True ? "true" : "false";
                    break;
                default:
                    fields[p.Name] = p.Value.GetRawText();
                    break;
            }
        }
        return new AchievementTrigger(kind, fields, items, rules);
    }

    /// <summary>A trigger field as a number (null when missing or not a number).</summary>
    public static Fix64? AchievementNumber(AchievementTrigger t, string field)
    {
        if (!t.Fields.TryGetValue(field, out var text)) return null;
        try
        {
            return Fix64.Parse(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private void ValidateAchievements(List<string> problems)
    {
        if (Achievements is not { } a) return;
        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in a.Categories)
        {
            if (!SnakeId.IsMatch(c.Id)) problems.Add($"Achievement category '{c.Id}': ids are lower_snake.");
            if (!categories.Add(c.Id)) problems.Add($"Achievement category {c.Id} is listed twice.");
            if (c.Name.Length == 0) problems.Add($"Achievement category {c.Id}: needs a name.");
            if (!AchievementIcons.Contains(c.Icon)) problems.Add($"Achievement category {c.Id}: unknown icon '{c.Icon}'.");
        }
        var tiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in a.Tiers)
        {
            if (!tiers.Add(t.Id)) problems.Add($"Achievement tier {t.Id} is listed twice.");
            if (t.Points <= 0) problems.Add($"Achievement tier {t.Id}: points must be positive.");
            if (!HexColour.IsMatch(t.Colour)) problems.Add($"Achievement tier {t.Id}: colour must be #rrggbb.");
        }
        var cosmetics = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in a.Cosmetics)
        {
            var where = $"Cosmetic {c.Id}";
            if (!SnakeId.IsMatch(c.Id)) problems.Add($"Cosmetic '{c.Id}': ids are lower_snake.");
            if (!cosmetics.Add(c.Id)) problems.Add($"{where} is listed twice.");
            if (c.Name.Length == 0) problems.Add($"{where}: needs a name.");
            switch (c.Kind)
            {
                case "rover_name" or "dropship_name":
                    if (c.Value.Trim().Length == 0 || c.Value.Length > 16) problems.Add($"{where}: the name must be 1 to 16 characters.");
                    break;
                case "hull_number":
                    if (!int.TryParse(c.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 0 || n > 99999)
                        problems.Add($"{where}: a hull number is a whole number 0 to 99999.");
                    break;
                case "paint":
                    if (!HexColour.IsMatch(c.Value)) problems.Add($"{where}: value must be a #rrggbb colour.");
                    if (!CosmeticPatterns.Contains(c.Pattern)) problems.Add($"{where}: pattern must be solid, band, chevrons or stripes.");
                    else if (c.Pattern != "solid" && !HexColour.IsMatch(c.Colour2)) problems.Add($"{where}: a {c.Pattern} pattern needs colour2 (#rrggbb).");
                    if (c.Colour2.Length > 0 && !HexColour.IsMatch(c.Colour2)) problems.Add($"{where}: colour2 must be #rrggbb or empty.");
                    break;
                case "beam_colour" or "dome_tint":
                    if (!HexColour.IsMatch(c.Value)) problems.Add($"{where}: value must be a #rrggbb colour.");
                    break;
                case "finish":
                    if (!CosmeticFinishes.Contains(c.Value)) problems.Add($"{where}: unknown finish '{c.Value}'.");
                    break;
                default:
                    problems.Add($"{where}: unknown kind '{c.Kind}'.");
                    break;
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rewardOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var x in a.List)
        {
            var where = $"Achievement {x.Id}";
            if (!SnakeId.IsMatch(x.Id)) problems.Add($"Achievement '{x.Id}': ids are lower_snake.");
            if (!ids.Add(x.Id)) problems.Add($"{where} is listed twice.");
            if (x.Name.Length == 0 || x.Description.Length == 0) problems.Add($"{where}: needs a name and a description.");
            if (!categories.Contains(x.Category)) problems.Add($"{where}: unknown category '{x.Category}'.");
            if (!tiers.Contains(x.Tier)) problems.Add($"{where}: unknown tier '{x.Tier}'.");
            if (!AchievementIcons.Contains(x.Icon)) problems.Add($"{where}: unknown icon '{x.Icon}'.");
            if (!cosmetics.Contains(x.Reward)) problems.Add($"{where}: unknown reward '{x.Reward}'.");
            else if (rewardOf.TryGetValue(x.Reward, out var other)) problems.Add($"{where}: reward {x.Reward} is already {other}'s.");
            else rewardOf[x.Reward] = x.Id;
            // hidden may go with any tier: nothing to check.
            ValidateTrigger(x.Trigger, where, 0, problems);
            if (x.Progress && !HasProgress(x.Trigger)) problems.Add($"{where}: progress needs a count, share, unit_counter, streak, set or view_count trigger.");
            if (x.Worlds is { } w)
            {
                foreach (var cls in w.Classes)
                    if (!_planetClasses.ContainsKey(cls)) problems.Add($"{where}: worlds names unknown planet class '{cls}'.");
                if (w.MinGravityG is < 0 || w.MaxGravityG is < 0 || (w.MinGravityG is { } lo && w.MaxGravityG is { } hi && hi < lo))
                    problems.Add($"{where}: worlds gravity range is invalid.");
                if (w.Tier != null && !Enum.GetValues<FinalFrontier.Sim.Surface.HabitabilityTier>().Any(t => HabitabilityRules.TierId(t) == w.Tier))
                    problems.Add($"{where}: worlds names unknown tier '{w.Tier}'.");
            }
        }
    }

    private static bool HasProgress(AchievementTrigger t) => t.Kind is "count" or "share" or "unit_counter" or "streak" or "set" or "view_count";

    private void ValidateTrigger(AchievementTrigger t, string where, int depth, List<string> problems)
    {
        if (!AchievementTriggerFields.TryGetValue(t.Kind, out var allowed))
        {
            problems.Add($"{where}: unknown trigger kind '{t.Kind}'.");
            return;
        }
        var eventFields = t.Kind == "event" && t.Fields.TryGetValue("event", out var ev) && AchievementEvents.TryGetValue(ev, out var ef)
            ? ef.Select(f => f.Field).ToArray() : Array.Empty<string>();
        foreach (var f in t.Fields.Keys.OrderBy(k => k, StringComparer.Ordinal))
            if (!allowed.Contains(f) && !eventFields.Contains(f)) problems.Add($"{where}: a {t.Kind} trigger has no field '{f}'.");

        string? Text(string field) => t.Fields.TryGetValue(field, out var v) ? v : null;
        void Positive(string field, bool required)
        {
            if (Text(field) == null)
            {
                if (required) problems.Add($"{where}: {t.Kind} needs {field}.");
                return;
            }
            if (AchievementNumber(t, field) is not { } n || n <= Fix64.Zero) problems.Add($"{where}: {field} must be a positive number.");
        }
        void Seconds(string field, bool required)
        {
            Positive(field, required);
            if (AchievementNumber(t, field) is { } n && n > Fix64.FromInt(AchievementMaxSeconds))
                problems.Add($"{where}: {field} is at most {AchievementMaxSeconds} s.");
        }
        void Measure(string kind)
        {
            var m = Text("measure");
            if (m == null || !AchievementMeasures.TryGetValue(m, out var info) || info.Kind != kind)
            {
                problems.Add($"{where}: unknown {kind} measure '{m}'.");
                return;
            }
            if (kind is "count" or "view_count")
            {
                var all = Text("all") == "true";
                if (Text("all") is { } at && at != "true") problems.Add($"{where}: all must be true (or left out).");
                if (all == (Text("at_least") != null)) problems.Add($"{where}: give at_least or all, not both.");
                if (all && !info.All) problems.Add($"{where}: measure {m} has no 'all'.");
                if (!all) Positive("at_least", true);
            }
        }

        switch (t.Kind)
        {
            case "milestone":
                var ms = Text("milestone");
                if (ms == null || !KnownMilestone(ms)) problems.Add($"{where}: unknown milestone '{ms}'.");
                if (Text("before_seconds") != null) Seconds("before_seconds", false);
                if (Text("without") is { } without && !_buildings.ContainsKey(without)) problems.Add($"{where}: without names unknown building '{without}'.");
                break;
            case "built":
                if (Text("building") is not { } b || !_buildings.ContainsKey(b)) problems.Add($"{where}: unknown building '{Text("building")}'.");
                Positive("count", false);
                break;
            case "count":
            case "view_count":
                Measure(t.Kind);
                break;
            case "share":
                Measure("share");
                Positive("at_least", true);
                if (AchievementNumber(t, "at_least") is { } share && share > Fix64.One) problems.Add($"{where}: a share is at most 1.");
                break;
            case "unit_counter":
                if (!AchievementCounters.Contains(Text("counter") ?? "")) problems.Add($"{where}: unknown counter '{Text("counter")}'.");
                Positive("at_least", true);
                if (t.List.Count == 0) problems.Add($"{where}: unit_counter needs units.");
                foreach (var u in t.List)
                    if (!_surfaceUnits.ContainsKey(u)) problems.Add($"{where}: unknown unit '{u}'.");
                break;
            case "event":
                var e = Text("event");
                if (e == null || !AchievementEvents.TryGetValue(e, out var fields))
                {
                    problems.Add($"{where}: unknown event '{e}'.");
                    break;
                }
                foreach (var (field, compare) in fields)
                {
                    if (Text(field) == null) continue; // optional: no condition on it
                    if (compare == "equals")
                    {
                        if (field == "class" && !_planetClasses.ContainsKey(Text(field)!)) problems.Add($"{where}: unknown planet class '{Text(field)}'.");
                    }
                    else if (field.EndsWith("_seconds", StringComparison.Ordinal)) Seconds(field, false);
                    else Positive(field, false);
                }
                break;
            case "streak":
                if (!AchievementConditions.Contains(Text("condition") ?? "")) problems.Add($"{where}: unknown condition '{Text("condition")}'.");
                Seconds("seconds", true);
                if (Text("from_start") is { } fs && fs is not ("true" or "false")) problems.Add($"{where}: from_start is true or false.");
                break;
            case "set":
                Measure("set");
                if (t.List.Count == 0) problems.Add($"{where}: set needs contains.");
                foreach (var k in t.List)
                    if (WeatherFromId(k) == null) problems.Add($"{where}: unknown weather kind '{k}'.");
                break;
            case "all_of":
            case "any_of":
                if (depth > 0) problems.Add($"{where}: {t.Kind} can't be nested (one level only).");
                if (t.Rules.Count == 0) problems.Add($"{where}: {t.Kind} needs rules.");
                foreach (var r in t.Rules)
                {
                    if (r.Kind is "view_count") problems.Add($"{where}: view_count can't be part of {t.Kind} (the view counts it).");
                    ValidateTrigger(r, where, depth + 1, problems);
                }
                break;
        }
    }

    /// <summary>A milestone name the tracker knows: AchievementMilestones, built:&lt;building id&gt; or unit:&lt;unit id&gt;.</summary>
    public bool KnownMilestone(string name) =>
        AchievementMilestones.Contains(name)
        || (name.StartsWith("built:", StringComparison.Ordinal) && _buildings.ContainsKey(name[6..]))
        || (name.StartsWith("unit:", StringComparison.Ordinal) && _surfaceUnits.ContainsKey(name[5..]));
}
