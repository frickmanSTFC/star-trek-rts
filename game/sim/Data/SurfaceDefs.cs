using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>A surface terrain type from surfaces.json. Index = position in the file.</summary>
public sealed record TerrainDef(int Index, string Id, string Name, string Color, bool Passable, bool Buildable, int MoveCost, int Height);

/// <summary>Lowland terrain picked by moisture share: the first band whose Below is above the tile's moisture.</summary>
public sealed record LowlandBand(Fix64 Below, string TerrainId);

/// <summary>A deposit rule: on matching terrain, roll Chance per tile; amount in [Min, Max].</summary>
public sealed record DepositRule(string ResourceId, IReadOnlyList<string> Terrain, bool Coastal, Fix64 Chance, int Min, int Max);

/// <summary>How to generate the surface of one planet class. Elevation thresholds are shares of the map.</summary>
public sealed record PlanetSurfaceDef(
    string ClassId,
    int Width,
    int Height,
    int FeatureSize,
    Fix64 OceanBelow,
    Fix64 ShallowsBelow,
    Fix64 HillsFrom,
    Fix64 MountainsFrom,
    IReadOnlyList<LowlandBand> Lowland,
    int PolarRows,
    string PolarTerrain,
    int RegionColumns,
    int RegionRows,
    IReadOnlyList<DepositRule> Deposits,
    LifeRules? Life);

/// <summary>Patches of something (wildlife herds, toxic zones): how many, how big, on which terrain.</summary>
public sealed record PatchRule(int Count, int RadiusMin, int RadiusMax, IReadOnlyList<string> Terrain);

/// <summary>
/// Life on a planet, for the orbital scans and landing safety: a chance of one native settlement (no landing or
/// building within NativeRadius), wildlife herds (a PredatorShare of them dangerous), toxic zones, and biomass per terrain.
/// </summary>
public sealed record LifeRules(Fix64 NativesChance, int NativeRadius, PatchRule Wildlife, Fix64 PredatorShare, PatchRule Toxic,
    IReadOnlyList<(string Terrain, Fix64 Biomass)> Biomass);

public sealed record HintRules(int Likely, int Rich);

/// <summary>The player's ship in orbit: its name and its ships.json class (faction.Name key).</summary>
public sealed record OrbitShip(string Name, string ClassName);

/// <summary>One transporter level: what it can beam (unit ids or "supplies"), how far from your bases or people (0 = anywhere), how much cargo, and the wait between beams.</summary>
public sealed record TransporterLevel(int Level, string Name, IReadOnlyList<string> Payloads, int Range, Fix64 Cargo, int CooldownSeconds);

/// <summary>
/// Orbit (surfaces.json orbit): the match era, each faction's ship, the ship's cargo hold, and the ways down:
/// outpost drops, the shuttle, survey satellites and transporters (level from the era, or a faction override).
/// </summary>
public sealed record OrbitRules(
    string Era,
    IReadOnlyList<(string Faction, OrbitShip Ship)> Ships,
    IReadOnlyList<ResourceAmount> Hold,
    string OutpostBuilding,
    IReadOnlyList<ResourceAmount> OutpostCost,
    int OutpostDescentSeconds,
    int OutpostMinDistance,
    int ShuttleSeconds,
    IReadOnlyList<string> ShuttlePayloads,
    IReadOnlyList<ResourceAmount> SatelliteCost,
    int SatelliteMax,
    int SatelliteBandRows,
    int SatelliteSweepSeconds,
    IReadOnlyList<TransporterLevel> TransporterLevels,
    IReadOnlyList<(string Era, int Level)> TransporterByEra,
    IReadOnlyList<(string Faction, int Level)> TransporterFactions)
{
    public const string Supplies = "supplies";

    /// <summary>Transporter level for a faction in this era (0 = none): the faction's override, else the era's.</summary>
    public int TransporterLevelFor(string faction, string era)
    {
        foreach (var (f, level) in TransporterFactions)
            if (f == faction) return level;
        foreach (var (e, level) in TransporterByEra)
            if (e == era) return level;
        return 0;
    }

    public TransporterLevel? Transporter(int level) => level >= 1 && level <= TransporterLevels.Count ? TransporterLevels[level - 1] : null;

    public OrbitShip? ShipFor(string faction)
    {
        foreach (var (f, ship) in Ships)
            if (f == faction) return ship;
        return null;
    }
}

public sealed record LandingRules(
    IReadOnlyList<string> Terrain,
    int DescentSeconds,
    IReadOnlyList<ResourceAmount> StartStock,
    int RevealRadius,
    string HubBuilding,
    IReadOnlyList<string> Units);

/// <summary>
/// Surface stats for a ground vehicle (the vehicle itself is in vehicles.json). A unit with ExtractPerMinute
/// is a mining crawler: it unfurls on a deposit in DeploySeconds (and packs up just as long), fills a hopper of
/// Hopper, and sends each full load home as a cargo pod flying PodSpeed tiles per second. Cost and BuildSeconds are
/// what a Vehicle Bay charges and takes to assemble one.
/// </summary>
public sealed record SurfaceUnitDef(string Id, Fix64 Speed, int SurveyRadius, Fix64 ExtractPerMinute, Fix64 Hopper, int DeploySeconds, Fix64 PodSpeed,
    BreakdownRules? Breakdown, RepairRules? Repair, IReadOnlyList<ResourceAmount> Cost, int BuildSeconds, string Description)
{
    public bool CanMine => ExtractPerMinute > Fix64.Zero;
}

/// <summary>
/// Building wear and maintenance (surfaces.json maintenance). Below DeclineBelow condition, output falls toward
/// SlowestOutput at 0, and a breakdown becomes possible (chance per minute rising to BreakChanceAtZero at 0).
/// Crew walk CrewWalkSeconds + CrewSecondsPerTile per tile from the core, then restore RepairPerSecond, paying
/// CostPerPoint. Auto-maintained buildings fix themselves below AutoBelow.
/// </summary>
public sealed record MaintenanceRules(Fix64 DeclineBelow, Fix64 SlowestOutput, Fix64 BreakChanceAtZero, Fix64 AutoBelow,
    Fix64 CrewWalkSeconds, Fix64 CrewSecondsPerTile, Fix64 RepairPerSecond, IReadOnlyList<ResourceAmount> CostPerPoint);

/// <summary>A surface tech a Research Lab works on. Index = position in the list (stable order for state).</summary>
public sealed record TechDef(int Index, string Id, string Name, Fix64 Cost, IReadOnlyList<string> Requires, string Effect, IReadOnlyList<Modifier> Modifiers, string Description);

/// <summary>One kind of breakdown: picked by Weight, knocks DamageMin..DamageMax off integrity; LosesLoad empties the hopper.</summary>
public sealed record BreakdownSeverity(string Id, string Name, int Weight, int DamageMin, int DamageMax, bool LosesLoad);

/// <summary>
/// How often a working miner breaks (ChancePerMinute while digging), how bad it gets, and how slowly a broken one
/// drives (SlowestDrive = share of its speed at 0 integrity, rising to full speed at 100).
/// </summary>
public sealed record BreakdownRules(Fix64 ChancePerMinute, Fix64 SlowestDrive, IReadOnlyList<BreakdownSeverity> Severities);

/// <summary>Repair at a colony core: crew walk out for CrewSeconds, then PerSecond integrity a second, paying CostPerPoint for each point.</summary>
public sealed record RepairRules(Fix64 PerSecond, int CrewSeconds, IReadOnlyList<ResourceAmount> CostPerPoint);
