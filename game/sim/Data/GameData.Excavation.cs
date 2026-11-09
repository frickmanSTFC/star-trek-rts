using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>One kind a dig layer can turn up (artifact, data_core, tech_fragment, resources, defence, pathogen) and its weight.</summary>
public sealed record DigFindWeight(string Kind, int Weight);

/// <summary>Finds that give research points (artifact, data_core): a range and the names used in the find lines.</summary>
public sealed record DigResearchFind(int Min, int Max, IReadOnlyList<string> Names);

/// <summary>A tech fragment: research points and/or modifiers kept for the rest of the game.</summary>
public sealed record TechFragmentDef(string Id, string Name, int Weight, Fix64 Research, IReadOnlyList<Modifier> Modifiers);

/// <summary>A cache of resources: each listed resource id gets an amount in its [Min, Max].</summary>
public sealed record ResourceCacheDef(string Name, int Weight, IReadOnlyList<(string Resource, int Min, int Max)> Give);

/// <summary>A woken defence system (see excavation.json _notes_excavation).</summary>
public sealed record DigDefenceRules(int DamageMin, int DamageMax, int Radius, int LockSeconds, Fix64 DisarmedChance,
    string Tech, Fix64 TechDisarmedChance, Fix64 TechDamageMultiplier, IReadOnlyList<string> Names);

/// <summary>A released pathogen: the base's crew sick for SickSeconds (× TechSecondsMultiplier with Tech known), their work
/// × WorkMultiplier; vehicles carry it to the player's other bases (SpreadShare of the carried time, then ImmunitySeconds
/// before a base can catch it again).</summary>
public sealed record DigPathogenRules(int SickSeconds, Fix64 WorkMultiplier, string Tech, Fix64 TechSecondsMultiplier,
    Fix64 SpreadShare, int ImmunitySeconds, IReadOnlyList<string> Names);

/// <summary>What a precursor site gives once its last layer is dug.</summary>
public sealed record PrecursorReward(string Name, string Effect, Fix64 Research, IReadOnlyList<Modifier> Modifiers, Fix64 ShuttleTurnaround);

/// <summary>A canon precursor (very rare): its weight against the invented species, its layers, danger and reward.</summary>
public sealed record PrecursorDef(string Id, string Species, int Weight, int LayersMin, int LayersMax, Fix64 Danger, PrecursorReward Reward);

/// <summary>Ruins on the colony's world and digging them (surfaces/excavation.json). See its _notes_excavation.</summary>
public sealed record ExcavationRules(
    Fix64 Chance, int SitesMin, int SitesMax, IReadOnlyList<string> Terrain, int EdgeRows, int MinDistance,
    string Building, int LayersMin, int LayersMax, int SecondsPerLayer,
    IReadOnlyList<DigFindWeight> Finds, DigResearchFind Artifact, DigResearchFind DataCore,
    IReadOnlyList<TechFragmentDef> TechFragments, IReadOnlyList<ResourceCacheDef> Caches,
    DigDefenceRules Defence, DigPathogenRules Pathogen,
    int SpeciesWeight, IReadOnlyList<string> Species, IReadOnlyList<PrecursorDef> Precursors)
{
    /// <summary>The find kinds, in excavation.json order.</summary>
    public static readonly string[] Kinds = { "artifact", "data_core", "tech_fragment", "resources", "defence", "pathogen" };

    public PrecursorDef? Precursor(string? id) => id == null ? null : Precursors.FirstOrDefault(p => p.Id == id);
}

// Ruins + excavation: read and checked here; played in Surface/Excavation.cs.
public sealed partial class GameData
{
    /// <summary>Ruins and digs on the colony's world; null = none (no map gets ruins).</summary>
    public ExcavationRules? Excavation { get; private init; }

