using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// Missions (data/missions/, docs/missions-contract.md C): parse + checks. The engine is game/sim/Missions/. The
// vocabulary (condition and action kinds with their fields) is listed here so Validate() can check the data; the
// engine's switch statements (MissionConditions.cs, MissionActions.cs) must know every kind listed.
public sealed partial class GameData
{
    /// <summary>The campaign missions (data/missions/), or null when the data set has none (the document is optional).</summary>
    public MissionData? Missions { get; private set; }

    /// <summary>A number from its raw JSON text (Fix64.Parse; never through double). False when it isn't one.</summary>
    public static bool TryFix(string? text, out Fix64 value)
    {
        value = Fix64.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            value = Fix64.Parse(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Condition kinds and their fields (lists are fields too). all / any hold a list of conditions, not one.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> MissionConditionFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["all"] = Array.Empty<string>(),
        ["any"] = Array.Empty<string>(),
        ["not"] = Array.Empty<string>(),
        ["resource"] = new[] { "res", "amount" },
        ["building"] = new[] { "id", "ids", "count", "state", "at", "radius" },
        ["unit"] = new[] { "id", "ids", "count", "state", "at", "radius", "far_from_pad", "bind" },
        ["surveyed"] = new[] { "count", "at", "radius" },
        ["deposits_found"] = new[] { "count", "rich", "res", "bind" },
        ["deposit"] = new[] { "at", "extracted" },
        ["site"] = new[] { "site", "state", "radius" },
        ["drone_launched"] = new[] { "count" },
        ["drone_at"] = new[] { "at", "radius", "count" },
        ["relay"] = new[] { "count", "covers" },
        ["covered"] = new[] { "at" },
        ["fabricated"] = new[] { "item", "count" },
        ["outpost"] = new[] { "count", "covered" },
        ["weather"] = new[] { "kinds", "near", "within_seconds" },
        ["exposed"] = new[] { "kinds" },
        ["time"] = new[] { "after_seconds", "before_seconds", "since" },
        ["flag"] = new[] { "name" },
        ["mission"] = new[] { "id", "state" },
        ["tech"] = new[] { "id" },
        ["uplink"] = Array.Empty<string>(),
        ["ruin_hint"] = new[] { "bind" },
        ["satellite_share"] = new[] { "share" },
        ["near"] = new[] { "at", "radius" },
    };

    /// <summary>Action kinds and their fields (grant: resource ids → amounts instead).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> MissionActionFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["message"] = new[] { "giver", "text", "channel" },
        ["grant"] = Array.Empty<string>(),
        ["research"] = new[] { "points" },
        ["reveal"] = new[] { "at", "radius" },
        ["place_site"] = new[] { "bind", "kind", "count", "min", "max", "terrain", "unsurveyed", "behind", "spacing", "near" },
        ["place_tiles"] = new[] { "bind", "count", "min", "max", "terrain", "unsurveyed", "behind", "spacing", "near" },
        ["marker"] = new[] { "at", "style", "radius", "offset", "label" },
        ["set_flag"] = new[] { "name" },
        ["start_mission"] = new[] { "id" },
        ["complete"] = Array.Empty<string>(),
        ["fail"] = Array.Empty<string>(),
    };

    /// <summary>Unit states a unit condition can ask for (drone states: Surface/Drones.cs DroneState, lower case;
    /// "away" = any drone state but docked or lost).</summary>
    public static readonly IReadOnlyList<string> MissionUnitStates = new[]
    {
        "any", "mobile", "parked", "deployed", "broken", "aerial",
        "docked", "launching", "flying", "loitering", "returning", "landing", "lost", "away",
    };

    public static readonly IReadOnlyList<string> MissionBuildingStates = new[] { "complete", "any", "building" };
    public static readonly IReadOnlyList<string> MissionSiteStates = new[] { "found", "reached", "visited", "done" };
    public static readonly IReadOnlyList<string> MissionMarkerStyles = new[] { "area", "target", "site", "unit" };
    public static readonly IReadOnlyList<string> MissionTerrains = new[] { "any", "passable", "impassable" };
    public static readonly IReadOnlyList<string> MissionTimeSince = new[] { "start", "uplink", "mission", "stage" };
    public static readonly IReadOnlyList<string> MissionChannels = new[] { "comms", "feed" };
    public static readonly IReadOnlyList<string> MissionStates = new[] { "locked", "offered", "active", "completed", "failed", "declined" };

