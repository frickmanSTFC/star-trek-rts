using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// data/civilizations/ (civilizations phase 1; docs/civilizations-contract.md). Read and checked in GameData.Civilizations.cs,
// played by game/sim/Space/Civilizations.cs. Every number is a placeholder.

/// <summary>A development level (levels.json): L1..L6.</summary>
public sealed record CivLevelDef(int Level, string Id, string Name, long PopulationMin, long PopulationMax, int TownsMin, int TownsMax,
    int SizeMin, int SizeMax, bool Lit, bool Signals, IReadOnlyList<string> OrbitalSigns);

/// <summary>An awareness band (rules.json awareness.bands): from this awareness up to the next band's.</summary>
public sealed record AwarenessBand(int From, string Id, string Name);

/// <summary>A ruin kind (ruins.json). Species = a canon precursor, or null for an invented (generated) species.</summary>
public sealed record RuinDef(string Id, string Name, string? Species, int Weight, bool Canon, string Notes);

/// <summary>Name parts (names.json).</summary>
public sealed record CivNameRules(IReadOnlyList<string> Onsets, IReadOnlyList<string> Vowels, IReadOnlyList<string> Codas,
    IReadOnlyList<string> Endings, int SyllablesMin, int SyllablesMax, IReadOnlyList<string> NationForms);

/// <summary>rules.json sites: where civilizations and ruins are rolled and what they are like.</summary>
public sealed record CivSiteRules(
    Fix64 LivingChance,
    Fix64 RuinsChance,
    IReadOnlyList<Weighted> LivingWeights,
    IReadOnlyList<Weighted> RuinsWeights,
    IReadOnlyList<(string Class, string Surface)> TownMaps,
    IReadOnlyList<(string Class, IReadOnlyList<int> Weights)> LevelWeights,
    IReadOnlyList<int> RuinsLevels,
    int LevelRange,
    IReadOnlyList<Weighted> TemperamentWeights,
    IReadOnlyList<Fix64> UnifiedChance,
    int NationsMin,
    int NationsMax,
    IReadOnlyList<Weighted> TownTerrain,
    int CoastalBonus,
    int TownSpacing)
{
    /// <summary>L1..L6 weights for a body class (its own row, else "default").</summary>
    public IReadOnlyList<int> LevelWeightsFor(string classId) =>
        LevelWeights.FirstOrDefault(x => x.Class == classId).Weights ?? LevelWeights.FirstOrDefault(x => x.Class == SystemGeneratorRules.Default).Weights
        ?? new[] { 1, 1, 1, 1, 1, 1 };

    /// <summary>The town-map stand-in surface class for a class without a surface of its own, or null.</summary>
    public string? TownMapFor(string classId) => TownMaps.FirstOrDefault(x => x.Class == classId).Surface;
}

/// <summary>rules.json awareness.</summary>
public sealed record CivAwarenessRules(IReadOnlyList<AwarenessBand> Bands, IReadOnlyList<int> Start, int SatelliteFromLevel, Fix64 SatellitePerMin,
    int ShipFromLevel, Fix64 ShipPerMin, Fix64 PostPerMin, int PostFound)
{
    /// <summary>The band an awareness falls in.</summary>
    public AwarenessBand BandOf(int awareness)
    {
        var band = Bands.Count > 0 ? Bands[0] : new AwarenessBand(0, "unaware", "Unaware");
        foreach (var b in Bands)
            if (awareness >= b.From) band = b;
        return band;
    }
}

/// <summary>rules.json study (seconds to finish with one source alone) and research (points a minute by level).</summary>
public sealed record CivStudyRules(int OrbitSeconds, int SatelliteSeconds, int PostSeconds, IReadOnlyList<Fix64> ObservePerMin, IReadOnlyList<Fix64> PostPerMin);

/// <summary>rules.json post: the hidden observation post.</summary>
public sealed record CivPostRules(IReadOnlyList<string> Factions, IReadOnlyList<ResourceAmount> Cost, int Max, int DiscoveryMinutes, Fix64 DiscoveryChance,
    int DoubleAboveAwareness, int StandingChange);

/// <summary>rules.json standing: Starfleet Standing (Federation, Prime Directive eras only).</summary>
public sealed record CivStandingRules(IReadOnlyList<string> Factions, int PrimeDirectiveFromYear, int Start, int Max, IReadOnlyList<int> Thresholds,
    Fix64 RecoveryPerMin, int RecoveryAfterMinutes, int RecalledIncidents, int InquiryHoldSeconds, int SanctionsSeconds, Fix64 SanctionsResearch);

/// <summary>The civilizations document (data/civilizations/).</summary>
public sealed record CivilizationData(CivSiteRules Sites, CivAwarenessRules Awareness, CivStudyRules Study, CivPostRules Post, CivStandingRules Standing,
    IReadOnlyList<CivLevelDef> Levels, CivNameRules Names, IReadOnlyList<RuinDef> Ruins)
{
    /// <summary>Level 1..6 (clamped).</summary>
    public CivLevelDef Level(int level) => Levels[System.Math.Clamp(level, 1, Levels.Count) - 1];

    public RuinDef? Ruin(string? id) => id == null ? null : Ruins.FirstOrDefault(r => r.Id == id);
}