    /// <summary>Reads surfaces/excavation.json; missing or malformed fields become problems (Validate), never exceptions.</summary>
    private static ExcavationRules? ReadExcavation(JsonElement surfaces, Func<JsonElement, string, List<Modifier>> modifiers, List<string> problems)
    {
        if (!surfaces.TryGetProperty("excavation", out var e)) return null;
        const string W = "Excavation";
        if (e.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{W}: \"excavation\" must be an object.");
            return null;
        }
        var r = new ExReader(problems);
        var sites = r.Range(e, "sites", W);
        var layers = r.Range(e, "layers", W);

        DigResearchFind ResearchFind(string name)
        {
            var o = r.Obj(e, name, W);
            var range = r.Range(o, "research", $"{W} {name}");
            return new DigResearchFind(range.Min, range.Max, r.Strings(o, "names", $"{W} {name}"));
        }

        var fragments = new List<TechFragmentDef>();
        foreach (var f in r.List(e, "tech_fragments", W))
        {
            var w = $"{W} tech fragment {(f.TryGetProperty("id", out var fid) ? fid.ToString() : "?")}";
            fragments.Add(new TechFragmentDef(r.Str(f, "id", w), r.Str(f, "name", w), r.Int(f, "weight", w), r.Fix(f, "research", w), modifiers(f, w)));
        }

        var caches = new List<ResourceCacheDef>();
        foreach (var c in r.List(e, "caches", W))
        {
            var w = $"{W} cache {(c.TryGetProperty("name", out var cn) ? cn.ToString() : "?")}";
            var give = new List<(string, int, int)>();
            var g = r.Obj(c, "give", w);
            if (g.ValueKind == JsonValueKind.Object)
                foreach (var p in g.EnumerateObject())
                {
                    var range = r.RangeOf(p.Value, $"{w} give {p.Name}");
                    give.Add((p.Name, range.Min, range.Max));
                }
            caches.Add(new ResourceCacheDef(r.Str(c, "name", w), r.Int(c, "weight", w), give));
        }

        var d = r.Obj(e, "defence", W);
        var dmg = r.Range(d, "damage", $"{W} defence");
        var defence = new DigDefenceRules(dmg.Min, dmg.Max, r.Int(d, "radius", $"{W} defence"), r.Int(d, "lock_seconds", $"{W} defence"),
            r.Fix(d, "disarmed_chance", $"{W} defence"), r.Str(d, "tech", $"{W} defence"), r.Fix(d, "tech_disarmed_chance", $"{W} defence"),
            r.Fix(d, "tech_damage_multiplier", $"{W} defence"), r.Strings(d, "names", $"{W} defence"));

        var pa = r.Obj(e, "pathogen", W);
        var pathogen = new DigPathogenRules(r.Int(pa, "sick_seconds", $"{W} pathogen"), r.Fix(pa, "work_multiplier", $"{W} pathogen"),
            r.Str(pa, "tech", $"{W} pathogen"), r.Fix(pa, "tech_seconds_multiplier", $"{W} pathogen"),
            r.Fix(pa, "spread_share", $"{W} pathogen"), r.Int(pa, "immunity_seconds", $"{W} pathogen"), r.Strings(pa, "names", $"{W} pathogen"));

        var sp = r.Obj(e, "species", W);
        var precursors = new List<PrecursorDef>();
        foreach (var p in r.List(e, "precursors", W))
        {
            var w = $"{W} precursor {(p.TryGetProperty("id", out var pid) ? pid.ToString() : "?")}";
            var pl = r.Range(p, "layers", w);
            var rw = r.Obj(p, "reward", w);
            precursors.Add(new PrecursorDef(r.Str(p, "id", w), r.Str(p, "species", w), r.Int(p, "weight", w), pl.Min, pl.Max, r.Fix(p, "danger", w),
                new PrecursorReward(r.Str(rw, "name", $"{w} reward"), r.Str(rw, "effect", $"{w} reward"), r.Fix(rw, "research", $"{w} reward"),
                    rw.ValueKind == JsonValueKind.Object ? modifiers(rw, $"{w} reward") : new List<Modifier>(),
                    r.Fix(rw, "shuttle_turnaround", $"{w} reward"))));
        }

