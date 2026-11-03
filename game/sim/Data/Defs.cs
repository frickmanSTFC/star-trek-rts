using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>A resource from resources.json. Index is its position in the file (stable ordering for bags).</summary>
public sealed record ResourceDef(int Index, string Id, string Name, string Tier);

/// <summary>An amount of one resource, referenced by resource index.</summary>
public readonly record struct ResourceAmount(int Resource, Fix64 Amount);

/// <summary>A planet class or other body type from planet_classes.json.</summary>
public sealed record PlanetClassDef(string Id, string Name, Fix64 Habitability, string ColonyType, IReadOnlyList<string> Yields)
{
    /// <summary>"full" colony classes (Class M) can be a player's home.</summary>
    public bool IsFullColony => ColonyType == "full";
}

/// <summary>
/// A structure from buildings.json (the "buildings" list). Economy fields are optional in the data:
/// BuildSeconds 0 = can't be built by the player; ExtractPerMinute = how fast it mines the deposit
/// under it (RequiresDeposit); BuildRadius / BuildQueues = for colony cores, how far from it you can
/// build and how many constructions run at once. PowerSupply / PowerUse feed the base's power budget.
/// Level and UpgradesTo describe a core's upgrade line (UpgradesTo "" = top level).
/// Storage = capacity it adds for stockpiled materials. Recipes run CyclesPerMinute in total, shared between
/// the recipes that have input. RequiresDepleted buildings go on a used-up deposit and reopen it once at
/// ReopenShare of its first size. RequiresLevel = the base's core must be at least this level.
/// Produces = surface units it assembles one at a time, with up to ProductionQueue orders waiting.
/// </summary>
public sealed record BuildingDef(
    string Id,
    string Name,
    string Layer,
    string Category,
    string Effect,
    IReadOnlyList<ResourceAmount> Cost,
    IReadOnlyList<ResourceAmount> OutputPerMinute,
    IReadOnlyList<ResourceAmount> UpkeepPerMinute,
    int BuildSeconds,
    bool RequiresDeposit,
    Fix64 ExtractPerMinute,
    int BuildRadius,
    int BuildQueues,
    Fix64 PowerSupply,
    Fix64 PowerUse,
    int Level,
    string UpgradesTo,
    Fix64 Storage,
    IReadOnlyList<Recipe> Recipes,
    Fix64 CyclesPerMinute,
    bool RequiresDepleted,
    Fix64 ReopenShare,
    int RequiresLevel,
    IReadOnlyList<string> Produces,
    int ProductionQueue)
{
    /// <summary>Assembles surface units (a Vehicle Bay).</summary>
    public bool IsFactory => Produces.Count > 0;

    /// <summary>A colony core (hub and its upgrades): defines a base, its build radius and queues.</summary>
    public bool IsCore => BuildRadius > 0;

    /// <summary>Digs the deposit under it (a mine, or a deep-core mine once it has reopened the deposit).</summary>
    public bool IsMine => ExtractPerMinute > Fix64.Zero;

    /// <summary>Turns stock into other stock (refinery, foundry).</summary>
    public bool IsConverter => Recipes.Count > 0 && CyclesPerMinute > Fix64.Zero;
}

/// <summary>One cycle of a converter: takes In from stock, gives Out.</summary>
public sealed record Recipe(IReadOnlyList<ResourceAmount> In, IReadOnlyList<ResourceAmount> Out);

public sealed record StarDef(string Name, string Class, Fix64 Radius);

/// <summary>A body on a map. Parent is the id of the body a moon circles, or null.</summary>
public sealed record BodyDef(
    string Id,
    string Name,
    string ClassId,
    int Size,
    FixVec2 Position,
    string? Parent,
    IReadOnlyList<ResourceAmount> Deposits);

public sealed record StartDef(int Slot, string HomeBodyId);

/// <summary>A skirmish map from maps.json: one star system.</summary>
public sealed record MapDef(
    string Id,
    string Name,
    string Era,
    int Players,
    StarDef Star,
    IReadOnlyList<ResourceAmount> StartStock,
    IReadOnlyList<StartDef> Starts,
    IReadOnlyList<BodyDef> Bodies);
