using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// "Fit Out Your Ship" (docs/fit-out-contract.md; design page docs/mockups/loadout): before leaving home the player picks a
// focus, splits the ship's crew over four divisions and loads the hold. Read from data/missions/fit_out.json (the missions
// document's "fit_out"); null when the data set has none. CONTRACT STUB: the shapes below are fixed; the SIM agent adds the
// reader and the checks.

/// <summary>A crew division: Id "exploration" | "engineering" | "science" | "defense"; Name; Who ("Pilots, surveyors, …").</summary>
public sealed record FitOutDivision(string Id, string Name, string Who);

/// <summary>A focus: Id "explorer"; Name; Division it leads with (null for the all-rounder); Line (one short sentence);
/// CrewPct (preset shares in the data's division order, summing to 100); PerkText (shown to the player; empty = no perk).</summary>
public sealed record FitOutFocus(string Id, string Name, string? Division, string Line, IReadOnlyList<int> CrewPct, string PerkText);

/// <summary>A number the crew moves: Id "drive_speed"; Division; MoreIsBetter (× (1 + strength)) or not (× (1 − strength));
/// Name for the summary ("Vehicle drive speed").</summary>
public sealed record FitOutLever(string Id, string Division, bool MoreIsBetter, string Name);

/// <summary>A cargo group: Id (a division id or "ship"); Name ("Engineering stores"); Floor (containers it must hold).</summary>
public sealed record FitOutCargoGroup(string Id, string Name, int Floor);

/// <summary>A cargo line: Id "mat"; Name; Group; Containers each; Min / Max count; Text (what one gives, shown on hover).</summary>
public sealed record FitOutCargoLine(string Id, string Name, string Group, int Containers, int Min, int Max, string Text);

/// <summary>A ship's budget: ShipId; Crew (heads to assign); Hold (containers); Suggest (the focus it starts on).</summary>
public sealed record FitOutShip(string ShipId, int Crew, int Hold, string Suggest);

/// <summary>The whole rule set.</summary>
public sealed class FitOutData
{
    /// <summary>Each division's least share of the crew (0.10).</summary>
    public Fix64 CrewFloorShare { get; init; }
    /// <summary>The focus's own division's least share (0.30).</summary>
    public Fix64 FocusFloorShare { get; init; }
    /// <summary>Strength per percentage point of share above or below an even quarter (0.004).</summary>
    public Fix64 EffectPerPoint { get; init; }
    public IReadOnlyList<FitOutDivision> Divisions { get; init; } = Array.Empty<FitOutDivision>();
    public IReadOnlyList<FitOutFocus> Focuses { get; init; } = Array.Empty<FitOutFocus>();
    public IReadOnlyList<FitOutLever> Levers { get; init; } = Array.Empty<FitOutLever>();
    public IReadOnlyList<FitOutCargoGroup> Groups { get; init; } = Array.Empty<FitOutCargoGroup>();
    public IReadOnlyList<FitOutCargoLine> Cargo { get; init; } = Array.Empty<FitOutCargoLine>();
    public IReadOnlyList<FitOutShip> Ships { get; init; } = Array.Empty<FitOutShip>();

    public FitOutShip? Ship(string? shipId) => shipId == null ? null : Ships.FirstOrDefault(s => s.ShipId == shipId);
    public FitOutFocus? Focus(string? id) => id == null ? null : Focuses.FirstOrDefault(f => f.Id == id);
}

public sealed partial class GameData
{
    /// <summary>The fit-out rules, or null when the data set has none (then no ship is fitted out and the menu skips the page).</summary>
    public FitOutData? FitOut { get; private init; }
}
