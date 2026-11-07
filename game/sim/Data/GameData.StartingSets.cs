using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FinalFrontier.Sim.Data;

// buildings.json starting_sets (each faction's Class M opening buildings, design data the sim doesn't play): an entry
// with "building_ref" is a copy of that building, so its cost must stay the building's cost.
public sealed partial class GameData
{
    private sealed record StartingSetRef(string Faction, string Id, string BuildingRef, IReadOnlyList<ResourceAmount> Cost);

    private IReadOnlyList<StartingSetRef> StartingSetRefs { get; init; } = Array.Empty<StartingSetRef>();

    private static List<StartingSetRef> ReadStartingSetRefs(JsonElement buildingsRoot, Func<JsonElement, string, string, List<ResourceAmount>> amounts)
    {
        var list = new List<StartingSetRef>();
        if (!buildingsRoot.TryGetProperty("starting_sets", out var sets) || !sets.TryGetProperty("factions", out var factions)) return list;
        foreach (var f in factions.EnumerateObject())
            foreach (var b in f.Value.GetProperty("buildings").EnumerateArray())
                if (b.TryGetProperty("building_ref", out var r) && r.GetString() is { Length: > 0 } id)
                    list.Add(new StartingSetRef(f.Name, b.TryGetProperty("id", out var bid) ? bid.GetString() ?? "" : "", id,
                        amounts(b, "cost", $"Starting set {f.Name}")));
        return list;
    }

    private void ValidateStartingSets(List<string> problems)
    {
        static string Text(IEnumerable<ResourceAmount> cost, GameData d) => string.Join(" + ", cost.Select(a => $"{a.Amount} {d.Resources[a.Resource].Id}"));
        foreach (var s in StartingSetRefs)
        {
            var where = $"Starting set {s.Faction} {s.Id}";
            if (!_buildings.TryGetValue(s.BuildingRef, out var b)) problems.Add($"{where}: building_ref '{s.BuildingRef}' is not a building.");
            else if (!s.Cost.SequenceEqual(b.Cost))
                problems.Add($"{where}: costs {Text(s.Cost, this)} but its building_ref {b.Id} costs {Text(b.Cost, this)}; a building_ref entry is a copy, so keep them equal (or drop building_ref for a different building).");
        }
    }
}
