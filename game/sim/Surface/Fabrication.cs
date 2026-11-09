using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Parts economy (docs/parts-contract.md; design doc "Early Era, Parts & Maintenance"). CONTRACT STUBS: the sim branch
// replaces every body below; the signatures are what the view and model agents build against.
//
// Machine Shop (building id machine_shop): a queue of recipes, one at a time, turning refined materials into parts, parts
// into spare kits and construction parts. A finished colony core has a small workshop: the four spare kits only, made
// straight from materials (the recipe tree flattened), at hub.speed and hub.time_factor x the summed shop time.
// Repairs (building maintenance, drone auto-repair, vehicle repair on the pad, pad tyres) use spare kits instead of
// cost_per_point metal; with kits short a repair waits (and the fabricators are told), or the player jury-rigs it.

/// <summary>One order in a fabricator's queue. Progress 0..1 of the current order (only the first started one moves).</summary>
public sealed record FabOrder(int Id, string RecipeId, bool Auto)
{
    public Fix64 Progress { get; internal set; }
    public bool Started { get; internal set; }
}

/// <summary>What a fabricator is doing now.</summary>
public enum FabStatus { None, Idle, Working, LowPower, NoInput, Broken, Paused, Building }

public sealed partial class PlanetSurface
{
    /// <summary>A finished building that has a fabrication queue: a Machine Shop, or a colony core (its workshop).</summary>
    public bool IsFabricator(SurfaceStructure s) => false;

    /// <summary>Is this structure a colony core's workshop (kits only, slower) rather than a Machine Shop?</summary>
    public bool IsHubWorkshop(SurfaceStructure s) => false;

    /// <summary>Recipe ids (= item ids) this fabricator can make, in menu order.</summary>
    public IReadOnlyList<string> RecipesAt(SurfaceStructure s) => System.Array.Empty<string>();

    /// <summary>What one order of the recipe takes here (a core's workshop: flattened to materials).</summary>
    public IReadOnlyList<ResourceAmount> InputsAt(SurfaceStructure s, string recipeId) => System.Array.Empty<ResourceAmount>();

    /// <summary>Seconds one order takes here at full speed (before power/condition).</summary>
    public Fix64 SecondsAt(SurfaceStructure s, string recipeId) => Fix64.Zero;

    /// <summary>Why the player can't queue this recipe here (null = they can).</summary>
    public string? FabricateProblem(int player, int structureId, string recipeId) => "not built yet";

    /// <summary>The fabricator's state right now (Working, NoInput when every order is short, ...).</summary>
    public FabStatus FabricatorStatus(SurfaceStructure s) => FabStatus.None;

    /// <summary>Why this order can't start now ("needs 2 Mechanism (have 1)"), or null.</summary>
    public string? OrderProblem(SurfaceStructure s, FabOrder order) => null;

    /// <summary>Seconds left on the whole queue at the current rate.</summary>
    public Fix64 QueueSecondsLeft(SurfaceStructure s) => Fix64.Zero;

    /// <summary>Every part, spare kit and construction part id, in display order (resources with tier part/spare/construction).</summary>
    public IReadOnlyList<string> PartItems => System.Array.Empty<string>();

    /// <summary>The player's keep-in-stock target for an item (0 = none).</summary>
    public int StockTarget(int player, string itemId) => 0;

    /// <summary>How many of an item are queued (all the player's fabricators).</summary>
    public int Queued(int player, string itemId) => 0;

    /// <summary>The spare kit a building or vehicle uses for repairs (resource id), or null.</summary>
    public string? KitOf(SurfaceStructure s) => null;
    public string? KitOf(SurfaceUnit u) => null;

    /// <summary>Kits a full repair from here takes (building: ceil(missing condition / kit_points); vehicle: by damage and
    /// breakdown severity, plus pad tyres for rovers).</summary>
    public int KitsNeeded(SurfaceStructure s) => 0;
    public int KitsNeeded(SurfaceUnit u) => 0;

    /// <summary>Why the repair is stuck ("needs 2 Mechanical Spares (have 1)"), or null.</summary>
    public string? RepairWaitReason(SurfaceStructure s) => null;
    public string? RepairWaitReason(SurfaceUnit u) => null;

    /// <summary>Seconds until the fabricators should have the kits this repair waits for (null = nothing queued).</summary>
    public Fix64? KitEtaSeconds(int player, string kitId) => null;

    /// <summary>Why the building can't be jury-rigged now (null = it can).</summary>
    public string? JuryRigProblem(int player, int structureId) => "not built yet";

    /// <summary>Why the vehicle can't be jury-rigged now (null = it can).</summary>
    public string? JuryRigProblem(SurfaceUnit u) => "not built yet";

    /// <summary>Handles the five parts-economy commands (stub: refuses them).</summary>
    private bool ApplyFabrication(Simulation sim, Command command) => sim.Refuse(command, "parts economy not built yet");
}
