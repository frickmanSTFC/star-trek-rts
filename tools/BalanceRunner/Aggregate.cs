using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinalFrontier.Balance;

/// <summary>min / median / max of one measure across seeds (null where no seed reached it; Reached = how many did).</summary>
public sealed record Spread(double? Min, double? Median, double? Max, int Reached, int Runs);

/// <summary>One headline measure of a variant, aggregated across its seeds.</summary>
public sealed record Measure(string Key, string Label, string Group, string Unit, bool? GoodUp, Spread Spread);

/// <summary>A set of runs that share data + build order + faction (one per seed).</summary>
public sealed class Variant
{
    public string Label { get; set; } = "";
    public string Data { get; set; } = "";
    public string Order { get; set; } = "";
    public string Faction { get; set; } = "";
    public List<string> Edits { get; set; } = new();
    public List<RunResult> Runs { get; set; } = new();

    [JsonIgnore] public List<Measure> Measures { get; set; } = new();
}

public static class Aggregate
{
    private static readonly string[] StockKeys = { "duranium", "tritanium", "duranium_ore", "tritanium_ore", "parasteel", "deuterium", "crew" };

    private static readonly Dictionary<string, string> Names = new()
    {
        ["hub_ready"] = "Colony Hub assembled",
        ["crawler_mining"] = "Crawler digging",
        ["first_pod"] = "First cargo pod home",
        ["first_metal"] = "First refined metal",
        ["first_parasteel"] = "First parasteel",
        ["mk2"] = "First Mk II building",
        ["mk3"] = "First Mk III building",
        ["second_base"] = "Second base up",
        ["storage_full"] = "Storage first full",
        ["order_done"] = "Build order finished",
        ["landed"] = "Touchdown",
    };

    public static string MilestoneName(string key)
    {
        if (Names.TryGetValue(key, out var n)) return n;
        var i = key.IndexOf(':');
        if (i < 0) return key;
        var (kind, id) = (key[..i], key[(i + 1)..]);
        return kind switch
        {
            "built" => $"{Pretty(id)} done",
            "unit" => $"{Pretty(id)} rolled out",
            "research" => $"Research: {Pretty(id)}",
            "core" => $"{Pretty(id)} (core upgrade)",
            "mk2" => $"{Pretty(id)} Mk II",
            "mk3" => $"{Pretty(id)} Mk III",
            _ => key,
        };
    }