    /// <summary>Binding references that always exist: the player's first base ("hub") and every finished base ("base").</summary>
    public static readonly IReadOnlyList<string> MissionBuiltInRefs = new[] { "base", "hub" };

    /// <summary>Stage targets that end the mission instead of naming a stage.</summary>
    public const string MissionComplete = "complete", MissionFail = "fail";

    private static readonly Regex Placeholder = new(@"\{([a-z_][a-z0-9_]*)(?:\.([a-z_]+))?\}", RegexOptions.CultureInvariant);

    /// <summary>The placeholders in a text: (binding, property or "").</summary>
    public static IEnumerable<(string Name, string Prop)> MissionPlaceholders(string text)
    {
        foreach (Match m in Placeholder.Matches(text ?? "")) yield return (m.Groups[1].Value, m.Groups[2].Value);
    }

    /// <summary>Placeholder properties a text may ask a binding for.</summary>
    public static readonly IReadOnlyList<string> MissionPlaceholderProps = new[] { "", "tile", "distance", "res", "name", "count" };

    /// <summary>Whether every id the mission needs exists in this data set (a mission whose needs are missing is left out
    /// of the game, e.g. drone missions before drones exist).</summary>
    public bool MissionAvailable(MissionDef m) => m.Needs.All(KnowsId);

    private bool KnowsId(string id) => _buildings.ContainsKey(id) || _surfaceUnits.ContainsKey(id) || _resources.ContainsKey(id) || _techs.ContainsKey(id);

    // ---------------------------------------------------------------- reading

