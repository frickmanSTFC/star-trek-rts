using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>A resource from resources.json. Index is its position in the file (stable ordering for bags).</summary>
public sealed record ResourceDef(int Index, string Id, string Name, string Tier)
{
    /// <summary>Storage space one unit takes on a surface (resources.json volume; 1 when not given: parts 1, kits and
    /// construction parts 2).</summary>
    public Fix64 Volume { get; init; } = Fix64.One;

    /// <summary>A part, spare kit or construction part (the parts economy's items).</summary>
    public bool IsPartItem => Tier is "part" or "spare" or "construction";
}

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
/// WearPerMinute = condition lost per minute of work; ResearchPerMinute = research points (labs); Levels = upgrades
/// after Mk I; RequiresTech = a surface tech that must be researched before it can be placed ("" = none).
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
    int ProductionQueue,
    Fix64 WearPerMinute,
    Fix64 ResearchPerMinute,
    IReadOnlyList<BuildingLevel> Levels,
    string RequiresTech,
    string Description)
{
    /// <summary>Loses condition while it works and needs maintenance.</summary>
    public bool Wears => WearPerMinute > Fix64.Zero;

    /// <summary>Makes research points (a Research Lab).</summary>
    public bool IsLab => ResearchPerMinute > Fix64.Zero;

    /// <summary>Highest level: 1 (Mk I, the base) plus one per entry in Levels.</summary>
    public int MaxLevel => Levels.Count + 1;

    /// <summary>Assembles surface units (a Vehicle Bay).</summary>
    public bool IsFactory => Produces.Count > 0;

    /// <summary>A colony core (hub and its upgrades): defines a base, its build radius and queues.</summary>
    public bool IsCore => BuildRadius > 0;

    /// <summary>People work inside it (buildings.json crewed): on a Domes world it must stand under cover and draws life
    /// support (habitats, labs, the foundry, the bay, cores); automated ones (mines, storage, reactor, refinery) don't.</summary>
    public bool Crewed { get; init; }

    /// <summary>A Machine Shop's fabrication queue (buildings.json fabricator), or null.</summary>
    public FabricatorDef? Fabricator { get; init; }

    /// <summary>Digs the deposit under it (a mine, or a deep-core mine once it has reopened the deposit).</summary>
    public bool IsMine => ExtractPerMinute > Fix64.Zero;

    /// <summary>Turns stock into other stock (refinery, foundry).</summary>
    public bool IsConverter => Recipes.Count > 0 && CyclesPerMinute > Fix64.Zero;
}

/// <summary>
/// A change to stats from a building level or a tech. Stat: speed, yield, wear (shares added, e.g. 0.2 = +20%),
/// auto_maintain (on/off), breakdown (a unit's breakdown chance). Buildings / Units = what it applies to
/// (both empty = every building; a level's modifiers apply to its own building).
/// </summary>
public sealed record Modifier(string Stat, IReadOnlyList<string> Buildings, IReadOnlyList<string> Units, Fix64 Add)
{
    public const string Speed = "speed", Yield = "yield", Wear = "wear", AutoMaintain = "auto_maintain", Breakdown = "breakdown";
    public static readonly string[] Stats = { Speed, Yield, Wear, AutoMaintain, Breakdown };
}

/// <summary>An upgrade level of a building (Mk II, Mk III): what it costs, how long it takes, and its stat totals.</summary>
public sealed record BuildingLevel(string Name, IReadOnlyList<ResourceAmount> Cost, int BuildSeconds, int RequiresLevel, IReadOnlyList<Modifier> Modifiers);

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
