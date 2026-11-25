using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Data;

// Space jobs (docs/space-jobs-contract.md): read maps/sites.json "space_sites" and maps/ship_status.json "ship_status"
// (tolerant: a missing field takes its default and Validate says what is wrong), and check them.
public sealed partial class GameData
{
    private static int JInt(JsonElement e, string p, int fallback) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && TryFix(v.GetRawText(), out var f)
            ? (int)System.Math.Clamp(f.FloorToInt(), int.MinValue, int.MaxValue) : fallback;

    private static Fix64 JFix(JsonElement e, string p, Fix64 fallback) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && TryFix(v.GetRawText(), out var f) ? f : fallback;

    private static string JStr(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static bool JBool(JsonElement e, string p, bool fallback = false) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.ValueKind == JsonValueKind.True : fallback;

    private static (int Min, int Max) JPair(JsonElement e, string p, int min, int max)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 2) return (min, max);
        int At(int i, int f) => v[i].ValueKind == JsonValueKind.Number && TryFix(v[i].GetRawText(), out var x) ? (int)System.Math.Clamp(x.FloorToInt(), int.MinValue, int.MaxValue) : f;
        return (At(0, min), At(1, max));
    }

    private static (Fix64 Min, Fix64 Max) JFixPair(JsonElement e, string p, Fix64 min, Fix64 max)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 2) return (min, max);
        Fix64 At(int i, Fix64 f) => v[i].ValueKind == JsonValueKind.Number && TryFix(v[i].GetRawText(), out var x) ? x : f;
        return (At(0, min), At(1, max));
    }

    private static List<string> JStrings(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new List<string>();

    private static List<Weighted> JWeights(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Object
            ? v.EnumerateObject().Select(x => new Weighted(x.Name, x.Value.ValueKind == JsonValueKind.Number && x.Value.TryGetInt32(out var i) ? i : -1)).ToList()
            : new List<Weighted>();

    private static JsonElement JObj(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    private static SpaceSiteRules? ReadSpaceSites(JsonElement maps)
    {
        if (maps.ValueKind != JsonValueKind.Object || !maps.TryGetProperty("space_sites", out var s) || s.ValueKind != JsonValueKind.Object) return null;
        var gen = JObj(s, "generation");
        var count = JPair(gen, "count", 4, 7);
        var orbit = JFixPair(gen, "orbit", Fix64.FromInt(130), Fix64.FromInt(480));
        var kinds = new List<SiteKindDef>();
        if (s.TryGetProperty("kinds", out var ks) && ks.ValueKind == JsonValueKind.Array)
            foreach (var k in ks.EnumerateArray())
            {
                var id = JStr(k, "id");
                var size = JPair(k, "size", 1, 1);
                var origin = JObj(k, "origin");
                var def = new SiteKindDef(id, SpaceSiteRules.KindOf(id) ?? (SiteKind)(-1), JStr(k, "name"), JStr(k, "letter"), JInt(k, "scan_seconds", 0),
                    size.Min, size.Max, JWeights(k, "variants"),
                    origin.ValueKind == JsonValueKind.Object ? origin.EnumerateObject().Select(o => (o.Name, o.Value.ValueKind == JsonValueKind.String ? o.Value.GetString()! : "")).ToList()
                        : new List<(string, string)>(),
                    JBool(k, "hazardous"), JStrings(k, "hazards"));
                var research = JPair(k, "research", 0, 0);
                def = def with
                {
                    AwayTable = JStr(k, "away_table"), ResearchMin = research.Min, ResearchMax = research.Max,
                    Alongside = JObj(k, "alongside") is { ValueKind: JsonValueKind.Object } al
                        ? new SiteAlongsideDef(JInt(al, "seconds", 20), JInt(al, "chance", 0), JPair(al, "hull", 0, 0).Min, JPair(al, "hull", 0, 0).Max) : null,
                    RestorePower = JObj(k, "restore_power") is { ValueKind: JsonValueKind.Object } rp ? new SiteRestorePowerDef(JInt(rp, "seconds", 60), JInt(rp, "kits", 1)) : null,
                    Logs = JObj(k, "logs") is { ValueKind: JsonValueKind.Object } lg
                        ? new SiteLogsDef(JInt(lg, "seconds", 20), JPair(lg, "latinum", 0, 0).Min, JPair(lg, "latinum", 0, 0).Max, JPair(lg, "research", 0, 0).Min, JPair(lg, "research", 0, 0).Max)
                        : null,
                    Reactor = JObj(k, "reactor") is { ValueKind: JsonValueKind.Object } re ? new SiteReactorDef(JInt(re, "seconds", 300), JInt(re, "hull", 0)) : null,
                    Sweep = JObj(k, "sweep") is { ValueKind: JsonValueKind.Object } sw
                        ? new SiteSweepDef(JInt(sw, "seconds", 90), JInt(sw, "hull_every_seconds", 10), JInt(sw, "hull", 0), JPair(sw, "finds", 3, 5).Min,
                            JPair(sw, "finds", 3, 5).Max, JStrings(sw, "always"), JWeights(sw, "items"), JPair(sw, "lifepod_survivors", 1, 1).Min,
                            JPair(sw, "lifepod_survivors", 1, 1).Max)
                        : null,
                    Mining = JObj(k, "mining") is { ValueKind: JsonValueKind.Object } mn ? ReadMining(mn) : null,
                    Tow = JObj(k, "tow") is { ValueKind: JsonValueKind.Object } tw
                        ? new SiteTowDef(JPair(tw, "latinum", 0, 0).Min, JPair(tw, "latinum", 0, 0).Max, JPair(tw, "research", 0, 0).Min, JPair(tw, "research", 0, 0).Max,
                            JPair(tw, "duranium", 0, 0).Min, JPair(tw, "duranium", 0, 0).Max)
                        : null,
                    VariantAway = JObj(k, "away_tables") is { ValueKind: JsonValueKind.Object } va
                        ? va.EnumerateObject().Select(x => (x.Name, x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString()! : "")).ToList()
                        : new List<(string, string)>(),
                    Rogue = JObj(k, "rogue") is { ValueKind: JsonValueKind.Object } ro ? ReadRogue(ro) : null,
                    Comet = JObj(k, "comet") is { ValueKind: JsonValueKind.Object } co ? ReadComet(co) : null,
                    Pods = JObj(k, "pods") is { ValueKind: JsonValueKind.Object } po
                        ? new SitePodsDef(JPair(po, "count", 6, 12).Min, JPair(po, "count", 6, 12).Max, JPair(po, "survivors", 1, 3).Min, JPair(po, "survivors", 1, 3).Max,
                            JPair(po, "air_seconds", 240, 600).Min, JPair(po, "air_seconds", 240, 600).Max, JInt(po, "pickup_seconds", 5), JFix(po, "near_debris", Fix64.FromInt(60)))
                        : null,
                    Stranded = JObj(k, "stranded") is { ValueKind: JsonValueKind.Object } sd ? ReadStranded(sd) : null,
                    Anomaly = JObj(k, "anomaly") is { ValueKind: JsonValueKind.Object } an ? ReadAnomaly(an) : null,
                };
                kinds.Add(def);
            }
        var salvage = new List<SalvageDef>();
        if (s.TryGetProperty("salvage", out var sv) && sv.ValueKind == JsonValueKind.Array)
            foreach (var x in sv.EnumerateArray())
                salvage.Add(new SalvageDef(JStr(x, "id"), JStr(x, "name"), JInt(x, "containers", 1), JInt(x, "latinum", 0), JInt(x, "research", 0)));
        var st = JObj(s, "station");
        var stOrbit = JFixPair(st, "orbit", Fix64.FromInt(200), Fix64.FromInt(320));
        var hire = JObj(st, "hire");
        var station = new StationRules(JStrings(st, "names"), stOrbit.Min, stOrbit.Max, JInt(st, "dock_seconds", 30), JInt(hire, "per_visit", 2),
            JInt(hire, "latinum", 15), JInt(st, "survivor_latinum", 10));
        var ast = JObj(gen, "asteroids");
        var ore = JObj(s, "ore");
        var ores = new List<OreKindDef>();
        if (ore.ValueKind == JsonValueKind.Object && ore.TryGetProperty("kinds", out var oks) && oks.ValueKind == JsonValueKind.Array)
            foreach (var x in oks.EnumerateArray())
                ores.Add(new OreKindDef(JStr(x, "id"), JStr(x, "name"), JFix(x, "latinum", Fix64.Zero)));
        var traders = JObj(ore, "traders");
        return new SpaceSiteRules(count.Min, count.Max, orbit.Min, orbit.Max, JFix(gen, "clear_of_body", Fix64.FromInt(40)), JFix(gen, "spacing", Fix64.FromInt(70)),
            JInt(gen, "tries", 40), JWeights(gen, "weights"), JFix(s, "identify_range", Fix64.FromInt(120)), JInt(s, "spent_seconds", 300),
            JInt(s, "colony_reach_seconds", 60), kinds, salvage, station)
        {
            Asteroids = ast.ValueKind == JsonValueKind.Object
                ? new AsteroidGenDef(JPair(ast, "count", 0, 0).Min, JPair(ast, "count", 0, 0).Max, JInt(ast, "near_belt", 0), JFix(ast, "belt_spread", Fix64.FromInt(25)),
                    JFix(ast, "spacing", Fix64.FromInt(40)))
                : null,
            OrePerContainer = JInt(ore, "per_container", 30),
            Ores = ores,
            TraderRoles = JStrings(traders, "roles"),
            TraderShare = JFix(traders, "share", Fix64.One),
            TowFlight = JFix(JObj(s, "tow"), "flight", Fix64.One),
            Events = JObj(s, "events") is { ValueKind: JsonValueKind.Object } ev
                ? new SpaceEventRules(JInt(ev, "calm_seconds", 300), JPair(ev, "every_seconds", 300, 420).Min, JPair(ev, "every_seconds", 300, 420).Max,
                    JInt(ev, "max_live", 3), JFixPair(ev, "edge", Fix64.FromInt(500), Fix64.FromInt(560)).Min, JFixPair(ev, "edge", Fix64.FromInt(500), Fix64.FromInt(560)).Max,
                    JWeights(ev, "weights"))
                : null,
            Anomalies = JObj(gen, "anomalies") is { ValueKind: JsonValueKind.Object } am
                ? new AnomalyGenDef(JPair(am, "count", 0, 0).Min, JPair(am, "count", 0, 0).Max, JFix(am, "drift", Fix64.Zero), JInt(am, "drift_seconds", 3600))
                : null,
        };
    }

    private static SiteRogueDef ReadRogue(JsonElement ro)
    {
        var reward = JObj(ro, "reward");
        var im = JObj(ro, "impact");
        return new SiteRogueDef(JPair(ro, "flight_seconds", 360, 480).Min, JPair(ro, "flight_seconds", 360, 480).Max, JInt(ro, "colony_chance", 0),
            JPair(ro, "push_seconds", 60, 120).Min, JPair(ro, "push_seconds", 60, 120).Max, JFix(ro, "veer", Fix64.FromInt(600)), JInt(ro, "veer_seconds", 90),
            JPair(reward, "latinum", 0, 0).Min, JPair(reward, "latinum", 0, 0).Max, JPair(reward, "research", 0, 0).Min, JPair(reward, "research", 0, 0).Max,
            JInt(ro, "civ_awareness", 0), JInt(ro, "civ_strike_awareness", 0),
            new RogueImpactDef(JInt(im, "spread", 6), JInt(im, "radius", 3), JPair(im, "damage", 20, 40).Min, JPair(im, "damage", 20, 40).Max,
                JInt(im, "max_buildings", 6), JInt(im, "max_vehicles", 4)));
    }

    private static SiteCometDef ReadComet(JsonElement co)
    {
        var window = JFixPair(co, "window", Fix64.Ratio(1, 4), Fix64.Ratio(4, 5));
        var pass = JFixPair(co, "pass", Fix64.FromInt(120), Fix64.FromInt(260));
        return new SiteCometDef(JPair(co, "cross_seconds", 360, 540).Min, JPair(co, "cross_seconds", 360, 540).Max, pass.Min, pass.Max, window.Min, window.Max,
            JInt(co, "sample_seconds", 30), JInt(co, "hull", 0), JInt(co, "hull_every_seconds", 10), JPair(co, "research", 0, 0).Min, JPair(co, "research", 0, 0).Max,
            JPair(co, "ice", 0, 0).Min, JPair(co, "ice", 0, 0).Max);
    }

    private static SiteStrandedDef ReadStranded(JsonElement sd)
    {
        var reward = JObj(sd, "reward");
        return new SiteStrandedDef(JPair(sd, "air_seconds", 600, 900).Min, JPair(sd, "air_seconds", 600, 900).Max, JFix(sd, "escort_flight", Fix64.One),
            JInt(sd, "bait", 0), JPair(sd, "ambush_hull", 0, 0).Min, JPair(sd, "ambush_hull", 0, 0).Max, JStrings(sd, "ambush_systems"),
            JPair(reward, "latinum", 0, 0).Min, JPair(reward, "latinum", 0, 0).Max, JPair(reward, "research", 0, 0).Min, JPair(reward, "research", 0, 0).Max);
    }

    private static SiteAnomalyDef ReadAnomaly(JsonElement an)
    {
        var rings = new List<AnomalyRingDef>();
        if (an.TryGetProperty("rings", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var r in rs.EnumerateArray())
                rings.Add(new AnomalyRingDef(JStr(r, "id"), JStr(r, "name"), JFix(r, "radius", Fix64.FromInt(10)), JInt(r, "research", 0), JInt(r, "hull", 0),
                    JInt(r, "knock", 0), JBool(r, "hazardous")));
        return new SiteAnomalyDef(JInt(an, "every_seconds", 10), JInt(an, "research_cap", 0), JStrings(an, "systems"), JPair(an, "life_seconds", 900, 1500).Min,
            JPair(an, "life_seconds", 900, 1500).Max, JFix(an, "drift", Fix64.Zero), rings);
    }

    private static SiteMiningDef ReadMining(JsonElement mn)
    {
        var list = new List<SiteOreDef>();
        var ore = JObj(mn, "ore");
        if (ore.ValueKind == JsonValueKind.Object)
            foreach (var v in ore.EnumerateObject())
            {
                var c = JPair(v.Value, "containers", 0, 0);
                list.Add(new SiteOreDef(v.Name, c.Min, c.Max, JWeights(v.Value, "ores")));
            }
        var rf = JObj(mn, "rockfall");
        return new SiteMiningDef(JInt(mn, "seconds", 30), list, rf.ValueKind == JsonValueKind.Object
            ? new SiteRockfallDef(JInt(rf, "chance_per_min", 0), JPair(rf, "hull", 0, 0).Min, JPair(rf, "hull", 0, 0).Max, JInt(rf, "tractor", 0)) : null);
    }

    private static ShipStatusRules? ReadShipStatus(JsonElement maps)
    {
        if (maps.ValueKind != JsonValueKind.Object || !maps.TryGetProperty("ship_status", out var s) || s.ValueKind != JsonValueKind.Object) return null;
        var hull = JObj(s, "hull");
        var bands = new List<HullBandDef>();
        if (hull.ValueKind == JsonValueKind.Object && hull.TryGetProperty("bands", out var bs) && bs.ValueKind == JsonValueKind.Array)
            foreach (var b in bs.EnumerateArray())
            {
                var id = JStr(b, "id");
                var band = Enum.GetValues<HullBand>().Where(x => x.ToString().ToLowerInvariant() == id).Select(x => (HullBand?)x).FirstOrDefault() ?? (HullBand)(-1);
                bands.Add(new HullBandDef(id, band, JInt(b, "from", 0), JFix(b, "flight", Fix64.One), JFix(b, "scan_reach", Fix64.One), JBool(b, "hazardous", true), JStr(b, "text")));
            }
        var crippled = JObj(hull, "crippled");
        var systems = new List<ShipSystemDef>();
        if (s.TryGetProperty("systems", out var ss) && ss.ValueKind == JsonValueKind.Array)
            foreach (var x in ss.EnumerateArray())
            {
                var id = JStr(x, "id");
                systems.Add(new ShipSystemDef(id, ShipSystemOf(id) ?? (ShipSystem)(-1), JStr(x, "name"), JFix(x, "scan", Fix64.One), JFix(x, "flight", Fix64.One), JStr(x, "text")));
            }
        var down = JPair(s, "down_seconds", 60, 180);
        var repairs = JObj(s, "repairs");
        var field = JObj(repairs, "field");
        var patch = JObj(repairs, "patch");
        var crew = JObj(s, "crew");
        var roles = JObj(crew, "roles");
        var def = JObj(s, "default_ship");
        var away = JObj(s, "away_team");
        var tables = new List<AwayTableDef>();
        var tablesEl = JObj(away, "tables");
        if (tablesEl.ValueKind == JsonValueKind.Object)
            foreach (var t in tablesEl.EnumerateObject())
            {
                var tb = t.Value;
                var bas = JObj(tb, "base");
                var unp = JObj(tb, "unpowered");
                var steps = new List<AwayStepDef>();
                if (tb.ValueKind == JsonValueKind.Object && tb.TryGetProperty("steps", out var sts) && sts.ValueKind == JsonValueKind.Array)
                    foreach (var st in sts.EnumerateArray())
                    {
                        var choices = new List<AwayChoiceDef>();
                        if (st.ValueKind == JsonValueKind.Object && st.TryGetProperty("choices", out var cs) && cs.ValueKind == JsonValueKind.Array)
                            foreach (var c in cs.EnumerateArray())
                            {
                                var shift = JObj(c, "shift");
                                var bring = JObj(c, "bring");
                                var surv = JPair(bring, "survivors", 0, 0);
                                choices.Add(new AwayChoiceDef(JStr(c, "label"), JStr(c, "hint"), JInt(c, "injury", 0), JInt(c, "loss", 0), JInt(c, "find", 0), JInt(c, "seconds", 0),
                                    shift.ValueKind == JsonValueKind.Object
                                        ? shift.EnumerateObject().Select(x => (x.Name, x.Value.ValueKind == JsonValueKind.Number && x.Value.TryGetInt32(out var i) ? i : 0)).ToList()
                                        : new List<(string, int)>())
                                {
                                    BringResearch = JBool(bring, "research"), BringSalvage = JStr(bring, "salvage"),
                                    BringSurvivorsMin = surv.Min, BringSurvivorsMax = surv.Max, BringChance = JInt(bring, "chance", 100),
                                });
                            }
                        steps.Add(new AwayStepDef(JStr(st, "title"), choices));
                    }
                var outcomes = new List<AwayOutcomeDef>();
                if (tb.ValueKind == JsonValueKind.Object && tb.TryGetProperty("outcomes", out var os) && os.ValueKind == JsonValueKind.Array)
                    foreach (var o in os.EnumerateArray())
                        outcomes.Add(new AwayOutcomeDef(JStr(o, "id"), JInt(o, "weight", 0), JStr(o, "text"), JBool(o, "research"), JInt(o, "hull", 0), JStr(o, "system"),
                            JInt(o, "injure", 0), JFix(o, "loss_mult", Fix64.One), JFix(o, "find_mult", Fix64.One), JBool(o, "quarantine")) { Deflect = JBool(o, "deflect") });
                tables.Add(new AwayTableDef(t.Name, JInt(bas, "injury", 0), JInt(bas, "loss", 0), JInt(bas, "find", 0), JInt(unp, "injury", 0), JInt(unp, "loss", 0),
                    JInt(unp, "find", 0), steps, outcomes));
            }
        var byShip = JObj(away, "max_by_ship");
        return new ShipStatusRules
        {
            Bands = bands,
            RecoverTo = JInt(crippled, "recover_to", 20),
            RecoverSeconds = JInt(crippled, "recover_seconds", 180),
            Systems = systems,
            DownMin = down.Min,
            DownMax = down.Max,
            FieldPer10s = JFix(field, "per_10s", Fix64.One),
            FieldUpTo = JInt(field, "up_to", 60),
            PatchHull = JInt(patch, "hull", 15),
            PatchSeconds = JInt(patch, "seconds", 30),
            InjurySeconds = JInt(crew, "injury_seconds", 180),
            MedicalSeconds = JInt(crew, "medical_seconds", 60),
            MedicalMax = JInt(crew, "medical_max", 3),
            InjuryMinSeconds = JInt(crew, "injury_min_seconds", 30),
            HalfCrewFlight = JFix(crew, "half_crew_flight", Fix64.One),
            LogMax = JInt(crew, "log", 40),
            Ranks = JStrings(crew, "ranks"),
            Roles = roles.ValueKind == JsonValueKind.Object
                ? roles.EnumerateObject().Select(r => (r.Name, (IReadOnlyList<string>)(r.Value.ValueKind == JsonValueKind.Array
                    ? r.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new List<string>()))).ToList()
                : new List<(string, IReadOnlyList<string>)>(),
            Names = JStrings(crew, "names"),
            DefaultCrew = def.ValueKind == JsonValueKind.Object && def.TryGetProperty("crew", out var dc) && dc.ValueKind == JsonValueKind.Array
                ? dc.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var i) ? i : -1).ToList() : new List<int>(),
            DefaultHold = JInt(def, "hold", 0),
            DefaultFocus = JStr(def, "focus"),
            DefaultPatchKits = JInt(s, "default_patch_kits", 0),
            SurvivorsPerContainer = JInt(JObj(s, "hold"), "survivors_per_container", 3),
            AwayMin = JInt(away, "min", 2),
            AwayMax = JInt(away, "max", 6),
            AwayMaxShare = JFix(away, "max_share", Fix64.Half),
            AwayMaxByShip = byShip.ValueKind == JsonValueKind.Object
                ? byShip.EnumerateObject().Select(x => (x.Name, x.Value.ValueKind == JsonValueKind.Number && x.Value.TryGetInt32(out var i) ? i : -1)).ToList()
                : new List<(string, int)>(),
            StepSeconds = JInt(away, "step_seconds", 60),
            ReturnSeconds = JInt(away, "return_seconds", 20),
            DefenseLoss = JInt(away, "defense_loss", 4),
            MedicDivision = JStr(away, "medic_division"),
            MedicShare = JFix(away, "medic_share", Fix64.Half),
            QuarantineSeconds = JInt(away, "quarantine_seconds", 180),
            Tables = tables,
        };
    }

    /// <summary>A ShipSystem from its data id ("life_support"), or null.</summary>
    public static ShipSystem? ShipSystemOf(string? id)
    {
        if (id == null) return null;
        foreach (var s in Enum.GetValues<ShipSystem>())
            if (SystemId(s) == id || s.ToString().ToLowerInvariant() == id) return s;
        return null;
    }

    /// <summary>A ShipSystem's data id ("life_support").</summary>
    public static string SystemId(ShipSystem s) => s == ShipSystem.LifeSupport ? "life_support" : s.ToString().ToLowerInvariant();

    // ---------------------------------------------------------------- checks

    private void ValidateSpaceJobs(List<string> problems)
    {
        if (SpaceSites is { } r)
        {
            const string w = "Space sites";
            if (r.CountMin < 0 || r.CountMax < r.CountMin || r.CountMax > 20) problems.Add($"{w}: generation count needs 0 <= min <= max <= 20.");
            if (r.OrbitMin <= Fix64.Zero || r.OrbitMax < r.OrbitMin || r.OrbitMax > Fix64.FromInt(5000)) problems.Add($"{w}: generation orbit needs 0 < min <= max <= 5000.");
            if (r.ClearOfBody < Fix64.Zero || r.Spacing < Fix64.Zero || r.ClearOfBody > Fix64.FromInt(1000) || r.Spacing > Fix64.FromInt(1000))
                problems.Add($"{w}: clear_of_body and spacing must be 0..1000.");
            if (r.Tries < 1 || r.Tries > 200) problems.Add($"{w}: tries must be 1..200.");
            if (r.IdentifyRange <= Fix64.Zero || r.IdentifyRange > Fix64.FromInt(5000)) problems.Add($"{w}: identify_range must be above 0 and at most 5000.");
            if (r.SpentSeconds < 0 || r.SpentSeconds > 36000) problems.Add($"{w}: spent_seconds must be 0..36000.");
            if (r.ColonyReachSeconds < 1 || r.ColonyReachSeconds > 3600) problems.Add($"{w}: colony_reach_seconds must be 1..3600.");
            if (r.CountMax > 0 && (r.Weights.Count == 0 || r.Weights.Sum(x => System.Math.Max(0, x.Weight)) <= 0)) problems.Add($"{w}: generation weights need a positive sum.");
            foreach (var x in r.Weights)
            {
                if (x.Weight < 0) problems.Add($"{w}: weight for {x.Id} can't be negative.");
                if (r.Kind(x.Id) == null) problems.Add($"{w}: generation weight for unknown kind '{x.Id}'.");
                else if (x.Id == "station") problems.Add($"{w}: the station is placed on its own, not by weight.");
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in r.Kinds)
            {
                var kw = $"{w} kind {k.Id}";
                if (!seen.Add(k.Id)) problems.Add($"{kw}: listed twice.");
                if (!Enum.IsDefined(k.Kind)) problems.Add($"{kw}: not a site kind ({string.Join(", ", Enum.GetValues<SiteKind>().Select(SpaceSiteRules.IdOf))}).");
                if (k.Name.Length == 0 || k.Letter.Length == 0) problems.Add($"{kw}: needs a name and a letter.");
                if (k.ScanSeconds < 1 || k.ScanSeconds > 600) problems.Add($"{kw}: scan_seconds must be 1..600.");
                if (k.SizeMin < 1 || k.SizeMax < k.SizeMin || k.SizeMax > 5) problems.Add($"{kw}: size needs 1 <= min <= max <= 5.");
                if (k.Variants.Count == 0 || k.Variants.Sum(v => System.Math.Max(0, v.Weight)) <= 0 || k.Variants.Any(v => v.Weight < 0)) problems.Add($"{kw}: variants need weights >= 0 with a positive sum.");
                foreach (var v in k.Variants)
                    if (k.OriginOf(v.Id).Length == 0) problems.Add($"{kw}: variant {v.Id} has no origin text.");
                if (k.Alongside is { } al && (al.Seconds < 1 || al.Seconds > 600 || al.Chance < 0 || al.Chance > 100 || al.HullMin < 0 || al.HullMax < al.HullMin || al.HullMax > 100))
                    problems.Add($"{kw}: alongside needs seconds 1..600, chance 0..100 and 0 <= hull min <= max <= 100.");
                if (k.RestorePower is { } rp && (rp.Seconds < 1 || rp.Seconds > 3600 || rp.Kits < 0 || rp.Kits > 20)) problems.Add($"{kw}: restore_power needs seconds 1..3600 and kits 0..20.");
                if (k.Logs is { } lg && (lg.Seconds < 1 || lg.Seconds > 3600 || lg.LatinumMin < 0 || lg.LatinumMax < lg.LatinumMin || lg.ResearchMin < 0 || lg.ResearchMax < lg.ResearchMin || lg.LatinumMax > 100000 || lg.ResearchMax > 100000))
                    problems.Add($"{kw}: logs needs seconds 1..3600 and 0 <= min <= max (latinum, research).");
                if (k.Logs != null && k.RestorePower == null) problems.Add($"{kw}: logs need restore_power (they are read once the ship is powered).");
                if (k.Reactor is { } re && (re.Seconds < 1 || re.Seconds > 36000 || re.Hull < 0 || re.Hull > 100)) problems.Add($"{kw}: reactor needs seconds 1..36000 and hull 0..100.");
                if (k.Sweep is { } sw)
                {
                    if (sw.Seconds < 1 || sw.Seconds > 3600 || sw.HullEverySeconds < 1 || sw.HullEverySeconds > 3600 || sw.Hull < 0 || sw.Hull > 100)
                        problems.Add($"{kw}: sweep needs seconds and hull_every_seconds 1..3600, hull 0..100.");
                    if (sw.FindsMin < 0 || sw.FindsMax < sw.FindsMin || sw.FindsMax > 20 || sw.Always.Count > sw.FindsMin) problems.Add($"{kw}: sweep finds need 0 <= min <= max <= 20 and room for every always item.");
                    if (sw.FindsMax > sw.Always.Count && (sw.Items.Count == 0 || sw.Items.Sum(x => System.Math.Max(0, x.Weight)) <= 0 || sw.Items.Any(x => x.Weight < 0)))
                        problems.Add($"{kw}: sweep items need weights >= 0 with a positive sum.");
                    foreach (var id in sw.Always.Concat(sw.Items.Select(x => x.Id)))
                        if (id != "lifepod" && r.SalvageItem(id) == null) problems.Add($"{kw}: sweep item '{id}' is not salvage (or lifepod).");
                    if (sw.SurvivorsMin < 0 || sw.SurvivorsMax < sw.SurvivorsMin || sw.SurvivorsMax > 20) problems.Add($"{kw}: lifepod_survivors needs 0 <= min <= max <= 20.");
                }
                if (k.ResearchMin < 0 || k.ResearchMax < k.ResearchMin || k.ResearchMax > 100000) problems.Add($"{kw}: research needs 0 <= min <= max.");
                if (k.AwayTable.Length > 0 && ShipStatus?.Table(k.AwayTable) == null) problems.Add($"{kw}: away_table '{k.AwayTable}' is not in ship_status away_team tables.");
                if (k.AnyAway && k.Alongside == null) problems.Add($"{kw}: an away team needs alongside (the team crosses from alongside).");
                foreach (var (v, t) in k.VariantAway)
                {
                    if (k.Variants.All(x => x.Id != v)) problems.Add($"{kw}: away_tables names unknown variant '{v}'.");
                    if (ShipStatus?.Table(t) == null) problems.Add($"{kw}: away_tables {v} '{t}' is not in ship_status away_team tables.");
                }
                if (k.Mining is { } mn)
                {
                    if (mn.Seconds < 1 || mn.Seconds > 3600) problems.Add($"{kw}: mining seconds must be 1..3600.");
                    foreach (var v in k.Variants)
                        if (mn.OreOf(v.Id) == null) problems.Add($"{kw}: mining ore has nothing for variant {v.Id}.");
                    foreach (var o in mn.Ore)
                    {
                        if (k.Variants.All(x => x.Id != o.Variant)) problems.Add($"{kw}: mining ore for unknown variant '{o.Variant}'.");
                        if (o.ContainersMin < 1 || o.ContainersMax < o.ContainersMin || o.ContainersMax > 100) problems.Add($"{kw}: mining ore {o.Variant} needs 1 <= containers min <= max <= 100.");
                        if (o.Ores.Count == 0 || o.Ores.Sum(x => System.Math.Max(0, x.Weight)) <= 0 || o.Ores.Any(x => x.Weight < 0)) problems.Add($"{kw}: mining ore {o.Variant} needs ores with weights >= 0 and a positive sum.");
                        foreach (var x in o.Ores)
                            if (r.Ore(x.Id) == null) problems.Add($"{kw}: mining ore {o.Variant}: '{x.Id}' is not in space_sites ore kinds.");
                    }
                    if (mn.Rockfall is { } rf && (rf.ChancePerMin < 0 || rf.ChancePerMin > 100 || rf.HullMin < 0 || rf.HullMax < rf.HullMin || rf.HullMax > 100 || rf.Tractor < 0 || rf.Tractor > 100))
                        problems.Add($"{kw}: mining rockfall needs chance_per_min 0..100, 0 <= hull min <= max <= 100, tractor 0..100.");
                }
                if (k.Tow is { } tw && (tw.LatinumMin < 0 || tw.LatinumMax < tw.LatinumMin || tw.ResearchMin < 0 || tw.ResearchMax < tw.ResearchMin || tw.DuraniumMin < 0
                        || tw.DuraniumMax < tw.DuraniumMin || tw.LatinumMax > 100000 || tw.ResearchMax > 100000 || tw.DuraniumMax > 100000))
                    problems.Add($"{kw}: tow needs 0 <= min <= max (latinum, research, duranium).");
                if (k.Tow != null && k.Alongside == null) problems.Add($"{kw}: towing needs alongside (the tractor locks on from alongside).");
                if (k.Kind == SiteKind.Station && (k.Tow != null || k.Mining != null)) problems.Add($"{kw}: the station can't be mined or towed.");
            }
            if (r.Asteroids is { } ag)
            {
                if (ag.CountMin < 0 || ag.CountMax < ag.CountMin || ag.CountMax > 20) problems.Add($"{w}: generation asteroids count needs 0 <= min <= max <= 20.");
                if (ag.NearBelt < 0 || ag.NearBelt > 100 || ag.BeltSpread < Fix64.Zero || ag.BeltSpread > Fix64.FromInt(1000) || ag.Spacing < Fix64.Zero || ag.Spacing > Fix64.FromInt(1000))
                    problems.Add($"{w}: generation asteroids needs near_belt 0..100, belt_spread and spacing 0..1000.");
                if (ag.CountMax > 0 && r.Kind(SiteKind.Asteroid) == null) problems.Add($"{w}: generation asteroids needs an asteroid kind.");
            }
            if (r.Weights.Any(x => x.Id == "asteroid")) problems.Add($"{w}: asteroids are rolled by generation.asteroids, not by weight.");
            ValidateSpaceEvents(r, problems); // GameData.SpaceEvents.cs (phase 3)
            if (r.OrePerContainer < 1 || r.OrePerContainer > 10000) problems.Add($"{w}: ore per_container must be 1..10000.");
            var oreIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in r.Ores)
            {
                if (!oreIds.Add(o.Id) || o.Name.Length == 0) problems.Add($"{w}: ore '{o.Id}' needs a unique id and a name.");
                if (!_resources.ContainsKey(o.Id)) problems.Add($"{w}: ore '{o.Id}' is not a resource.");
                if (o.Latinum < Fix64.Zero || o.Latinum > Fix64.FromInt(10000)) problems.Add($"{w}: ore {o.Id} latinum must be 0..10000.");
            }
            if (r.TraderShare < Fix64.Zero || r.TraderShare > Fix64.FromInt(10)) problems.Add($"{w}: ore traders share must be 0..10.");
            if (r.TowFlight < Fix64.One || r.TowFlight > Fix64.FromInt(10)) problems.Add($"{w}: tow flight must be 1..10.");
            if (r.Kind(SiteKind.Station) == null) problems.Add($"{w}: needs a station kind.");
            foreach (var k in new[] { SiteKind.Derelict, SiteKind.KnownDerelict, SiteKind.Debris })
                if (r.Kind(k) == null) problems.Add($"{w}: needs a {SpaceSiteRules.IdOf(k)} kind (phase 1).");
            var salvageIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var x in r.Salvage)
            {
                if (!SnakeId.IsMatch(x.Id) || !salvageIds.Add(x.Id) || x.Name.Length == 0) problems.Add($"{w}: salvage '{x.Id}' needs a unique lower-case id and a name.");
                if (x.Containers < 0 || x.Containers > 20 || x.Latinum < 0 || x.Latinum > 100000 || x.Research < 0 || x.Research > 100000)
                    problems.Add($"{w}: salvage {x.Id} needs containers 0..20, latinum and research 0..100000.");
            }
            var st = r.Station;
            if (st.Names.Count == 0 || st.Names.Any(string.IsNullOrWhiteSpace)) problems.Add($"{w}: station names can't be empty.");
            if (st.OrbitMin <= Fix64.Zero || st.OrbitMax < st.OrbitMin || st.OrbitMax > Fix64.FromInt(5000)) problems.Add($"{w}: station orbit needs 0 < min <= max <= 5000.");
            if (st.DockSeconds < 1 || st.DockSeconds > 3600) problems.Add($"{w}: station dock_seconds must be 1..3600.");
            if (st.HirePerVisit < 0 || st.HirePerVisit > 50 || st.HireLatinum < 0 || st.HireLatinum > 100000 || st.SurvivorLatinum < 0 || st.SurvivorLatinum > 100000)
                problems.Add($"{w}: station hire per_visit 0..50, latinum and survivor_latinum 0..100000.");
            if (!_resources.ContainsKey(Latinum)) problems.Add($"{w}: needs the resource '{Latinum}' (what stations pay and take).");
        }

        if (ShipStatus is not { } s) return;
        const string sw2 = "Ship status";
        foreach (var b in Enum.GetValues<HullBand>())
            if (s.Bands.Count(x => x.Band == b) != 1) problems.Add($"{sw2}: hull bands need each of scuffed, worn, damaged, critical, crippled once.");
        foreach (var b in s.Bands)
        {
            if (!Enum.IsDefined(b.Band)) problems.Add($"{sw2}: hull band '{b.Id}' is unknown.");
            if (b.Band == HullBand.Crippled ? b.From != 0 : b.From < 1 || b.From > 100) problems.Add($"{sw2}: hull band {b.Id}: from must be 1..100 (crippled: 0).");
            if (b.Flight <= Fix64.Zero || b.Flight > Fix64.FromInt(10) || b.ScanReach <= Fix64.Zero || b.ScanReach > Fix64.FromInt(10)) problems.Add($"{sw2}: hull band {b.Id}: flight and scan_reach must be above 0 and at most 10.");
        }
        var froms = s.Bands.Where(b => b.Band != HullBand.Crippled).OrderBy(b => (int)b.Band).Select(b => b.From).ToList();
        for (var i = 1; i < froms.Count; i++)
            if (froms[i] >= froms[i - 1]) problems.Add($"{sw2}: hull bands must step down (scuffed > worn > damaged > critical).");
        if (s.RecoverTo < 1 || s.RecoverTo > 100 || s.RecoverSeconds < 1 || s.RecoverSeconds > 36000) problems.Add($"{sw2}: crippled needs recover_to 1..100 and recover_seconds 1..36000.");
        foreach (var x in Enum.GetValues<ShipSystem>())
            if (s.Systems.Count(y => y.System == x) != 1) problems.Add($"{sw2}: systems need {SystemId(x)} once.");
        foreach (var x in s.Systems)
        {
            if (!Enum.IsDefined(x.System)) problems.Add($"{sw2}: system '{x.Id}' is unknown.");
            if (x.Scan <= Fix64.Zero || x.Scan > Fix64.FromInt(10) || x.Flight <= Fix64.Zero || x.Flight > Fix64.FromInt(10)) problems.Add($"{sw2}: system {x.Id}: scan and flight above 0 and at most 10.");
        }
        if (s.DownMin < 1 || s.DownMax < s.DownMin || s.DownMax > 36000) problems.Add($"{sw2}: down_seconds needs 1 <= min <= max <= 36000.");
        if (s.FieldPer10s < Fix64.Zero || s.FieldPer10s > Fix64.FromInt(100) || s.FieldUpTo < 0 || s.FieldUpTo > 100) problems.Add($"{sw2}: repairs field needs per_10s 0..100 and up_to 0..100.");
        if (s.PatchHull < 0 || s.PatchHull > 100 || s.PatchSeconds < 1 || s.PatchSeconds > 3600) problems.Add($"{sw2}: repairs patch needs hull 0..100 and seconds 1..3600.");
        if (s.InjurySeconds < 1 || s.InjurySeconds > 36000 || s.MedicalSeconds < 0 || s.MedicalMax < 0 || s.MedicalMax > 20 || s.InjuryMinSeconds < 1 || s.InjuryMinSeconds > s.InjurySeconds)
            problems.Add($"{sw2}: crew needs injury_seconds 1..36000, medical_seconds and medical_max >= 0, 1 <= injury_min_seconds <= injury_seconds.");
        if (s.HalfCrewFlight <= Fix64.Zero || s.HalfCrewFlight > Fix64.FromInt(10)) problems.Add($"{sw2}: half_crew_flight must be above 0 and at most 10.");
        if (s.LogMax < 1 || s.LogMax > 500) problems.Add($"{sw2}: crew log must be 1..500.");
        if (s.Ranks.Count == 0 || s.Names.Count == 0) problems.Add($"{sw2}: crew needs ranks and names.");
        if (FitOut is { } fit)
        {
            foreach (var d in fit.Divisions)
                if (s.RolesOf(d.Id).Count == 0) problems.Add($"{sw2}: crew roles needs a list for {d.Id}.");
            if (s.DefaultCrew.Count != fit.Divisions.Count || s.DefaultCrew.Any(c => c < 1 || c > 1000)) problems.Add($"{sw2}: default_ship crew needs one count 1..1000 per division.");
            if (fit.Focus(s.DefaultFocus) == null) problems.Add($"{sw2}: default_ship focus '{s.DefaultFocus}' is not a fit-out focus.");
            if (fit.DivisionIndex(s.MedicDivision) < 0) problems.Add($"{sw2}: away_team medic_division '{s.MedicDivision}' is not a division.");
            foreach (var (ship, max) in s.AwayMaxByShip)
                if (ShipChoice(ship) == null || max < 1 || max > 20) problems.Add($"{sw2}: away_team max_by_ship {ship}: a ship_choice id and 1..20.");
        }
        if (s.DefaultHold < 0 || s.DefaultHold > 200 || s.DefaultPatchKits < 0 || s.DefaultPatchKits > 20) problems.Add($"{sw2}: default_ship hold 0..200 and default_patch_kits 0..20.");
        if (s.SurvivorsPerContainer < 1 || s.SurvivorsPerContainer > 100) problems.Add($"{sw2}: hold survivors_per_container must be 1..100.");
        if (s.AwayMin < 1 || s.AwayMax < s.AwayMin || s.AwayMax > 20) problems.Add($"{sw2}: away_team needs 1 <= min <= max <= 20.");
        if (s.AwayMaxShare <= Fix64.Zero || s.AwayMaxShare > Fix64.One || s.MedicShare < Fix64.Zero || s.MedicShare > Fix64.One) problems.Add($"{sw2}: away_team max_share (0, 1] and medic_share [0, 1].");
        if (s.StepSeconds < 5 || s.StepSeconds > 3600 || s.ReturnSeconds < 0 || s.ReturnSeconds > 3600 || s.QuarantineSeconds < 1 || s.QuarantineSeconds > 36000 || s.DefenseLoss < 0 || s.DefenseLoss > 100)
            problems.Add($"{sw2}: away_team step_seconds 5..3600, return_seconds 0..3600, quarantine_seconds 1..36000, defense_loss 0..100.");
        foreach (var t in s.Tables)
        {
            var tw = $"{sw2} away table {t.Id}";
            foreach (var n in new[] { t.Injury, t.Loss, t.Find })
                if (n < 0 || n > 100) problems.Add($"{tw}: base injury, loss and find must be 0..100.");
            if (t.Steps.Count < 1 || t.Steps.Count > 5) problems.Add($"{tw}: needs 1 to 5 steps.");
            foreach (var st in t.Steps)
            {
                if (st.Title.Length == 0 || st.Choices.Count < 2 || st.Choices.Count > 4) problems.Add($"{tw}: step '{st.Title}' needs a title and 2 to 4 choices.");
                foreach (var c in st.Choices)
                {
                    if (c.Label.Length == 0) problems.Add($"{tw}: a choice needs a label.");
                    if (c.Injury < -100 || c.Injury > 100 || c.Loss < -100 || c.Loss > 100 || c.Find < -100 || c.Find > 100 || c.Seconds < -600 || c.Seconds > 600)
                        problems.Add($"{tw} choice {c.Label}: injury / loss / find -100..100 and seconds -600..600.");
                    foreach (var (o, pts) in c.Shift)
                        if (t.Outcomes.All(x => x.Id != o) || pts < -100 || pts > 100) problems.Add($"{tw} choice {c.Label}: shift '{o}' must be an outcome, by -100..100.");
                    if (c.BringSalvage.Length > 0 && SpaceSites?.SalvageItem(c.BringSalvage) == null) problems.Add($"{tw} choice {c.Label}: bring salvage '{c.BringSalvage}' is not salvage.");
                    if (c.BringSurvivorsMin < 0 || c.BringSurvivorsMax < c.BringSurvivorsMin || c.BringSurvivorsMax > 20 || c.BringChance < 0 || c.BringChance > 100)
                        problems.Add($"{tw} choice {c.Label}: bring survivors 0 <= min <= max <= 20 and chance 0..100.");
                }
            }
            if (t.Steps.Count > 0 && t.Steps[^1].Choices.All(c => !c.BringResearch && c.BringSalvage.Length == 0 && c.BringSurvivorsMax == 0))
                problems.Add($"{tw}: the last step's choices must bring something back.");
            if (t.Outcomes.Count == 0 || t.Outcomes.Sum(o => System.Math.Max(0, o.Weight)) <= 0) problems.Add($"{tw}: outcomes need weights with a positive sum.");
            foreach (var o in t.Outcomes)
            {
                if (!SnakeId.IsMatch(o.Id) || o.Text.Length == 0) problems.Add($"{tw}: outcome '{o.Id}' needs a lower-case id and a text.");
                if (o.Weight < 0 || o.Hull < 0 || o.Hull > 100 || o.Injure < 0 || o.Injure > 20 || o.LossMult < Fix64.Zero || o.LossMult > Fix64.FromInt(10)
                    || o.FindMult < Fix64.Zero || o.FindMult > Fix64.FromInt(10))
                    problems.Add($"{tw} outcome {o.Id}: weight >= 0, hull 0..100, injure 0..20, loss_mult and find_mult 0..10.");
                if (o.System.Length > 0 && ShipSystemOf(o.System) == null) problems.Add($"{tw} outcome {o.Id}: unknown system '{o.System}'.");
            }
        }
    }

    /// <summary>A hold item a mission may name: patch_kit, medical, salvage:&lt;id&gt; (maps space_sites salvage), survivors
    /// (any division) or survivors:&lt;division&gt;.</summary>
    public bool ValidHoldItem(string? item)
    {
        if (item == null) return false;
        if (item is StarSystemState.PatchKitItem or StarSystemState.MedicalItem or "survivors") return true;
        if (item.StartsWith(StarSystemState.SalvagePrefix, StringComparison.Ordinal)) return SpaceSites?.SalvageItem(item[StarSystemState.SalvagePrefix.Length..]) != null;
        if (item.StartsWith(StarSystemState.SurvivorsPrefix, StringComparison.Ordinal)) return FitOut?.DivisionIndex(item[StarSystemState.SurvivorsPrefix.Length..]) >= 0;
        if (item == "ore") return SpaceSites?.Ores.Count > 0;
        if (item.StartsWith(StarSystemState.OrePrefix, StringComparison.Ordinal)) return SpaceSites?.Ore(item[StarSystemState.OrePrefix.Length..]) != null;
        return false;
    }

    /// <summary>The resource stations pay in and charge (latinum).</summary>
    public const string Latinum = "latinum";
}
