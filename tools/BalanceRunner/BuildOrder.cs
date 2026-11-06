using System.Globalization;
using System.Text.Json;

namespace FinalFrontier.Balance;

/// <summary>
/// A scripted player's plan, read from a balance/orders/*.json file (format in balance/README.md): standing behaviour
/// (landing preference, surveying, crawlers, upkeep thresholds, a research list) plus an ordered list of steps that the
/// bot works through one at a time. A step can wait for a condition ("when") or something to exist ("after").
/// </summary>
public sealed class BuildOrder
{
    public string Name { get; init; } = "unnamed";
    public string Description { get; init; } = "";
    public string Faction { get; init; } = "federation";
    public string Planet { get; init; } = "class_m";

    /// <summary>Raw ores the landing prefers (by the orbital scan's region hints), most wanted first.</summary>
    public IReadOnlyList<string> LandingPrefer { get; init; } = new[] { "duranium_ore", "tritanium_ore" };

    /// <summary>How far (tiles from the first base) survey rovers explore before they go home.</summary>
    public int SurveyMaxDistance { get; init; } = 14;

    /// <summary>Ores crawlers mine, most wanted first; crawler n starts its preference at entry n (so two crawlers split).</summary>
    public IReadOnlyList<string> CrawlerPrefer { get; init; } = new[] { "duranium_ore", "tritanium_ore" };

    /// <summary>Deposits further than this from every finished base are left alone.</summary>
    public int CrawlerMaxDistance { get; init; } = 12;

    /// <summary>Order the base crew to maintain a building below this condition (0 = never).</summary>
    public int MaintainBelow { get; init; } = 55;

    /// <summary>Send a rover to the repair pad below this integrity.</summary>
    public int RepairBelow { get; init; } = 45;

    /// <summary>Techs to research, in order (whenever a lab is free; a tech whose prerequisite is missing waits).</summary>
    public IReadOnlyList<string> Research { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OrderStep> Steps { get; init; } = Array.Empty<OrderStep>();

    public static BuildOrder Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception e) when (e is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException($"{path}: {e.Message}", e);
        }
    }

    public static BuildOrder Parse(string json, string fallbackName = "unnamed")
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var r = doc.RootElement;
        var steps = new List<OrderStep>();
        if (r.TryGetProperty("steps", out var list))
            foreach (var s in list.EnumerateArray()) steps.Add(OrderStep.Parse(s, steps.Count + 1));
        var landing = r.TryGetProperty("landing", out var l) ? l : default;
        var survey = r.TryGetProperty("survey", out var sv) ? sv : default;
        var crawlers = r.TryGetProperty("crawlers", out var c) ? c : default;
        var upkeep = r.TryGetProperty("upkeep", out var u) ? u : default;
        return new BuildOrder
        {
            Name = Str(r, "name") ?? fallbackName,
            Description = Str(r, "description") ?? "",
            Faction = Str(r, "faction") ?? "federation",
            Planet = Str(r, "planet") ?? "class_m",
            LandingPrefer = Strings(landing, "prefer") ?? new[] { "duranium_ore", "tritanium_ore" },
            SurveyMaxDistance = Int(survey, "max_distance") ?? 14,
            CrawlerPrefer = Strings(crawlers, "prefer") ?? new[] { "duranium_ore", "tritanium_ore" },
            CrawlerMaxDistance = Int(crawlers, "max_distance") ?? 12,
            MaintainBelow = Int(upkeep, "maintain_below") ?? 55,
            RepairBelow = Int(upkeep, "repair_below") ?? 45,
            Research = Strings(r, "research") ?? Array.Empty<string>(),
            Steps = steps,
        };
    }

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    internal static IReadOnlyList<string>? Strings(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return new[] { v.GetString()! };
        return v.EnumerateArray().Select(x => x.GetString()!).ToList();
    }
}

public enum StepKind { Build, Upgrade, Unit, Research, FoundOutpost, DropOutpost, BeamSupplies, Wait }

/// <summary>One line of a build order. Target = the building / unit / tech id it is about.</summary>
public sealed class OrderStep
{
    public int Number { get; init; }
    public StepKind Kind { get; init; }
    public string Target { get; init; } = "";

    /// <summary>How many times (builds, units).</summary>
    public int Count { get; init; } = 1;

    /// <summary>Which base a building goes to: "main" (the first), "newest", "any", or a base number (1 = first).</summary>
    public string Base { get; init; } = "main";

    /// <summary>Mines: the ore to prefer ("" = any); another ore is used when none of it is in range.</summary>
    public string Deposit { get; init; } = "";

    /// <summary>Builds: place even when the base's build queues are all busy (pays up front and waits in line).</summary>
    public bool Queue { get; init; }

    /// <summary>Wait: seconds; other steps: give up after this many seconds of waiting (0 = never).</summary>
    public int Seconds { get; init; }

