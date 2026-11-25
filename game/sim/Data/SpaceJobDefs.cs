using System;
using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Space;

namespace FinalFrontier.Sim.Data;

// Space jobs (docs/space-jobs-contract.md): the maps document's "space_sites" (maps/sites.json) and "ship_status"
// (maps/ship_status.json), read by GameData.SpaceJobs.cs. Used by Space/SpaceSites.cs, ShipStatus.cs and AwayTeam.cs.

/// <summary>Coming alongside a site: Seconds (× the alongside_time lever), then one debris roll: Chance percent for hull
/// HullMin..HullMax.</summary>
public sealed record SiteAlongsideDef(int Seconds, int Chance, int HullMin, int HullMax);

/// <summary>A known derelict's power: Seconds of Engineering work, or Kits patch kits at once.</summary>
public sealed record SiteRestorePowerDef(int Seconds, int Kits);

/// <summary>A powered known derelict's logs: Seconds, then latinum and research by the site's hash.</summary>
public sealed record SiteLogsDef(int Seconds, int LatinumMin, int LatinumMax, int ResearchMin, int ResearchMax);

/// <summary>A known derelict's failing reactor: Seconds after coming alongside, Hull damage if still alongside then.</summary>
public sealed record SiteReactorDef(int Seconds, int Hull);

/// <summary>Sweeping a debris field: Seconds inside it, Hull lost every HullEverySeconds, FindsMin..FindsMax things found
/// over the sweep (Always first, then Items by weight), survivors in a lifepod.</summary>
public sealed record SiteSweepDef(int Seconds, int HullEverySeconds, int Hull, int FindsMin, int FindsMax, IReadOnlyList<string> Always,
    IReadOnlyList<Weighted> Items, int SurvivorsMin, int SurvivorsMax);

/// <summary>What one asteroid variant holds: ContainersMin..Max containers of ore (by its hash), each container's ore picked
/// from Ores (resource id: weight) by the hash.</summary>
public sealed record SiteOreDef(string Variant, int ContainersMin, int ContainersMax, IReadOnlyList<Weighted> Ores);

/// <summary>Rock falls while mining: ChancePerMin percent a minute (rolled each second) of HullMin..HullMax damage; Tractor
/// percent of them knock the tractor out.</summary>
public sealed record SiteRockfallDef(int ChancePerMin, int HullMin, int HullMax, int Tractor);

/// <summary>Mining an asteroid (phase 2): a container of ore every Seconds (÷ the mining lever); what each variant holds.</summary>
public sealed record SiteMiningDef(int Seconds, IReadOnlyList<SiteOreDef> Ore, SiteRockfallDef? Rockfall)
{
    public SiteOreDef? OreOf(string variant) => Ore.FirstOrDefault(o => o.Variant == variant);
}

/// <summary>A site that can be towed (phase 2): what dropping it pays at a station (latinum) and at a colony ship's colony
/// body (research and duranium), each [min, max] by the site's hash.</summary>
public sealed record SiteTowDef(int LatinumMin, int LatinumMax, int ResearchMin, int ResearchMax, int DuraniumMin, int DuraniumMax);

/// <summary>An ore the hold can carry (maps space_sites ore.kinds): Id = a resource id, Latinum a unit at a station.</summary>
public sealed record OreKindDef(string Id, string Name, Fix64 Latinum);

/// <summary>Asteroids rolled with a system (generation.asteroids): CountMin..Max, NearBelt percent of them at a belt's
/// distance from the star ± BeltSpread, Spacing from other sites.</summary>
public sealed record AsteroidGenDef(int CountMin, int CountMax, int NearBelt, Fix64 BeltSpread, Fix64 Spacing);

// ---------------------------------------------------------------- phase 3: events and anomalies (maps/events.json)

/// <summary>Sites that arrive during the match (space_sites events): after CalmSeconds an event every EveryMin..EveryMax
/// seconds (by the system's hash), at most MaxLive at once; the kind by Weights; moving things come in at EdgeMin..EdgeMax
/// from the star.</summary>
public sealed record SpaceEventRules(int CalmSeconds, int EveryMin, int EveryMax, int MaxLive, Fix64 EdgeMin, Fix64 EdgeMax, IReadOnlyList<Weighted> Weights);

/// <summary>Anomalies rolled with the system (generation.anomalies): CountMin..Max, each drifting Drift map units over
/// DriftSeconds, then staying.</summary>
public sealed record AnomalyGenDef(int CountMin, int CountMax, Fix64 Drift, int DriftSeconds);

/// <summary>A rogue asteroid striking the colony's body: a spot within Spread hexes of a landed player's core; buildings and
/// vehicles within Radius hexes lose DamageMin..Max × (1 − distance ÷ (Radius + 1)), the MaxBuildings / MaxVehicles nearest
/// only, never below 1.</summary>
public sealed record RogueImpactDef(int Spread, int Radius, int DamageMin, int DamageMax, int MaxBuildings, int MaxVehicles);

