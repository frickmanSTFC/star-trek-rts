using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

public sealed record FactionDef(string Id, string Name, bool Playable);

/// <summary>Hull stats from ships.json. Speed is in map units per second (placeholder scale).</summary>
public sealed record HullDef(string Id, int Size, int Hp, Fix64 Speed, int Crew);

/// <summary>A faction's ship class: a named template on a hull. Key is "faction.Name".</summary>
public sealed record ShipClassDef(string Faction, string Name, string HullId, string Era, string Source)
{
    public string Key => $"{Faction}.{Name}";
}

/// <summary>
/// Read-only game data loaded from the design repo's data/*.json files (the same files the
/// design pages use). Lists keep file order, so iteration is deterministic.
/// Numbers are parsed from their raw text into Fix64, never through double.
/// </summary>
public sealed class GameData
{
    public IReadOnlyList<FactionDef> Factions { get; }
    public IReadOnlyList<HullDef> Hulls { get; }
    public IReadOnlyList<ShipClassDef> ShipClasses { get; }

    private readonly Dictionary<string, FactionDef> _factions;
    private readonly Dictionary<string, HullDef> _hulls;
    private readonly Dictionary<string, ShipClassDef> _classes;

    private GameData(List<FactionDef> factions, List<HullDef> hulls, List<ShipClassDef> classes)
    {
        Factions = factions;
        Hulls = hulls;
        ShipClasses = classes;
        _factions = factions.ToDictionary(f => f.Id, StringComparer.Ordinal);
        _hulls = hulls.ToDictionary(h => h.Id, StringComparer.Ordinal);
        _classes = new Dictionary<string, ShipClassDef>(StringComparer.Ordinal);
        foreach (var c in classes) _classes.TryAdd(c.Key, c);
    }

    public FactionDef Faction(string id) =>
        _factions.TryGetValue(id, out var f) ? f : throw new KeyNotFoundException($"Unknown faction '{id}'.");

    public HullDef Hull(string id) =>
        _hulls.TryGetValue(id, out var h) ? h : throw new KeyNotFoundException($"Unknown hull '{id}'.");

    public ShipClassDef ShipClass(string key) =>
        _classes.TryGetValue(key, out var c) ? c : throw new KeyNotFoundException($"Unknown ship class '{key}'.");

    public bool HasShipClass(string key) => _classes.ContainsKey(key);

    public static GameData Load(string dataDirectory)
    {
        using var factionsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "factions.json")));
        using var shipsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "ships.json")));

        var factions = factionsDoc.RootElement.GetProperty("factions").EnumerateArray()
            .Select(f => new FactionDef(
                f.GetProperty("id").GetString()!,
                f.GetProperty("name").GetString()!,
                f.GetProperty("playable").ValueKind == JsonValueKind.True))
            .ToList();

        var hulls = shipsDoc.RootElement.GetProperty("hulls").EnumerateArray()
            .Select(h => new HullDef(
                h.GetProperty("id").GetString()!,
                h.GetProperty("size").GetInt32(),
                h.GetProperty("hp").GetInt32(),
                Fix64.Parse(h.GetProperty("speed").GetRawText()),
                h.GetProperty("crew").GetInt32()))
            .ToList();

        var classes = shipsDoc.RootElement.GetProperty("classes").EnumerateArray()
            .Select(c => new ShipClassDef(
                c.GetProperty("faction").GetString()!,
                c.GetProperty("name").GetString()!,
                c.GetProperty("hull").GetString()!,
                c.GetProperty("era").GetString()!,
                c.GetProperty("source").GetString()!))
            .ToList();

        return new GameData(factions, hulls, classes);
    }

    /// <summary>Problems the sim can't work around. Should come back empty; fix the data, not the check.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        foreach (var c in ShipClasses)
        {
            if (!_hulls.ContainsKey(c.HullId)) problems.Add($"Ship class {c.Key}: unknown hull '{c.HullId}'.");
            if (!_factions.ContainsKey(c.Faction)) problems.Add($"Ship class {c.Key}: unknown faction '{c.Faction}'.");
        }
        foreach (var dup in ShipClasses.GroupBy(c => c.Key).Where(g => g.Count() > 1))
            problems.Add($"Ship class {dup.Key} is listed {dup.Count()} times.");
        return problems;
    }

    /// <summary>Walks up from <paramref name="startDirectory"/> to find the design repo's data/ folder.</summary>
    public static string FindDataDirectory(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "ships.json"))) return candidate;
        }
        throw new DirectoryNotFoundException($"No data/ships.json found above '{startDirectory}'.");
    }
}
