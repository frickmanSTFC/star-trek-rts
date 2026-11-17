using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

// "Fit Out Your Ship", game side (docs/fit-out-contract.md). CONTRACT STUB: the names and signatures are fixed; the SIM agent
// fills the bodies. No fit-out (null) must always mean today's game exactly.

/// <summary>A player's fit-out: the focus id, the crew per division (heads, in the data's division order) and the cargo
/// (line id → count). Set once by MatchSetup.CreateSystemGame(..., shipId, fitOut).</summary>
public sealed record FitOut(string Focus, IReadOnlyList<int> Crew, IReadOnlyDictionary<string, int> Cargo);

/// <summary>Pure rules shared by the sim and the menu page, so the page's summary and the game agree.</summary>
public static class FitOutRules
{
    /// <summary>Whether this ship is fitted out at all (the data lists it).</summary>
    public static bool Applies(GameData data, string? shipId) => data.FitOut?.Ship(shipId) != null;

    /// <summary>The focus's preset for this ship: a legal fit-out, or null when the ship is not fitted out.</summary>
    public static FitOut? Preset(GameData data, string shipId, string focusId) => null;

    /// <summary>Why this fit-out is not allowed ("Science needs at least 12 crew"), or null.</summary>
    public static string? Problem(GameData data, string shipId, FitOut fitOut) => null;

    /// <summary>The least crew this division may hold under this focus.</summary>
    public static int CrewFloor(GameData data, string shipId, string focusId, string divisionId) => 0;

    /// <summary>Containers the cargo takes.</summary>
    public static int ContainersUsed(GameData data, FitOut fitOut) => 0;

    /// <summary>A division's strength: EffectPerPoint × (its share in percent − 25). 0 with an even split.</summary>
    public static Fix64 Strength(GameData data, string shipId, FitOut fitOut, string divisionId) => Fix64.Zero;

    /// <summary>The multiplier for a lever (crew strength, the focus's perk and cargo that acts on it all included).
    /// 1 with no fit-out or an unknown lever.</summary>
    public static Fix64 Factor(GameData data, string? shipId, FitOut? fitOut, string leverId) => Fix64.One;

    /// <summary>The whole landing stock this fit-out gives (the base start stock, the ship's own bonus and the cargo), for the
    /// page's summary: resource id → amount.</summary>
    public static IReadOnlyDictionary<string, int> LandingStock(GameData data, string shipId, FitOut fitOut) => new Dictionary<string, int>();

    /// <summary>Every vehicle that lands with the colony under this fit-out (unit ids, the usual landing party included).</summary>
    public static IReadOnlyList<string> LandingUnits(GameData data, string shipId, FitOut fitOut) => Array.Empty<string>();
}

public sealed partial class Simulation
{
    /// <summary>This player's fit-out, or null (none: the game plays as it always did).</summary>
    public FitOut? FitOutOf(int player) => null;

    /// <summary>The multiplier this player's fit-out puts on a lever (1 with none).</summary>
    public Fix64 FitOutFactor(int player, string leverId) => Fix64.One;
}