/// <summary>A rogue asteroid (kind rogue_asteroid "rogue"): flight to its body FlightMin..Max s; ColonyChance percent head for
/// the colony's body; pushed aside in PushMin..Max s with the tractor; turned, it veers Veer map units out over VeerSeconds;
/// the turner's reward latinum / research; a living civilization's awareness when saved (CivAwareness) or struck
/// (CivStrikeAwareness).</summary>
public sealed record SiteRogueDef(int FlightMin, int FlightMax, int ColonyChance, int PushMin, int PushMax, Fix64 Veer, int VeerSeconds,
    int LatinumMin, int LatinumMax, int ResearchMin, int ResearchMax, int CivAwareness, int CivStrikeAwareness, RogueImpactDef Impact);

/// <summary>A comet (kind comet "comet"): crosses in CrossMin..Max s passing PassMin..Max from the star; its tail can be
/// sampled between the shares WindowFrom..WindowTo of the crossing, for SampleSeconds (hull Hull every HullEverySeconds):
/// research and IceMin..Max containers of deuterium ore.</summary>
public sealed record SiteCometDef(int CrossMin, int CrossMax, Fix64 PassMin, Fix64 PassMax, Fix64 WindowFrom, Fix64 WindowTo, int SampleSeconds,
    int Hull, int HullEverySeconds, int ResearchMin, int ResearchMax, int IceMin, int IceMax);

/// <summary>Lifepods (kind lifepods "pods"): CountMin..Max pods, SurvivorsMin..Max each, air AirMin..Max s from the call,
/// PickupSeconds each with the tractor; placed NearDebris map units off a debris field when there is one.</summary>
public sealed record SitePodsDef(int CountMin, int CountMax, int SurvivorsMin, int SurvivorsMax, int AirMin, int AirMax, int PickupSeconds, Fix64 NearDebris);

/// <summary>A stranded ship (kind stranded_ship "stranded"): air AirMin..Max s unless powered; flights × EscortFlight while
/// escorting; Bait percent are traps (hull AmbushHullMin..Max, one of AmbushSystems knocked out); rescue reward.</summary>
public sealed record SiteStrandedDef(int AirMin, int AirMax, Fix64 EscortFlight, int Bait, int AmbushHullMin, int AmbushHullMax,
    IReadOnlyList<string> AmbushSystems, int LatinumMin, int LatinumMax, int ResearchMin, int ResearchMax);

/// <summary>A ring of an anomaly: Research every interval, Hull lost every interval, Knock percent a system goes down;
/// Radius (map units) for the plot; Hazardous = not with a critical hull.</summary>
public sealed record AnomalyRingDef(string Id, string Name, Fix64 Radius, int Research, int Hull, int Knock, bool Hazardous);

/// <summary>An anomaly (kind anomaly "anomaly"): recording at a ring every EverySeconds, up to ResearchCap per player;
/// Systems the inner ring can knock out; an event anomaly lasts LifeMin..Max s drifting Drift map units.</summary>
public sealed record SiteAnomalyDef(int EverySeconds, int ResearchCap, IReadOnlyList<string> Systems, int LifeMin, int LifeMax, Fix64 Drift,
    IReadOnlyList<AnomalyRingDef> Rings)
{
    public AnomalyRingDef? Ring(string? id) => id == null ? null : Rings.FirstOrDefault(r => r.Id == id);
}

/// <summary>One kind of space site (maps/sites.json kinds).</summary>
public sealed record SiteKindDef(string Id, SiteKind Kind, string Name, string Letter, int ScanSeconds, int SizeMin, int SizeMax,
    IReadOnlyList<Weighted> Variants, IReadOnlyList<(string Variant, string Text)> Origin, bool Hazardous, IReadOnlyList<string> Hazards)
{
    public SiteAlongsideDef? Alongside { get; init; }
    public SiteRestorePowerDef? RestorePower { get; init; }
    public SiteLogsDef? Logs { get; init; }
    public SiteReactorDef? Reactor { get; init; }
    public SiteSweepDef? Sweep { get; init; }
    /// <summary>The ship_status away_team table a boarding uses ("" = no away team here).</summary>
    public string AwayTable { get; init; } = "";
    public int ResearchMin { get; init; }
    public int ResearchMax { get; init; }
    /// <summary>Phase 2: mining (asteroids), towing, and per-variant away tables (variant, table; over AwayTable).</summary>
    public SiteMiningDef? Mining { get; init; }
    public SiteTowDef? Tow { get; init; }
    public IReadOnlyList<(string Variant, string Table)> VariantAway { get; init; } = Array.Empty<(string, string)>();
    /// <summary>Phase 3: the event kinds' rules (null on the others).</summary>
    public SiteRogueDef? Rogue { get; init; }
    public SiteCometDef? Comet { get; init; }
    public SitePodsDef? Pods { get; init; }
    public SiteStrandedDef? Stranded { get; init; }
    public SiteAnomalyDef? Anomaly { get; init; }

    /// <summary>The away table a site of this variant boards with ("" = none).</summary>
    public string AwayTableFor(string variant)
    {
        foreach (var (v, t) in VariantAway)
            if (v == variant) return t;
        return AwayTable;
    }

    /// <summary>Whether any variant of the kind can be boarded.</summary>
    public bool AnyAway => AwayTable.Length > 0 || VariantAway.Count > 0;

    public string OriginOf(string variant)
    {
        foreach (var (v, t) in Origin)
            if (v == variant) return t;
        return "";
    }
}