        return new ExcavationRules(r.Fix(e, "chance", W), sites.Min, sites.Max, r.Strings(e, "terrain", W), r.Int(e, "edge_rows", W),
            r.Int(e, "min_distance", W), r.Str(e, "building", W), layers.Min, layers.Max, r.Int(e, "seconds_per_layer", W),
            r.List(e, "finds", W).Select(f => new DigFindWeight(r.Str(f, "kind", $"{W} find"), r.Int(f, "weight", $"{W} find"))).ToList(),
            ResearchFind("artifact"), ResearchFind("data_core"), fragments, caches, defence, pathogen,
            r.Int(sp, "weight", $"{W} species"), r.Strings(sp, "names", $"{W} species"), precursors);
    }

    private void ValidateExcavation(List<string> problems)
    {
        if (Excavation is not { } x) return;
        const string W = "Excavation";
        if (x.Chance < Fix64.Zero || x.Chance > Fix64.One) problems.Add($"{W}: chance must be 0 to 1.");
        if (x.SitesMin < 1 || x.SitesMax < x.SitesMin || x.SitesMax > 8) problems.Add($"{W}: sites must be [min, max] with 1 <= min <= max <= 8.");
        if (x.LayersMin < 1 || x.LayersMax < x.LayersMin || x.LayersMax > 50) problems.Add($"{W}: layers must be [min, max] with 1 <= min <= max <= 50.");
        if (x.SecondsPerLayer < 1) problems.Add($"{W}: seconds_per_layer must be at least 1.");
        if (x.EdgeRows < 0 || x.MinDistance < 1) problems.Add($"{W}: edge_rows must not be negative and min_distance at least 1.");
        if (x.Terrain.Count == 0) problems.Add($"{W}: terrain needs at least one terrain id.");
        foreach (var t in x.Terrain)
        {
            if (!_terrain.TryGetValue(t, out var def)) problems.Add($"{W}: unknown terrain '{t}'.");
            else if (!def.Buildable || !def.Passable) problems.Add($"{W}: terrain {t} is not buildable land.");
            if (Landing.Terrain.Contains(t)) problems.Add($"{W}: terrain {t} is landing ground (a colony could land on a ruin).");
        }
        if (!_buildings.TryGetValue(x.Building, out var site)) problems.Add($"{W}: unknown building '{x.Building}'.");
        else
        {
            if (!site.RequiresRuin) problems.Add($"{W}: building {x.Building} needs requires_ruin.");
            if (!BuildMenu.Contains(x.Building)) problems.Add($"{W}: building {x.Building} is not in the build menu.");
            if (site.IsMine || site.IsConverter || site.IsFactory || site.IsLab || site.IsCore || site.RequiresDeposit || site.RequiresDepleted)
                problems.Add($"{W}: building {x.Building} digs ruins only (no mine, converter, bay, lab, core or deposit rules).");
        }
        foreach (var b in Buildings.Where(b => b.RequiresRuin && b.Id != x.Building))
            problems.Add($"Building {b.Id}: requires_ruin, but excavation.building is {x.Building}.");

        foreach (var k in ExcavationRules.Kinds)
            if (x.Finds.Count(f => f.Kind == k) != 1) problems.Add($"{W}: finds must list {k} once.");
        foreach (var f in x.Finds)
        {
            if (!ExcavationRules.Kinds.Contains(f.Kind)) problems.Add($"{W}: unknown find kind '{f.Kind}'.");
            if (f.Weight < 0) problems.Add($"{W}: find {f.Kind} weight must not be negative.");
        }
        if (x.Finds.Sum(f => System.Math.Max(0, f.Weight)) <= 0) problems.Add($"{W}: find weights need a positive sum.");
        foreach (var (rf, name) in new[] { (x.Artifact, "artifact"), (x.DataCore, "data_core") })
        {
            if (rf.Min < 0 || rf.Max < rf.Min) problems.Add($"{W}: {name} research must be [min, max] with 0 <= min <= max.");
            if (rf.Names.Count == 0) problems.Add($"{W}: {name} needs names.");
        }
        if (x.TechFragments.Count == 0 || x.TechFragments.All(f => f.Weight <= 0)) problems.Add($"{W}: tech_fragments needs one with a positive weight.");
        foreach (var f in x.TechFragments)
        {
            if (f.Weight < 0 || f.Research < Fix64.Zero) problems.Add($"{W} tech fragment {f.Id}: weight and research must not be negative.");
            if (f.Research <= Fix64.Zero && f.Modifiers.Count == 0) problems.Add($"{W} tech fragment {f.Id}: gives nothing (research or modifiers).");
            CheckModifiers(f.Modifiers, $"{W} tech fragment {f.Id}", problems);
        }
        if (x.TechFragments.Select(f => f.Id).Distinct().Count() != x.TechFragments.Count) problems.Add($"{W}: tech fragment ids repeat.");
        if (x.Caches.Count == 0 || x.Caches.All(c => c.Weight <= 0)) problems.Add($"{W}: caches needs one with a positive weight.");
        foreach (var c in x.Caches)
        {
            if (c.Weight < 0) problems.Add($"{W} cache {c.Name}: weight must not be negative.");
            if (c.Give.Count == 0) problems.Add($"{W} cache {c.Name}: gives nothing.");
            foreach (var (res, min, max) in c.Give)
            {
                if (!_resources.TryGetValue(res, out var rd)) problems.Add($"{W} cache {c.Name}: unknown resource '{res}'.");
                else if (rd.Id == PowerResource) problems.Add($"{W} cache {c.Name}: {PowerResource} is a power budget, not stock.");
                if (min < 0 || max < min) problems.Add($"{W} cache {c.Name}: {res} must be [min, max] with 0 <= min <= max.");
            }
        }
        var d = x.Defence;
        if (d.DamageMin < 0 || d.DamageMax < d.DamageMin || d.DamageMax > 100) problems.Add($"{W}: defence damage must be [min, max] within 0..100.");
        if (d.Radius < 0 || d.Radius > 6 || d.LockSeconds < 0) problems.Add($"{W}: defence radius must be 0..6 and lock_seconds not negative.");
        foreach (var (v, n) in new[] { (d.DisarmedChance, "disarmed_chance"), (d.TechDisarmedChance, "tech_disarmed_chance") })
            if (v < Fix64.Zero || v > Fix64.One) problems.Add($"{W}: defence {n} must be 0 to 1.");
        if (d.TechDamageMultiplier < Fix64.Zero) problems.Add($"{W}: defence tech_damage_multiplier must not be negative.");
        if (!HasTech(d.Tech)) problems.Add($"{W}: defence tech '{d.Tech}' isn't in surfaces.json research.");
        if (d.Names.Count == 0) problems.Add($"{W}: defence needs names.");
        var p = x.Pathogen;
        if (p.SickSeconds < 1 || p.ImmunitySeconds < 0) problems.Add($"{W}: pathogen sick_seconds must be at least 1 and immunity_seconds not negative.");
        if (p.WorkMultiplier <= Fix64.Zero || p.WorkMultiplier > Fix64.One) problems.Add($"{W}: pathogen work_multiplier must be in (0, 1].");
        if (p.TechSecondsMultiplier <= Fix64.Zero || p.TechSecondsMultiplier > Fix64.One) problems.Add($"{W}: pathogen tech_seconds_multiplier must be in (0, 1].");
        if (p.SpreadShare < Fix64.Zero || p.SpreadShare > Fix64.One) problems.Add($"{W}: pathogen spread_share must be 0 to 1.");
        if (!HasTech(p.Tech)) problems.Add($"{W}: pathogen tech '{p.Tech}' isn't in surfaces.json research.");
        if (p.Names.Count == 0) problems.Add($"{W}: pathogen needs names.");
        if (x.SpeciesWeight < 0 || x.Species.Count == 0) problems.Add($"{W}: species needs names and a weight that isn't negative.");
        if (x.SpeciesWeight + x.Precursors.Sum(q => System.Math.Max(0, q.Weight)) <= 0) problems.Add($"{W}: species and precursor weights need a positive sum.");
        foreach (var q in x.Precursors)
        {
            var w = $"{W} precursor {q.Id}";
            if (q.Weight < 0) problems.Add($"{w}: weight must not be negative.");
            if (q.LayersMin < 1 || q.LayersMax < q.LayersMin || q.LayersMax > 50) problems.Add($"{w}: layers must be [min, max] with 1 <= min <= max <= 50.");
            if (q.Danger < Fix64.Zero) problems.Add($"{w}: danger must not be negative.");
            if (q.Species.Length == 0) problems.Add($"{w}: needs a species name.");
            var rw = q.Reward;
            if (rw.Research < Fix64.Zero) problems.Add($"{w}: reward research must not be negative.");
            if (rw.ShuttleTurnaround <= Fix64.Zero || rw.ShuttleTurnaround > Fix64.One) problems.Add($"{w}: reward shuttle_turnaround must be in (0, 1].");
            if (rw.Research <= Fix64.Zero && rw.Modifiers.Count == 0 && rw.ShuttleTurnaround >= Fix64.One) problems.Add($"{w}: reward gives nothing.");
            CheckModifiers(rw.Modifiers, $"{w} reward", problems);
        }
        if (x.Precursors.Select(q => q.Id).Distinct().Count() != x.Precursors.Count) problems.Add($"{W}: precursor ids repeat.");
    }

    private void CheckModifiers(IReadOnlyList<Modifier> modifiers, string where, List<string> problems)
    {
        foreach (var m in modifiers)
        {
            foreach (var bid in m.Buildings)
                if (!_buildings.ContainsKey(bid)) problems.Add($"{where}: modifier names unknown building '{bid}'.");
            foreach (var uid in m.Units)
                if (!_surfaceUnits.ContainsKey(uid)) problems.Add($"{where}: modifier names unknown unit '{uid}'.");
        }
    }

    /// <summary>A small tolerant reader for excavation.json: every missing or mistyped field is a problem, never an exception.</summary>
    private sealed class ExReader
    {
        private readonly List<string> _problems;

        public ExReader(List<string> problems) => _problems = problems;

        private bool Get(JsonElement e, string name, string where, JsonValueKind kind, out JsonElement value)
        {
            value = default;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value) && value.ValueKind == kind) return true;
            _problems.Add($"{where}: {name} is missing or not a{(kind == JsonValueKind.Array ? " list" : kind == JsonValueKind.Object ? "n object" : kind == JsonValueKind.String ? " text" : " number")}.");
            return false;
        }

        public JsonElement Obj(JsonElement e, string name, string where) => Get(e, name, where, JsonValueKind.Object, out var v) ? v : default;

        public IEnumerable<JsonElement> List(JsonElement e, string name, string where) =>
            Get(e, name, where, JsonValueKind.Array, out var v) ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList() : new List<JsonElement>();

        public int Int(JsonElement e, string name, string where)
        {
            if (!Get(e, name, where, JsonValueKind.Number, out var v)) return 0;
            if (v.TryGetInt32(out var i)) return i;
            _problems.Add($"{where}: {name} must be a whole number.");
            return 0;
        }

        public Fix64 Fix(JsonElement e, string name, string where) => Get(e, name, where, JsonValueKind.Number, out var v) ? Fix64.Parse(v.GetRawText()) : Fix64.Zero;

        public string Str(JsonElement e, string name, string where) => Get(e, name, where, JsonValueKind.String, out var v) ? v.GetString()! : "";

        public List<string> Strings(JsonElement e, string name, string where) =>
            Get(e, name, where, JsonValueKind.Array, out var v) ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new List<string>();

        public (int Min, int Max) Range(JsonElement e, string name, string where) =>
            Get(e, name, where, JsonValueKind.Array, out var v) ? RangeOf(v, $"{where} {name}") : (0, -1);

        public (int Min, int Max) RangeOf(JsonElement v, string where)
        {
            if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 && v[0].TryGetInt32(out var a) && v[1].TryGetInt32(out var b)) return (a, b);
            _problems.Add($"{where}: must be [min, max] whole numbers.");
            return (0, -1);
        }
    }
}