    public Condition? When { get; init; }

    /// <summary>Ids that must exist first: a finished building, a known tech, a built unit, or a milestone key.</summary>
    public IReadOnlyList<string> After { get; init; } = Array.Empty<string>();

    public string Label { get; init; } = "";

    public override string ToString() => Label;

    internal static OrderStep Parse(JsonElement e, int number)
    {
        (StepKind kind, string target) = KindOf(e);
        var count = BuildOrder.Int(e, "count") ?? 1;
        var when = BuildOrder.Str(e, "when") is { } w ? Condition.Parse(w) : null;
        var after = BuildOrder.Strings(e, "after") ?? Array.Empty<string>();
        var baseText = e.TryGetProperty("base", out var b) ? (b.ValueKind == JsonValueKind.Number ? b.GetInt32().ToString(CultureInfo.InvariantCulture) : b.GetString()!) : "main";
        var label = kind switch
        {
            StepKind.Build => $"build {target}" + (count > 1 ? $" ×{count}" : ""),
            StepKind.Upgrade => $"upgrade {target}",
            StepKind.Unit => $"order {target}" + (count > 1 ? $" ×{count}" : ""),
            StepKind.Research => $"research {target}",
            StepKind.FoundOutpost => "found an outpost",
            StepKind.DropOutpost => "drop an outpost",
            StepKind.BeamSupplies => "beam supplies down",
            _ => $"wait {BuildOrder.Int(e, "wait") ?? 0} s",
        };
        if (when != null) label += $" when {when}";
        if (after.Count > 0) label += $" after {string.Join(", ", after)}";
        return new OrderStep
        {
            Number = number,
            Kind = kind,
            Target = target,
            Count = System.Math.Max(1, count),
            Base = baseText,
            Deposit = BuildOrder.Str(e, "deposit") ?? "",
            Queue = e.TryGetProperty("queue", out var q) && q.ValueKind == JsonValueKind.True,
            Seconds = kind == StepKind.Wait ? BuildOrder.Int(e, "wait") ?? 0 : BuildOrder.Int(e, "timeout") ?? 0,
            When = when,
            After = after,
            Label = label,
        };
    }

    private static (StepKind, string) KindOf(JsonElement e)
    {
        if (BuildOrder.Str(e, "build") is { } b) return (StepKind.Build, b);
        if (BuildOrder.Str(e, "upgrade") is { } u) return (StepKind.Upgrade, u);
        if (BuildOrder.Str(e, "unit") is { } n) return (StepKind.Unit, n);
        if (BuildOrder.Str(e, "research") is { } r) return (StepKind.Research, r);
        if (e.TryGetProperty("found_outpost", out _)) return (StepKind.FoundOutpost, "construction_rover");
        if (e.TryGetProperty("drop_outpost", out _)) return (StepKind.DropOutpost, "");
        if (e.TryGetProperty("beam_supplies", out _)) return (StepKind.BeamSupplies, "supplies");
        if (e.TryGetProperty("wait", out _)) return (StepKind.Wait, "");
        throw new FormatException($"step {e}: needs one of build, upgrade, unit, research, found_outpost, drop_outpost, beam_supplies, wait");
    }
}

/// <summary>
/// "duranium >= 200 and minute >= 5": terms joined by "and"; each is a name, an operator (&gt;= &lt;= &gt; &lt; ==) and a
/// number. Names: a resource id (stock), "minute", "storage_free", "storage_pct", "power_free" (the main base),
/// "income:&lt;resource&gt;" (per minute), "count:&lt;building or unit id&gt;" (finished buildings / units).
/// </summary>
public sealed class Condition
{
    public IReadOnlyList<(string Name, string Op, double Value)> Terms { get; }
    private readonly string _text;

    private Condition(List<(string, string, double)> terms, string text)
    {
        Terms = terms;
        _text = text;
    }

    public static Condition Parse(string text)
    {
        var terms = new List<(string, string, double)>();
        foreach (var part in text.Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length != 3 || bits[1] is not (">=" or "<=" or ">" or "<" or "=="))
                throw new FormatException($"condition '{part}': write it as <name> <op> <number>, e.g. duranium >= 200");
            terms.Add((bits[0], bits[1], double.Parse(bits[2], CultureInfo.InvariantCulture)));
        }
        return new Condition(terms, text);
    }

    public bool Holds(Func<string, double?> value)
    {
        foreach (var (name, op, v) in Terms)
        {
            if (value(name) is not { } x) throw new FormatException($"condition: unknown name '{name}'");
            var ok = op switch { ">=" => x >= v, "<=" => x <= v, ">" => x > v, "<" => x < v, _ => System.Math.Abs(x - v) < 1e-9 };
            if (!ok) return false;
        }
        return true;
    }

    public override string ToString() => _text;
}
