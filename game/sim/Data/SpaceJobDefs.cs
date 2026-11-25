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
    Fix64 FindMult, bool Quarantine);

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
