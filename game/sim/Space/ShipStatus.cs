using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// CONTRACT STUB (2026-11-25, docs/space-jobs-contract.md): hull, systems, crew by division and the hold of a player's
// flagship. Stubs: no status (null).

/// <summary>Ship systems a hazard can knock out.</summary>
public enum ShipSystem { Sensors, Engines, Tractor, LifeSupport }

/// <summary>Hull bands (ship_status.json): Scuffed 75-100, Worn 50-75, Damaged 25-50, Critical 1-25, Crippled 0.</summary>
public enum HullBand { Scuffed, Worn, Damaged, Critical, Crippled }

/// <summary>A knocked-out system: which, and when it comes back on its own (sim tick).</summary>
public sealed record SystemDown(ShipSystem System, long BackTick);

/// <summary>Crew of one division: On duty, Injured (back later), Lost (gone for the match), the floor it shouldn't drop below.</summary>
public sealed record CrewLine(string Division, int OnDuty, int Injured, int Lost, int Floor);

/// <summary>One kind of thing in the hold: Item id ("patch_kit", "probe", "duranium_ore", "salvage:power_cell",
/// "survivors"…), Count, Containers it takes, Label.</summary>
public sealed record HoldLine(string Item, int Count, int Containers, string Label);

/// <summary>A line of the ship's damage / crew log (newest first in the view): Tick, Text, Tone "" | "warn" | "bad" | "good".</summary>
public sealed record ShipLogLine(long Tick, string Text, string Tone);

/// <summary>A flagship's state for the status screen.</summary>
public sealed record ShipStatusView(int ShipId, Fix64 Hull, HullBand Band, string BandEffect, IReadOnlyList<SystemDown> Down,
    IReadOnlyList<CrewLine> Crew, int CrewTotal, int CrewStart, IReadOnlyList<HoldLine> Hold, int HoldUsed, int HoldSize,
    bool Crippled, int FieldRepairTo, IReadOnlyList<ShipLogLine> Log);

public sealed partial class StarSystemState
{
    /// <summary>The status of a player's flagship, or null (no system / not built yet).</summary>
    public ShipStatusView? StatusOf(int player) => null;

    /// <summary>Why a ShipRepairCommand action can't run now, or null.</summary>
    public string? RepairProblem(int player, string action) => "not built yet";

    /// <summary>Why a ShipHoldCommand can't run now, or null.</summary>
    public string? HoldProblem(int player, string action, string item, int count) => "not built yet";
}
