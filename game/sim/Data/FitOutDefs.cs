using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// "Fit Out Your Ship" (docs/fit-out-contract.md; design page docs/mockups/loadout): before leaving home the player picks a
// focus, splits the ship's crew over four divisions and loads the hold. Read from data/missions/fit_out.json (the missions
// document's "fit_out") by GameData.FitOut.cs; null when the data set has none. The rules that use these are in
// Core/FitOut.cs (FitOutRules), shared by the sim and the menu page.

/// <summary>A crew division: Id "exploration" | "engineering" | "science" | "defense"; Name; Who ("Pilots, surveyors, …").</summary>
public sealed record FitOutDivision(string Id, string Name, string Who);

/// <summary>What a perk or a cargo line does to a lever. On a focus (PerkLevers) Value is a multiplier (1.3 = 30% more);
/// on a cargo line (Levers) it is added to the multiplier per item loaded (-0.08 × 3 items = × 0.76).</summary>
public readonly record struct FitOutLeverEffect(string Lever, Fix64 Value);

/// <summary>A resource amount one cargo item puts into the landing stock.</summary>
public readonly record struct FitOutStock(string Resource, int Amount);

/// <summary>A count of one cargo line (a ship's base load).</summary>
public readonly record struct FitOutLoad(string Line, int Count);

/// <summary>A focus: Id "explorer"; Name; Division it leads with (null for the all-rounder); Line (one short sentence);
/// CrewPct (preset shares in the data's division order, summing to 100); PerkText (shown to the player; empty = no perk).
/// The perk itself: PerkLevers (multipliers on levers) and LandingReveal (hexes revealed round the hub at the landing; 0 =
/// the landing's own radius).</summary>
public sealed record FitOutFocus(string Id, string Name, string? Division, string Line, IReadOnlyList<int> CrewPct, string PerkText)
{
    public IReadOnlyList<FitOutLeverEffect> PerkLevers { get; init; } = Array.Empty<FitOutLeverEffect>();
    public int LandingReveal { get; init; }
}

/// <summary>A number the fit-out moves: Id "drive_speed"; Division whose strength moves it ("" = none: only perks and cargo
/// do); MoreIsBetter (× (1 + strength)) or not (× (1 − strength)); Name for the summary ("Vehicle drive speed").</summary>
public sealed record FitOutLever(string Id, string Division, bool MoreIsBetter, string Name);

/// <summary>A cargo group: Id (a division id or "ship"); Name ("Engineering stores"); Floor (containers it must hold).</summary>
public sealed record FitOutCargoGroup(string Id, string Name, int Floor);

/// <summary>A cargo line: Id "mat"; Name; Group; Containers each; Min / Max count; Text (what one gives, shown on hover).
/// What one item does: Stock (into the landing stock), Unit (a surface unit that lands with the colony; "" = none),
/// Satellites (survey satellites that launch without paying), Research (points banked toward the first tech), Levers
/// (added to those levers' multipliers).</summary>
public sealed record FitOutCargoLine(string Id, string Name, string Group, int Containers, int Min, int Max, string Text)
{
    public IReadOnlyList<FitOutStock> Stock { get; init; } = Array.Empty<FitOutStock>();
    public string Unit { get; init; } = "";
    public int Satellites { get; init; }
    public int Research { get; init; }
    public IReadOnlyList<FitOutLeverEffect> Levers { get; init; } = Array.Empty<FitOutLeverEffect>();
}

/// <summary>A ship's budget: ShipId; Crew (heads to assign); Hold (containers); Suggest (the focus it starts on). Base = the
/// cargo every preset starts from; Fill = per focus id, the lines a preset adds one at a time, round and round, while the
/// line is under its max and the hold has room.</summary>
public sealed record FitOutShip(string ShipId, int Crew, int Hold, string Suggest)
{
    public IReadOnlyList<FitOutLoad> Base { get; init; } = Array.Empty<FitOutLoad>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Fill { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
}

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
    /// <summary>The landing start-stock resources a fit-out's cargo replaces (duranium, tritanium, deuterium).</summary>
    public IReadOnlyList<string> ReplacesStartStock { get; init; } = Array.Empty<string>();

    public FitOutShip? Ship(string? shipId) => shipId == null ? null : Ships.FirstOrDefault(s => s.ShipId == shipId);
    public FitOutFocus? Focus(string? id) => id == null ? null : Focuses.FirstOrDefault(f => f.Id == id);
    public FitOutLever? Lever(string? id) => id == null ? null : Levers.FirstOrDefault(l => l.Id == id);
    public FitOutCargoLine? Line(string? id) => id == null ? null : Cargo.FirstOrDefault(l => l.Id == id);
    public FitOutCargoGroup? Group(string? id) => id == null ? null : Groups.FirstOrDefault(g => g.Id == id);

    /// <summary>A division's place in <see cref="Divisions"/> (and in a fit-out's crew list), or -1.</summary>
    public int DivisionIndex(string? id)
    {
        for (var i = 0; i < Divisions.Count; i++)
            if (Divisions[i].Id == id) return i;
        return -1;
    }

    /// <summary>An even split's share in percent (25 with four divisions): the share at which a division's strength is 0.</summary>
    public Fix64 EvenSharePct => Divisions.Count == 0 ? Fix64.Zero : Fix64.FromInt(100) / Fix64.FromInt(Divisions.Count);
}

public sealed partial class GameData
{
    /// <summary>The fit-out rules, or null when the data set has none (then no ship is fitted out and the menu skips the page).</summary>
    public FitOutData? FitOut { get; private init; }
}
