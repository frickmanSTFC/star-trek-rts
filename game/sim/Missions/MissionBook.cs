using System.Collections.Generic;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Missions;

/// <summary>Where a mission is (docs/missions-contract.md C).</summary>
public enum MissionState { Locked, Offered, Active, Completed, Failed, Declined }

/// <summary>Main = the story chain (auto-accepted, one at a time); Side = optional jobs (offered, accept / decline).</summary>
public enum MissionKind { Main, Side }

/// <summary>One objective of the current stage for the view: Text already filled ({unit}, {tile}… placeholders
/// replaced); Progress / Target for counters (Target 0 = a plain yes/no objective); Optional = bonus only.</summary>
public readonly record struct ObjectiveView(string Id, string Text, bool Done, bool Optional, Fix64 Progress, Fix64 Target);

/// <summary>A choice button (a branch option, or accept / decline on an offer).</summary>
public readonly record struct MissionOption(string Id, string Text);

/// <summary>A world marker a mission asks the view to draw: a ring of RadiusTiles round Tile (style "area", "target",
/// "site"), or on a unit (UnitId &gt; 0, style "unit").</summary>
public readonly record struct MissionMarker(string MissionId, string Style, int Tile, int RadiusTiles, int UnitId, string Label);

/// <summary>What the view needs to draw one mission.</summary>
public sealed record MissionView(
    string Id, string Title, MissionKind Kind, MissionState State, string Giver, string Briefing,
    string StageTitle, IReadOnlyList<ObjectiveView> Objectives, IReadOnlyList<MissionOption> Options,
    long TimerEndTick, long StartTick, long EndTick, string Debrief, IReadOnlyList<string> Rewards);

/// <summary>A line of mission comms: Tick, Giver (id), GiverName, Text (filled), MissionId, Channel ("comms" = the
/// incoming-transmission card, "feed" = the event feed only).</summary>
public readonly record struct MissionMessage(long Tick, string Giver, string GiverName, string Text, string MissionId, string Channel);

/// <summary>A place on the map a mission put there (a probe wreck, a signal source, a photo point). Found once a tile
/// is surveyed or seen; Done once its objective is met. Kind = a model id the view draws ("probe_wreck",
/// "signal_source", "photo_point", "survey_marker").</summary>
public readonly record struct MissionSite(int Id, int Owner, string MissionId, string Kind, int Tile, bool Found, bool Done);

/// <summary>Every player's missions (sim state, hashed). STUB (contract): agent S2 fills it in. Keep the signatures.</summary>
public sealed class MissionBook
{
    /// <summary>On for a campaign game (set at set-up, before tick 0; off = nothing below ever changes).</summary>
    public bool Enabled { get; internal set; }

    /// <summary>This player's missions that are not Locked, main chain first then sides, each in file order.</summary>
    public IReadOnlyList<MissionView> For(int player) => System.Array.Empty<MissionView>();

    /// <summary>This player's mission comms, oldest first (bounded log).</summary>
    public IReadOnlyList<MissionMessage> Messages(int player) => System.Array.Empty<MissionMessage>();

    /// <summary>Markers of this player's offered / active missions.</summary>
    public IReadOnlyList<MissionMarker> Markers(int player) => System.Array.Empty<MissionMarker>();

    /// <summary>Mission sites of this player (found or not: the view shows only found ones, or ones in sight).</summary>
    public IReadOnlyList<MissionSite> Sites(int player) => System.Array.Empty<MissionSite>();
}
