using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// Civilizations (data/civilizations/): parse + checks. Played by game/sim/Space/Civilizations.cs.
public sealed partial class GameData
{
    /// <summary>The civilizations document, or null when the data set has none (then no system has natives or ruins).</summary>
    public CivilizationData? Civilizations { get; private init; }

    /// <summary>Levels a civilization can have (CivLevel L1..L6).</summary>
    public const int CivLevels = 6;

    private static readonly string[] TemperamentIds = { "peaceful", "wary", "aggressive" };

    /// <summary>Reads the document; missing or malformed fields become problems (reported by Validate), never exceptions.</summary>
    private static CivilizationData? ReadCivilizations(JsonElement root, Func<JsonElement, string, string, List<ResourceAmount>> amounts, List<string> problems)
    {
        var r = new CivReader(problems);
        if (!root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Object)
        {
            problems.Add("Civilizations: rules.json needs a \"rules\" object.");
            return null;
        }
        var s = r.Obj(rules, "sites", "Civilizations rules");
        var range = r.Ints(s, "nations", "Civilizations sites");
        var sites = new CivSiteRules(
            r.Fix(s, "living_chance", "Civilizations sites"), r.Fix(s, "ruins_chance", "Civilizations sites"),
            r.Weights(s, "living_weights", "Civilizations sites"), r.Weights(s, "ruins_weights", "Civilizations sites"),
            r.Strings(s, "town_maps", "Civilizations sites"),
            r.Obj(s, "level_weights", "Civilizations sites").ValueKind == JsonValueKind.Object
                ? s.GetProperty("level_weights").EnumerateObject().Select(p => (p.Name, (IReadOnlyList<int>)r.IntList(p.Value, $"Civilizations level_weights {p.Name}"))).ToList()
                : new List<(string, IReadOnlyList<int>)>(),
            r.Ints(s, "ruins_levels", "Civilizations sites"), r.Int(s, "level_range", "Civilizations sites"),
            r.Weights(s, "temperament_weights", "Civilizations sites"), r.Fixes(s, "unified_chance", "Civilizations sites"),
            range.Count > 0 ? range[0] : 0, range.Count > 1 ? range[1] : 0,
            r.Weights(s, "town_terrain", "Civilizations sites"), r.Int(s, "coastal_bonus", "Civilizations sites"), r.Int(s, "town_spacing", "Civilizations sites"));

        var a = r.Obj(rules, "awareness", "Civilizations rules");
        var bands = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("bands", out var bs) && bs.ValueKind == JsonValueKind.Array
            ? bs.EnumerateArray().Select(b => new AwarenessBand(r.Int(b, "from", "Civilizations awareness band"), r.Str(b, "id", "Civilizations awareness band"),
                r.Str(b, "name", "Civilizations awareness band"))).ToList()
            : new List<AwarenessBand>();
        var awareness = new CivAwarenessRules(bands, r.Ints(a, "start", "Civilizations awareness"),
            r.Int(a, "satellite_seen_from_level", "Civilizations awareness"), r.Fix(a, "satellite_per_min", "Civilizations awareness"),
            r.Int(a, "ship_seen_from_level", "Civilizations awareness"), r.Fix(a, "ship_per_min", "Civilizations awareness"),
            r.Fix(a, "post_per_min", "Civilizations awareness"), r.Int(a, "post_found", "Civilizations awareness"));

        var st = r.Obj(rules, "study", "Civilizations rules");
        var re = r.Obj(rules, "research", "Civilizations rules");
        var study = new CivStudyRules(r.Int(st, "orbit_seconds", "Civilizations study"), r.Int(st, "satellite_seconds", "Civilizations study"),
            r.Int(st, "post_seconds", "Civilizations study"), r.Fixes(re, "observe_per_min", "Civilizations research"), r.Fixes(re, "post_per_min", "Civilizations research"));

        var p = r.Obj(rules, "post", "Civilizations rules");
        var post = new CivPostRules(r.StringList(p, "factions", "Civilizations post"),
            p.ValueKind == JsonValueKind.Object && p.TryGetProperty("cost", out _) ? amounts(p, "cost", "Civilizations post") : new List<ResourceAmount>(),
            r.Int(p, "max", "Civilizations post"), r.Int(p, "discovery_minutes", "Civilizations post"), r.Fix(p, "discovery_chance", "Civilizations post"),
            r.Int(p, "double_above_awareness", "Civilizations post"), r.Int(p, "standing_change", "Civilizations post"));

        var sd = r.Obj(rules, "standing", "Civilizations rules");
        var standing = new CivStandingRules(r.StringList(sd, "factions", "Civilizations standing"), r.Int(sd, "prime_directive_from_year", "Civilizations standing"),
            r.Int(sd, "start", "Civilizations standing"), r.Int(sd, "max", "Civilizations standing"), r.Ints(sd, "thresholds", "Civilizations standing"),
            r.Fix(sd, "recovery_per_min", "Civilizations standing"), r.Int(sd, "recovery_after_minutes", "Civilizations standing"),
            r.Int(sd, "recalled_incidents", "Civilizations standing"), r.Int(sd, "inquiry_hold_seconds", "Civilizations standing"),
            r.Int(sd, "sanctions_seconds", "Civilizations standing"), r.Fix(sd, "sanctions_research", "Civilizations standing"));

        var levels = new List<CivLevelDef>();
        if (root.TryGetProperty("levels", out var ls) && ls.ValueKind == JsonValueKind.Array)
            foreach (var l in ls.EnumerateArray())
            {
                var w = $"Civilization level {(l.TryGetProperty("id", out var lid) ? lid.ToString() : "?")}";
                var pop = r.Longs(l, "population", w);
                var towns = r.Ints(l, "towns", w);
                var size = r.Ints(l, "town_size", w);
                levels.Add(new CivLevelDef(r.Int(l, "level", w), r.Str(l, "id", w), r.Str(l, "name", w),
                    pop.Count > 0 ? pop[0] : 0, pop.Count > 1 ? pop[1] : 0, towns.Count > 0 ? towns[0] : 0, towns.Count > 1 ? towns[1] : 0,
                    size.Count > 0 ? size[0] : 0, size.Count > 1 ? size[1] : 0, r.Bool(l, "lit", w), r.Bool(l, "signals", w), r.StringList(l, "orbital_signs", w)));
            }
        else problems.Add("Civilizations: levels.json needs a \"levels\" list.");

        var n = root.TryGetProperty("names", out var ns) && ns.ValueKind == JsonValueKind.Object ? ns : default;
        if (n.ValueKind != JsonValueKind.Object) problems.Add("Civilizations: names.json needs a \"names\" object.");
        var syl = r.Ints(n, "syllables", "Civilization names");
        var names = new CivNameRules(r.StringList(n, "onsets", "Civilization names"), r.StringList(n, "vowels", "Civilization names"),
            r.StringList(n, "codas", "Civilization names"), r.StringList(n, "endings", "Civilization names"),
            syl.Count > 0 ? syl[0] : 0, syl.Count > 1 ? syl[1] : 0, r.StringList(n, "nation_forms", "Civilization names"));

        var ruins = new List<RuinDef>();
        if (root.TryGetProperty("ruins", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var x in rs.EnumerateArray())
            {
                var w = $"Ruin {(x.TryGetProperty("id", out var rid) ? rid.ToString() : "?")}";
                string? species = x.TryGetProperty("species", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null;
                ruins.Add(new RuinDef(r.Str(x, "id", w), r.Str(x, "name", w), species, r.Int(x, "weight", w), r.Bool(x, "canon", w),
                    x.TryGetProperty("notes", out var nt) && nt.ValueKind == JsonValueKind.String ? nt.GetString()! : ""));
            }
        else problems.Add("Civilizations: ruins.json needs a \"ruins\" list.");

        return new CivilizationData(sites, awareness, study, post, standing, levels, names, ruins);
    }

    /// <summary>Tolerant field reads for the civilizations document: a missing or wrongly typed field is a problem, not an exception.</summary>
    private sealed class CivReader
    {
        private readonly List<string> _problems;

        public CivReader(List<string> problems) => _problems = problems;

        private bool Get(JsonElement e, string name, string where, JsonValueKind kind, out JsonElement value)
        {
            value = default;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value) && value.ValueKind == kind) return true;
            _problems.Add($"{where}: {name} is missing or not a{(kind == JsonValueKind.Array ? " list" : kind == JsonValueKind.Object ? "n object" : kind == JsonValueKind.String ? " text" : " number")}.");
            return false;
        }

        public JsonElement Obj(JsonElement e, string name, string where) => Get(e, name, where, JsonValueKind.Object, out var v) ? v : default;

        public int Int(JsonElement e, string name, string where) =>
            Get(e, name, where, JsonValueKind.Number, out var v) ? v.TryGetInt32(out var i) ? i : Bad(where, name, 0) : 0;

        public Fix64 Fix(JsonElement e, string name, string where) => Get(e, name, where, JsonValueKind.Number, out var v) ? Fix64.Parse(v.GetRawText()) : Fix64.Zero;

        public string Str(JsonElement e, string name, string where) => Get(e, name, where, JsonValueKind.String, out var v) ? v.GetString()! : "";

        public bool Bool(JsonElement e, string name, string where)
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return v.ValueKind == JsonValueKind.True;
            _problems.Add($"{where}: {name} must be true or false.");
            return false;
        }

