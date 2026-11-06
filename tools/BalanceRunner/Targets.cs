using System.Globalization;
using System.Text.Json;

namespace FinalFrontier.Balance;

/// <summary>A window a measure should land in for a build order (from balance/targets.json); times in minutes.</summary>
public sealed record TargetWindow(double? Min, double? Max, string Why);

/// <summary>How a variant's measure did against its window: Verdict "in", "early"/"late" (times), "low"/"high"
/// (other measures), "missed" (no seed reached the milestone) or "after run" (not reached, and the window starts after
/// the run's end: not judged); InSeeds = seeds whose own value is in the window.</summary>
public sealed record TargetResult(double? Min, double? Max, string Why, string Verdict, int InSeeds, int Seeds);

/// <summary>
/// Target windows per build order, read from balance/targets.json:
/// <c>{ "orders": { "standard_opener": { "ms:built:refinery": { "min": 1, "max": 2, "why": "…" }, "peak_storage": { "min": 80 } } } }</c>.
/// Keys are the report's measure keys (summary.json); time measures (milestones) take minutes, the rest their own unit.
/// "*" applies to every order (an order's own entry wins).
/// </summary>
public sealed class Targets
{
    private readonly Dictionary<string, Dictionary<string, TargetWindow>> _orders = new(StringComparer.Ordinal);

    public static Targets Empty { get; } = new();

    public string Source { get; private init; } = "";

    public static Targets Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var t = new Targets { Source = path };
        if (!doc.RootElement.TryGetProperty("orders", out var orders)) throw new FormatException($"{path}: needs an \"orders\" object");
        foreach (var o in orders.EnumerateObject())
        {
            var list = new Dictionary<string, TargetWindow>(StringComparer.Ordinal);
            foreach (var m in o.Value.EnumerateObject())
            {
                if (m.Name.StartsWith('_')) continue;
                double? Num(string name) => m.Value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                var w = new TargetWindow(Num("min"), Num("max"), m.Value.TryGetProperty("why", out var why) ? why.GetString() ?? "" : "");
                if (w.Min == null && w.Max == null) throw new FormatException($"{path}: {o.Name} › {m.Name} needs a min or a max");
                list[m.Name] = w;
            }
            t._orders[o.Name] = list;
        }
        return t;
    }

    /// <summary>The windows that apply to an order: "*" first, then the order's own.</summary>
    public IReadOnlyDictionary<string, TargetWindow> For(string order)
    {
        var all = new Dictionary<string, TargetWindow>(StringComparer.Ordinal);
        foreach (var key in new[] { "*", order })
            if (_orders.TryGetValue(key, out var list))
                foreach (var (k, w) in list) all[k] = w;
        return all;
    }

    /// <summary>Judges a measure against its window (null when the order has none for it).</summary>
    public static TargetResult? Judge(TargetWindow? w, Measure m, IReadOnlyList<double?> perSeed, double runMinutes = 0)
    {
        if (w == null) return null;
        var time = m.Unit == "time";
        double? Scale(double? v) => v is { } x && time ? x / 60 : v; // milestones are seconds; windows are minutes
        bool In(double v) => (w.Min is not { } lo || v >= lo - 1e-9) && (w.Max is not { } hi || v <= hi + 1e-9);
        var inSeeds = perSeed.Count(v => Scale(v) is { } x && In(x));
        var median = Scale(m.Spread.Median);
        string verdict;
        if (median is not { } med) verdict = time && runMinutes > 0 && w.Min is { } from && from >= runMinutes ? "after run" : time ? "missed" : "–";
        else if (In(med)) verdict = "in";
        else if (w.Min is { } lo && med < lo) verdict = time ? "early" : "low";
        else verdict = time ? "late" : "high";
        return new TargetResult(w.Min, w.Max, w.Why, verdict, inSeeds, perSeed.Count);
    }

    public static string Window(TargetWindow w, string unit)
    {
        string F(double v) => unit == "time" ? $"{v.ToString("0.##", CultureInfo.InvariantCulture)} min" : v.ToString("0.##", CultureInfo.InvariantCulture) + (unit == "%" ? "%" : "");
        return (w.Min, w.Max) switch
        {
            ({ } a, { } b) => $"{F(a)}–{F(b)}",
            ({ } a, null) => $"≥ {F(a)}",
            (null, { } b) => $"≤ {F(b)}",
            _ => "",
        };
    }
}
