using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FinalFrontier.Sim.Data;

// Building unlocks (owner 2026-11-12: "limit the number of buildings that can be built to the bare minimum" at the first
// outpost; the rest unlocks through the missions). surfaces.json start_menu = what a campaign player can build from the
// start (a part of build_menu); missions' unlock_buildings actions (on_enter or rewards) add the rest. With missions off
// (the DEBUG sandbox, tests, the balance runner) every building stays open. The sim side is Surface/Unlocks.cs.
public sealed partial class GameData
{
    /// <summary>surfaces.json start_menu: the buildings a campaign game (missions on) starts with, in menu order. The
    /// domes kit (habitability.domes.build_menu) is always open on top of it. Missing = the whole build_menu.</summary>
    public IReadOnlyList<string> StartMenu { get; private init; } = new List<string>();

    private static List<string>? ReadStartMenu(JsonElement surfacesRoot) =>
        surfacesRoot.TryGetProperty("start_menu", out var sm) && sm.ValueKind == JsonValueKind.Array
            ? sm.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : null;

    /// <summary>The ids an unlock_buildings action opens (its ids list).</summary>
    public static IReadOnlyList<string> UnlockIds(MissionActionDef a) => a.Kind == "unlock_buildings" ? a.L("ids") : System.Array.Empty<string>();

    /// <summary>The first mission of the chain (file order, main missions first) whose on_enter or rewards unlock this
    /// building, or null (a start-menu building, the domes kit, or one no mission opens).</summary>
    public MissionDef? UnlockMission(string buildingId, string chainId, bool availableOnly = true)
    {
        if (Missions is not { } data) return null;
        foreach (var main in new[] { true, false })
            foreach (var m in data.List)
            {
                if (m.Chain != chainId || m.IsMain != main || (availableOnly && !MissionAvailable(m))) continue;
                if (m.Rewards.Concat(m.Stages.SelectMany(s => s.OnEnter)).Any(a => UnlockIds(a).Contains(buildingId))) return m;
            }
        return null;
    }

    /// <summary>Whether the mission unlocks the building when it starts (on_enter of its first stage) rather than as a reward.</summary>
    public static bool UnlocksOnStart(MissionDef m, string buildingId) =>
        m.Stages.Count > 0 && m.Stages[0].OnEnter.Any(a => UnlockIds(a).Contains(buildingId));

    private void ValidateUnlocks(List<string> problems)
    {
        const string w = "Start menu";
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var id in StartMenu)
        {
            if (!seen.Add(id)) problems.Add($"{w}: {id} is listed twice.");
            if (!BuildMenu.Contains(id)) problems.Add($"{w}: {id} is not in build_menu (start_menu must be a part of it).");
        }
        if (StartMenu.Count == 0) problems.Add($"{w}: empty (a campaign player could build nothing).");
        // Every building a campaign starts without must be opened by some mission of every chain (counting missions a
        // data set leaves out for missing needs: those games simply never open it).
        if (Missions is not { } data) return;
        var domes = Habitability?.Domes?.BuildMenu ?? new List<string>();
        foreach (var chain in data.Chains)
            foreach (var id in BuildMenu)
                if (!StartMenu.Contains(id) && !domes.Contains(id) && UnlockMission(id, chain.Id, availableOnly: false) == null)
                    problems.Add($"{w}: {id} is not in start_menu and no mission of chain {chain.Id} unlocks it (unlock_buildings).");
    }
}
