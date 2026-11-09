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
    Fix64 ExpectedLivingMin,
    Fix64 ExpectedLivingMax,
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

    /// <summary>contact.json (phase 2: first contact, relations, trade, ally ships, aggressive natives, L4 → L5), or null
    /// when the data set has none (then nobody can make contact).</summary>
    public ContactRules? Contact { get; init; }
}

// ---- phase 2 (contact.json; docs/civilizations-phase2-contract.md; played by game/sim/Space/Contact.cs)

/// <summary>One option at a first-contact step. Shock = percentage points on the culture-shock chance; Research = points
/// added to the one-off research boost; StandingIfShock = Starfleet Standing change if the event ends in culture shock;
/// WaitMinutes = added to the shock wait before trade.</summary>
public sealed record ContactChoiceDef(string Text, string Hint, int Shock, int Research, int StandingIfShock, int WaitMinutes);

/// <summary>A step of the first-contact event; its first choice is the default.</summary>
public sealed record ContactStepDef(string Id, string Title, IReadOnlyList<ContactChoiceDef> Choices);

/// <summary>A unique local tech a trade partner shares: modifiers like a surface tech's, applied to the player's surface.</summary>
public sealed record UniqueTechDef(string Id, string Name, string Effect, IReadOnlyList<Modifier> Modifiers);

/// <summary>A native ship class for ally ships (not in ships.json: ClassId is "native.&lt;id&gt;").</summary>
public sealed record AllyClassDef(string Id, string Name, string Role, int Weight);

/// <summary>contact.json membership: the steps Trade partner → Candidate → Allied world.</summary>
public sealed record MembershipRules(int MinStanding, int PartnerMinutes, int CandidateMinutes, Fix64 WalkoutChance, Fix64 WalkoutIncomeCut);

/// <summary>contact.json ally: native ships that escort the flagship or scout a body.</summary>
public sealed record AllyRules(int ShipsTradePartner, int ShipsCandidate, int ShipsAllied, IReadOnlyList<AllyClassDef> Classes, IReadOnlyList<string> Names,
    Fix64 TravelScale, int EscortDelaySeconds, int ScoutSweepSeconds)
{
    /// <summary>How many ally ships a relations stage brings (2 trade partner, 3 candidate, 4 allied; 0 below).</summary>
    public int ShipsFor(int stage) => stage switch { 2 => ShipsTradePartner, 3 => ShipsCandidate, >= 4 => ShipsAllied, _ => 0 };
}

/// <summary>contact.json aggressive: natives with an aggressive temperament past an awareness mark.</summary>
public sealed record AggressiveRules(int FromAwareness, int ShootFromLevel, Fix64 ShootChance, int ShootDownStanding, int RefuseMinutes);

/// <summary>contact.json advance: an L4 world reaching L5 (warp) during a match.</summary>
public sealed record AdvanceRules(Fix64 ChancePerMin, Fix64 PerAwareness, int WreckSpike, Fix64 WreckChance);

/// <summary>contact.json unrest: after a culture shock, once trade starts the world pays TradeShare of its trade for Minutes,
/// and its natives stay unsettled until then (awareness held at AwarenessFloor or above).</summary>
public sealed record UnrestRules(Fix64 TradeShare, int Minutes, int AwarenessFloor);

/// <summary>contact.json freighter: the native trade freighter that flies each trade route (ClassId "native.&lt;ClassIdPart&gt;").</summary>
public sealed record FreighterRules(string ClassId, string ClassName, string Role, int IntervalMinutes, int DwellSeconds, Fix64 TravelScale,
    IReadOnlyList<string> Names);

/// <summary>contact.json spacefaring: civilizations of FromLevel or more (L6) are stronger partners but wary of a low Standing.</summary>
public sealed record SpacefaringRules(int FromLevel, Fix64 TradeMultiplier, int AllyShips, int PartnerMinutes, int CandidateMinutes, int LowStanding,
    int LowStandingShock, Fix64 LowStandingRefuseChance);

/// <summary>contact.json: first contact and what follows.</summary>
public sealed record ContactRules(
    IReadOnlyList<string> Factions,
    int MinLevel,
    bool NeedsStudy,
    int StepSeconds,
    int BaseShock,
    IReadOnlyList<Weighted> ShockByTemperament,
    Fix64 ShockPerAwareness,
    int PreWarpShock,
    int ShockMin,
    int ShockMax,
    IReadOnlyList<ContactStepDef> Steps,
    int ViolationStanding,
    int ShockWaitMinutes,
    int CultureShockAwareness,
    int CultureShockMinutes,
    int ResearchBoost,
    IReadOnlyList<ResourceAmount> TradeIncome,
    Fix64 TradePartnerMultiplier,
    Fix64 CandidateMultiplier,
    Fix64 AlliedMultiplier,
    MembershipRules Membership,
    IReadOnlyList<UniqueTechDef> UniqueTechs,
    AllyRules Ally,
    AggressiveRules Aggressive,
    int RefuseAfterIncidentMinutes,
    AdvanceRules Advance,
    UnrestRules Unrest,
    FreighterRules Freighter,
    SpacefaringRules Spacefaring)
{
    /// <summary>A civilization of this level is spacefaring (contact.json spacefaring.from_level).</summary>
    public bool IsSpacefaring(int level) => level >= Spacefaring.FromLevel;

    /// <summary>The shock shift of a temperament id ("peaceful", "wary", "aggressive"); 0 when not listed.</summary>
    public int ShockFor(string temperament) => ShockByTemperament.FirstOrDefault(w => w.Id == temperament).Weight;

    /// <summary>Trade multiplier of a relations stage (2 trade partner, 3 candidate, 4 allied; 0 below).</summary>
    public Fix64 MultiplierFor(int stage) => stage switch
    {
        2 => TradePartnerMultiplier,
        3 => CandidateMultiplier,
        >= 4 => AlliedMultiplier,
        _ => Fix64.Zero,
    };
}
