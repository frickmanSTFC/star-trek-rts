using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// Civilizations phase 1 (docs/civilizations-contract.md; design doc "Civilizations & First Contact"). CONTRACT STUBS:
// the sim branch replaces every body below; the signatures are what the view and model agents build against.

/// <summary>Development level of a living civilization (L1..L6).</summary>
public enum CivLevel { None = 0, StoneBronze = 1, IronMedieval = 2, Industrial = 3, Atomic = 4, EarlyWarp = 5, Spacefaring = 6 }

public enum Temperament { Unknown, Peaceful, Wary, Aggressive }

public enum CivUnity { Unknown, Unified, Fractured }

/// <summary>A civilization rolled with the system (pure: from the system seed). Living = false: ruins of a dead one.</summary>
public sealed record CivSite(string BodyId, string Species, bool Living, CivLevel Level, Temperament Temperament, CivUnity Unity,
    int Nations, long Population, int Seed);

/// <summary>A native town on the body's look-only surface map (SystemGenerator.SurfaceSeed), for the globe and the feed.
/// Size 1..3; Lit = shows lights at night (L3+).</summary>
public readonly record struct NativeTown(int Tile, int Size, bool Lit);

/// <summary>What one player knows about the civilization on a body. LevelMin..LevelMax narrows to one level once studied
/// (Study = 1); Temperament/Unity are Unknown until then. Signals = true at Detected/Scanned ("possible civilization").</summary>
public sealed record CivReport(string BodyId, bool Signals, bool Charted, bool Living, string? Species, CivLevel LevelMin, CivLevel LevelMax,
    Temperament Temperament, CivUnity Unity, int Nations, long Population, int Awareness, Fix64 Study, bool Live, bool PostHere,
    bool PostFound);

public sealed partial class StarSystemState
{
    /// <summary>Every civilization site in the system (living and ruins), by body order. Never the colony's landing body.</summary>
    public IReadOnlyList<CivSite> Civilizations => System.Array.Empty<CivSite>();

    /// <summary>The site on a body, or null (the truth: the view shows <see cref="CivView"/> instead).</summary>
    public CivSite? CivAt(string bodyId) => null;

    /// <summary>What the player knows about the body's civilization, or null when there is none or nothing is known yet.</summary>
    public CivReport? CivView(int player, string bodyId) => null;

    /// <summary>The natives' awareness of aliens, 0..100 (shared by every visitor; only rises).</summary>
    public int AwarenessOf(string bodyId) => 0;

    /// <summary>Native towns on the body's look-only map (empty for ruins and no civ). Pure; cached per body.</summary>
    public IReadOnlyList<NativeTown> TownsOn(string bodyId) => System.Array.Empty<NativeTown>();

    /// <summary>Starfleet Standing 0..100 for a Federation player in an era with the Prime Directive (null otherwise).</summary>
    public int? StandingOf(int player) => null;

    /// <summary>The consequence step reached: 0 none, 1 warning (&lt;70), 2 inquiry (&lt;50), 3 relieved (&lt;30), 4 sanctions (&lt;10),
    /// 5 recalled for good (0, or a third incident on one world).</summary>
    public int StandingRung(int player) => 0;

    /// <summary>Contamination incidents the player has had on a body.</summary>
    public int IncidentsOn(int player, string bodyId) => 0;

    /// <summary>Research points a minute the player gets from observing civilizations now (orbit study + posts).</summary>
    public Fix64 ObservationResearchPerMinute(int player) => Fix64.Zero;

    /// <summary>Why the player can't place a hidden observation post at that body now (null = they can).</summary>
    public string? PostProblem(int player, string bodyId) => "not built yet";

    /// <summary>Stub: refuses. The sim branch places the post (cost from stock) and starts its discovery rolls.</summary>
    private void ApplyObservationPost(Simulation sim, ObservationPostCommand post) => sim.Refuse(post, "civilizations not built yet");
}
