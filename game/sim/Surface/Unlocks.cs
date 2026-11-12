using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;

namespace FinalFrontier.Sim.Surface;

// Building unlocks (data side: Data/GameData.Unlocks.cs). Each player has a set of buildings they may place
// (SurfacePlayer.UnlockedBuildings, hashed once it exists): null = everything open (missions off). Turning missions on
// (MissionBook.TurnOn) sets it to surfaces.json start_menu plus whatever the player has built already; mission
// unlock_buildings actions and the test panel's DebugUnlockCommand add to it. The view only reads it. The domes kit
// (Life Support Plant, Pressure Dome on a Domes world) is always open: the colony can't live without it.
public sealed partial class PlanetSurface
{
    /// <summary>Whether the player may place this building now (true for a bad player id: other checks refuse that).</summary>
    public bool BuildingUnlocked(int player, string buildingId)
    {
        if (player < 0 || player >= _players.Count || _players[player]._unlocked is not { } open) return true;
        if (open.Contains(buildingId)) return true;
        return Domes is { } d && d.BuildMenu.Contains(buildingId);
    }

    /// <summary>The buildings of this surface's build menu the player may place now, in menu order.</summary>
    public IReadOnlyList<string> UnlockedMenu(int player) => BuildMenu.Where(id => BuildingUnlocked(player, id)).ToList();

    /// <summary>Why a locked building can't be placed yet: "unlocks with the mission Ground Truth" (the mission of this
    /// game's chain that opens it), else "not unlocked yet". Null when it is open.</summary>
    public string? LockedText(Simulation sim, int player, string buildingId)
    {
        if (BuildingUnlocked(player, buildingId)) return null;
        var chain = sim.Missions.ChainId;
        if (_data.UnlockMission(buildingId, chain) is { } m)
            return GameData.UnlocksOnStart(m, buildingId) ? $"unlocks when the mission {m.Title} starts" : $"unlocks with the mission {m.Title}";
        return "not unlocked yet (a mission opens it)";
    }

    /// <summary>Missions turned on: the player starts with start_menu (plus the kinds of building they already have).
    /// Does nothing when the player is already restricted.</summary>
    internal void LockToStartMenu(int player)
    {
        if (player < 0 || player >= _players.Count || _players[player]._unlocked != null) return;
        var open = new List<string>(_data.StartMenu);
        foreach (var s in _structures)
            if (s.Owner == player && BuildMenu.Contains(s.DefId) && !open.Contains(s.DefId)) open.Add(s.DefId);
        open.Sort(System.StringComparer.Ordinal);
        _players[player]._unlocked = open;
    }

    /// <summary>Opens these buildings for the player (a mission's unlock_buildings). Returns the ids that were newly
    /// opened, in the order given (ids already open, or not in the build menu, are skipped).</summary>
    internal List<string> UnlockBuildings(int player, IEnumerable<string> ids)
    {
        var added = new List<string>();
        if (player < 0 || player >= _players.Count || _players[player]._unlocked is not { } open) return added;
        foreach (var id in ids)
        {
            if (open.Contains(id) || !_data.EveryBuildMenu.Contains(id)) continue;
            open.Add(id);
            added.Add(id);
        }
        open.Sort(System.StringComparer.Ordinal);
        return added;
    }

    private string? UnlockDebugProblem(DebugUnlockCommand c)
    {
        if (!IsPlayer(c.Target)) return "no such player";
        if (c.BuildingId.Length > 0 && !_data.EveryBuildMenu.Contains(c.BuildingId)) return "not in the build menu";
        return null;
    }

    /// <summary>Test panel: "" + Unlocked = every building open; "" + locked = back to the start menu; an id opens or
    /// closes that one building (the set is made from the whole menu first when the player had no limits).</summary>
    private void ApplyUnlockDebug(DebugUnlockCommand c)
    {
        var p = _players[c.Target];
        if (c.BuildingId.Length == 0)
        {
            if (c.Unlocked)
            {
                var all = new List<string>(_data.EveryBuildMenu);
                all.Sort(System.StringComparer.Ordinal);
                p._unlocked = all;
            }
            else
            {
                p._unlocked = new List<string>(_data.StartMenu);
                p._unlocked.Sort(System.StringComparer.Ordinal);
            }
            return;
        }
        if (p._unlocked == null)
        {
            p._unlocked = new List<string>(_data.EveryBuildMenu);
            p._unlocked.Sort(System.StringComparer.Ordinal);
        }
        if (c.Unlocked) UnlockBuildings(c.Target, new[] { c.BuildingId });
        else p._unlocked.Remove(c.BuildingId);
    }
}
