using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// Founding outposts on the ground (surfaces.json units' "found": the construction rover): read and checked here.
public sealed partial class GameData
{
    private static FoundRules? ReadFound(JsonElement unit, Func<JsonElement, string, string, List<ResourceAmount>> amounts)
    {
        if (!unit.TryGetProperty("found", out var f)) return null;
        return new FoundRules(Str(f, "building"), amounts(f, "cost", $"Surface unit {Str(unit, "id")} found"),
            f.TryGetProperty("min_distance", out var md) ? md.GetInt32() : 0);
    }

    private void ValidateFounding(List<string> problems)
    {
        foreach (var u in SurfaceUnits)
        {
            if (u.Found is not { } f) continue;
            var where = $"Surface unit {u.Id} found";
            if (!_buildings.TryGetValue(f.Building, out var b)) problems.Add($"{where}: unknown building '{f.Building}'.");
            else
            {
                if (!b.IsCore || b.BuildQueues < 1) problems.Add($"{where}: '{f.Building}' must be a colony core (build_radius and build_queues).");
                if (b.BuildSeconds < 1) problems.Add($"{where}: '{f.Building}' needs build_seconds (how long the exocomps take to build it).");
                if (b.Storage <= Fix64.Zero) problems.Add($"{where}: '{f.Building}' has no storage, so the new base couldn't take mining loads.");
            }
            if (f.MinDistance < 1) problems.Add($"{where}: min_distance must be at least 1.");
            if (f.Cost.Count == 0 || f.Cost.Any(a => a.Amount <= Fix64.Zero)) problems.Add($"{where}: needs a cost with positive amounts.");
            if (u.DeploySeconds < 1) problems.Add($"Surface unit {u.Id}: a founder needs deploy_seconds (the time it takes to unfold on the site).");
            if (u.CanMine) problems.Add($"Surface unit {u.Id}: can't both mine and found outposts.");
            if (!BuildMenu.Any(id => _buildings.TryGetValue(id, out var bay) && bay.Produces.Contains(u.Id)))
                problems.Add($"Surface unit {u.Id}: founds outposts, but no building in the build menu produces it.");
        }
    }
}