    private static MissionData ReadMissions(JsonElement root, List<string> problems)
    {
        string S(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        int I(JsonElement e, string p, int fallback) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : fallback;

        var rules = new MissionRules(2, 90, 60, Fix64.FromInt(2400), Array.Empty<string>());
        if (root.TryGetProperty("rules", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            var rich = r.TryGetProperty("rich_deposit", out var rd) && TryFix(rd.GetRawText(), out var rv) ? rv : Fix64.FromInt(2400);
            var kinds = r.TryGetProperty("site_kinds", out var sk) && sk.ValueKind == JsonValueKind.Array
                ? sk.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
            rules = new MissionRules(I(r, "max_side_active", 2), I(r, "offer_seconds", 90), I(r, "message_log", 60), rich, kinds);
        }
        var givers = root.TryGetProperty("givers", out var gs) && gs.ValueKind == JsonValueKind.Array
            ? gs.EnumerateArray().Select(g => new MissionGiverDef(S(g, "id"), S(g, "name"), S(g, "role"), S(g, "initials"))).ToList()
            : new List<MissionGiverDef>();
        var chains = root.TryGetProperty("chains", out var cs) && cs.ValueKind == JsonValueKind.Array
            ? cs.EnumerateArray().Select(c => new MissionChainDef(S(c, "id"), S(c, "name"), S(c, "era"))).ToList()
            : new List<MissionChainDef>();
        var list = new List<MissionDef>();
        if (root.TryGetProperty("missions", out var ms) && ms.ValueKind == JsonValueKind.Array)
            foreach (var m in ms.EnumerateArray())
            {
                var id = S(m, "id");
                var where = $"Mission {id}";
                MissionCondDef? trigger = m.TryGetProperty("trigger", out var tr) ? ReadMissionCond(tr, where + " trigger", problems) : null;
                var stages = new List<MissionStageDef>();
                if (m.TryGetProperty("stages", out var st) && st.ValueKind == JsonValueKind.Array)
                    foreach (var s in st.EnumerateArray())
                    {
                        var sw = $"{where} stage {S(s, "id")}";
                        var objectives = new List<MissionObjectiveDef>();
                        if (s.TryGetProperty("objectives", out var os) && os.ValueKind == JsonValueKind.Array)
                            foreach (var o in os.EnumerateArray())
                            {
                                var cond = o.TryGetProperty("cond", out var c) ? ReadMissionCond(c, $"{sw} objective {S(o, "id")}", problems) : null;
                                if (cond == null)
                                {
                                    problems.Add($"{sw} objective {S(o, "id")}: needs a cond.");
                                    continue;
                                }
                                objectives.Add(new MissionObjectiveDef(S(o, "id"), S(o, "text"), cond,
                                    o.TryGetProperty("optional", out var op) && op.ValueKind == JsonValueKind.True, I(o, "hold_seconds", 0), S(o, "hint")));
                            }
                        var choices = s.TryGetProperty("choices", out var ch) && ch.ValueKind == JsonValueKind.Array
                            ? ch.EnumerateArray().Select(x => new MissionChoiceDef(S(x, "id"), S(x, "text"), S(x, "next"))).ToList()
                            : new List<MissionChoiceDef>();
                        var timeout = S(s, "on_timeout");
                        stages.Add(new MissionStageDef(S(s, "id"), S(s, "title"), ReadMissionActions(s, "on_enter", sw, problems), objectives, choices,
                            S(s, "next"), I(s, "timer_seconds", 0), timeout.Length > 0 ? timeout : MissionFail, S(s, "hint")));
                    }
                var needs = m.TryGetProperty("needs", out var nd) && nd.ValueKind == JsonValueKind.Array
                    ? nd.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
                list.Add(new MissionDef(id, S(m, "chain"), S(m, "kind"), S(m, "title"), S(m, "giver"), S(m, "briefing"), S(m, "debrief"),
                    trigger, stages, ReadMissionActions(m, "rewards", where, problems), needs, I(m, "offer_seconds", 0)));
            }
        return new MissionData(rules, givers, chains, list);
    }

    private static List<MissionActionDef> ReadMissionActions(JsonElement owner, string property, string where, List<string> problems)
    {
        var list = new List<MissionActionDef>();
        if (!owner.TryGetProperty(property, out var arr)) return list;
        if (arr.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{where}: {property} must be a list of actions.");
            return list;
        }
        foreach (var a in arr.EnumerateArray())
        {
            if (a.ValueKind != JsonValueKind.Object || a.EnumerateObject().Count() != 1)
            {
                problems.Add($"{where} {property}: each action is an object with one key (its kind).");
                continue;
            }
            var p = a.EnumerateObject().First();
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var amounts = new List<(string, Fix64)>();
            if (p.Value.ValueKind == JsonValueKind.Object)
                foreach (var f in p.Value.EnumerateObject())
                {
                    if (p.Name == "grant")
                    {
                        if (f.Value.ValueKind == JsonValueKind.Number && TryFix(f.Value.GetRawText(), out var n)) amounts.Add((f.Name, n));
                        else problems.Add($"{where} grant: {f.Name} must be a number.");
                        continue;
                    }
                    ReadField(f, fields, lists, $"{where} {p.Name}", problems);
                }
            else if (p.Value.ValueKind != JsonValueKind.True && p.Value.ValueKind != JsonValueKind.Null)
                problems.Add($"{where} {p.Name}: its fields must be an object.");
            list.Add(new MissionActionDef(p.Name, fields, lists, amounts));
        }
        return list;
    }

    private static MissionCondDef? ReadMissionCond(JsonElement c, string where, List<string> problems)
    {
        if (c.ValueKind != JsonValueKind.Object || c.EnumerateObject().Count() != 1)
        {
            problems.Add($"{where}: a condition is an object with one key (its kind).");
            return null;
        }
        var p = c.EnumerateObject().First();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var children = new List<MissionCondDef>();
        if (p.Name is "all" or "any")
        {
            if (p.Value.ValueKind != JsonValueKind.Array) problems.Add($"{where}: {p.Name} takes a list of conditions.");
            else
                foreach (var x in p.Value.EnumerateArray())
                    if (ReadMissionCond(x, where, problems) is { } child) children.Add(child);
        }
        else if (p.Name == "not")
        {
            if (ReadMissionCond(p.Value, where, problems) is { } child) children.Add(child);
        }
        else if (p.Value.ValueKind == JsonValueKind.Object)
            foreach (var f in p.Value.EnumerateObject()) ReadField(f, fields, lists, $"{where} {p.Name}", problems);
        else if (p.Value.ValueKind != JsonValueKind.True)
            problems.Add($"{where} {p.Name}: its fields must be an object.");
        return new MissionCondDef(p.Name, fields, lists, children);
    }

    private static void ReadField(JsonProperty f, Dictionary<string, string> fields, Dictionary<string, IReadOnlyList<string>> lists, string where, List<string> problems)
    {
        switch (f.Value.ValueKind)
        {
            case JsonValueKind.String:
                fields[f.Name] = f.Value.GetString()!;
                break;
            case JsonValueKind.Number:
                fields[f.Name] = f.Value.GetRawText();
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                fields[f.Name] = f.Value.ValueKind == JsonValueKind.True ? "true" : "false";
                break;
            case JsonValueKind.Array when f.Value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String):
                lists[f.Name] = f.Value.EnumerateArray().Select(x => x.GetString()!).ToList();
                break;
            default:
                problems.Add($"{where}: {f.Name} must be a string, number, true/false or a list of strings.");
                break;
        }
    }

    // ---------------------------------------------------------------- checks

    private void ValidateMissions(List<string> problems)
    {
        if (Missions is not { } data) return;
        const string w = "Missions";
        var rules = data.Rules;
        if (rules.MaxSideActive < 0) problems.Add($"{w} rules: max_side_active must be 0 or more.");
        if (rules.OfferSeconds <= 0) problems.Add($"{w} rules: offer_seconds must be above 0.");
        if (rules.MessageLog <= 0) problems.Add($"{w} rules: message_log must be above 0.");
        if (rules.RichDeposit <= Fix64.Zero) problems.Add($"{w} rules: rich_deposit must be above 0.");
        if (rules.SiteKinds.Count == 0) problems.Add($"{w} rules: site_kinds is empty.");
        foreach (var k in rules.SiteKinds)
            if (!SnakeId.IsMatch(k)) problems.Add($"{w} rules: site kind '{k}' must be a lower-case id.");
        Unique(data.Givers.Select(g => g.Id), "giver", problems);
        Unique(data.Chains.Select(c => c.Id), "chain", problems);
        Unique(data.List.Select(m => m.Id), "mission", problems);
        foreach (var g in data.Givers)
            if (!SnakeId.IsMatch(g.Id) || g.Name.Length == 0) problems.Add($"{w} giver '{g.Id}': needs a lower-case id and a name.");
        foreach (var c in data.Chains)
        {
            if (!SnakeId.IsMatch(c.Id) || c.Name.Length == 0) problems.Add($"{w} chain '{c.Id}': needs a lower-case id and a name.");
            if (c.Era.Length > 0 && Eras.Count > 0 && Era(c.Era) == null) problems.Add($"{w} chain {c.Id}: unknown era '{c.Era}'.");
            if (!data.List.Any(m => m.Chain == c.Id && m.IsMain)) problems.Add($"{w} chain {c.Id}: has no main mission.");
        }
        foreach (var m in data.List) ValidateMission(data, m, problems);
    }

    private static void Unique(IEnumerable<string> ids, string what, List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (!seen.Add(id)) problems.Add($"Missions: duplicate {what} id '{id}'.");
    }

    private void ValidateMission(MissionData data, MissionDef m, List<string> problems)
    {
        var w = $"Mission {m.Id}";
        if (!SnakeId.IsMatch(m.Id)) problems.Add($"{w}: the id must be lower-case letters, digits and _.");
        if (m.Kind is not ("main" or "side")) problems.Add($"{w}: kind must be main or side.");
        if (data.Chain(m.Chain) == null) problems.Add($"{w}: unknown chain '{m.Chain}'.");
        if (data.Giver(m.Giver) == null) problems.Add($"{w}: unknown giver '{m.Giver}'.");
        if (m.Title.Length == 0 || m.Briefing.Length == 0 || m.Debrief.Length == 0) problems.Add($"{w}: needs a title, a briefing and a debrief.");
        if (m.OfferSeconds < 0) problems.Add($"{w}: offer_seconds must be 0 (the rules' default) or more.");
        if (m.Kind == "side" && m.Trigger == null) problems.Add($"{w}: a side job needs a trigger (when it is offered).");
        foreach (var n in m.Needs)
            if (!SnakeId.IsMatch(n)) problems.Add($"{w}: needs '{n}' is not an id.");
        if (m.Stages.Count == 0)
        {
            problems.Add($"{w}: needs at least one stage.");
            return;
        }
        // Ids of stages, and where each stage can go.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in m.Stages)
        {
            if (!SnakeId.IsMatch(s.Id) || s.Id is MissionComplete or MissionFail) problems.Add($"{w}: stage id '{s.Id}' must be a lower-case id (not complete/fail).");
            if (!ids.Add(s.Id)) problems.Add($"{w}: duplicate stage '{s.Id}'.");
        }
        bool Target(string t) => t == MissionComplete || t == MissionFail || ids.Contains(t);
        foreach (var s in m.Stages)
        {
            var sw = $"{w} stage {s.Id}";
            if (s.Title.Length == 0) problems.Add($"{sw}: needs a title.");
            if (s.Objectives.Count == 0 && s.Choices.Count == 0) problems.Add($"{sw}: needs an objective or a choice (a stage must wait for something).");
            if (s.Objectives.Count > 0 && s.Objectives.All(o => o.Optional)) problems.Add($"{sw}: every objective is optional, so it would finish at once.");
            if (s.Objectives.Count > 0 && !Target(s.Next)) problems.Add($"{sw}: next '{s.Next}' is not a stage, complete or fail.");
            if (s.Objectives.Count == 0 && s.Next.Length > 0 && !Target(s.Next)) problems.Add($"{sw}: next '{s.Next}' is not a stage, complete or fail.");
            if (s.TimerSeconds < 0) problems.Add($"{sw}: timer_seconds must be 0 (none) or more.");
            if (s.TimerSeconds > 0 && !Target(s.OnTimeout)) problems.Add($"{sw}: on_timeout '{s.OnTimeout}' is not a stage, complete or fail.");
            if (s.Choices.Count == 1 || s.Choices.Count > 4) problems.Add($"{sw}: a branch has 2 to 4 choices.");
            var oids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in s.Objectives)
            {
                if (!SnakeId.IsMatch(o.Id) || !oids.Add(o.Id)) problems.Add($"{sw}: objective id '{o.Id}' must be a unique lower-case id.");
                if (o.Text.Length == 0) problems.Add($"{sw} objective {o.Id}: needs a text.");
                if (o.HoldSeconds < 0 || o.HoldSeconds > AchievementMaxSeconds) problems.Add($"{sw} objective {o.Id}: hold_seconds must be 0..{AchievementMaxSeconds}.");
            }
            var cids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in s.Choices)
            {
                if (!SnakeId.IsMatch(c.Id) || c.Id is "accept" or "decline" || !cids.Add(c.Id)) problems.Add($"{sw}: choice id '{c.Id}' must be a unique lower-case id (not accept/decline).");
                if (c.Text.Length == 0) problems.Add($"{sw} choice {c.Id}: needs a text.");
                if (!Target(c.Next)) problems.Add($"{sw} choice {c.Id}: next '{c.Next}' is not a stage, complete or fail.");
            }
        }
        // Every stage reachable from the first; complete reachable from the first.
        var reach = new HashSet<string>(StringComparer.Ordinal) { m.Stages[0].Id };
        var queue = new Queue<string>(reach);
        var canComplete = false;
        while (queue.Count > 0)
        {
            var s = m.Stages[m.StageIndex(queue.Dequeue())];
            foreach (var t in Exits(s))
            {
                if (t == MissionComplete) canComplete = true;
                if (ids.Contains(t) && reach.Add(t)) queue.Enqueue(t);
            }
        }
        foreach (var s in m.Stages)
            if (!reach.Contains(s.Id)) problems.Add($"{w}: stage {s.Id} can't be reached from the first stage.");
        if (!canComplete) problems.Add($"{w}: no way through the stages reaches complete.");
        // Rewards never complete / fail themselves, and never energy.
        foreach (var a in m.Rewards)
            if (a.Kind is "complete" or "fail") problems.Add($"{w} rewards: {a.Kind} is not a reward.");

