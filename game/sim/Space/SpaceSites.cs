using System.Collections.Generic;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Space;

// CONTRACT STUB (2026-11-25, docs/space-jobs-contract.md). Shapes are fixed so the view and model agents can build
// against them; the sim agent fills them in. Stubs: no sites, every command refused "not built yet".

/// <summary>What a space site is. Phase 1: Station, Derelict, KnownDerelict, Debris. Phase 2: Asteroid. Phase 3:
/// RogueAsteroid, Comet, Lifepods, StrandedShip. Chain-only (phase 4): HiddenShip, AlienProbe.</summary>
public enum SiteKind { Station, Derelict, KnownDerelict, Debris, Asteroid, RogueAsteroid, Anomaly, Comet, Lifepods, StrandedShip, HiddenShip, AlienProbe }

/// <summary>What a player knows of a site: Detected = a grey contact with a rough size; Identified = scanned (kind,
/// hazards, contents); Spent = worked out (greyed, drifts off after a while).</summary>
public enum SiteKnowledge { Unknown, Detected, Identified, Spent }

/// <summary>A place in the star system between the bodies (map units, like BodyDef.Position).</summary>
public sealed class SpaceSite
{
    /// <summary>"s1", "s2"… (never reused in a match).</summary>
    public required string Id { get; init; }
    public required SiteKind Kind { get; init; }
    /// <summary>Kind-specific flavour: derelict origin "unknown|earth|trader|native"; asteroid "common|metal|ice|hollow";
    /// anomaly "ion_storm|…"; "" when the kind has none. Models and names key off it.</summary>
    public string Variant { get; init; } = "";
    /// <summary>Display name once identified ("Derelict C-3", "Kessa Station"); before that the view shows "Unknown contact".</summary>
    public string Name { get; init; } = "";
    /// <summary>Plot position now (moving sites update it every tick: comets, rogue asteroids, drifting wrecks).</summary>
    public FixVec2 Position { get; internal set; }
    /// <summary>Rough size 1 (small) .. 5 (huge), shown while only Detected.</summary>
    public int Size { get; init; } = 1;
    /// <summary>Sim tick it appeared; and when it leaves the system (-1 = stays).</summary>
    public long SpawnTick { get; init; }
    public long ExpireTick { get; internal set; } = -1;
    /// <summary>Its own seed (model variety, contents); a hash of the system seed and its id.</summary>
    public ulong Seed { get; init; }
}

/// <summary>One line of a site card: Label, Value, Tone "" | "good" | "warn" | "bad".</summary>
public sealed record SiteRow(string Label, string Value, string Tone = "");

/// <summary>What the player can see of a site (null when Unknown). Actions = work actions open now (ids for
/// SiteWorkCommand), each with Problem null when it can run.</summary>
public sealed record SiteReport(string SiteId, SiteKind Kind, SiteKnowledge Knowledge, string Title, string Subtitle,
    FixVec2 Position, IReadOnlyList<SiteRow> Rows, IReadOnlyList<SiteAction> Actions, IReadOnlyList<string> Hazards);

/// <summary>A work action on a site card: Id (SiteWorkCommand.Action, or "scan", "course", "away_team"), Label, and
/// Problem (null = can run now).</summary>
public sealed record SiteAction(string Id, string Label, string? Problem);

public sealed partial class StarSystemState
{
    /// <summary>Every site in the system (including Spent ones still drifting); not fogged: use SiteKnowledgeOf.</summary>
    public IReadOnlyList<SpaceSite> Sites => _sites;
    private readonly List<SpaceSite> _sites = new();

    public SpaceSite? Site(string? id)
    {
        if (id == null) return null;
        foreach (var s in _sites)
            if (s.Id == id) return s;
        return null;
    }

    public SiteKnowledge SiteKnowledgeOf(int player, string siteId) => SiteKnowledge.Unknown;

    /// <summary>The player's card for a site, or null if they don't know it.</summary>
    public SiteReport? SiteReportFor(int player, string siteId) => null;

    /// <summary>The site a ship holds at (or flies to: <see cref="SiteBound"/>), or null.</summary>
    public string? ShipSite(SystemShip ship) => null;
    public string? SiteBound(SystemShip ship) => null;

    public string? SiteCourseProblem(int player, int shipId, string siteId) => "not built yet";
    public string? SiteScanProblem(int player, int shipId, string siteId) => "not built yet";
    public string? SiteWorkProblem(int player, int shipId, string siteId, string action) => "not built yet";

    /// <summary>Scan progress 0..1 of the player's running site scan, or null.</summary>
    public Fix64? SiteScanShare(int player, string siteId) => null;

    /// <summary>Work in progress at the site for the player: label ("Sweeping the field", "Mining"), share 0..1 and
    /// seconds left; null when nothing runs.</summary>
    public (string Label, Fix64 Share, int SecondsLeft)? SiteWorkProgress(int player, string siteId) => null;

    internal void ApplySpaceJobs(Simulation sim, Command command) => sim.Refuse(command, "not built yet");

    public string? DebugSpaceProblem(Simulation sim, DebugSpaceCommand command) => "not built yet";
    internal void ApplyDebugSpace(Simulation sim, DebugSpaceCommand command) { }
}
