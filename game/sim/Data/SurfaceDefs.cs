using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>A surface terrain type from surfaces.json. Index = position in the file.</summary>
public sealed record TerrainDef(int Index, string Id, string Name, string Color, bool Passable, bool Buildable, int MoveCost, int Height);

/// <summary>Lowland terrain picked by moisture share: the first band whose Below is above the tile's moisture.</summary>
public sealed record LowlandBand(Fix64 Below, string TerrainId);

/// <summary>
/// A deposit rule on matching terrain (Coastal = the tile, or a field's first hex, touches water). Without Fields: roll
/// Chance per tile, amount in [Min, Max]. With Fields (owner 2026-11-12: "fewer deposits, larger"): Fields.Count fields,
/// each a contiguous blob of Fields.SizeMin..SizeMax neighbouring hexes, every hex its own deposit of [Min, Max].
/// </summary>
public sealed record DepositRule(string ResourceId, IReadOnlyList<string> Terrain, bool Coastal, Fix64 Chance, int Min, int Max)
{
    public FieldRule? Fields { get; init; }
}

/// <summary>Deposit fields (surfaces.json planets deposits fields): Count fields, or Per1000Tiles of the map's tiles
/// (rounded, at least 1) when Count is 0; each SizeMin..SizeMax hexes; a field's first hex at least Spacing hexes from
/// every hex of the fields placed before it (any resource).</summary>
public sealed record FieldRule(int Count, Fix64 Per1000Tiles, int SizeMin, int SizeMax, int Spacing)
{
    /// <summary>How many fields a map of this many tiles gets.</summary>
    public int CountFor(int tiles) => Count > 0 ? Count : System.Math.Max(1, (Per1000Tiles * Fix64.FromInt(tiles) / Fix64.FromInt(1000) + Fix64.Half).FloorToInt());
}

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

    /// <summary>What one drop-ship trip burns at 1 g (orbit.shuttle.fuel), before the surface's lift-cost multiplier
    /// (<see cref="Surface.GravityProfile.LiftCost"/>); transporters ignore gravity.</summary>
    public IReadOnlyList<ResourceAmount> ShuttleFuel { get; init; } = System.Array.Empty<ResourceAmount>();

    /// <summary>Seconds a new survey satellite rises from the ship and unfolds before it starts sweeping (orbit.satellite.deploy_seconds).</summary>
    public int SatelliteDeploySeconds { get; init; } = 8;

    /// <summary>Seconds a retasked satellite manoeuvres to its new band, not sweeping (orbit.satellite.retask_seconds).</summary>
    public int SatelliteRetaskSeconds { get; init; } = 6;

    /// <summary>How many findings each satellite's log keeps (orbit.satellite.log; the newest).</summary>
    public int SatelliteLog { get; init; } = 40;

    /// <summary>Names for satellite call signs (orbit.satellite.call_signs; empty = PlanetSurface's built-in list).</summary>
    public IReadOnlyList<string> SatelliteCallSigns { get; init; } = System.Array.Empty<string>();

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
    int AssembleSeconds,
    IReadOnlyList<ResourceAmount> StartStock,
    int RevealRadius,
    string HubBuilding,
    IReadOnlyList<string> Units);

/// <summary>
/// Surface stats for a ground vehicle (the vehicle itself is in vehicles.json). A unit with ExtractPerMinute
/// is a mining crawler: it unfurls on a deposit in DeploySeconds (and packs up just as long), fills a hopper of
/// Hopper, and sends each full load home as a cargo pod flying PodSpeed tiles per second. Cost and BuildSeconds are
/// what a Vehicle Bay charges and takes to assemble one. Mark = which mark of a vehicle line it is (1..3, e.g.
/// survey_rover is the Mk I Scout), or null for a unit with no marks.
/// </summary>
public sealed record SurfaceUnitDef(string Id, Fix64 Speed, int SurveyRadius, Fix64 ExtractPerMinute, Fix64 Hopper, int DeploySeconds, Fix64 PodSpeed,
    BreakdownRules? Breakdown, RepairRules? Repair, IReadOnlyList<ResourceAmount> Cost, int BuildSeconds, string Description, int? Mark = null,
    DamageRules? Damage = null, TyreRules? Tyres = null, FoundRules? Found = null)
{
    public bool CanMine => ExtractPerMinute > Fix64.Zero;

    /// <summary>A construction rover: it can found a new outpost on the ground (see <see cref="FoundRules"/>).</summary>
    public bool CanFound => Found != null;
}

/// <summary>
/// A construction rover's founding (surfaces.json units' "found"): it unfolds on a site for the unit's DeploySeconds,
/// paying Cost as it starts, then is used up and Building (a colony core) stands there as a new base under construction.
/// The site must be at least MinDistance tiles from the player's other colony cores.
/// </summary>
public sealed record FoundRules(string Building, IReadOnlyList<ResourceAmount> Cost, int MinDistance);

/// <summary>
/// Names and hull numbers for new units (surfaces.json unit_names): Pool of names (uppercase letters) and the first
/// band of numbers, NumbersFrom..NumbersTo. When a player has used every number in a band, the next band has one more
/// digit (10..99 → 100..999 → 1000..9999 …).
/// </summary>
public sealed record UnitNameRules(IReadOnlyList<string> Pool, int NumbersFrom, int NumbersTo);