    public static string Pretty(string id) =>
        string.Join(' ', id.Split('_').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));

    public static Spread SpreadOf(IReadOnlyList<double?> values)
    {
        var got = values.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToList();
        if (got.Count == 0) return new Spread(null, null, null, 0, values.Count);
        var mid = got.Count % 2 == 1 ? got[got.Count / 2] : (got[got.Count / 2 - 1] + got[got.Count / 2]) / 2;
        return new Spread(R(got[0]), R(mid), R(got[^1]), got.Count, values.Count);
    }

    private static double R(double v) => System.Math.Round(v, 2);

    /// <summary>The headline measures of a variant (milestone keys = the union over every variant, so tables line up).</summary>
    public static void Measure(Variant v, IReadOnlyList<string> milestoneKeys)
    {
        var list = new List<Measure>();
        void Add(string key, string label, string group, string unit, bool? goodUp, Func<RunResult, double?> f) =>
            list.Add(new Measure(key, label, group, unit, goodUp, SpreadOf(v.Runs.Select(f).ToList())));

        foreach (var k in milestoneKeys)
            Add("ms:" + k, MilestoneName(k), "Milestones", "time", k == "storage_full" ? true : false, r => r.Milestones.TryGetValue(k, out var t) ? t : null);

        foreach (var res in StockKeys)
            Add("end:" + res, $"{Pretty(res)} at the end", "End of run", "", res.EndsWith("_ore") ? null : true, r => r.Series.TryGetValue("stock:" + res, out var s) && s.Count > 0 ? s[^1] : null);
        foreach (var res in new[] { "duranium", "tritanium", "parasteel" })
            Add("made:" + res, $"{Pretty(res)} income / min (mean after touchdown)", "Income", "", true, r => MeanAfter(r, "income:" + res, r.Milestones.GetValueOrDefault("hub_ready", 0)));
        Add("peak_storage", "Peak storage fill", "Storage", "%", null, r => r.Series.TryGetValue("storage_pct", out var s) && s.Count > 0 ? s.Max() : null);
        Add("end_storage_cap", "Storage capacity at the end", "Storage", "", true, r => Last(r, "storage_cap"));
        foreach (var kind in Recorder.StallKinds)
            Add("stall:" + kind, StallName(kind), "Stalls", "min", false, r => r.StallSeconds.GetValueOrDefault(kind) / 60);
        Add("crawler_uptime", "Crawler time spent digging", "Vehicles", "%", true, r =>
        {
            var total = r.CrawlerSeconds.Values.Sum();
            return total <= 0 ? null : 100 * r.CrawlerSeconds.GetValueOrDefault("mining") / total;
        });
        Add("crawler_broken", "Crawler time broken or in repair", "Vehicles", "min", false, r => (r.CrawlerSeconds.GetValueOrDefault("broken") + r.CrawlerSeconds.GetValueOrDefault("repairing")) / 60);
        Add("ore_mined", "Ore dug out of deposits", "Vehicles", "", true, r => r.Totals.GetValueOrDefault("ore_mined"));
        Add("pods", "Cargo pods flown home", "Vehicles", "", true, r => r.Totals.GetValueOrDefault("pods"));
        Add("flats", "Flat tyres", "Vehicles", "", false, r => r.Totals.GetValueOrDefault("flats"));
        Add("hazards", "Rover hazard hits", "Vehicles", "", false, r => r.Totals.GetValueOrDefault("hazards"));
        Add("rover_min", "Lowest rover integrity seen", "Vehicles", "", true, r => r.Series.TryGetValue("rover_integrity_min", out var s) && s.Count > 1 ? s.Skip(1).Where(x => x > 0).DefaultIfEmpty(0).Min() : null);
        Add("weather_lost", "Driving time lost to weather (vehicle-min)", "Vehicles", "min", false, r => r.Totals.GetValueOrDefault("weather_lost_vehicle_s") / 60);
        Add("grounded", "Drop ships grounded over the hub", "Vehicles", "min", false, r => r.Totals.GetValueOrDefault("drops_grounded_s") / 60);
        Add("crawler_breakdowns", "Crawler breakdowns", "Upkeep", "", false, r => r.Totals.GetValueOrDefault("crawler_breakdowns"));
        Add("building_breakdowns", "Building breakdowns", "Upkeep", "", false, r => r.Totals.GetValueOrDefault("building_breakdowns"));
        Add("maintenance_orders", "Crew maintenance jobs", "Upkeep", "", false, r => r.Totals.GetValueOrDefault("maintenance_orders"));
        Add("buildings", "Finished buildings at the end", "Base", "", true, r => Last(r, "buildings"));
        Add("techs", "Techs known at the end", "Base", "", true, r => r.EndTechs.Count);
        Add("steps", "Build-order steps done", "Base", "", true, r => r.StepsDone);
        Add("idle_share", "Building time idle (no input, full, no upkeep, depleted, broken)", "Base", "%", false, IdleShare);
        Add("speed", "Simulation speed (× real time)", "Run", "×", null, r => r.Speedup);
        Add("crashed", "Run stopped by an exception (1 = yes; see Runs)", "Run", "", false, r => r.Crash.Length > 0 ? 1 : 0);
        v.Measures = list;
    }

    public static string StallName(string kind) => kind switch
    {
        "storage_full" => "Storage full",
        "mining_blocked" => "Mining blocked (storage full / nowhere to send pods)",
        "refinery_idle" => "Every refinery idle (no ore)",
        "power_short" => "A base short of power",
        "building_broken" => "A building broken down",
        _ => kind,
    };

    private static double? Last(RunResult r, string series) => r.Series.TryGetValue(series, out var s) && s.Count > 0 ? s[^1] : null;

    private static double? MeanAfter(RunResult r, string series, double from)
    {
        if (!r.Series.TryGetValue(series, out var s)) return null;
        var values = s.Where((x, i) => r.Times[i] >= from && r.Times[i] > 0).ToList();
        return values.Count == 0 ? null : values.Average();
    }

    private static double? IdleShare(RunResult r)
    {
        double total = 0, idle = 0;
        foreach (var (_, bag) in r.StatusSeconds)
            foreach (var (status, s) in bag)
            {
                total += s;
                if (status is not ("Working" or "LowPower")) idle += s;
            }
        return total <= 0 ? null : 100 * idle / total;
    }

    /// <summary>Every milestone any run reached, ordered by the earliest median time.</summary>
    public static List<string> MilestoneKeys(IEnumerable<Variant> variants)
    {
        var all = variants.SelectMany(v => v.Runs).ToList();
        return all.SelectMany(r => r.Milestones.Keys).Distinct()
            .Where(k => k != "landed")
            .Select(k => (k, t: SpreadOf(all.Select(r => r.Milestones.TryGetValue(k, out var x) ? x : (double?)null).ToList()).Median ?? 1e9))
            .OrderBy(x => x.t).ThenBy(x => x.k, StringComparer.Ordinal).Select(x => x.k).ToList();
    }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>The run as JSON without its wall-clock fields: equal for equal data + order + seed (the determinism check).</summary>
    public static string Fingerprint(RunResult r)
    {
        var copy = JsonSerializer.Deserialize<RunResult>(JsonSerializer.Serialize(r, Json), Json)!;
        copy.WallMs = 0;
        copy.TicksPerSecond = 0;
        copy.Speedup = 0;
        return JsonSerializer.Serialize(copy, Json);
    }

    /// <summary>The run's series as CSV: time_s then one column per series.</summary>
    public static string Csv(RunResult r)
    {
        var keys = r.Series.Keys.ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append("time_s");
        foreach (var k in keys) sb.Append(',').Append(k);
        sb.Append('\n');
        for (var i = 0; i < r.Times.Count; i++)
        {
            sb.Append(r.Times[i].ToString(CultureInfo.InvariantCulture));
            foreach (var k in keys) sb.Append(',').Append(r.Series[k][i].ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