        // Bindings: what the trigger binds, then each stage's on_enter (in order); a reference must be bound before it.
        var available = MissionAvailable(m);
        var bound = new List<string>(MissionBuiltInRefs);
        if (m.Trigger != null) ValidateMissionCond(data, m.Trigger, $"{w} trigger", bound, available, problems, true);
        CheckText(m.Briefing, $"{w} briefing", bound, problems);
        foreach (var s in m.Stages)
        {
            var sw = $"{w} stage {s.Id}";
            foreach (var a in s.OnEnter) ValidateMissionAction(data, a, $"{sw} on_enter", bound, available, problems);
            foreach (var o in s.Objectives)
            {
                ValidateMissionCond(data, o.Cond, $"{sw} objective {o.Id}", bound, available, problems, false);
                CheckText(o.Text, $"{sw} objective {o.Id}", bound, problems);
                CheckText(o.Hint, $"{sw} objective {o.Id} hint", bound, problems);
            }
        }
        foreach (var a in m.Rewards) ValidateMissionAction(data, a, $"{w} rewards", bound, available, problems);
        CheckText(m.Debrief, $"{w} debrief", bound, problems);
    }

    private static IEnumerable<string> Exits(MissionStageDef s)
    {
        if (s.Objectives.Count > 0 || s.Next.Length > 0) yield return s.Next;
        foreach (var c in s.Choices) yield return c.Next;
        if (s.TimerSeconds > 0) yield return s.OnTimeout;
    }

    private static void CheckText(string text, string where, List<string> bound, List<string> problems)
    {
        foreach (var (name, prop) in MissionPlaceholders(text))
        {
            if (!bound.Contains(name)) problems.Add($"{where}: placeholder {{{name}}} is not bound yet.");
            if (!MissionPlaceholderProps.Contains(prop)) problems.Add($"{where}: placeholder {{{name}.{prop}}}: unknown property (one of tile, distance, res, name, count).");
        }
    }

    private void CheckRef(string? reference, string where, List<string> bound, List<string> problems, bool required = true)
    {
        if (reference == null)
        {
            if (required) problems.Add($"{where}: needs at (a binding, base or hub).");
            return;
        }
        if (!bound.Contains(reference)) problems.Add($"{where}: '{reference}' is not bound yet (bind it in the trigger or an earlier on_enter).");
    }

    private void CheckFields(string kind, IReadOnlyDictionary<string, string> fields, IReadOnlyDictionary<string, IReadOnlyList<string>> lists,
        IReadOnlyDictionary<string, string[]> vocabulary, string where, List<string> problems)
    {
        if (!vocabulary.TryGetValue(kind, out var allowed))
        {
            problems.Add($"{where}: unknown kind '{kind}'.");
            return;
        }
        foreach (var f in fields.Keys.Concat(lists.Keys))
            if (!allowed.Contains(f)) problems.Add($"{where} {kind}: unknown field '{f}'.");
    }

    private void CheckNumber(IReadOnlyDictionary<string, string> fields, string field, string where, List<string> problems, bool whole = true, bool positive = false)
    {
        if (!fields.TryGetValue(field, out var text)) return;
        if (!TryFix(text, out var n)) problems.Add($"{where}: {field} must be a number.");
        else if (n < Fix64.Zero || (positive && n <= Fix64.Zero)) problems.Add($"{where}: {field} must be {(positive ? "above" : "at least")} 0.");
        else if (whole && n != Fix64.FromInt(n.FloorToInt())) problems.Add($"{where}: {field} must be a whole number.");
    }

    private void CheckOne(IReadOnlyDictionary<string, string> fields, string field, IReadOnlyList<string> allowed, string where, List<string> problems)
    {
        if (fields.TryGetValue(field, out var v) && !allowed.Contains(v)) problems.Add($"{where}: {field} '{v}' is not one of {string.Join(", ", allowed)}.");
    }

    private void ValidateMissionCond(MissionData data, MissionCondDef c, string where, List<string> bound, bool available, List<string> problems, bool trigger)
    {
        CheckFields(c.Kind, c.Fields, c.Lists, MissionConditionFields, where, problems);
        var cw = $"{where} {c.Kind}";
        foreach (var f in new[] { "count", "radius", "far_from_pad", "within_seconds", "after_seconds", "before_seconds" }) CheckNumber(c.Fields, f, cw, problems);
        foreach (var f in new[] { "amount", "extracted" }) CheckNumber(c.Fields, f, cw, problems, whole: false);
        if (c.F("share") is { } share && (!TryFix(share, out var sv) || sv <= Fix64.Zero || sv > Fix64.One)) problems.Add($"{cw}: share must be above 0 and at most 1.");
        if (c.F("bind") is { } bind)
        {
            if (!trigger) problems.Add($"{cw}: only a trigger's conditions bind (bind '{bind}').");
            else if (!SnakeId.IsMatch(bind) || MissionBuiltInRefs.Contains(bind)) problems.Add($"{cw}: bind '{bind}' must be a lower-case id (not base/hub).");
            else if (!bound.Contains(bind)) bound.Add(bind);
        }
        bool Building(string id) => _buildings.ContainsKey(id);
        bool Unit(string id) => _surfaceUnits.ContainsKey(id);
        bool Res(string id) => _resources.ContainsKey(id);
        void Ids(Func<string, bool> exists, string what)
        {
            if (!available) return; // left out of this data set anyway
            var list = c.L("ids").ToList();
            if (c.F("id") is { } one) list.Add(one);
            if (list.Count == 0) problems.Add($"{cw}: needs id or ids.");
            foreach (var id in list)
                if (!exists(id)) problems.Add($"{cw}: unknown {what} '{id}'.");
        }
        switch (c.Kind)
        {
            case "all":
            case "any":
                if (c.Children.Count == 0) problems.Add($"{cw}: needs at least one condition.");
                break;
            case "not":
                if (c.Children.Count != 1) problems.Add($"{cw}: takes exactly one condition.");
                break;
            case "resource":
                if (available && (c.F("res") is not { } r || !Res(r))) problems.Add($"{cw}: unknown resource '{c.F("res")}'.");
                if (c.F("amount") == null) problems.Add($"{cw}: needs amount.");
                break;
            case "building":
                Ids(Building, "building");
                CheckOne(c.Fields, "state", MissionBuildingStates, cw, problems);
                if (c.F("at") is { } bat) CheckRef(bat, cw, bound, problems);
                break;
            case "unit":
                Ids(Unit, "surface unit");
                CheckOne(c.Fields, "state", MissionUnitStates, cw, problems);
                if (c.F("at") is { } uat) CheckRef(uat, cw, bound, problems);
                break;
            case "surveyed":
                if (c.F("at") is { } sat) CheckRef(sat, cw, bound, problems);
                else if (c.F("count") == null) problems.Add($"{cw}: needs count or at.");
                break;
            case "deposits_found":
                if (available && c.F("res") is { } dr && !Res(dr)) problems.Add($"{cw}: unknown resource '{dr}'.");
                break;
            case "deposit":
            case "drone_at":
            case "covered":
            case "near":
                CheckRef(c.F("at"), cw, bound, problems);
                if (c.Kind == "deposit" && c.F("extracted") == null) problems.Add($"{cw}: needs extracted.");
                break;
            case "site":
                CheckRef(c.F("site"), cw, bound, problems);
                CheckOne(c.Fields, "state", MissionSiteStates, cw, problems);
                break;
            case "relay":
                if (c.F("covers") is { } cov) CheckRef(cov, cw, bound, problems);
                break;
            case "fabricated":
                if (available && (c.F("item") is not { } item || !Res(item))) problems.Add($"{cw}: unknown item '{c.F("item")}'.");
                break;
            case "weather":
            case "exposed":
                if (c.L("kinds").Count == 0) problems.Add($"{cw}: needs kinds.");
                foreach (var k in c.L("kinds"))
                    if (!AllWeather.Any(x => WeatherId(x) == k)) problems.Add($"{cw}: unknown weather kind '{k}'.");
                if (c.F("near") is { } near) CheckRef(near, cw, bound, problems);
                break;
            case "time":
                CheckOne(c.Fields, "since", MissionTimeSince, cw, problems);
                if (c.F("after_seconds") == null && c.F("before_seconds") == null) problems.Add($"{cw}: needs after_seconds or before_seconds.");
                break;
            case "flag":
                if (c.F("name") is not { } fl || !SnakeId.IsMatch(fl)) problems.Add($"{cw}: needs a lower-case flag name.");
                break;
            case "mission":
                if (c.F("id") is not { } mid || data.Find(mid) == null) problems.Add($"{cw}: unknown mission '{c.F("id")}'.");
                CheckOne(c.Fields, "state", MissionStates, cw, problems);
                break;
            case "tech":
                if (available && (c.F("id") is not { } tid || !_techs.ContainsKey(tid))) problems.Add($"{cw}: unknown surface tech '{c.F("id")}'.");
                break;
        }
        foreach (var child in c.Children) ValidateMissionCond(data, child, where, bound, available, problems, trigger);
    }

    private void ValidateMissionAction(MissionData data, MissionActionDef a, string where, List<string> bound, bool available, List<string> problems)
    {
        CheckFields(a.Kind, a.Fields, a.Lists, MissionActionFields, where, problems);
        var aw = $"{where} {a.Kind}";
        foreach (var f in new[] { "count", "min", "max", "radius", "offset", "spacing", "points" }) CheckNumber(a.Fields, f, aw, problems, whole: f != "points");
        switch (a.Kind)
        {
            case "message":
                if (a.F("text") is not { Length: > 0 } text) problems.Add($"{aw}: needs a text.");
                else CheckText(text, aw, bound, problems);
                if (a.F("giver") is { } g && data.Giver(g) == null) problems.Add($"{aw}: unknown giver '{g}'.");
                CheckOne(a.Fields, "channel", MissionChannels, aw, problems);
                break;
            case "grant":
                if (a.Amounts.Count == 0) problems.Add($"{aw}: grants nothing.");
                foreach (var (res, amount) in a.Amounts)
                {
                    if (amount <= Fix64.Zero) problems.Add($"{aw}: {res} must be above 0.");
                    if (res == PowerResource) problems.Add($"{aw}: never {res} (power is a per-base budget, not a stockpile).");
                    else if (available && !_resources.ContainsKey(res)) problems.Add($"{aw}: unknown resource '{res}'.");
                }
                break;
            case "research":
                if (a.F("points") == null) problems.Add($"{aw}: needs points.");
                break;
            case "reveal":
                CheckRef(a.F("at"), aw, bound, problems);
                break;
            case "place_site":
            case "place_tiles":
            {
                if (a.Kind == "place_site" && (a.F("kind") is not { } k || !data.Rules.SiteKinds.Contains(k)))
                    problems.Add($"{aw}: kind '{a.F("kind")}' is not one of the rules' site_kinds.");
                CheckOne(a.Fields, "terrain", MissionTerrains, aw, problems);
                var min = a.I("min", 0);
                var max = a.I("max", 0);
                if (max <= 0 || max < min) problems.Add($"{aw}: needs max above 0 and at least min.");
                if (a.F("near") is { } near) CheckRef(near, aw, bound, problems);
                if (a.F("bind") is not { } b || !SnakeId.IsMatch(b) || MissionBuiltInRefs.Contains(b)) problems.Add($"{aw}: needs bind (a lower-case id, not base/hub).");
                else if (!bound.Contains(b)) bound.Add(b);
                break;
            }
            case "marker":
                CheckRef(a.F("at"), aw, bound, problems);
                CheckOne(a.Fields, "style", MissionMarkerStyles, aw, problems);
                CheckText(a.F("label") ?? "", aw, bound, problems);
                break;
            case "set_flag":
                if (a.F("name") is not { } fl || !SnakeId.IsMatch(fl)) problems.Add($"{aw}: needs a lower-case flag name.");
                break;
            case "start_mission":
                if (a.F("id") is not { } mid || data.Find(mid) == null) problems.Add($"{aw}: unknown mission '{a.F("id")}'.");
                break;
        }
    }
}
