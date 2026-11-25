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
        // Space chains (docs/ship-pick-contract.md; Missions/MissionSpace.cs): the player's flagship in the star system.
        ["ship_at"] = new[] { "body", "hold_seconds" },
        ["visited"] = new[] { "count" },
        ["charted"] = new[] { "count", "share" },
        ["scanned"] = new[] { "count" },
        ["swept"] = new[] { "body", "count" },
        ["system_satellites"] = new[] { "count" },
        ["cargo"] = new[] { "id", "state", "count" },
        ["with_ship"] = new[] { "ship", "hold_seconds", "at_arrival" },
        ["contact"] = new[] { "body", "stage" },
        // Space jobs (docs/space-jobs-contract.md; Missions/MissionSpaceJobs.cs): space sites and the flagship's status.
        ["space_site"] = new[] { "site", "state", "kind", "kinds", "variant", "variants", "spent", "bind", "count", "body" },
        ["space_tally"] = new[] { "key", "keys", "count" },
        ["hull"] = new[] { "below", "at_least" },
        ["crew"] = new[] { "division", "at_least", "below" },
        ["hold"] = new[] { "item", "count" },
    };

    /// <summary>space_site states: detected / identified (what the player knows), at (the flagship holds there),
    /// alongside, powered, logs (a known derelict), boarded (an away team came back from it), swept (a debris field), done
    /// (worked out: Spent); phase 2: mined (an asteroid mined out), towing (in tow behind the flagship now), towed (delivered
    /// at a station or the colony); phase 3 (events): deflected (a rogue asteroid turned aside by the player), sampled (a
    /// comet's tail), picked (lifepods picked up: count), escorting / escorted (a stranded ship under escort now / brought in
    /// by the player), recorded (research recorded at an anomaly: count). A trigger with bind may only ask detected or
    /// identified. An active pool job fails when its stage's event state can no longer come true.</summary>
    public static readonly IReadOnlyList<string> MissionSpaceSiteStates = new[] { "detected", "identified", "at", "alongside", "powered", "logs", "boarded", "swept", "done", "mined", "towing", "towed",
        "deflected", "sampled", "picked", "escorting", "escorted", "recorded", "inspected", "decoded", "answered", "answered_well", "delivered" };

    /// <summary>space_tally key prefixes: sold (ore or salvage sold: sold:ore, sold:ore:deuterium), drop (ore dropped at the
    /// colony or a trade partner's world), mined (ore mined: mined:ore:…), tow (tows delivered: tow:station, tow:colony).</summary>
    public static readonly IReadOnlyList<string> MissionTallyKinds = new[] { "sold", "drop", "mined", "tow" };

    /// <summary>bind_site picks: nearest (to the flagship) or random (by the hash) of the sites of that kind the player knows.</summary>
    public static readonly IReadOnlyList<string> MissionSitePicks = new[] { "nearest", "random" };

    /// <summary>place_space_site near: the flagship (ship), the colony's body (colony), the star past the outermost orbit
    /// (edge: min and max are measured beyond it), the belt nearest the flagship (belt; the outermost planet when there is
    /// none), or a body or space-site binding.</summary>
    public static readonly IReadOnlyList<string> MissionSiteNear = new[] { "ship", "colony", "edge", "belt" };

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
        ["unlock_buildings"] = new[] { "ids" },
        ["complete"] = Array.Empty<string>(),
        ["fail"] = Array.Empty<string>(),
        // Space chains (Missions/MissionSpace.cs).
        ["bind_body"] = new[] { "as", "pick", "exclude", "near" },
        ["bind_ship"] = new[] { "as", "pick" },
        ["place_beacon"] = new[] { "at", "label" },
        ["load_cargo"] = new[] { "id", "label", "from", "to" },
        // Space jobs (Missions/MissionSpaceJobs.cs).
        ["place_space_site"] = new[] { "kind", "variant", "bind", "near", "min", "max", "again", "pods", "hold_room", "target", "cold_reactor", "known" },
        ["hand_over"] = new[] { "site" },
        ["civ"] = new[] { "at", "awareness", "incident", "settle" },
        ["bind_site"] = new[] { "as", "kind", "pick" },
        ["damage"] = new[] { "hull", "system", "seconds" },
        ["lose_crew"] = new[] { "division", "count" },
        ["give_crew"] = new[] { "division", "count" },
        ["hold_add"] = new[] { "item", "count" },
    };

    /// <summary>bind_body picks: farthest = the planet or belt farthest from the star; outermost_moon = the moon of the
    /// outermost planet that has one; gas_or_ice = the nearest gas giant or ice body (not yet Charted first; the biggest
    /// bodies in a system without one); civ = the
    /// living civilization's world; random_other = any other body, by the hash; nearest_other = the nearest other body;
    /// here = where the flagship is. Every pick but here skips the flagship's body and the excluded ones.</summary>
    public static readonly IReadOnlyList<string> MissionBodyPicks = new[] { "farthest", "outermost_moon", "gas_or_ice", "civ", "random_other", "nearest_other", "here",
        "nearest_planet", "inhabited" };

    /// <summary>bind_ship picks: freighter = a civilian freighter of the traffic; departing = the civilian ship whose next
    /// arrival the flagship can beat by the most.</summary>
    public static readonly IReadOnlyList<string> MissionShipPicks = new[] { "freighter", "departing" };

    public static readonly IReadOnlyList<string> MissionCargoStates = new[] { "aboard", "delivered" };
    public static readonly IReadOnlyList<string> MissionContactStages = new[] { "post", "studied", "met" };

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
            ? cs.EnumerateArray().Select(c => new MissionChainDef(S(c, "id"), S(c, "name"), S(c, "era"),
                c.ValueKind == JsonValueKind.Object && c.TryGetProperty("space", out var sp) && sp.ValueKind == JsonValueKind.True)
            {
                Pool = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("pool", out var pl) && pl.ValueKind == JsonValueKind.True,
                ReturnSeconds = I(c, "return_seconds", 0),
            }).ToList()
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
                                // A space condition's hold_seconds (ship_at, with_ship) is the objective's when it has none of its own.
                                var hold = I(o, "hold_seconds", 0);
                                if (hold == 0 && cond.F("hold_seconds") is { } condHold && TryFix(condHold, out var hv)) hold = hv.FloorToInt();
                                objectives.Add(new MissionObjectiveDef(S(o, "id"), S(o, "text"), cond,
                                    o.TryGetProperty("optional", out var op) && op.ValueKind == JsonValueKind.True, hold, S(o, "hint")));
                            }
                        var choices = s.TryGetProperty("choices", out var ch) && ch.ValueKind == JsonValueKind.Array
                            ? ch.EnumerateArray().Select(x => new MissionChoiceDef(S(x, "id"), S(x, "text"), S(x, "next"))).ToList()
                            : new List<MissionChoiceDef>();
                        var timeout = S(s, "on_timeout");
                        stages.Add(new MissionStageDef(S(s, "id"), S(s, "title"), ReadMissionActions(s, "on_enter", sw, problems), objectives, choices,
                            S(s, "next"), I(s, "timer_seconds", 0), timeout.Length > 0 ? timeout : MissionFail, S(s, "hint")) { OnLost = S(s, "on_lost") });
                    }
                var needs = m.TryGetProperty("needs", out var nd) && nd.ValueKind == JsonValueKind.Array
                    ? nd.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
                var imports = new List<(string, string)>();
                if (m.TryGetProperty("imports", out var im) && im.ValueKind == JsonValueKind.Array)
                    foreach (var x in im.EnumerateArray())
                    {
                        var text = x.GetString() ?? "";
                        var dot = text.IndexOf('.');
                        if (dot <= 0 || dot == text.Length - 1) problems.Add($"{where}: imports '{text}' must be mission_id.binding.");
                        else imports.Add((text[..dot], text[(dot + 1)..]));
                    }
                list.Add(new MissionDef(id, S(m, "chain"), S(m, "kind"), S(m, "title"), S(m, "giver"), S(m, "briefing"), S(m, "debrief"),
                    trigger, stages, ReadMissionActions(m, "rewards", where, problems), needs, I(m, "offer_seconds", 0)) { Imports = imports });
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
            if (p.Name == "unlock_buildings" && p.Value.ValueKind == JsonValueKind.Array) // shorthand: { "unlock_buildings": ["science_lab"] }
            {
                if (p.Value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)) lists["ids"] = p.Value.EnumerateArray().Select(x => x.GetString()!).ToList();
                else problems.Add($"{where} unlock_buildings: a list of building ids.");
            }
            else if (p.Value.ValueKind == JsonValueKind.Object)
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
            if (c.Pool)
            {
                if (c.Space) problems.Add($"{w} chain {c.Id}: a pool is offered to every chain; it can't be a space chain too.");
                if (data.List.Any(m => m.Chain == c.Id && m.IsMain)) problems.Add($"{w} chain {c.Id}: a pool holds side jobs only.");
                if (data.Chains.Count > 0 && data.Chains[0] == c) problems.Add($"{w} chain {c.Id}: a pool can't be the first chain (the default a game plays).");
                if (c.ReturnSeconds < 0 || c.ReturnSeconds > 36000) problems.Add($"{w} chain {c.Id}: return_seconds must be 0..36000.");
            }
            else if (c.ReturnSeconds != 0) problems.Add($"{w} chain {c.Id}: return_seconds is for a pool."); 
            else if (!data.List.Any(m => m.Chain == c.Id && m.IsMain)) problems.Add($"{w} chain {c.Id}: has no main mission.");
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
            if (s.OnLost.Length > 0 && !Target(s.OnLost)) problems.Add($"{sw}: on_lost '{s.OnLost}' is not a stage, complete or fail.");
            if (s.OnLost.Length > 0 && m.Kind != "main") problems.Add($"{sw}: on_lost is for main missions (a side job fails when its site is lost).");
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
        foreach (var (from, name) in m.Imports)
        {
            if (data.Find(from) is not { } source || source == m) problems.Add($"{w}: imports from unknown mission '{from}'.");
            else if (!Binds(source).Contains(name)) problems.Add($"{w}: mission {from} never binds '{name}'.");
            else
            {
                foreach (var a in source.Stages.SelectMany(s => s.OnEnter))
                    if (a.Kind is "bind_body" or "bind_ship" && a.F("as") == name) bound.Add((a.Kind == "bind_body" ? BodyRefTag : ShipRefTag) + name);
                    else if ((a.Kind == "bind_site" && a.F("as") == name) || (a.Kind == "place_space_site" && a.F("bind") == name)) bound.Add(SiteRefTag + name);
                if (SpaceSiteTriggerBinds(source.Trigger, name)) bound.Add(SiteRefTag + name);
            }
            if (!bound.Contains(name)) bound.Add(name);
        }
        if (m.Trigger != null) ValidateMissionCond(data, m.Trigger, $"{w} trigger", bound, available, problems, true);
        var space = data.Chain(m.Chain) is { Space: true }; // a space chain briefs after the first stage's on_enter
        if (!space) CheckText(m.Briefing, $"{w} briefing", bound, problems);
        foreach (var s in m.Stages)
        {
            var sw = $"{w} stage {s.Id}";
            foreach (var a in s.OnEnter) ValidateMissionAction(data, a, $"{sw} on_enter", bound, available, problems);
            if (space && s == m.Stages[0]) CheckText(m.Briefing, $"{w} briefing", bound, problems);
            if (MissionPlaceholders(s.Title).Any()) problems.Add($"{sw}: a stage title can't have placeholders (it isn't filled).");
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

    /// <summary>Every binding name a mission makes (trigger binds, place_site / place_tiles).</summary>
    public static List<string> Binds(MissionDef m)
    {
        var names = new List<string>();
        void Cond(MissionCondDef? c)
        {
            if (c == null) return;
            if (c.F("bind") is { } b) names.Add(b);
            foreach (var x in c.Children) Cond(x);
        }
        Cond(m.Trigger);
        foreach (var s in m.Stages)
            foreach (var a in s.OnEnter)
                if (a.Kind is "place_site" or "place_tiles" or "place_space_site" && a.F("bind") is { } b) names.Add(b);
                else if (a.Kind is "bind_body" or "bind_ship" or "bind_site" && a.F("as") is { } n) names.Add(n);
        foreach (var (_, name) in m.Imports) names.Add(name);
        return names;
    }

    private static IEnumerable<string> Exits(MissionStageDef s)
    {
        if (s.Objectives.Count > 0 || s.Next.Length > 0) yield return s.Next;
        foreach (var c in s.Choices) yield return c.Next;
        if (s.TimerSeconds > 0) yield return s.OnTimeout;
        if (s.OnLost.Length > 0) yield return s.OnLost;
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

    /// <summary>Tags in the bound list marking a binding's kind (bind_body / bind_ship / a space site).</summary>
    private const string BodyRefTag = "@body:", ShipRefTag = "@ship:", SiteRefTag = "@site:";

    /// <summary>Whether a trigger binds this name with a space_site condition.</summary>
    private static bool SpaceSiteTriggerBinds(MissionCondDef? c, string name)
    {
        if (c == null) return false;
        if (c.Kind == "space_site" && c.F("bind") == name) return true;
        foreach (var x in c.Children)
            if (SpaceSiteTriggerBinds(x, name)) return true;
        return false;
    }

    /// <summary>A space reference: bound before, and by bind_body (a body) or bind_ship (a ship).</summary>
    private void CheckSpaceRef(string? reference, string tag, string where, List<string> bound, List<string> problems)
    {
        var what = tag == BodyRefTag ? "a body (bind it with bind_body)" : tag == SiteRefTag ? "a space site (bind it with a space_site trigger, bind_site or place_space_site)" : "a ship (bind it with bind_ship)";
        if (reference == null)
        {
            problems.Add($"{where}: needs {what}.");
            return;
        }
        if (!bound.Contains(reference)) problems.Add($"{where}: '{reference}' is not bound yet (bind it in an earlier on_enter).");
        else if (!bound.Contains(tag + reference)) problems.Add($"{where}: '{reference}' is not {what}.");
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
        foreach (var f in new[] { "count", "radius", "far_from_pad", "within_seconds", "after_seconds", "before_seconds", "hold_seconds" }) CheckNumber(c.Fields, f, cw, problems);
        if (trigger && c.F("hold_seconds") != null) problems.Add($"{cw}: hold_seconds counts only on an objective's condition, not in a trigger.");
        foreach (var f in new[] { "amount", "extracted" }) CheckNumber(c.Fields, f, cw, problems, whole: false);
        if (c.F("share") is { } share && (!TryFix(share, out var sv) || sv <= Fix64.Zero || sv > Fix64.One)) problems.Add($"{cw}: share must be above 0 and at most 1.");
        if (c.F("bind") is { } bind)
        {
            if (!trigger) problems.Add($"{cw}: only a trigger's conditions bind (bind '{bind}').");
            else if (!SnakeId.IsMatch(bind) || MissionBuiltInRefs.Contains(bind)) problems.Add($"{cw}: bind '{bind}' must be a lower-case id (not base/hub).");
            else
            {
                if (!bound.Contains(bind)) bound.Add(bind);
                if (c.Kind == "space_site") bound.Add(SiteRefTag + bind);
            }
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
            case "ship_at":
                CheckSpaceRef(c.F("body"), BodyRefTag, cw, bound, problems);
                break;
            case "swept":
                if (c.F("body") is { } sb) CheckSpaceRef(sb, BodyRefTag, cw, bound, problems);
                break;
            case "charted":
                if (c.F("count") != null && c.F("share") != null) problems.Add($"{cw}: count or share, not both.");
                break;
            case "cargo":
                if (c.F("id") is not { } cid || !SnakeId.IsMatch(cid)) problems.Add($"{cw}: needs id (the load_cargo id it counts).");
                CheckOne(c.Fields, "state", MissionCargoStates, cw, problems);
                break;
            case "with_ship":
                CheckSpaceRef(c.F("ship"), ShipRefTag, cw, bound, problems);
                if (c.F("at_arrival") is { } aa && aa is not ("true" or "false")) problems.Add($"{cw}: at_arrival is true or false.");
                break;
            case "contact":
                CheckSpaceRef(c.F("body"), BodyRefTag, cw, bound, problems);
                CheckOne(c.Fields, "stage", MissionContactStages, cw, problems);
                break;
            case "space_site":
            {
                CheckOne(c.Fields, "state", MissionSpaceSiteStates, cw, problems);
                if (c.F("bind") != null)
                {
                    if (c.F("site") != null) problems.Add($"{cw}: site or bind, not both.");
                    var kinds = c.L("kinds").ToList();
                    if (c.F("kind") is { } one) kinds.Add(one);
                    if (kinds.Count == 0) problems.Add($"{cw}: a binding trigger needs kind (or kinds).");
                    foreach (var kn in kinds)
                        if (SpaceSiteRules.KindOf(kn) is not { } k || k == Space.SiteKind.Station || (SpaceSites != null && SpaceSites.Kind(k) == null))
                            problems.Add($"{cw}: kind '{kn}' is not a site kind in maps space_sites (not the station).");
                    if (c.F("state") is { } bs && bs is not ("detected" or "identified")) problems.Add($"{cw}: a binding trigger asks detected or identified.");
                    var variants = c.L("variants").ToList();
                    if (c.F("variant") is { } v1) variants.Add(v1);
                    foreach (var v in variants)
                        if (SpaceSites != null && !kinds.Any(kn => SpaceSiteRules.KindOf(kn) is { } vk && SpaceSites.Kind(vk)?.Variants.Any(x => x.Id == v) == true))
                            problems.Add($"{cw}: variant '{v}' is not one of that kind's.");
                    if (c.F("spent") is { } sp && sp is not ("true" or "false")) problems.Add($"{cw}: spent is true or false.");
                }
                else
                {
                    CheckSpaceRef(c.F("site"), SiteRefTag, cw, bound, problems);
                    if (c.F("kind") != null || c.F("variant") != null || c.L("kinds").Count > 0 || c.L("variants").Count > 0 || c.F("spent") != null)
                        problems.Add($"{cw}: kind(s), variant(s) and spent only go with bind (a trigger).");
                }
                if (c.F("state") == "delivered") CheckSpaceRef(c.F("body"), BodyRefTag, cw, bound, problems);
                else if (c.F("body") != null) problems.Add($"{cw}: body only goes with state delivered.");
                break;
            }
            case "hull":
                foreach (var f in new[] { "below", "at_least" }) CheckNumber(c.Fields, f, cw, problems);
                if ((c.F("below") == null) == (c.F("at_least") == null)) problems.Add($"{cw}: below or at_least (one of them).");
                if (c.N("below", Fix64.Zero) > Fix64.FromInt(100) || c.N("at_least", Fix64.Zero) > Fix64.FromInt(100)) problems.Add($"{cw}: hull is 0..100.");
                break;
            case "crew":
                foreach (var f in new[] { "below", "at_least" }) CheckNumber(c.Fields, f, cw, problems);
                if ((c.F("below") == null) == (c.F("at_least") == null)) problems.Add($"{cw}: below or at_least (one of them).");
                if (c.F("division") is { } div && FitOut?.DivisionIndex(div) < 0) problems.Add($"{cw}: unknown division '{div}'.");
                break;
            case "hold":
                if (!ValidHoldItem(c.F("item"))) problems.Add($"{cw}: item '{c.F("item")}' is not patch_kit, medical, salvage:<id>, ore, ore:<id> or survivors.");
                break;
            case "space_tally":
            {
                var keys = c.L("keys").ToList();
                if (c.F("key") is { } k1) keys.Add(k1);
                if (keys.Count == 0) problems.Add($"{cw}: needs key (or keys).");
                foreach (var k in keys)
                    if (!MissionTallyKinds.Any(t => k == t || k.StartsWith(t + ":", StringComparison.Ordinal))) problems.Add($"{cw}: key '{k}' starts with none of {string.Join(", ", MissionTallyKinds)}.");
                CheckNumber(c.Fields, "count", cw, problems, positive: true);
                break;
            }
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
            case "bind_body":
            case "bind_ship":
            {
                var picks = a.Kind == "bind_body" ? MissionBodyPicks : MissionShipPicks;
                if (a.F("pick") is not { } pick || !picks.Contains(pick)) problems.Add($"{aw}: pick '{a.F("pick")}' is not one of {string.Join(", ", picks)}.");
                var excluded = a.L("exclude").ToList();
                if (a.F("exclude") is { } ex) excluded.Add(ex);
                foreach (var x in excluded) CheckSpaceRef(x, BodyRefTag, aw, bound, problems);
                if (a.F("near") is { } bn)
                {
                    if (a.Kind != "bind_body") problems.Add($"{aw}: near only goes with bind_body.");
                    else CheckSpaceRef(bn, SiteRefTag, aw, bound, problems);
                }
                if (a.F("as") is not { } name || !SnakeId.IsMatch(name) || MissionBuiltInRefs.Contains(name)) problems.Add($"{aw}: needs as (a lower-case id, not base/hub).");
                else
                {
                    if (!bound.Contains(name)) bound.Add(name);
                    bound.Add((a.Kind == "bind_body" ? BodyRefTag : ShipRefTag) + name);
                }
                break;
            }
            case "place_beacon":
                CheckSpaceRef(a.F("at"), BodyRefTag, aw, bound, problems);
                CheckText(a.F("label") ?? "", aw, bound, problems);
                break;
            case "load_cargo":
                if (a.F("id") is not { } lid || !SnakeId.IsMatch(lid)) problems.Add($"{aw}: needs id (a lower-case id the cargo condition counts).");
                CheckSpaceRef(a.F("to"), BodyRefTag, aw, bound, problems);
                if (a.F("from") is { } lf) CheckSpaceRef(lf, BodyRefTag, aw, bound, problems);
                if (a.F("label") is not { Length: > 0 } ll) problems.Add($"{aw}: needs a label (what the cargo is).");
                else CheckText(ll, aw, bound, problems);
                break;
            case "place_space_site":
            {
                if (SpaceSiteRules.KindOf(a.F("kind")) is not { } k || k == Space.SiteKind.Station || (SpaceSites != null && SpaceSites.Kind(k) == null))
                    problems.Add($"{aw}: kind '{a.F("kind")}' is not a site kind in maps space_sites (not the station).");
                else if (a.F("variant") is { } v && SpaceSites?.Kind(k)?.Variants.All(x => x.Id != v) == true) problems.Add($"{aw}: variant '{v}' is not one of that kind's.");
                if (a.F("near") is { } near && !MissionSiteNear.Contains(near))
                {
                    if (!bound.Contains(near) || !(bound.Contains(BodyRefTag + near) || bound.Contains(SiteRefTag + near)))
                        problems.Add($"{aw}: near '{near}' is not one of {string.Join(", ", MissionSiteNear)} or a body or space-site binding.");
                }
                if (a.I("max", 0) <= 0 || a.I("max", 0) < a.I("min", 0) || a.I("max", 0) > 2000) problems.Add($"{aw}: needs max (map units, 1..2000) at least min.");
                foreach (var f in new[] { "again", "hold_room", "cold_reactor", "known" })
                    if (a.F(f) is { } fv && fv is not ("true" or "false")) problems.Add($"{aw}: {f} is true or false.");
                var placed = SpaceSiteRules.KindOf(a.F("kind"));
                if (a.F("pods") != null && (placed != Space.SiteKind.Lifepods || a.I("pods", 0) is < 1 or > 31)) problems.Add($"{aw}: pods (1..31) only for lifepods.");
                if (a.F("hold_room") != null && placed != Space.SiteKind.Lifepods) problems.Add($"{aw}: hold_room only for lifepods.");
                if (a.F("target") is { } tg)
                {
                    if (placed != Space.SiteKind.RogueAsteroid) problems.Add($"{aw}: target only for a rogue_asteroid.");
                    CheckSpaceRef(tg, BodyRefTag, aw, bound, problems);
                }
                if (a.F("cold_reactor") != null && (placed is not { } ck || SpaceSites?.Kind(ck)?.Reactor == null)) problems.Add($"{aw}: cold_reactor only for a kind with a reactor.");
                if (a.F("known") == "true" && placed == Space.SiteKind.HiddenShip) problems.Add($"{aw}: a hidden ship can't be known (the player's scans find it).");
                if (a.F("bind") is not { } b || !SnakeId.IsMatch(b) || MissionBuiltInRefs.Contains(b)) problems.Add($"{aw}: needs bind (a lower-case id, not base/hub).");
                else
                {
                    if (!bound.Contains(b)) bound.Add(b);
                    bound.Add(SiteRefTag + b);
                }
                break;
            }
            case "bind_site":
                if (SpaceSiteRules.KindOf(a.F("kind")) is not { } bk || (SpaceSites != null && SpaceSites.Kind(bk) == null)) problems.Add($"{aw}: kind '{a.F("kind")}' is not a site kind in maps space_sites.");
                CheckOne(a.Fields, "pick", MissionSitePicks, aw, problems);
                if (a.F("as") is not { } sn || !SnakeId.IsMatch(sn) || MissionBuiltInRefs.Contains(sn)) problems.Add($"{aw}: needs as (a lower-case id, not base/hub).");
                else
                {
                    if (!bound.Contains(sn)) bound.Add(sn);
                    bound.Add(SiteRefTag + sn);
                }
                break;
            case "hand_over":
                CheckSpaceRef(a.F("site"), SiteRefTag, aw, bound, problems);
                break;
            case "civ":
                CheckSpaceRef(a.F("at"), BodyRefTag, aw, bound, problems);
                if (a.F("awareness") is { } aws && (!TryFix(aws, out var av) || av != Fix64.FromInt(av.FloorToInt()) || av < Fix64.FromInt(-100) || av > Fix64.FromInt(100)))
                    problems.Add($"{aw}: awareness is a whole number -100..100.");
                if (a.F("incident") is { } inc && (!TryFix(inc, out var iv) || iv < Fix64.Zero || iv > Fix64.FromInt(100))) problems.Add($"{aw}: incident is a chance 0..100.");
                if (a.F("settle") is { } st && st is not ("true" or "false")) problems.Add($"{aw}: settle is true or false.");
                if (a.F("awareness") == null && a.F("incident") == null && a.F("settle") == null) problems.Add($"{aw}: needs awareness, incident or settle.");
                break;
            case "damage":
                CheckNumber(a.Fields, "hull", aw, problems);
                CheckNumber(a.Fields, "seconds", aw, problems);
                if (a.N("hull", Fix64.Zero) > Fix64.FromInt(100)) problems.Add($"{aw}: hull is 0..100.");
                if (a.F("system") is { } sys && ShipSystemOf(sys) == null) problems.Add($"{aw}: unknown system '{sys}'.");
                if (a.F("hull") == null && a.F("system") == null) problems.Add($"{aw}: needs hull or system.");
                if (a.F("system") != null && a.I("seconds", 0) <= 0) problems.Add($"{aw}: a system knocked out needs seconds.");
                break;
            case "lose_crew":
            case "give_crew":
                if (FitOut?.DivisionIndex(a.F("division")) < 0) problems.Add($"{aw}: unknown division '{a.F("division")}'.");
                if (a.I("count", 0) is < 1 or > 100) problems.Add($"{aw}: count 1..100.");
                break;
            case "hold_add":
                if (!ValidHoldItem(a.F("item")) || a.F("item") == "survivors") problems.Add($"{aw}: item '{a.F("item")}' is not patch_kit, medical or salvage:<id>.");
                if (a.I("count", 0) is < 1 or > 100) problems.Add($"{aw}: count 1..100.");
                break;
            case "unlock_buildings":
                if (a.L("ids").Count == 0) problems.Add($"{aw}: needs ids (the buildings it opens).");
                foreach (var id in a.L("ids"))
                    if (available && !EveryBuildMenu.Contains(id)) problems.Add($"{aw}: '{id}' is not in the build menu.");
                    else if (available && StartMenu.Contains(id)) problems.Add($"{aw}: '{id}' is in start_menu already.");
                break;
        }
    }
}
