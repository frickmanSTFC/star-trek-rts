using System.Collections.Generic;
using FinalFrontier.Sim.Data;

namespace FinalFrontier.Sim.Core;

// CONTRACT (2026-11-08, achievements; docs/achievements-contract.md). Shapes are fixed so the UI and cosmetics agents
// can build against them; the sim agent fills the bodies (tracker, events, data loading, cosmetics in the naming).

/// <summary>One unlock in this game: a player earned an achievement at a tick. In the state hash.</summary>
public sealed record AchievementUnlock(int Player, string Id, long Tick);

/// <summary>How far a player is toward an achievement with a counter, share or streak (for the screen's bar).</summary>
/// <param name="Unit">"hexes", "%", "s", "satellites", … (from the trigger; the view formats it).</param>
public sealed record AchievementProgress(long Current, long Target, string Unit);

/// <summary>A player's picked cosmetics that live in the sim (names and numbers are game state). Set before the game
/// starts with <see cref="Simulation.SetCosmetics"/>; hashed.</summary>
/// <param name="RoverNames">Extra names handed out first, in this order, before the shuffled pool.</param>
/// <param name="HullNumbers">Extra hull numbers handed out first, in this order (1 → "01").</param>
public sealed record PlayerCosmetics(IReadOnlyList<string> RoverNames, IReadOnlyList<int> HullNumbers)
{
    public static readonly PlayerCosmetics None = new(new List<string>(), new List<int>());
}

/// <summary>
/// Checks the achievement rules (GameData.Achievements) against sim state about once a second of game time and records
/// unlocks. Records nothing while <see cref="Gate"/> is non-null. View-only rules (trigger kind view_count) are never
/// checked here.
/// </summary>
public sealed class AchievementTracker
{
    private readonly List<AchievementUnlock> _unlocks = new();

    /// <summary>Every unlock of this game, oldest first (each player × id at most once).</summary>
    public IReadOnlyList<AchievementUnlock> Unlocks => _unlocks;

    /// <summary>Whether the player has earned it in this game.</summary>
    public bool Earned(int player, string id)
    {
        foreach (var u in _unlocks)
            if (u.Player == player && u.Id == id) return true;
        return false;
    }

    /// <summary>Progress toward an achievement with a progress bar, or null (no data yet / not that kind).</summary>
    public AchievementProgress? Progress(int player, string id) => null; // STUB

    /// <summary>Why nothing unlocks now ("test mode", "draft data", "turned off"), or null when unlocks count.</summary>
    public string? Gate { get; internal set; } = "not built yet"; // STUB
}

public sealed partial class Simulation
{
    /// <summary>The achievement tracker of this game (never null).</summary>
    public AchievementTracker Achievements { get; } = new();

    /// <summary>False: the tracker records nothing (balance runner, draft data loaded by the game). Default true.</summary>
    public bool AchievementsEnabled { get; set; } = true;

    /// <summary>Sets a player's sim-side cosmetics (rover names, hull numbers); call before the first Step. STUB.</summary>
    public void SetCosmetics(int player, PlayerCosmetics cosmetics) { }
}
