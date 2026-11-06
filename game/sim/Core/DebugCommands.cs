using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

// The test panel's edits (dev builds only, F9). They are ordinary commands: scheduled, validated when they run
// (Simulation.DebugProblem says why one is refused), kept in replays, and counted in Simulation.DebugEdits, which is in
// the state hash, so a tested game never passes for an untouched one.

/// <summary>Every test-panel command derives from this.</summary>
public abstract record DebugCommand(int Player) : Command(Player);

public enum DebugUnitField
{
    /// <summary>0..100.</summary>
    Integrity,

    /// <summary>Value 1 = broken down (a crawler folds up; a rover is disabled), 0 = fixed.</summary>
    Broken,

    /// <summary>Rovers: which tyres are flat, one bit per wheel (see Surface.Wheels), 0 = none.</summary>
    FlatTyres,

    /// <summary>Rovers: spare tyres on board, 0..20.</summary>
    Spares,
}

/// <summary>Set one value on a vehicle (any owner's).</summary>
public sealed record DebugUnitCommand(int Player, int UnitId, DebugUnitField Field, Fix64 Value) : DebugCommand(Player);

/// <summary>Put a vehicle straight onto a passable tile (it stops, a crawler folds up).</summary>
public sealed record DebugMoveUnitCommand(int Player, int UnitId, int Tile) : DebugCommand(Player);

/// <summary>Take a vehicle off the map.</summary>
public sealed record DebugRemoveUnitCommand(int Player, int UnitId) : DebugCommand(Player);

/// <summary>A new vehicle for any player, on the tile or the nearest free spot beside it (clone and "place unit at cursor").</summary>
public sealed record DebugSpawnUnitCommand(int Player, int Owner, string UnitId, int Tile) : DebugCommand(Player);

public enum DebugStructureField
{
    /// <summary>0..100 (buildings that wear).</summary>
    Condition,

    /// <summary>1 = broken down, 0 = fixed.</summary>
    Broken,

    /// <summary>1 = it stops wearing, 0 = wears as normal.</summary>
    NoWear,

    /// <summary>1 (Mk I) .. its top level (buildings with levels).</summary>
    Level,

    /// <summary>Work speed multiplier, above 0 up to 100 (1 = normal).</summary>
    WorkSpeed,

    /// <summary>Share of its construction done, 0..1; 1 finishes it now.</summary>
    BuildProgress,

    /// <summary>Share of its upgrade done, 0..1; 1 finishes it now.</summary>
    UpgradeProgress,

    /// <summary>Share of the Vehicle Bay's current order done, 0..1; 1 rolls it out now.</summary>
    ProductionProgress,
}

/// <summary>Set one value on a building (any owner's).</summary>
public sealed record DebugStructureCommand(int Player, int StructureId, DebugStructureField Field, Fix64 Value) : DebugCommand(Player);

/// <summary>Take a building away, no refund. A colony core only once the rest of its base is gone.</summary>
public sealed record DebugRemoveStructureCommand(int Player, int StructureId) : DebugCommand(Player);

/// <summary>A finished building for any player, free: a colony core founds a new base, anything else must be in that player's build range.</summary>
public sealed record DebugPlaceBuildingCommand(int Player, int Owner, string BuildingId, int Tile, int Facing) : DebugCommand(Player);

public enum DebugBaseAction
{
    /// <summary>Every unfinished building and upgrade at the base completes now.</summary>
    FinishConstruction,

    /// <summary>Every building that wears goes back to 100 and is fixed.</summary>
    RepairAll,

    /// <summary>Every building that wears is set to Value condition (e.g. 30).</summary>
    SetCondition,

    /// <summary>Every order queued at the base's Vehicle Bays rolls out now.</summary>
    FinishOrders,
}

/// <summary>An action on every building at one base (BaseId = its colony core).</summary>
public sealed record DebugBaseCommand(int Player, int BaseId, DebugBaseAction Action, Fix64 Value) : DebugCommand(Player);

/// <summary>Set a player's stock of the listed resources to exactly these amounts (0 .. 1 000 000 000).</summary>
public sealed record DebugStockCommand(int Player, int Target, IReadOnlyList<ResourceAmount> Values) : DebugCommand(Player);

public enum DebugFlag
{
    /// <summary>Storage never fills: mines, crawlers and outputs ignore the capacity.</summary>
    NoStorageCap,

    /// <summary>Every base's power supply covers its use.</summary>
    UnlimitedPower,

    /// <summary>Placing buildings costs nothing (and cancelling or salvaging one placed free gives nothing back).</summary>
    FreeBuilding,

    /// <summary>Construction and upgrades finish the tick they start.</summary>
    InstantBuild,
}

/// <summary>Turn a test-panel limit on or off for a player.</summary>
public sealed record DebugFlagCommand(int Player, int Target, DebugFlag Flag, bool On) : DebugCommand(Player);

/// <summary>Set a surface tech's state for a player: known or not, and the research points put into it (0..cost).</summary>
public sealed record DebugTechCommand(int Player, int Target, string TechId, bool Known, Fix64 Progress) : DebugCommand(Player);

/// <summary>The player knows every tile's deposit (as if surveyed everywhere).</summary>
public sealed record DebugSurveyAllCommand(int Player, int Target) : DebugCommand(Player);

/// <summary>Set how much is left in the deposit on a tile (0 .. 1 000 000 000).</summary>
public sealed record DebugDepositCommand(int Player, int Tile, Fix64 Amount) : DebugCommand(Player);

/// <summary>Add a player (e.g. a Cardassian to own test buildings and vehicles). Up to 8 players.</summary>
public sealed record DebugAddPlayerCommand(int Player, string FactionId) : DebugCommand(Player);

/// <summary>Change numbers in the loaded data files for this game (see DataTuning).</summary>
public sealed record DebugDataCommand(int Player, IReadOnlyList<DataEdit> Edits) : DebugCommand(Player);

/// <summary>Replace whole data files for this game (the panel's "reload data files": the new texts travel in the command).</summary>
public sealed record DebugReloadDataCommand(int Player, IReadOnlyList<DataFile> Files) : DebugCommand(Player);