        public List<int> Ints(JsonElement e, string name, string where) => Get(e, name, where, JsonValueKind.Array, out var v) ? IntList(v, $"{where} {name}") : new List<int>();

        public List<int> IntList(JsonElement v, string where)
        {
            var list = new List<int>();
            if (v.ValueKind != JsonValueKind.Array)
            {
                _problems.Add($"{where}: must be a list of whole numbers.");
                return list;
            }
            foreach (var x in v.EnumerateArray())
                list.Add(x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var i) ? i : Bad(where, "item", 0));
            return list;
        }

        public List<long> Longs(JsonElement e, string name, string where)
        {
            var list = new List<long>();
            if (!Get(e, name, where, JsonValueKind.Array, out var v)) return list;
            foreach (var x in v.EnumerateArray())
                list.Add(x.ValueKind == JsonValueKind.Number && long.TryParse(x.GetRawText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : Bad(where, name, 0L));
            return list;
        }

        public List<Fix64> Fixes(JsonElement e, string name, string where)
        {
            var list = new List<Fix64>();
            if (!Get(e, name, where, JsonValueKind.Array, out var v)) return list;
            foreach (var x in v.EnumerateArray())
                list.Add(x.ValueKind == JsonValueKind.Number ? Fix64.Parse(x.GetRawText()) : Bad(where, name, Fix64.Zero));
            return list;
        }

        public List<string> StringList(JsonElement e, string name, string where)
        {
            var list = new List<string>();
            if (!Get(e, name, where, JsonValueKind.Array, out var v)) return list;
            foreach (var x in v.EnumerateArray())
                list.Add(x.ValueKind == JsonValueKind.String ? x.GetString()! : Bad(where, name, ""));
            return list;
        }

        public List<Weighted> Weights(JsonElement e, string name, string where)
        {
            var list = new List<Weighted>();
            if (!Get(e, name, where, JsonValueKind.Object, out var v)) return list;
            foreach (var p in v.EnumerateObject())
                list.Add(new Weighted(p.Name, p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var i) ? i : Bad(where, $"{name}.{p.Name}", 0)));
            return list;
        }

        public List<(string, string)> Strings(JsonElement e, string name, string where)
        {
            var list = new List<(string, string)>();
            if (!Get(e, name, where, JsonValueKind.Object, out var v)) return list;
            foreach (var p in v.EnumerateObject())
                list.Add((p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : Bad(where, $"{name}.{p.Name}", "")));
            return list;
        }

        private T Bad<T>(string where, string name, T value)
        {
            _problems.Add($"{where}: {name} has a value of the wrong kind.");
            return value;
        }
    }

    private void ValidateCivilizations(List<string> problems)
    {
        if (Civilizations is not { } c) return;
        const string w = "Civilizations";
        var s = c.Sites;
        void Share(Fix64 x, string name)
        {
            if (x < Fix64.Zero || x > Fix64.One) problems.Add($"{w}: {name} must be between 0 and 1.");
        }
        bool KnownClass(string id) => PlanetClasses.Any(p => p.Id == id);

        Share(s.LivingChance, "sites.living_chance");
        Share(s.RuinsChance, "sites.ruins_chance");
        foreach (var (list, name) in new[] { (s.LivingWeights, "living_weights"), (s.RuinsWeights, "ruins_weights") })
        {
            if (list.Count == 0 || list.All(x => x.Weight <= 0)) problems.Add($"{w}: sites.{name} needs a class with a positive weight.");
            foreach (var x in list)
            {
                if (x.Weight < 0) problems.Add($"{w}: sites.{name}.{x.Id} must not be negative.");
                if (!KnownClass(x.Id)) problems.Add($"{w}: sites.{name}: unknown planet class '{x.Id}'.");
                if (SystemGenerator is { } g && (g.IsBelt(x.Id) || x.Id is "class_j" or "class_t")) problems.Add($"{w}: sites.{name}: {x.Id} has no ground for a site.");
            }
        }
        foreach (var x in s.LivingWeights.Where(x => x.Weight > 0 && !IsLandableClass(x.Id)))
            if (s.TownMapFor(x.Id) is not { } stand) problems.Add($"{w}: sites.living_weights: {x.Id} has no surface and no town_maps stand-in.");
            else if (!IsLandableClass(stand)) problems.Add($"{w}: sites.town_maps: {x.Id} → '{stand}' has no surface (surfaces.json planets).");
        foreach (var (cls, surface) in s.TownMaps)
        {
            if (!KnownClass(cls)) problems.Add($"{w}: sites.town_maps: unknown planet class '{cls}'.");
            if (!IsLandableClass(surface)) problems.Add($"{w}: sites.town_maps: {cls} → '{surface}' has no surface (surfaces.json planets).");
        }
        if (!s.LevelWeights.Any(x => x.Class == SystemGeneratorRules.Default)) problems.Add($"{w}: sites.level_weights needs a \"default\" row.");
        foreach (var (cls, weights) in s.LevelWeights)
        {
            if (cls != SystemGeneratorRules.Default && !KnownClass(cls)) problems.Add($"{w}: sites.level_weights: unknown planet class '{cls}'.");
            Levels6(weights, $"sites.level_weights.{cls}");
        }
        Levels6(s.RuinsLevels, "sites.ruins_levels");
        void Levels6(IReadOnlyList<int> weights, string name)
        {
            if (weights.Count != CivLevels) problems.Add($"{w}: {name} needs {CivLevels} weights (L1..L6).");
            if (weights.Any(x => x < 0) || weights.Sum() <= 0) problems.Add($"{w}: {name} must not be negative and needs a positive sum.");
        }
        if (s.LevelRange < 1 || s.LevelRange > CivLevels) problems.Add($"{w}: sites.level_range must be 1..{CivLevels}.");
        foreach (var t in s.TemperamentWeights)
        {
            if (!TemperamentIds.Contains(t.Id)) problems.Add($"{w}: sites.temperament_weights: unknown temperament '{t.Id}' (peaceful, wary, aggressive).");
            if (t.Weight < 0) problems.Add($"{w}: sites.temperament_weights.{t.Id} must not be negative.");
        }
        if (s.TemperamentWeights.Sum(t => System.Math.Max(0, t.Weight)) <= 0) problems.Add($"{w}: sites.temperament_weights needs a positive weight.");
        if (s.UnifiedChance.Count != CivLevels) problems.Add($"{w}: sites.unified_chance needs {CivLevels} shares (L1..L6).");
        foreach (var u in s.UnifiedChance) Share(u, "sites.unified_chance");
        if (s.NationsMin < 2 || s.NationsMax < s.NationsMin || s.NationsMax > 20) problems.Add($"{w}: sites.nations must be [min, max] with 2 ≤ min ≤ max ≤ 20.");
        foreach (var t in s.TownTerrain)
        {
            if (!_terrain.ContainsKey(t.Id)) problems.Add($"{w}: sites.town_terrain: unknown terrain '{t.Id}'.");
            else if (!_terrain[t.Id].Passable) problems.Add($"{w}: sites.town_terrain: '{t.Id}' is not land.");
            if (t.Weight < 0) problems.Add($"{w}: sites.town_terrain.{t.Id} must not be negative.");
        }
        if (s.TownTerrain.Sum(t => System.Math.Max(0, t.Weight)) <= 0) problems.Add($"{w}: sites.town_terrain needs a positive weight.");
        if (s.CoastalBonus < 0) problems.Add($"{w}: sites.coastal_bonus must not be negative.");
        if (s.TownSpacing < 1 || s.TownSpacing > 40) problems.Add($"{w}: sites.town_spacing must be 1..40 tiles.");

        var a = c.Awareness;
        if (a.Bands.Count == 0 || a.Bands[0].From != 0) problems.Add($"{w}: awareness.bands must start at 0.");
        for (var i = 1; i < a.Bands.Count; i++)
            if (a.Bands[i].From <= a.Bands[i - 1].From || a.Bands[i].From > 100) problems.Add($"{w}: awareness.bands must rise, within 0..100.");
        if (a.Bands.Select(b => b.Id).Distinct().Count() != a.Bands.Count) problems.Add($"{w}: awareness.bands ids repeat.");
        if (a.Start.Count != CivLevels || a.Start.Any(x => x < 0 || x > 100)) problems.Add($"{w}: awareness.start needs {CivLevels} values in 0..100.");
        foreach (var (lvl, name) in new[] { (a.SatelliteFromLevel, "satellite_seen_from_level"), (a.ShipFromLevel, "ship_seen_from_level") })
            if (lvl < 1 || lvl > CivLevels + 1) problems.Add($"{w}: awareness.{name} must be 1..{CivLevels + 1} ({CivLevels + 1} = never).");
        foreach (var (x, name) in new[] { (a.SatellitePerMin, "satellite_per_min"), (a.ShipPerMin, "ship_per_min"), (a.PostPerMin, "post_per_min") })
            if (x < Fix64.Zero || x > Fix64.FromInt(100)) problems.Add($"{w}: awareness.{name} must be 0..100.");
        if (a.PostFound < 0 || a.PostFound > 100) problems.Add($"{w}: awareness.post_found must be 0..100.");

        var st = c.Study;
        if (st.OrbitSeconds <= 0 || st.SatelliteSeconds <= 0 || st.PostSeconds <= 0) problems.Add($"{w}: study seconds must be positive.");
        else if (st.SatelliteSeconds > st.OrbitSeconds || st.PostSeconds > st.SatelliteSeconds)
            problems.Add($"{w}: study: a satellite should study at least as fast as a ship in orbit, a post at least as fast as a satellite.");
        foreach (var (list, name) in new[] { (st.ObservePerMin, "research.observe_per_min"), (st.PostPerMin, "research.post_per_min") })
            if (list.Count != CivLevels || list.Any(x => x < Fix64.Zero || x > Fix64.FromInt(1000))) problems.Add($"{w}: {name} needs {CivLevels} values in 0..1000.");

        var p = c.Post;
        foreach (var f in p.Factions)
            if (!_factions.ContainsKey(f)) problems.Add($"{w}: post.factions: unknown faction '{f}'.");
        if (p.Cost.Any(x => x.Amount < Fix64.Zero)) problems.Add($"{w}: post.cost must not be negative.");
        if (p.Max < 1 || p.Max > 20) problems.Add($"{w}: post.max must be 1..20.");
        if (p.DiscoveryMinutes < 1) problems.Add($"{w}: post.discovery_minutes must be at least 1.");
        Share(p.DiscoveryChance, "post.discovery_chance");
        if (p.DoubleAboveAwareness < 0 || p.DoubleAboveAwareness > 100) problems.Add($"{w}: post.double_above_awareness must be 0..100.");
        if (p.StandingChange > 0 || p.StandingChange < -100) problems.Add($"{w}: post.standing_change must be -100..0.");

        var sd = c.Standing;
        foreach (var f in sd.Factions)
            if (!_factions.ContainsKey(f)) problems.Add($"{w}: standing.factions: unknown faction '{f}'.");
        if (sd.Max < 1 || sd.Max > 100) problems.Add($"{w}: standing.max must be 1..100.");
        if (sd.Start < 1 || sd.Start > sd.Max) problems.Add($"{w}: standing.start must be 1..max.");
        if (sd.Thresholds.Count != 4) problems.Add($"{w}: standing.thresholds needs 4 steps (warning, inquiry, relieved, sanctions).");
        for (var i = 0; i < sd.Thresholds.Count; i++)
            if (sd.Thresholds[i] <= 0 || sd.Thresholds[i] > sd.Start || (i > 0 && sd.Thresholds[i] >= sd.Thresholds[i - 1]))
                problems.Add($"{w}: standing.thresholds must fall, each in 1..start.");
        if (sd.RecoveryPerMin < Fix64.Zero || sd.RecoveryPerMin > Fix64.FromInt(100)) problems.Add($"{w}: standing.recovery_per_min must be 0..100.");
        if (sd.RecoveryAfterMinutes < 0) problems.Add($"{w}: standing.recovery_after_minutes must not be negative.");
        if (sd.RecalledIncidents < 1) problems.Add($"{w}: standing.recalled_incidents must be at least 1.");
        if (sd.InquiryHoldSeconds < 0 || sd.SanctionsSeconds < 0) problems.Add($"{w}: standing hold/sanction seconds must not be negative.");
        Share(sd.SanctionsResearch, "standing.sanctions_research");
        if (sd.PrimeDirectiveFromYear < 1900 || sd.PrimeDirectiveFromYear > 4000) problems.Add($"{w}: standing.prime_directive_from_year looks wrong.");

        if (c.Levels.Count != CivLevels) problems.Add($"{w}: levels.json needs exactly {CivLevels} levels (L1..L6).");
        for (var i = 0; i < c.Levels.Count; i++)
        {
            var l = c.Levels[i];
            var lw = $"Civilization level {l.Id}";
            if (l.Level != i + 1) problems.Add($"{lw}: level must be {i + 1} (levels in order L1..L6).");
            if (l.PopulationMin < 1 || l.PopulationMax < l.PopulationMin) problems.Add($"{lw}: population must be [min, max] with 1 ≤ min ≤ max.");
            if (l.TownsMin < 1 || l.TownsMax < l.TownsMin || l.TownsMax > 30) problems.Add($"{lw}: towns must be [min, max] with 1 ≤ min ≤ max ≤ 30.");
            if (l.SizeMin < 1 || l.SizeMax < l.SizeMin || l.SizeMax > 3) problems.Add($"{lw}: town_size must be [min, max] within 1..3.");
            if (l.Name.Length == 0) problems.Add($"{lw}: needs a name.");
        }
        if (c.Levels.Select(l => l.Id).Distinct().Count() != c.Levels.Count) problems.Add($"{w}: level ids repeat.");

        var n = c.Names;
        if (n.Onsets.Count == 0 || n.Vowels.Count == 0 || n.Codas.Count == 0 || n.Endings.Count == 0 || n.NationForms.Count == 0)
            problems.Add($"{w}: names needs onsets, vowels, codas, endings and nation_forms.");
        if (n.SyllablesMin < 1 || n.SyllablesMax < n.SyllablesMin || n.SyllablesMax > 5) problems.Add($"{w}: names.syllables must be [min, max] within 1..5.");
        foreach (var f in n.NationForms.Where(f => !f.Contains("{root}"))) problems.Add($"{w}: nation form '{f}' has no {{root}}.");

        if (c.Ruins.Count == 0 || c.Ruins.All(r => r.Weight <= 0)) problems.Add($"{w}: ruins.json needs a ruin kind with a positive weight.");
        foreach (var r in c.Ruins)
        {
            if (r.Id.Length == 0 || r.Name.Length == 0) problems.Add($"{w}: every ruin kind needs an id and a name.");
            if (r.Weight < 0) problems.Add($"Ruin {r.Id}: weight must not be negative.");
            if (r.Canon && r.Species == null) problems.Add($"Ruin {r.Id}: a canon ruin names its species.");
        }
        if (c.Ruins.Select(r => r.Id).Distinct().Count() != c.Ruins.Count) problems.Add($"{w}: ruin ids repeat.");
    }
}