/// <summary>Something the hold can carry from a site: Containers each, Latinum a station pays, Research a dropped one gives.</summary>
public sealed record SalvageDef(string Id, string Name, int Containers, int Latinum, int Research);

/// <summary>The station every system has (maps/sites.json station).</summary>
public sealed record StationRules(IReadOnlyList<string> Names, Fix64 OrbitMin, Fix64 OrbitMax, int DockSeconds, int HirePerVisit, int HireLatinum,
    int SurvivorLatinum);

/// <summary>maps/sites.json "space_sites".</summary>
public sealed record SpaceSiteRules(int CountMin, int CountMax, Fix64 OrbitMin, Fix64 OrbitMax, Fix64 ClearOfBody, Fix64 Spacing, int Tries,
    IReadOnlyList<Weighted> Weights, Fix64 IdentifyRange, int SpentSeconds, int ColonyReachSeconds, IReadOnlyList<SiteKindDef> Kinds,
    IReadOnlyList<SalvageDef> Salvage, StationRules Station)
{
    /// <summary>Phase 2: asteroids rolled with the system (null = none).</summary>
    public AsteroidGenDef? Asteroids { get; init; }
    /// <summary>Units of ore in a hold container.</summary>
    public int OrePerContainer { get; init; } = 30;
    public IReadOnlyList<OreKindDef> Ores { get; init; } = Array.Empty<OreKindDef>();
    /// <summary>Roles of neutral ships that buy ore, and their share of the station's price.</summary>
    public IReadOnlyList<string> TraderRoles { get; init; } = Array.Empty<string>();
    public Fix64 TraderShare { get; init; } = Fix64.One;
    /// <summary>× a ship's flights while it tows something.</summary>
    public Fix64 TowFlight { get; init; } = Fix64.One;
    /// <summary>Phase 3: events during the match (null = none) and anomalies rolled with the system (null = none).</summary>
    public SpaceEventRules? Events { get; init; }
    public AnomalyGenDef? Anomalies { get; init; }

    public OreKindDef? Ore(string? id) => id == null ? null : Ores.FirstOrDefault(o => o.Id == id);

    public SiteKindDef? Kind(SiteKind kind) => Kinds.FirstOrDefault(k => k.Kind == kind);
    public SiteKindDef? Kind(string id) => Kinds.FirstOrDefault(k => k.Id == id);
    public SalvageDef? SalvageItem(string? id) => id == null ? null : Salvage.FirstOrDefault(s => s.Id == id);

    /// <summary>A SiteKind's data id: "known_derelict" for KnownDerelict.</summary>
    public static string IdOf(SiteKind kind)
    {
        var name = kind.ToString();
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    /// <summary>The SiteKind of a data id ("known_derelict", or "knownderelict"), or null.</summary>
    public static SiteKind? KindOf(string? id)
    {
        if (id == null) return null;
        foreach (var k in Enum.GetValues<SiteKind>())
            if (IdOf(k) == id || k.ToString().ToLowerInvariant() == id) return k;
        return null;
    }
}

/// <summary>A hull band (ship_status hull.bands): it holds hull &gt;= From.</summary>
public sealed record HullBandDef(string Id, HullBand Band, int From, Fix64 Flight, Fix64 ScanReach, bool Hazardous, string Text);

/// <summary>A ship system: Scan × site scans and Flight × flights while it is down.</summary>
public sealed record ShipSystemDef(string Id, ShipSystem System, string Name, Fix64 Scan, Fix64 Flight, string Text);

/// <summary>An away-team choice as data (AwayChoice is what the view gets): Shift = points onto outcome weights; Bring =
/// what a step-3 choice brings back on a good find.</summary>
public sealed record AwayChoiceDef(string Label, string Hint, int Injury, int Loss, int Find, int Seconds, IReadOnlyList<(string Outcome, int Points)> Shift)
{
    public bool BringResearch { get; init; }
    public string BringSalvage { get; init; } = "";
    public int BringSurvivorsMin { get; init; }
    public int BringSurvivorsMax { get; init; }
    /// <summary>Percent that a survivors reading is real (100 = always).</summary>
    public int BringChance { get; init; } = 100;
}

public sealed record AwayStepDef(string Title, IReadOnlyList<AwayChoiceDef> Choices);

/// <summary>What can happen to a boarding (rolled once when the team is back).</summary>
public sealed record AwayOutcomeDef(string Id, int Weight, string Text, bool Research, int Hull, string System, int Injure, Fix64 LossMult,
    Fix64 FindMult, bool Quarantine)
{
    /// <summary>Phase 3: the boarding turned a rogue asteroid aside (the charges table).</summary>
    public bool Deflect { get; init; }
}

public sealed record AwayTableDef(string Id, int Injury, int Loss, int Find, int UnpoweredInjury, int UnpoweredLoss, int UnpoweredFind,
    IReadOnlyList<AwayStepDef> Steps, IReadOnlyList<AwayOutcomeDef> Outcomes);

/// <summary>maps/ship_status.json "ship_status".</summary>
public sealed class ShipStatusRules
{
    public IReadOnlyList<HullBandDef> Bands { get; init; } = Array.Empty<HullBandDef>();
    public int RecoverTo { get; init; } = 20;
    public int RecoverSeconds { get; init; } = 180;
    public IReadOnlyList<ShipSystemDef> Systems { get; init; } = Array.Empty<ShipSystemDef>();
    public int DownMin { get; init; } = 60;
    public int DownMax { get; init; } = 180;
    public Fix64 FieldPer10s { get; init; } = Fix64.One;
    public int FieldUpTo { get; init; } = 60;
    public int PatchHull { get; init; } = 15;
    public int PatchSeconds { get; init; } = 30;
    public int InjurySeconds { get; init; } = 180;
    public int MedicalSeconds { get; init; } = 60;
    public int MedicalMax { get; init; } = 3;
    public int InjuryMinSeconds { get; init; } = 30;
    public Fix64 HalfCrewFlight { get; init; } = Fix64.One;
    public int LogMax { get; init; } = 40;
    public IReadOnlyList<string> Ranks { get; init; } = Array.Empty<string>();
    public IReadOnlyList<(string Division, IReadOnlyList<string> Roles)> Roles { get; init; } = Array.Empty<(string, IReadOnlyList<string>)>();
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> DefaultCrew { get; init; } = Array.Empty<int>();
    public int DefaultHold { get; init; }
    public string DefaultFocus { get; init; } = "";
    public int DefaultPatchKits { get; init; }
    public int SurvivorsPerContainer { get; init; } = 3;
    public int AwayMin { get; init; } = 2;
    public int AwayMax { get; init; } = 6;
    public Fix64 AwayMaxShare { get; init; } = Fix64.Half;
    public IReadOnlyList<(string Ship, int Max)> AwayMaxByShip { get; init; } = Array.Empty<(string, int)>();
    public int StepSeconds { get; init; } = 60;
    public int ReturnSeconds { get; init; } = 20;
    public int DefenseLoss { get; init; } = 4;
    public string MedicDivision { get; init; } = "";
    public Fix64 MedicShare { get; init; } = Fix64.Half;
    public int QuarantineSeconds { get; init; } = 180;
    public IReadOnlyList<AwayTableDef> Tables { get; init; } = Array.Empty<AwayTableDef>();

    public AwayTableDef? Table(string? id) => id == null ? null : Tables.FirstOrDefault(t => t.Id == id);
    public ShipSystemDef? System(ShipSystem s) => Systems.FirstOrDefault(x => x.System == s);

    public IReadOnlyList<string> RolesOf(string division)
    {
        foreach (var (d, r) in Roles)
            if (d == division) return r;
        return Array.Empty<string>();
    }

    /// <summary>The band a hull value is in (crippled at 0).</summary>
    public HullBandDef? BandOf(Fix64 hull)
    {
        if (hull <= Fix64.Zero) return Bands.FirstOrDefault(b => b.Band == HullBand.Crippled);
        HullBandDef? lowest = null;
        foreach (var b in Bands.Where(b => b.Band != HullBand.Crippled).OrderByDescending(b => b.From))
        {
            if (hull >= Fix64.FromInt(b.From)) return b;
            lowest = b;
        }
        return lowest;
    }
}

public sealed partial class GameData
{
    /// <summary>maps/sites.json, or null when the data set has none (then no system has sites).</summary>
    public SpaceSiteRules? SpaceSites { get; private init; }

    /// <summary>maps/ship_status.json, or null when the data set has none (then flagships have no status).</summary>
    public ShipStatusRules? ShipStatus { get; private init; }
}