/// <summary>
/// Building wear and maintenance (surfaces.json maintenance). Below DeclineBelow condition, output falls toward
/// SlowestOutput at 0, and a breakdown becomes possible (chance per minute rising to BreakChanceAtZero at 0).
/// Crew walk CrewWalkSeconds + CrewSecondsPerTile per tile from the core, take the spare kits the repair needs
/// (GameData.Fabrication), then restore RepairPerSecond. CostPerPoint is the old per-point metal cost: empty since the
/// parts economy (2026-11-09), kept for old data. Auto-maintained buildings fix themselves below AutoBelow.
/// </summary>
public sealed record MaintenanceRules(Fix64 DeclineBelow, Fix64 SlowestOutput, Fix64 BreakChanceAtZero, Fix64 AutoBelow,
    Fix64 CrewWalkSeconds, Fix64 CrewSecondsPerTile, Fix64 RepairPerSecond, IReadOnlyList<ResourceAmount> CostPerPoint);

/// <summary>A surface tech a Research Lab works on. Index = position in the list (stable order for state).</summary>
public sealed record TechDef(int Index, string Id, string Name, Fix64 Cost, IReadOnlyList<string> Requires, string Effect, IReadOnlyList<Modifier> Modifiers, string Description)
{
    /// <summary>Situational (e.g. only useful with ruins): not part of the core list the balance runner's research_all counts.</summary>
    public bool Optional { get; init; }
}

/// <summary>One kind of breakdown: picked by Weight, knocks DamageMin..DamageMax off integrity; LosesLoad empties the hopper.</summary>
public sealed record BreakdownSeverity(string Id, string Name, int Weight, int DamageMin, int DamageMax, bool LosesLoad);

/// <summary>
/// How often a working miner breaks (ChancePerMinute while digging), how bad it gets, and how slowly a broken one
/// drives (SlowestDrive = share of its speed at 0 integrity, rising to full speed at 100).
/// </summary>
public sealed record BreakdownRules(Fix64 ChancePerMinute, Fix64 SlowestDrive, IReadOnlyList<BreakdownSeverity> Severities);

/// <summary>Repair at a colony core: crew walk out for CrewSeconds, take the spare kits it needs, then PerSecond integrity a second.
/// CostPerPoint (metal a point) is empty since the parts economy (kits instead).</summary>
public sealed record RepairRules(Fix64 PerSecond, int CrewSeconds, IReadOnlyList<ResourceAmount> CostPerPoint);


/// <summary>How a terrain wears a driving rover: Wear integrity a minute, and the chances a minute of a hazard and of a flat tyre.</summary>
public sealed record TerrainWear(string Terrain, Fix64 Wear, Fix64 Hazard, Fix64 Flat);

/// <summary>A driving hazard (rock strike, sinkhole, ...): picked by Weight among those listed for the terrain; knocks
/// DamageMin..DamageMax off integrity, and Flat also punctures a tyre.</summary>
public sealed record RoverHazard(string Id, string Name, int Weight, int DamageMin, int DamageMax, IReadOnlyList<string> Terrain, bool Flat);

/// <summary>
/// Rover damage (surfaces.json units' damage): driving wears integrity by terrain and rolls hazards; a toxic tile
/// corrodes ToxicPerMinute; predators attack (WildlifeChancePerMinute, WildlifeMin..Max damage). Below SlowBelow it
/// drives slower, down to SlowestDrive at 0; below SensorsBelow its survey radius drops by SensorLoss (at least 1). At
/// 0 integrity it is disabled (no survey, only a repair order moves it, at Crawl share of its speed). Crawl is also the
/// slowest it ever drives.
/// </summary>
public sealed record DamageRules(IReadOnlyList<TerrainWear> Terrain, IReadOnlyList<RoverHazard> Hazards, Fix64 ToxicPerMinute,
    Fix64 WildlifeChancePerMinute, int WildlifeMin, int WildlifeMax, Fix64 SlowBelow, Fix64 SlowestDrive, Fix64 SensorsBelow, int SensorLoss, Fix64 Crawl)
{
    public TerrainWear? On(string terrainId)
    {
        foreach (var t in Terrain)
            if (t.Terrain == terrainId) return t;
        return null;
    }
}

/// <summary>
/// Flat tyres (surfaces.json units' tyres): Wheels on the rover (4 or 6), Spares carried. The flat chance from the
/// terrain is raised by LowIntegrity × the share of integrity lost. One flat drives at FlatSpeed, two or more at
/// TwoFlatSpeed, and each flat adds FlatWear (a share) to the terrain wear. The crew change a tyre in ChangeSeconds
/// parked (uses a spare); a repair pad fixes every flat and restocks the spares for spare kits (GameData.Fabrication
/// pad_tyre_kits a tyre; PadCost is the old metal cost, empty since the parts economy).
/// </summary>
public sealed record TyreRules(int Wheels, int Spares, Fix64 LowIntegrity, Fix64 FlatSpeed, Fix64 TwoFlatSpeed, Fix64 FlatWear, int ChangeSeconds,
    IReadOnlyList<ResourceAmount> PadCost);
